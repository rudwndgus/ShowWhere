import { describe, expect, test } from 'vitest';
import { ScenarioSchema, SeedExampleSchema } from './contracts';
import { validateLearningData } from './pipeline';

describe('learning contracts', () => {
  test('rejects a seed whose highlighted target is absent', () => {
    const result = SeedExampleSchema.safeParse({
      id: 'bad_seed', version: '1', category: 'test', goal: 'Test',
      exampleUserMessages: ['one', 'two'],
      initialContext: { platform: 'windows', application: 'Test', screenState: 'Test' },
      decisionRules: ['one', 'two'], possibleCandidates: ['present'],
      correctNextAction: { target: 'missing', instruction: 'Click it', mode: 'highlight' },
      expectedChange: 'Change', successCondition: 'Done', riskLevel: 'low',
      requiresConfirmation: false, tags: [],
    });
    expect(result.success).toBe(false);
  });

  test('rejects hallucinated scenario targets', () => {
    const result = ScenarioSchema.safeParse({
      schemaVersion: 'showwhere-scenario-v1', scenarioId: 's1', seedId: 'seed', goal: 'Goal', userMessage: 'Help',
      context: { platform: 'windows', application: 'Test', screenState: 'Test', locale: 'ko-KR', previousSteps: [] },
      candidates: [{ id: 'real', label: 'Real', role: 'button', enabled: true, visible: true }],
      proposedAction: 'highlight', proposedCorrectTargetId: 'invented', instruction: 'Click', expectedChange: 'Change',
      successCondition: 'Done', difficulty: 'easy', ambiguity: 0, riskLevel: 'low', requiresConfirmation: false,
      generatorModel: 'test', generationTimestamp: new Date().toISOString(), provenance: 'synthetic',
    });
    expect(result.success).toBe(false);
  });

  test('validates the checked-in seed library and benchmark', async () => {
    await expect(validateLearningData()).resolves.toEqual({ seeds: 24, benchmark: 8 });
  });
});
