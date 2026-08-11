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
});
