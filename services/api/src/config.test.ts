import { describe, expect, it } from 'vitest';
import { loadApiConfig } from './config';

describe('API environment configuration', () => {
  it('starts in mock mode without Featherless credentials', () => {
    const config = loadApiConfig({ SHOWWHERE_AI_MODE: 'mock' });
    expect(config.aiMode).toBe('mock');
    expect(config.featherless).toBeUndefined();
  });

  it('requires every server-only Featherless setting in live mode', () => {
    expect(() => loadApiConfig({ SHOWWHERE_AI_MODE: 'featherless' })).toThrow();
  });

  it('normalizes an exact CORS allowlist', () => {
    const config = loadApiConfig({
      SHOWWHERE_AI_MODE: 'featherless',
      FEATHERLESS_API_KEY: 'server-secret',
      FEATHERLESS_BASE_URL: 'https://api.featherless.ai/v1',
      FEATHERLESS_GUIDE_MODEL: 'configurable-model',
      SHOWWHERE_ALLOWED_ORIGINS: 'chrome-extension://first, https://local.example ',
    });
    expect([...config.allowedOrigins]).toEqual([
      'chrome-extension://first',
      'https://local.example',
    ]);
  });
});
