import { describe, expect, it, vi } from 'vitest';
import type { GuideDecision } from '../../../../src/contracts';
import type { LearningEventV2 } from '../../../../src/brain-v2/contracts';
import type { LearningEventRecorder, SemanticRetriever } from '../../../../src/brain-v2/interfaces';
import { guideRequestFixture } from '../testFixtures';
import { BrainV2Provider } from './BrainV2Provider';

function dependencies(overrides: Record<string, unknown> = {}) {
  const events: LearningEventV2[] = [];
  const recorder: LearningEventRecorder = {
    append: vi.fn(async (event) => { events.push(event); }),
    appendTrajectory: vi.fn(async () => undefined),
    updateOutcome: vi.fn(async () => undefined),
  };
  const memory: SemanticRetriever = {
    search: vi.fn(async () => []),
    remember: vi.fn(async () => undefined),
  };
  const legacyDecision: GuideDecision = { status: 'blocked', action: 'explain', message: 'legacy', confidence: 1 };
  const options = {
    mode: 'v2' as const, baseUrl: 'http://127.0.0.1:8790', timeoutMs: 100,
    dataRoot: 'data/test', memoryReuseThreshold: 0.92, rerankThreshold: 0.62,
    legacy: { decideNextAction: vi.fn(async () => legacyDecision) }, memory, recorder,
    outcomeVerifier: { verify: vi.fn(async () => ({ transitionMatched: true, finalOutcome: 'success' as const })) },
    reasoner: { decide: vi.fn(async () => ({
      model: 'brain', latencyMs: 3,
      decision: {
        intent: 'open_settings', taskId: 'windows.settings.open', stateId: 'windows.start',
        nextSemanticAction: 'navigate' as const, targetConcept: 'Settings',
        expectedNextState: 'windows.settings.home', expectedEvidence: ['Settings'],
        confidence: 0.95, needsVision: false, reasoningMode: 'off' as const,
      },
    })) },
    reranker: { rerank: vi.fn(async () => ({
      scores: [{ candidateId: 'candidate-settings', score: 0.98 }], selectedId: 'candidate-settings',
      confidence: 0.98, margin: 0.8, latencyMs: 2, model: 'reranker',
    })) },
    grounder: { ground: vi.fn(async () => ({ point: { x: 0.5, y: 0.5 }, label: 'Settings', confidence: 0.9, latencyMs: 4, model: 'grounder' })) },
    ...overrides,
  };
  return { options, events, legacyDecision };
}

describe('BrainV2Provider', () => {
  it('uses semantic reasoning and reranking to select an existing UIA target', async () => {
    const { options, events } = dependencies();
    const decision = await new BrainV2Provider(options).decideNextAction(guideRequestFixture) as GuideDecision;
    expect(decision).toMatchObject({ action: 'highlight', targetId: 'candidate-settings' });
    expect(events).toHaveLength(1);
    expect(events[0].rerankerSelectedId).toBe('candidate-settings');
  });

  it('uses visual grounding only after candidate confidence is insufficient', async () => {
    const reranker = { rerank: vi.fn(async () => ({ scores: [], confidence: 0.1, margin: 0, latencyMs: 1, model: 'reranker' })) };
    const grounder = { ground: vi.fn(async () => ({ point: { x: 0.25, y: 0.4 }, label: 'Settings', confidence: 0.9, latencyMs: 4, model: 'grounder' })) };
    const { options } = dependencies({ reranker, grounder });
    const request = { ...guideRequestFixture, screenshot: 'aGVsbG8=', screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 } };
    const decision = await new BrainV2Provider(options).decideNextAction(request) as GuideDecision;
    expect(decision.action).toBe('highlight_visual');
    expect(grounder.ground).toHaveBeenCalledOnce();
  });

  it('falls back to legacy when a specialist fails', async () => {
    const { options, legacyDecision } = dependencies({ reasoner: { decide: vi.fn(async () => { throw new Error('offline'); }) } });
    await expect(new BrainV2Provider(options).decideNextAction(guideRequestFixture)).resolves.toEqual(legacyDecision);
  });
});

