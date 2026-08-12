import { z } from 'zod';

const integerFromEnvironment = (minimum: number, maximum: number) => z.preprocess(
  (value) => value === undefined || value === '' ? undefined : Number(value),
  z.number().int().min(minimum).max(maximum),
);

const booleanFromEnvironment = z.preprocess(
  (value) => value === undefined || value === '' ? undefined : String(value).toLowerCase() === 'true',
  z.boolean(),
);

const environmentSchema = z.object({
  OPENAI_API_KEY: z.string().trim().min(1),
  OPENAI_MODEL: z.string().trim().min(1).default('gpt-5.6'),
  OPENAI_BASE_URL: z.string().url().default('https://api.openai.com/v1'),
  OPENAI_REQUEST_TIMEOUT_MS: integerFromEnvironment(1_000, 120_000).default(30_000),
  OPENAI_MAX_RETRIES: integerFromEnvironment(0, 3).default(1),
  SHOWWHERE_API_HOST: z.string().trim().min(1).default('127.0.0.1'),
  SHOWWHERE_API_PORT: integerFromEnvironment(1, 65_535).default(8787),
  SHOWWHERE_MAX_REQUEST_BYTES: integerFromEnvironment(1_024, 20_000_000).default(12_000_000),
  SHOWWHERE_DEBUG: booleanFromEnvironment.default(false),
});

export interface ApiConfig {
  host: string;
  port: number;
  maxRequestBytes: number;
  debug: boolean;
  openai: {
    apiKey: string;
    model: string;
    baseUrl: string;
    requestTimeoutMs: number;
    maxRetries: number;
  };
}

export function loadApiConfig(environment: NodeJS.ProcessEnv): ApiConfig {
  const parsed = environmentSchema.parse(environment);
  return {
    host: parsed.SHOWWHERE_API_HOST,
    port: parsed.SHOWWHERE_API_PORT,
    maxRequestBytes: parsed.SHOWWHERE_MAX_REQUEST_BYTES,
    debug: parsed.SHOWWHERE_DEBUG,
    openai: {
      apiKey: parsed.OPENAI_API_KEY,
      model: parsed.OPENAI_MODEL,
      baseUrl: parsed.OPENAI_BASE_URL.replace(/\/$/u, ''),
      requestTimeoutMs: parsed.OPENAI_REQUEST_TIMEOUT_MS,
      maxRetries: parsed.OPENAI_MAX_RETRIES,
    },
  };
}
