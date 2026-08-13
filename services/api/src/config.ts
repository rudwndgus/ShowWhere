import { z } from 'zod';

const integerFromEnvironment = (minimum: number, maximum: number) => z.preprocess(
  (value) => value === undefined || value === '' ? undefined : Number(value),
  z.number().int().min(minimum).max(maximum),
);

const booleanFromEnvironment = z.preprocess(
  (value) => value === undefined || value === '' ? undefined : String(value).toLowerCase() === 'true',
  z.boolean(),
);

const optionalSecretFromEnvironment = (minimum: number) => z.preprocess(
  (value) => value === undefined || String(value).trim() === '' ? undefined : value,
  z.string().trim().min(minimum).optional(),
);

const environmentSchema = z.object({
  OPENAI_API_KEY: z.string().trim().min(1),
  OPENAI_FAST_MODEL: z.string().trim().min(1).default('gpt-5.6-luna'),
  OPENAI_MODEL: z.string().trim().min(1).default('gpt-5.6-terra'),
  OPENAI_STRONG_MODEL: z.string().trim().min(1).default('gpt-5.6-sol'),
  OPENAI_BASE_URL: z.string().url().default('https://api.openai.com/v1'),
  OPENAI_REQUEST_TIMEOUT_MS: integerFromEnvironment(1_000, 120_000).default(30_000),
  OPENAI_MAX_RETRIES: integerFromEnvironment(0, 3).default(1),
  HF_TOKEN: optionalSecretFromEnvironment(1),
  HF_EMBEDDING_MODEL: z.string().trim().min(1).default('intfloat/multilingual-e5-large'),
  HF_BASE_URL: z.string().url().default('https://router.huggingface.co/hf-inference/models'),
  HF_REQUEST_TIMEOUT_MS: integerFromEnvironment(500, 30_000).default(4_000),
  HF_MIN_SCORE: z.preprocess(
    (value) => value === undefined || value === '' ? undefined : Number(value),
    z.number().min(0).max(1),
  ).default(0.68),
  HF_MIN_MARGIN: z.preprocess(
    (value) => value === undefined || value === '' ? undefined : Number(value),
    z.number().min(0).max(1),
  ).default(0.08),
  SHOWWHERE_API_HOST: z.string().trim().min(1).default('127.0.0.1'),
  SHOWWHERE_API_PORT: integerFromEnvironment(1, 65_535).default(8787),
  PORT: integerFromEnvironment(1, 65_535).optional(),
  SHOWWHERE_MAX_REQUEST_BYTES: integerFromEnvironment(1_024, 20_000_000).default(12_000_000),
  SHOWWHERE_WEB_KNOWLEDGE_DIR: z.string().trim().min(1).default('knowledge/web'),
  SHOWWHERE_DEBUG: booleanFromEnvironment.default(false),
  SHOWWHERE_CLIENT_TOKEN: optionalSecretFromEnvironment(24),
  SHOWWHERE_RATE_LIMIT_WINDOW_MS: integerFromEnvironment(1_000, 3_600_000).default(60_000),
  SHOWWHERE_RATE_LIMIT_MAX_REQUESTS: integerFromEnvironment(1, 1_000).default(20),
  SHOWWHERE_TRUST_PROXY: booleanFromEnvironment.default(false),
  SHOWWHERE_CENTRAL_DATA_DIR: z.string().trim().min(1).default('data/central'),
  SHOWWHERE_DEVELOPER_TOKEN: optionalSecretFromEnvironment(24),
  SHOWWHERE_ADMIN_TOKEN: optionalSecretFromEnvironment(24),
  SHOWWHERE_PUBLIC_BASE_URL: z.string().url().optional(),
  SHOWWHERE_PAIRING_TTL_SECONDS: integerFromEnvironment(30, 300).default(60),
});

export interface ApiConfig {
  host: string;
  port: number;
  maxRequestBytes: number;
  webKnowledgeDirectory: string;
  debug: boolean;
  security: {
    clientToken?: string;
    developerToken?: string;
    adminToken?: string;
    rateLimitWindowMs: number;
    rateLimitMaxRequests: number;
    trustProxy: boolean;
  };
  centralDataDirectory: string;
  publicBaseUrl?: string;
  pairingTtlSeconds: number;
  openai: {
    apiKey: string;
    fastModel: string;
    model: string;
    strongModel: string;
    baseUrl: string;
    requestTimeoutMs: number;
    maxRetries: number;
  };
  huggingFace?: {
    token: string;
    model: string;
    baseUrl: string;
    requestTimeoutMs: number;
    minScore: number;
    minMargin: number;
  };
}

export function loadApiConfig(environment: NodeJS.ProcessEnv): ApiConfig {
  const parsed = environmentSchema.parse(environment);
  return {
    host: parsed.PORT === undefined ? parsed.SHOWWHERE_API_HOST : '0.0.0.0',
    port: parsed.PORT ?? parsed.SHOWWHERE_API_PORT,
    maxRequestBytes: parsed.SHOWWHERE_MAX_REQUEST_BYTES,
    webKnowledgeDirectory: parsed.SHOWWHERE_WEB_KNOWLEDGE_DIR,
    debug: parsed.SHOWWHERE_DEBUG,
    security: {
      clientToken: parsed.SHOWWHERE_CLIENT_TOKEN,
      developerToken: parsed.SHOWWHERE_DEVELOPER_TOKEN,
      adminToken: parsed.SHOWWHERE_ADMIN_TOKEN,
      rateLimitWindowMs: parsed.SHOWWHERE_RATE_LIMIT_WINDOW_MS,
      rateLimitMaxRequests: parsed.SHOWWHERE_RATE_LIMIT_MAX_REQUESTS,
      trustProxy: parsed.SHOWWHERE_TRUST_PROXY,
    },
    centralDataDirectory: parsed.SHOWWHERE_CENTRAL_DATA_DIR,
    publicBaseUrl: parsed.SHOWWHERE_PUBLIC_BASE_URL?.replace(/\/$/u, ''),
    pairingTtlSeconds: parsed.SHOWWHERE_PAIRING_TTL_SECONDS,
    openai: {
      apiKey: parsed.OPENAI_API_KEY,
      fastModel: parsed.OPENAI_FAST_MODEL,
      model: parsed.OPENAI_MODEL,
      strongModel: parsed.OPENAI_STRONG_MODEL,
      baseUrl: parsed.OPENAI_BASE_URL.replace(/\/$/u, ''),
      requestTimeoutMs: parsed.OPENAI_REQUEST_TIMEOUT_MS,
      maxRetries: parsed.OPENAI_MAX_RETRIES,
    },
    huggingFace: parsed.HF_TOKEN ? {
      token: parsed.HF_TOKEN,
      model: parsed.HF_EMBEDDING_MODEL,
      baseUrl: parsed.HF_BASE_URL.replace(/\/$/u, ''),
      requestTimeoutMs: parsed.HF_REQUEST_TIMEOUT_MS,
      minScore: parsed.HF_MIN_SCORE,
      minMargin: parsed.HF_MIN_MARGIN,
    } : undefined,
  };
}
