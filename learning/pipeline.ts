import path from 'node:path';
import { z } from 'zod';
import {
  BenchmarkCaseSchema,
  HumanReviewDecisionSchema,
  JudgeResultSchema,
  JudgedScenarioSchema,
  ScenarioSchema,
  SeedExampleSchema,
  type JudgedScenario,
  type HumanReviewDecision,
  type Scenario,
  type SeedExample,
} from './contracts';
import { dataRoot, readJson, readJsonl, scenarioFingerprint, stableHash, writeJson, writeJsonl } from './io';
import { LearningProvider } from './provider';

const seedsSchema = z.object({
  seedVersion: z.string(),
  datasetVersion: z.string(),
  seeds: z.array(SeedExampleSchema).min(1),
}).strict();

const generatedEnvelopeSchema = z.object({ scenarios: z.array(ScenarioSchema).min(1).max(10) }).strict();
const judgeEnvelopeSchema = z.object({ result: JudgeResultSchema }).strict();
const benchmarkSchema = z.object({ benchmarkVersion: z.string(), cases: z.array(BenchmarkCaseSchema).min(1) }).strict();

export interface PipelineConfig {
  provider: LearningProvider;
  generatorModel: string;
  judgeModels: string[];
  maxExamples: number;
  concurrency: number;
}

export interface GenerateOptions {
  seed?: string;
  category?: string;
  count: number;
  dryRun: boolean;
}

export async function loadSeeds(): Promise<{ seedVersion: string; datasetVersion: string; seeds: SeedExample[] }> {
  const library = await readJson(path.join(dataRoot, 'seeds', 'showwhere-seeds.json'), seedsSchema);
  const ids = library.seeds.map((seed) => seed.id);
  if (new Set(ids).size !== ids.length) throw new Error('Seed IDs must be unique.');
  return library;
}

export async function validateLearningData(): Promise<{ seeds: number; benchmark: number }> {
  const seeds = await loadSeeds();
  const benchmark = await readJson(path.join(dataRoot, 'evaluation', 'benchmark.json'), benchmarkSchema);
  const seedIds = new Set(seeds.seeds.map((seed) => seed.id));
  for (const item of benchmark.cases) {
    if (!seedIds.has(item.seedId)) throw new Error(`Benchmark ${item.id} references unknown seed ${item.seedId}.`);
    const candidateIds = new Set(item.candidates.map((candidate) => candidate.id));
    if (item.expectedAction === 'highlight' && !candidateIds.has(item.expectedTargetId ?? '')) {
      throw new Error(`Benchmark ${item.id} has an unknown expected target.`);
    }
  }
  return { seeds: seeds.seeds.length, benchmark: benchmark.cases.length };
}

export async function bootstrapSeedScenarios(): Promise<Scenario[]> {
  const library = await loadSeeds();
  const scenarios = library.seeds.map((seed) => {
    const candidates = seed.possibleCandidates.map((label, index) => ({
      id: `candidate_${index + 1}`,
      label,
      role: inferRole(label),
      enabled: true,
      visible: true,
      description: `Human-authored candidate from seed ${seed.id}`,
    }));
    const targetIndex = seed.correctNextAction.target === null
      ? -1
      : seed.possibleCandidates.indexOf(seed.correctNextAction.target);
    return ScenarioSchema.parse({
      schemaVersion: 'showwhere-scenario-v1',
      scenarioId: `${seed.id}_canonical_v1`,
      seedId: seed.id,
      goal: seed.goal,
      userMessage: seed.exampleUserMessages[0],
      context: {
        platform: seed.initialContext.platform,
        application: seed.initialContext.application,
        screenState: seed.initialContext.screenState,
        locale: /[가-힣]/u.test(seed.exampleUserMessages[0]) ? 'ko-KR' : 'en-US',
        previousSteps: [],
      },
      candidates,
      proposedAction: seed.correctNextAction.mode,
      proposedCorrectTargetId: targetIndex >= 0 ? candidates[targetIndex].id : null,
      instruction: seed.correctNextAction.instruction,
      expectedChange: seed.expectedChange,
      successCondition: seed.successCondition,
      difficulty: seed.correctNextAction.mode === 'clarify' ? 'hard' : 'medium',
      ambiguity: seed.correctNextAction.mode === 'clarify' ? 0.8 : 0.05,
      riskLevel: seed.riskLevel,
      requiresConfirmation: seed.requiresConfirmation,
      generatorModel: 'human-seed-bootstrap-v1',
      generationTimestamp: new Date().toISOString(),
      provenance: 'human',
    });
  });
  await writeJsonl(path.join(dataRoot, 'human-reviewed', 'seed-scenarios.jsonl'), scenarios);
  await writeJson(path.join(dataRoot, 'human-reviewed', 'seed-scenarios-manifest.json'), {
    datasetVersion: library.datasetVersion,
    seedVersion: library.seedVersion,
    provenance: 'human',
    transformation: 'Deterministic one-to-one conversion of runtime-validated human seed examples; no model generation.',
    count: scenarios.length,
    generatedAt: new Date().toISOString(),
  });
  return scenarios;
}

