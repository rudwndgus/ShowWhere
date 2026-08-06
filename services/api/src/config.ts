import { z } from 'zod';

const integerFromEnvironment = (minimum: number, maximum: number) => z.preprocess(
  (value) => value === undefined || value === '' ? undefined : Number(value),
  z.number().int().min(minimum).max(maximum),
);

const environmentSchema = z.object({
  SHOWWHERE_AI_MODE: z.enum(['mock', 'featherless']).default('mock'),
  SHOWWHERE_API_HOST: z.string().trim().min(1).default('127.0.0.1'),
  SHOWWHERE_API_PORT: integerFromEnvironment(1, 65_535).default(8787),
  SHOWWHERE_ALLOWED_ORIGINS: z.string().default(''),
  SHOWWHERE_MAX_REQUEST_BYTES: integerFromEnvironment(1_024, 10_000_000).default(1_000_000),
  FEATHERLESS_API_KEY: z.string().trim().min(1).optional(),
  FEATHERLESS_BASE_URL: z.string().url().optional(),
  FEATHERLESS_GUIDE_MODEL: z.string().trim().min(1).optional(),
  FEATHERLESS_REQUEST_TIMEOUT_MS: integerFromEnvironment(500, 120_000).default(15_000),
  FEATHERLESS_MAX_RETRIES: integerFromEnvironment(0, 3).default(2),
  FEATHERLESS_RETRY_BASE_DELAY_MS: integerFromEnvironment(0, 10_000).default(250),
  FEATHERLESS_MAX_TOKENS: integerFromEnvironment(64, 4_096).default(800),
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
  host: string;
  port: number;
  allowedOrigins: ReadonlySet<string>;
  maxRequestBytes: number;
  featherless?: {
    apiKey: string;
    baseUrl: string;
    model: string;
    requestTimeoutMs: number;
    maxRetries: number;
    retryBaseDelayMs: number;
    maxTokens: number;
  };
}

export function loadApiConfig(environment: NodeJS.ProcessEnv): ApiConfig {
  const parsed = environmentSchema.parse(environment);
  const allowedOrigins = new Set(
    parsed.SHOWWHERE_ALLOWED_ORIGINS
      .split(',')
      .map((origin) => origin.trim())
      .filter(Boolean),
  );

  const config: ApiConfig = {
    aiMode: parsed.SHOWWHERE_AI_MODE,
    host: parsed.SHOWWHERE_API_HOST,
    port: parsed.SHOWWHERE_API_PORT,
    allowedOrigins,
    maxRequestBytes: parsed.SHOWWHERE_MAX_REQUEST_BYTES,
  };

  if (parsed.SHOWWHERE_AI_MODE === 'featherless') {
    config.featherless = {
      apiKey: parsed.FEATHERLESS_API_KEY!,
      baseUrl: parsed.FEATHERLESS_BASE_URL!,
      model: parsed.FEATHERLESS_GUIDE_MODEL!,
      requestTimeoutMs: parsed.FEATHERLESS_REQUEST_TIMEOUT_MS,
      maxRetries: parsed.FEATHERLESS_MAX_RETRIES,
      retryBaseDelayMs: parsed.FEATHERLESS_RETRY_BASE_DELAY_MS,
      maxTokens: parsed.FEATHERLESS_MAX_TOKENS,
    };
  }

  return config;
}
