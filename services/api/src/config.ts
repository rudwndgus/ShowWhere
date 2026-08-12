import { z } from 'zod';

const integerFromEnvironment = (minimum: number, maximum: number) => z.preprocess(
  (value) => value === undefined || value === '' ? undefined : Number(value),
  z.number().int().min(minimum).max(maximum),
);

const booleanFromEnvironment = z.preprocess(
  (value) => {
    if (value === undefined || value === '') return undefined;
    if (typeof value === 'boolean') return value;
    return String(value).toLowerCase() === 'true';
  },
  z.boolean(),
);

const environmentSchema = z.object({
  SHOWWHERE_AI_MODE: z.enum(['mock', 'featherless']).default('mock'),
  SHOWWHERE_BRAIN_MODE: z.enum(['legacy', 'v2', 'shadow']).default('legacy'),
  SHOWWHERE_AI_BASE_URL: z.string().url().default('http://127.0.0.1:8790'),
  SHOWWHERE_AI_TOKEN: z.string().trim().min(16).optional(),
  SHOWWHERE_MODEL_ROOT: z.string().trim().min(1).default('C:\\ShowWhere_Models'),
  SHOWWHERE_BRAIN_DATA_ROOT: z.string().trim().min(1).default('data/brain-v2'),
  SHOWWHERE_LOCAL_AI_TIMEOUT_MS: integerFromEnvironment(500, 300_000).default(30_000),
  SHOWWHERE_LOCAL_AI_MAX_RETRIES: integerFromEnvironment(0, 3).default(1),
  SHOWWHERE_MEMORY_REUSE_THRESHOLD: z.coerce.number().min(0).max(1).default(0.92),
  SHOWWHERE_RERANK_THRESHOLD: z.coerce.number().min(0).max(1).default(0.62),
  SHOWWHERE_API_HOST: z.string().trim().min(1).default('127.0.0.1'),
  SHOWWHERE_API_PORT: integerFromEnvironment(1, 65_535).default(8787),
  SHOWWHERE_MAX_REQUEST_BYTES: integerFromEnvironment(1_024, 10_000_000).default(10_000_000),
  SHOWWHERE_DEBUG: booleanFromEnvironment.default(false),
  FEATHERLESS_API_KEY: z.string().trim().min(1).optional(),
  FEATHERLESS_BASE_URL: z.string().url().optional(),
  FEATHERLESS_GUIDE_MODEL: z.string().trim().min(1).optional(),
  FEATHERLESS_VISION_MODEL: z.string().trim().min(1).optional(),
  FEATHERLESS_VISION_MODELS: z.string().trim().min(1).optional(),
  FEATHERLESS_REQUEST_TIMEOUT_MS: integerFromEnvironment(500, 120_000).default(15_000),
  FEATHERLESS_MAX_RETRIES: integerFromEnvironment(0, 3).default(2),
  FEATHERLESS_RETRY_BASE_DELAY_MS: integerFromEnvironment(0, 10_000).default(250),
  FEATHERLESS_MAX_TOKENS: integerFromEnvironment(64, 4_096).default(128),
  FEATHERLESS_ENABLE_THINKING: booleanFromEnvironment.default(false),
}).superRefine((environment, context) => {
  if (environment.SHOWWHERE_AI_MODE !== 'featherless') return;

  for (const key of [
    'FEATHERLESS_API_KEY',
    'FEATHERLESS_BASE_URL',
    'FEATHERLESS_GUIDE_MODEL',
  ] as const) {
    if (!environment[key]) {
      context.addIssue({
        code: 'custom',
        path: [key],
        message: `${key} is required when SHOWWHERE_AI_MODE=featherless.`,
      });
    }
  }
});

export interface ApiConfig {
  aiMode: 'mock' | 'featherless';
  brainMode: 'legacy' | 'v2' | 'shadow';
  host: string;
  port: number;
  maxRequestBytes: number;
  debug: boolean;
  brainV2: {
    baseUrl: string;
    apiToken?: string;
    modelRoot: string;
    dataRoot: string;
    timeoutMs: number;
    maxRetries: number;
    memoryReuseThreshold: number;
    rerankThreshold: number;
  };
  featherless?: {
    apiKey: string;
    baseUrl: string;
    model: string;
    visionModels: string[];
    requestTimeoutMs: number;
    maxRetries: number;
    retryBaseDelayMs: number;
    maxTokens: number;
    enableThinking: boolean;
    debug: boolean;
  };
}

export function loadApiConfig(environment: NodeJS.ProcessEnv): ApiConfig {
  const parsed = environmentSchema.parse(environment);
  const config: ApiConfig = {
    aiMode: parsed.SHOWWHERE_AI_MODE,
    brainMode: parsed.SHOWWHERE_BRAIN_MODE,
    host: parsed.SHOWWHERE_API_HOST,
    port: parsed.SHOWWHERE_API_PORT,
    maxRequestBytes: parsed.SHOWWHERE_MAX_REQUEST_BYTES,
    debug: parsed.SHOWWHERE_DEBUG,
    brainV2: {
      baseUrl: parsed.SHOWWHERE_AI_BASE_URL,
      apiToken: parsed.SHOWWHERE_AI_TOKEN,
      modelRoot: parsed.SHOWWHERE_MODEL_ROOT,
      dataRoot: parsed.SHOWWHERE_BRAIN_DATA_ROOT,
      timeoutMs: parsed.SHOWWHERE_LOCAL_AI_TIMEOUT_MS,
      maxRetries: parsed.SHOWWHERE_LOCAL_AI_MAX_RETRIES,
      memoryReuseThreshold: parsed.SHOWWHERE_MEMORY_REUSE_THRESHOLD,
      rerankThreshold: parsed.SHOWWHERE_RERANK_THRESHOLD,
    },
  };

  if (parsed.SHOWWHERE_AI_MODE === 'featherless') {
    config.featherless = {
      apiKey: parsed.FEATHERLESS_API_KEY!,
      baseUrl: parsed.FEATHERLESS_BASE_URL!,
      model: parsed.FEATHERLESS_GUIDE_MODEL!,
      visionModels: (parsed.FEATHERLESS_VISION_MODELS
        ?? parsed.FEATHERLESS_VISION_MODEL
        ?? parsed.FEATHERLESS_GUIDE_MODEL!)
        .split(',')
        .map((model) => model.trim())
        .filter(Boolean),
      requestTimeoutMs: parsed.FEATHERLESS_REQUEST_TIMEOUT_MS,
      maxRetries: parsed.FEATHERLESS_MAX_RETRIES,
      retryBaseDelayMs: parsed.FEATHERLESS_RETRY_BASE_DELAY_MS,
      maxTokens: parsed.FEATHERLESS_MAX_TOKENS,
      enableThinking: parsed.FEATHERLESS_ENABLE_THINKING,
      debug: parsed.SHOWWHERE_DEBUG,
    };
  }

  return config;
}