const GENERATOR_PROMPT = `You generate diverse ShowWhere UI guidance scenarios from one human-approved seed.
Return JSON {"scenarios":[...]}. Follow the supplied schema example exactly. Generate meaningful screen-state variations, not mere paraphrases. Candidate IDs must be local stable IDs. For highlight, the proposed target ID must exist. For clarify/complete/explain, target must be null. Preserve decision rules, confirmation requirements, risk, expected change, and success condition. Never include credentials, personal data, screenshots, chain-of-thought, or invented claims. provenance must be synthetic and schemaVersion showwhere-scenario-v1.`;

export async function generateScenarios(config: PipelineConfig, options: GenerateOptions): Promise<Scenario[]> {
  const library = await loadSeeds();
  const selected = library.seeds.filter((seed) => (!options.seed || seed.id === options.seed)
    && (!options.category || seed.category === options.category));
  if (selected.length === 0) throw new Error('No seeds matched the requested filter.');
  const requested = selected.length * options.count;
  if (requested > config.maxExamples) throw new Error(`Requested ${requested}; limit is ${config.maxExamples}. Narrow the seed/category or raise LEARNING_MAX_EXAMPLES_PER_RUN explicitly.`);
  if (options.dryRun) {
    console.log(JSON.stringify({ mode: 'dry-run', seeds: selected.map((seed) => seed.id), variantsPerSeed: options.count, requests: selected.length, maximumExamples: requested, model: config.generatorModel }, null, 2));
    return [];
  }

  const batches = await mapLimited(selected, config.concurrency, async (seed) => {
    try {
      const response = await config.provider.completeJson(config.generatorModel, GENERATOR_PROMPT, {
        seed,
        count: options.count,
        requiredScenarioShape: scenarioShape(),
      }, generatedEnvelopeSchema);
      return {
        seedId: seed.id,
        scenarios: response.scenarios.map((scenario) => ScenarioSchema.parse({
          ...scenario,
          seedId: seed.id,
          generatorModel: config.generatorModel,
          generationTimestamp: new Date().toISOString(),
          provenance: 'synthetic',
        })),
      };
    } catch (error) {
      return { seedId: seed.id, scenarios: [], error: error instanceof Error ? error.message : String(error) };
    }
  });
  const output = path.join(dataRoot, 'generated', 'scenarios.jsonl');
  const previous = await readJsonl(output, ScenarioSchema);
  const generated = batches.flatMap((batch) => batch.scenarios);
  const fresh = generated.filter(isPrivacySafeScenario);
  const unique = deduplicate([...previous, ...fresh]);
  await writeJsonl(output, unique);
  const manifest = {
    datasetVersion: library.datasetVersion,
    generatedAt: new Date().toISOString(),
    generatorModel: config.generatorModel,
    requested, previous: previous.length, privacyRejected: generated.length - fresh.length,
    failures: batches.filter((batch) => batch.error).map((batch) => ({ seedId: batch.seedId, error: batch.error })),
    acceptedBySchemaAndDedup: unique.length,
    output: 'learning/data/generated/scenarios.jsonl',
  };
  await writeJson(path.join(dataRoot, 'generated', 'manifest.json'), manifest);
  const runId = manifest.generatedAt.replace(/[:.]/gu, '-');
  await writeJson(path.join(dataRoot, 'generated', 'runs', `${runId}.json`), manifest);
  if (fresh.length === 0 && batches.some((batch) => batch.error)) {
    throw new Error(`Generation produced no valid scenarios. ${batches.filter((batch) => batch.error).map((batch) => `${batch.seedId}: ${batch.error}`).join('; ')}`);
  }
  return unique;
}

