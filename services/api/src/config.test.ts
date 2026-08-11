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

  it('parses all model routes without requiring optional models', () => {
    const config = loadApiConfig({
      SHOWWHERE_AI_MODE: 'featherless',
      FEATHERLESS_API_KEY: 'server-secret',
      FEATHERLESS_BASE_URL: 'https://api.example/v1',
      FEATHERLESS_GUIDE_MODEL: 'primary',
      FEATHERLESS_GUIDE_FALLBACK_MODEL: 'fallback',
      FEATHERLESS_VISION_MODELS: 'vision-a,vision-b',
      FEATHERLESS_REASONING_MODEL: 'reasoner',
      LEARNING_GENERATOR_MODEL: 'generator',
      LEARNING_JUDGE_A_MODEL: 'judge-a',
    });

    expect(config.featherless).toMatchObject({
      model: 'primary',
      guideFallbackModel: 'fallback',
      visionModels: ['vision-a', 'vision-b'],
      reasoningModel: 'reasoner',
      learningGeneratorModel: 'generator',
      learningJudgeModels: ['judge-a'],
    });
  });
});
