import 'dotenv/config';
import { appendFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { GuideDecisionSchema, GuideRequestSchema, type GuideRequest } from '../src/contracts';
import { loadApiConfig } from '../services/api/src/config';
import { createAiProvider } from '../services/api/src/providers/createAiProvider';

interface Scenario {
  id: string;
  category: string;
  question: string;
  application: string;
  failureCount?: number;
  candidates: Array<{ id: string; label: string; role: string }>;
  expectedTargetId?: string;
  expectedAction?: string;
}

function toRequest(scenario: Scenario): GuideRequest {
  return GuideRequestSchema.parse({
    session: {
      sessionId: `bench-${scenario.id}`, originalUserMessage: scenario.question,
      goal: scenario.question, mode: 'guidance', status: 'waiting_for_ai', completedSteps: [], knownFacts: [],
      failureCount: scenario.failureCount ?? 0,
    },
    context: { platform: 'windows', applicationName: scenario.application, windowTitle: scenario.application },
    candidates: scenario.candidates.map((candidate, index) => ({
      ...candidate, enabled: true, visible: true, clickable: true,
      bounds: { x: 20 + index * 150, y: 20, width: 120, height: 40 },
    })),
  });
}

async function main() {
  const requestedMode = process.argv[2] ?? 'compare';
  const modes = requestedMode === 'compare' ? ['legacy', 'v2'] as const : [requestedMode as 'legacy' | 'v2'];
  const fixturePath = resolve('fixtures/showwhere-bench/scenarios.json');
  const scenarios = JSON.parse(await readFile(fixturePath, 'utf8')) as Scenario[];
  const resultPath = resolve('data/brain-v2/eval/showwhere-bench-results.jsonl');
  await mkdir(dirname(resultPath), { recursive: true });
  const runId = crypto.randomUUID();
  const summary: Record<string, { correct: number; total: number; latencyMs: number[] }> = {};

  for (const mode of modes) {
    const config = loadApiConfig({ ...process.env, SHOWWHERE_BRAIN_MODE: mode });
    const provider = createAiProvider(config);
    summary[mode] = { correct: 0, total: 0, latencyMs: [] };
    for (const scenario of scenarios) {
      const started = performance.now();
      let output: unknown;
      let error: string | undefined;
      try {
        output = await provider.decideNextAction(toRequest(scenario));
      } catch (caught) {
        error = caught instanceof Error ? caught.message : String(caught);
      }
      const elapsed = Math.round(performance.now() - started);
      const parsed = GuideDecisionSchema.safeParse(output);
      const correct = parsed.success && (
        scenario.expectedTargetId ? parsed.data.targetId === scenario.expectedTargetId : parsed.data.action === scenario.expectedAction
      );
      const record = {
        schemaVersion: 'showwhere-bench-v1', runId, timestamp: new Date().toISOString(),
        scenarioId: scenario.id, category: scenario.category, model: mode,
        input: toRequest(scenario), output, expected: { targetId: scenario.expectedTargetId, action: scenario.expectedAction },
        correct, latencyMs: elapsed, confidence: parsed.success ? parsed.data.confidence : 0,
        fallbackUsed: parsed.success ? parsed.data.action : 'error', error,
      };
      await appendFile(resultPath, `${JSON.stringify(record)}\n`, 'utf8');
      summary[mode].total += 1;
      summary[mode].correct += Number(correct);
      summary[mode].latencyMs.push(elapsed);
    }
  }
  const report = Object.fromEntries(Object.entries(summary).map(([mode, value]) => [mode, {
    accuracy: value.correct / Math.max(1, value.total), correct: value.correct, total: value.total,
    averageLatencyMs: value.latencyMs.reduce((sum, item) => sum + item, 0) / Math.max(1, value.latencyMs.length),
  }]));
  await writeFile(resolve(`data/brain-v2/eval/showwhere-bench-${runId}.json`), JSON.stringify(report, null, 2), 'utf8');
  console.log(JSON.stringify(report, null, 2));
}

void main();