const JUDGE_PROMPT = `You are an independent ShowWhere evaluator. Solve the UI scenario yourself. You are not shown the generator answer. Choose the best immediate action from highlight, clarify, complete, or explain. A highlight target must be one exact candidate ID. Prefer clarification when a material choice is unresolved. Enforce explicit confirmation for consequential actions. Return only {"result":{"selectedTargetId":string|null,"action":"...","verdict":"valid|invalid|ambiguous","confidence":0..1,"shortReason":"concise observable reason","safetyConcern":boolean}}. Do not provide or request chain-of-thought.`;

export async function judgeScenarios(config: PipelineConfig): Promise<JudgedScenario[]> {
  if (config.judgeModels.length < 2) throw new Error('At least two judge models are required. Configure LEARNING_JUDGE_A_MODEL and LEARNING_JUDGE_B_MODEL.');
  const scenarios = await readJsonl(path.join(dataRoot, 'generated', 'scenarios.jsonl'), ScenarioSchema);
  if (scenarios.length === 0) throw new Error('No generated scenarios found. Run learning:generate first.');
  const judged = await mapLimited(scenarios.slice(0, config.maxExamples), config.concurrency, async (scenario) => {
    const blindScenario = {
      scenarioId: scenario.scenarioId,
      seedId: scenario.seedId,
      goal: scenario.goal,
      userMessage: scenario.userMessage,
      context: scenario.context,
      candidates: scenario.candidates,
      expectedChange: scenario.expectedChange,
      successCondition: scenario.successCondition,
      riskLevel: scenario.riskLevel,
      requiresConfirmation: scenario.requiresConfirmation,
    };
    const judgments = await mapLimited(config.judgeModels, config.concurrency, async (model) => {
      const startedAt = performance.now();
      const response = await config.provider.completeJson(model, JUDGE_PROMPT, blindScenario, judgeEnvelopeSchema);
      return { model, latencyMs: Math.round(performance.now() - startedAt), result: response.result };
    });
    const disposition = classify(scenario, judgments.map((entry) => entry.result));
    return JudgedScenarioSchema.parse({ scenario, judgments, ...disposition, evaluatedAt: new Date().toISOString() });
  });
  await writeJudgmentOutputs(judged);
  return judged;
}

export async function buildReviewQueue(): Promise<JudgedScenario[]> {
  const items = await readJsonl(path.join(dataRoot, 'judged', 'all.jsonl'), JudgedScenarioSchema);
  const review = items.filter((item) => item.disposition === 'human_review');
  await writeJson(path.join(dataRoot, 'human-reviewed', 'review-queue.json'), {
    generatedAt: new Date().toISOString(),
    instructions: 'Accept, reject, correct target/instruction, mark ambiguous, or add a seed rule. Never convert an uncertain synthetic item into human-approved data without explicit review.',
    items: review,
  });
  return review;
}

export async function recordHumanReview(input: Omit<HumanReviewDecision, 'reviewedAt'>): Promise<HumanReviewDecision> {
  const judged = await readJsonl(path.join(dataRoot, 'judged', 'all.jsonl'), JudgedScenarioSchema);
  const item = judged.find((entry) => entry.scenario.scenarioId === input.scenarioId);
  if (!item) throw new Error(`Scenario ${input.scenarioId} was not found in judged data.`);
  if (item.disposition !== 'human_review') throw new Error('Only queued human-review scenarios can be reviewed with this command.');
  if (input.action === 'correct') {
    if (!input.correctedTargetId) throw new Error('--target is required for a correction.');
    if (!item.scenario.candidates.some((candidate) => candidate.id === input.correctedTargetId)) {
      throw new Error('The corrected target must be one of the scenario candidates.');
    }
  }
  const decision = HumanReviewDecisionSchema.parse({ ...input, reviewedAt: new Date().toISOString() });
  const decisionPath = path.join(dataRoot, 'human-reviewed', 'decisions.jsonl');
  const previous = await readJsonl(decisionPath, HumanReviewDecisionSchema);
  const decisions = [...previous.filter((entry) => entry.scenarioId !== decision.scenarioId), decision];
  await writeJsonl(decisionPath, decisions);
  const accepted = decisions.filter((entry) => ['accept', 'correct'].includes(entry.action)).map((entry) => {
    const source = judged.find((candidate) => candidate.scenario.scenarioId === entry.scenarioId)!;
    return {
      source: 'human-reviewed', review: entry,
      scenario: entry.action === 'correct' ? {
        ...source.scenario,
        proposedAction: 'highlight',
        proposedCorrectTargetId: entry.correctedTargetId,
        instruction: entry.rewrittenInstruction ?? source.scenario.instruction,
        provenance: 'human',
      } : { ...source.scenario, provenance: 'human' },
      originalJudgments: source.judgments,
    };
  });
  await writeJsonl(path.join(dataRoot, 'human-reviewed', 'accepted.jsonl'), accepted);
  return decision;
}

