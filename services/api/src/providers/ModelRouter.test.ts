import { describe, expect, it } from 'vitest';
import { ModelRouter } from './ModelRouter';

describe('ModelRouter', () => {
  const router = new ModelRouter({
    guideModel: 'deepseek',
    guideFallbackModel: 'qwen',
    reasoningModel: 'reasoner',
    visionModels: ['vision-a', 'vision-b'],
    learningGeneratorModel: 'generator',
    learningJudgeModels: ['judge-a', 'judge-b'],
  });

  it('routes primary and fallback guide models in deterministic order', () => {
    expect(router.guide()).toEqual(['deepseek', 'qwen']);
  });

  it('keeps vision and learning roles separate from normal guidance', () => {
    expect(router.vision()).toEqual(['vision-a', 'vision-b']);
    expect(router.learningGenerator()).toEqual(['generator', 'qwen', 'deepseek']);
    expect(router.learningJudge()).toEqual(['judge-a', 'judge-b']);
  });

  it('does not duplicate a model used by more than one fallback position', () => {
    const duplicate = new ModelRouter({ guideModel: 'same', guideFallbackModel: 'same', visionModels: [] });
    expect(duplicate.guide()).toEqual(['same']);
  });
});
