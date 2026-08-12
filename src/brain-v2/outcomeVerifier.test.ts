import { describe, expect, it } from 'vitest';
import type { LearningEventV2 } from './contracts';
import { EvidenceOutcomeVerifier } from './outcomeVerifier';
import type { GuideRequest } from '../contracts';

const event = {
  schemaVersion: 'showwhere-learning-event-v2', eventId: 'e6e232e7-9f6c-47f4-b585-c05b5a44d1cb', sessionId: 's',
  timestamp: new Date().toISOString(), source: 'live', authority: 'raw', userQuestion: 'open settings',
  visibleConcepts: ['Settings', 'Power'], candidates: [], retrievedMemoryIds: [], retrievalScores: [],
  rerankerScores: [], visionUsed: false, expectedEvidence: ['Bluetooth & devices'], retryOccurred: false,
  fallbackUsed: 'brain', finalOutcome: 'pending', totalLatencyMs: 1, dataQualityStatus: 'scrubbed',
} satisfies LearningEventV2;

function request(labels: string[]): GuideRequest {
  return {
    session: { sessionId: 's', originalUserMessage: 'open settings', mode: 'guidance', status: 'observing', completedSteps: [], knownFacts: [], failureCount: 0 },
    context: { platform: 'windows', applicationName: 'Settings', windowTitle: 'Settings' },
    candidates: labels.map((label, index) => ({ id: String(index), label, role: 'button', enabled: true, visible: true, clickable: true, bounds: { x: 0, y: 0, width: 1, height: 1 } })),
  };
}

describe('EvidenceOutcomeVerifier', () => {
  it('promotes a real transition only when expected evidence appears', async () => {
    const outcome = await new EvidenceOutcomeVerifier().verify(event, request(['Bluetooth & devices', 'System']));
    expect(outcome).toMatchObject({ transitionMatched: true, finalOutcome: 'success', authority: 'verified_real' });
  });

  it('does not turn an unchanged or unrelated screen into training truth', async () => {
    const outcome = await new EvidenceOutcomeVerifier().verify(event, request(['Settings', 'Power']));
    expect(outcome).toMatchObject({ transitionMatched: false, finalOutcome: 'failure', authority: 'raw' });
  });
});