export async function buildDatasets(): Promise<Record<string, number>> {
  const accepted = await readJsonl(path.join(dataRoot, 'accepted', 'scenarios.jsonl'), JudgedScenarioSchema);
  const humanSeeds = await readJsonl(path.join(dataRoot, 'human-reviewed', 'seed-scenarios.jsonl'), ScenarioSchema);
  const benchmark = await readJson(path.join(dataRoot, 'evaluation', 'benchmark.json'), benchmarkSchema);
  const heldOutSeedIds = new Set(benchmark.cases.map((item) => item.seedId));
  const records = [
    ...humanSeeds.map((scenario) => ({ scenario, source: 'human_seed' as const, judgments: [] })),
    ...accepted.map((item) => ({ scenario: item.scenario, source: 'auto_accepted_synthetic' as const, judgments: item.judgments })),
  ];
  type DatasetRecord = typeof records[number];
  const partitions: Record<'training' | 'validation' | 'evaluation', DatasetRecord[]> = { training: [], validation: [], evaluation: [] };
  for (const record of records) {
    const bucket = Number.parseInt(stableHash(record.scenario.seedId).slice(0, 8), 16) % 100;
    const partition = heldOutSeedIds.has(record.scenario.seedId)
      ? 'evaluation'
      : bucket < 82 ? 'training' : 'validation';
    partitions[partition].push(record);
  }
  for (const [name, records] of Object.entries(partitions)) {
    await writeJsonl(path.join(dataRoot, name, 'scenarios.jsonl'), records);
  }
  const summary = Object.fromEntries(Object.entries(partitions).map(([key, value]) => [key, value.length]));
  await writeJson(path.join(dataRoot, 'dataset-manifest.json'), {
    datasetVersion: 'showwhere-v0.1', splitMethod: 'permanent benchmark seed families held out; remaining sha256(seedId), 82/18 training/validation', familyLeakagePrevented: true, sources: { humanSeed: humanSeeds.length, autoAcceptedSynthetic: accepted.length }, heldOutSeedIds: [...heldOutSeedIds].sort(), ...summary,
  });
  return summary;
}

export async function runBenchmark(config: PipelineConfig, model?: string, dryRun = false): Promise<unknown> {
  const benchmark = await readJson(path.join(dataRoot, 'evaluation', 'benchmark.json'), benchmarkSchema);
  const selectedModel = model ?? config.judgeModels[0] ?? config.generatorModel;
  if (dryRun) {
    const result = { mode: 'dry-run', model: selectedModel, cases: benchmark.cases.length, requests: benchmark.cases.length };
    console.log(JSON.stringify(result, null, 2));
    return result;
  }
  const results = await mapLimited(benchmark.cases, config.concurrency, async (item) => {
    const startedAt = performance.now();
    const blindCase = {
      id: item.id, seedId: item.seedId, category: item.category, goal: item.goal,
      userMessage: item.userMessage, context: item.context, candidates: item.candidates,
      riskLevel: item.riskLevel, requiresConfirmation: item.requiresConfirmation,
    };
    try {
      const response = await config.provider.completeJson(selectedModel, JUDGE_PROMPT, blindCase, judgeEnvelopeSchema);
      const latencyMs = Math.round(performance.now() - startedAt);
      const targetCorrect = response.result.selectedTargetId === item.expectedTargetId;
      const actionCorrect = response.result.action === item.expectedAction;
      const hallucinatedTarget = response.result.selectedTargetId !== null
        && !item.candidates.some((candidate) => candidate.id === response.result.selectedTargetId);
      const safetyCorrect = !item.requiresConfirmation || response.result.safetyConcern || response.result.action === 'clarify';
      return {
        id: item.id, seedId: item.seedId, category: item.category, expectedAction: item.expectedAction,
        expectedTargetId: item.expectedTargetId, result: response.result, targetCorrect, actionCorrect,
        hallucinatedTarget, safetyCorrect, invalidSchema: false,
        unnecessaryClarification: item.expectedAction !== 'clarify' && response.result.action === 'clarify',
        missedClarification: item.expectedAction === 'clarify' && response.result.action !== 'clarify',
        koreanQuestion: /[가-힣]/u.test(item.userMessage),
        crossLanguage: /[가-힣]/u.test(item.userMessage) !== item.candidates.some((candidate) => /[가-힣]/u.test(candidate.label)),
        latencyMs,
      };
    } catch {
      return {
        id: item.id, seedId: item.seedId, category: item.category, expectedAction: item.expectedAction,
        expectedTargetId: item.expectedTargetId, result: null, targetCorrect: false, actionCorrect: false,
        hallucinatedTarget: false, safetyCorrect: false, invalidSchema: true,
        unnecessaryClarification: false, missedClarification: item.expectedAction === 'clarify',
        koreanQuestion: /[가-힣]/u.test(item.userMessage),
        crossLanguage: /[가-힣]/u.test(item.userMessage) !== item.candidates.some((candidate) => /[가-힣]/u.test(candidate.label)),
        latencyMs: Math.round(performance.now() - startedAt),
      };
    }
  });
  const report = createReport(selectedModel, benchmark.benchmarkVersion, results);
  await writeJsonl(path.join(dataRoot, 'evaluation', 'latest-results.jsonl'), results);
  await writeJson(path.join(dataRoot, 'evaluation', 'latest-report.json'), report);
  return report;
}

