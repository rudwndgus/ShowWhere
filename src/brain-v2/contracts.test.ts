import { describe, expect, it } from 'vitest';
import { LearningEventV2Schema, SemanticDecisionSchema } from './contracts';
import { containsSensitiveMaterial, scrubForLearning } from './scrubber';

describe('Brain v2 contracts', () => {
  it('rejects unstructured or extra semantic output', () => {
    expect(SemanticDecisionSchema.safeParse({ answer: 'click settings' }).success).toBe(false);
    expect(SemanticDecisionSchema.safeParse({
      intent: 'open_settings', taskId: 'windows.settings.open', stateId: 'windows.start',
      nextSemanticAction: 'navigate', targetConcept: 'windows.settings',
      expectedNextState: 'windows.settings.home', expectedEvidence: ['Settings'], confidence: 0.94,
      needsVision: false, reasoningMode: 'off', invented: true,
    }).success).toBe(false);
  });

  it('scrubs secrets before an event can be stored', () => {
    const source = { question: 'api_key=secret-value-1234 and user@example.com' };
    const scrubbed = scrubForLearning(source);
    expect(scrubbed.question).not.toContain('secret-value-1234');
    expect(scrubbed.question).not.toContain('user@example.com');
    expect(containsSensitiveMaterial(scrubbed)).toBe(false);
  });

  it('requires the complete learning-event envelope', () => {
    expect(LearningEventV2Schema.safeParse({ schemaVersion: 'showwhere-learning-event-v2' }).success).toBe(false);
  });
});

