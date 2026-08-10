import 'dotenv/config';
import { LearningProvider } from './provider';
import {
  buildDatasets,
  buildReviewQueue,
  bootstrapSeedScenarios,
  generateScenarios,
  judgeScenarios,
  readLatestReport,
  recordHumanReview,
  runBenchmark,
  validateLearningData,
  type PipelineConfig,
} from './pipeline';

function option(name: string): string | undefined {
  const prefix = `--${name}=`;
  const inline = process.argv.find((value) => value.startsWith(prefix));
  if (inline) return inline.slice(prefix.length);
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function integer(name: string, fallback: number, minimum: number, maximum: number): number {
  const value = Number(option(name) ?? process.env[name] ?? fallback);
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    throw new Error(`${name} must be an integer from ${minimum} to ${maximum}.`);
  }
  return value;
}

function createConfig(): PipelineConfig {
  const apiKey = process.env.FEATHERLESS_API_KEY ?? '';
  const baseUrl = process.env.FEATHERLESS_BASE_URL ?? 'https://api.featherless.ai/v1';
  const generatorModel = option('model') ?? process.env.LEARNING_GENERATOR_MODEL ?? process.env.FEATHERLESS_GUIDE_MODEL ?? '';
  const judgeModels = [
    process.env.LEARNING_JUDGE_A_MODEL,
    process.env.LEARNING_JUDGE_B_MODEL,
    process.env.LEARNING_JUDGE_C_MODEL,
  ].filter((model): model is string => Boolean(model?.trim()));
  return {
    provider: new LearningProvider({
      apiKey,
      baseUrl,
      timeoutMs: integer('FEATHERLESS_REQUEST_TIMEOUT_MS', 30_000, 500, 120_000),
      maxTokens: integer('LEARNING_MAX_TOKENS', 1_400, 256, 4_096),
      retries: integer('FEATHERLESS_MAX_RETRIES', 2, 0, 3),
    }),
    generatorModel,
    judgeModels,
    maxExamples: integer('LEARNING_MAX_EXAMPLES_PER_RUN', 50, 1, 500),
    concurrency: integer('LEARNING_CONCURRENCY', 2, 1, 5),
  };
}

function requireProviderConfig(config: PipelineConfig, judges = false): void {
  if (!process.env.FEATHERLESS_API_KEY) throw new Error('FEATHERLESS_API_KEY is required for model calls. Use --dry-run to inspect the run without calling a model.');
  if (!config.generatorModel) throw new Error('Set LEARNING_GENERATOR_MODEL or FEATHERLESS_GUIDE_MODEL.');
  if (judges && config.judgeModels.length < 2) throw new Error('Set LEARNING_JUDGE_A_MODEL and LEARNING_JUDGE_B_MODEL to independently validate scenarios.');
}

async function main(): Promise<void> {
  const command = process.argv[2] ?? 'help';
  const dryRun = process.argv.includes('--dry-run');
  const config = createConfig();
  switch (command) {
    case 'validate':
      console.log(JSON.stringify(await validateLearningData(), null, 2));
      break;
    case 'bootstrap':
      console.log(JSON.stringify({ humanSeedScenarios: (await bootstrapSeedScenarios()).length }, null, 2));
      break;
    case 'generate':
      if (!dryRun) requireProviderConfig(config);
      console.log(JSON.stringify({ generated: (await generateScenarios(config, {
        seed: option('seed'), category: option('category'), count: integer('count', 5, 1, 10), dryRun,
      })).length }, null, 2));
      break;
    case 'judge':
      requireProviderConfig(config, true);
      console.log(JSON.stringify({ judged: (await judgeScenarios(config)).length }, null, 2));
      break;
    case 'review-queue':
      console.log(JSON.stringify({ reviewItems: (await buildReviewQueue()).length }, null, 2));
      break;
    case 'review': {
      const scenarioId = option('scenario');
      const action = option('action');
      if (!scenarioId || !['accept', 'reject', 'correct', 'mark_ambiguous'].includes(action ?? '')) {
        throw new Error('Use --scenario <id> --action accept|reject|correct|mark_ambiguous. Corrections also require --target <candidate-id>.');
      }
      console.log(JSON.stringify(await recordHumanReview({
        scenarioId,
        action: action as 'accept' | 'reject' | 'correct' | 'mark_ambiguous',
        correctedTargetId: option('target') ?? null,
        rewrittenInstruction: option('instruction') ?? null,
        seedRuleToAdd: option('seed-rule') ?? null,
        reviewerComment: option('comment') ?? '',
      }), null, 2));
      break;
    }
    case 'build-dataset':
      console.log(JSON.stringify(await buildDatasets(), null, 2));
      break;
    case 'run':
      if (!dryRun) requireProviderConfig(config, true);
      await generateScenarios(config, { seed: option('seed'), category: option('category'), count: integer('count', 5, 1, 10), dryRun });
      if (!dryRun) {
        await judgeScenarios(config);
        await buildReviewQueue();
        console.log(JSON.stringify(await buildDatasets(), null, 2));
      }
      break;
    case 'eval':
      if (!dryRun) requireProviderConfig(config);
      console.log(JSON.stringify(await runBenchmark(config, option('model'), dryRun), null, 2));
      break;
    case 'report':
      console.log(JSON.stringify(await readLatestReport(), null, 2));
      break;
    default:
      console.log('Commands: validate | bootstrap | generate | judge | review-queue | review | build-dataset | run | eval | report');
      console.log('Options: --seed <id> --category <name> --count 1..10 --model <id> --dry-run');
  }
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
});