export async function runBenchmarkComparison(
  config: PipelineConfig,
  models: readonly string[],
  dryRun = false,
): Promise<unknown> {
  const uniqueModels = [...new Set(models.map((model) => model.trim()).filter(Boolean))];
  if (uniqueModels.length < 2) throw new Error('Model comparison requires at least two distinct model IDs.');
  if (dryRun) return { mode: 'dry-run', models: uniqueModels, modelCount: uniqueModels.length };
  const reports = [];
  for (const model of uniqueModels) reports.push(await runBenchmark(config, model, false));
  const comparison = { benchmark: 'ShowWhere model comparison', comparedAt: new Date().toISOString(), reports };
  await writeJson(path.join(dataRoot, 'evaluation', 'latest-comparison.json'), comparison);
  return comparison;
}

export async function readLatestReport(): Promise<unknown> {
  return readJson(path.join(dataRoot, 'evaluation', 'latest-report.json'), z.unknown());
}

function classify(scenario: Scenario, results: Array<z.infer<typeof JudgeResultSchema>>): Pick<JudgedScenario, 'disposition' | 'dispositionReason'> {
  const ids = new Set(scenario.candidates.map((candidate) => candidate.id));
  if (scenario.riskLevel === 'high') return { disposition: 'human_review', dispositionReason: 'High-risk synthetic examples require human review.' };
  if (results.some((result) => result.selectedTargetId !== null && !ids.has(result.selectedTargetId))) {
    return { disposition: 'reject', dispositionReason: 'A judge hallucinated a target outside the candidate set.' };
  }
  if (results.some((result) => result.verdict === 'invalid')) return { disposition: 'reject', dispositionReason: 'At least one independent judge marked the scenario invalid.' };
  const signatures = results.map((result) => `${result.action}:${result.selectedTargetId ?? ''}`);
  const agrees = signatures.every((signature) => signature === signatures[0]);
  const matchesGenerator = results.every((result) => result.action === scenario.proposedAction
    && result.selectedTargetId === scenario.proposedCorrectTargetId);
  const confidence = Math.min(...results.map((result) => result.confidence));
  if (agrees && matchesGenerator && confidence >= 0.9 && scenario.ambiguity < 0.35
      && results.every((result) => result.verdict === 'valid' && !result.safetyConcern)) {
    return { disposition: 'auto_accept', dispositionReason: 'Generator and independent judges agree with confidence >= 0.90; schema and safety checks passed.' };
  }
  return { disposition: 'human_review', dispositionReason: 'Disagreement, ambiguity, safety concern, or confidence below the auto-accept threshold.' };
}

async function writeJudgmentOutputs(items: JudgedScenario[]): Promise<void> {
  await writeJsonl(path.join(dataRoot, 'judged', 'all.jsonl'), items);
  await writeJsonl(path.join(dataRoot, 'accepted', 'scenarios.jsonl'), items.filter((item) => item.disposition === 'auto_accept'));
  await writeJsonl(path.join(dataRoot, 'rejected', 'scenarios.jsonl'), items.filter((item) => item.disposition === 'reject'));
  await writeJsonl(path.join(dataRoot, 'generated', 'needs-review.jsonl'), items.filter((item) => item.disposition === 'human_review'));
}

function deduplicate(scenarios: Scenario[]): Scenario[] {
  const seen = new Set<string>();
  return scenarios.filter((scenario) => {
    const fingerprint = scenarioFingerprint(scenario);
    if (seen.has(fingerprint)) return false;
    seen.add(fingerprint);
    return true;
  });
}

function isPrivacySafeScenario(scenario: Scenario): boolean {
  const text = JSON.stringify(scenario);
  const forbidden = [
    /bearer\s+[a-z0-9._-]{12,}/iu,
    /(?:api[_ -]?key|password|token)\s*[:=]\s*[^\s"']{8,}/iu,
    /\b(?:\d[ -]*?){13,19}\b/u,
    /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/iu,
  ];
  return forbidden.every((pattern) => !pattern.test(text));
}

function scenarioShape(): Record<string, unknown> {
  return {
    schemaVersion: 'showwhere-scenario-v1', scenarioId: 'seed_id_variant_001', seedId: 'copied', goal: 'string', userMessage: 'string',
    context: { platform: 'windows', application: 'string', screenState: 'string', locale: 'ko-KR', previousSteps: [] },
    candidates: [{ id: 'candidate_1', label: 'visible label', role: 'button', enabled: true, visible: true, description: 'optional' }],
    proposedAction: 'highlight', proposedCorrectTargetId: 'candidate_1', instruction: 'string', expectedChange: 'string', successCondition: 'string', difficulty: 'medium', ambiguity: 0.1, riskLevel: 'low', requiresConfirmation: false, generatorModel: 'overwritten', generationTimestamp: new Date(0).toISOString(), provenance: 'synthetic',
  };
}

function inferRole(label: string): string {
  if (/search|검색/iu.test(label)) return 'searchbox';
  if (/file name|phone number|주소|이름/iu.test(label)) return 'textbox';
  if (/image files|file type|파일 형식/iu.test(label)) return 'combobox';
  if (/home|downloads|documents|settings|devices|camera|microphone|wifi|wi-fi|network|printer/iu.test(label)) return 'navigation';
  return 'button';
}

async function mapLimited<T, R>(items: readonly T[], concurrency: number, worker: (item: T) => Promise<R>): Promise<R[]> {
  const output = new Array<R>(items.length);
  let index = 0;
  const runners = Array.from({ length: Math.min(Math.max(1, concurrency), items.length) }, async () => {
    while (index < items.length) {
      const current = index;
      index += 1;
      output[current] = await worker(items[current]);
    }
  });
  await Promise.all(runners);
  return output;
}

function createReport(model: string, benchmarkVersion: string, results: Array<{
  targetCorrect: boolean; actionCorrect: boolean; hallucinatedTarget: boolean; safetyCorrect: boolean;
  invalidSchema: boolean; unnecessaryClarification: boolean; missedClarification: boolean;
  koreanQuestion: boolean; crossLanguage: boolean; category: string; latencyMs: number;
}>): unknown {
  const rate = (field: keyof typeof results[number]) => results.length === 0 ? 0 : results.filter((item) => item[field] === true).length / results.length;
  const subsetRate = (items: typeof results, field: 'targetCorrect' | 'actionCorrect') =>
    items.length === 0 ? null : items.filter((item) => item[field]).length / items.length;
  const latencies = results.map((item) => item.latencyMs).sort((a, b) => a - b);
  const percentile = (value: number) => latencies.length === 0 ? 0 : latencies[Math.min(latencies.length - 1, Math.floor((latencies.length - 1) * value))];
  return {
    model, benchmarkVersion, evaluatedAt: new Date().toISOString(), cases: results.length,
    targetAccuracy: rate('targetCorrect'), taskModeAccuracy: rate('actionCorrect'),
    hallucinatedTargetRate: rate('hallucinatedTarget'), safetyAccuracy: rate('safetyCorrect'),
    unnecessaryClarificationRate: rate('unnecessaryClarification'),
    missedClarificationRate: rate('missedClarification'),
    koreanIntentTargetAccuracy: subsetRate(results.filter((item) => item.koreanQuestion), 'targetCorrect'),
    crossLanguageTargetAccuracy: subsetRate(results.filter((item) => item.crossLanguage), 'targetCorrect'),
    recoveryTargetAccuracy: subsetRate(results.filter((item) => item.category === 'recovery'), 'targetCorrect'),
    averageLatencyMs: results.length ? Math.round(results.reduce((sum, item) => sum + item.latencyMs, 0) / results.length) : 0,
    p50LatencyMs: percentile(0.5), p95LatencyMs: percentile(0.95),
    invalidSchemaRate: rate('invalidSchema'),
    note: 'Token usage and cost are unavailable unless the provider returns usage metadata.',
  };
}
