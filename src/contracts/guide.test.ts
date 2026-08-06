import { describe, expect, it } from 'vitest';
import {
  GuideDecisionSchema,
  GuideRequestSchema,
  TaskSessionSchema,
  UiCandidateSchema,
} from './guide';

const session = {
  sessionId: 'session-1',
  originalUserMessage: '로그인하고 싶어요',
  goal: '로그인',
  mode: 'guidance' as const,
  status: 'waiting_for_ai' as const,
  completedSteps: [],
  knownFacts: [],
  failureCount: 0,
};

const candidate = {
  id: 'candidate-1',
  label: '로그인',
  role: 'button',
  enabled: true,
  visible: true,
  clickable: true,
  bounds: { x: 10, y: 20, width: 100, height: 40 },
};

describe('guide contracts', () => {
  it('validates a normalized guide request', () => {
    const parsed = GuideRequestSchema.parse({
      session,
      context: {
        platform: 'browser',
        applicationName: 'example.com',
        url: 'https://example.com/login',
      },
      candidates: [candidate],
    });
    expect(parsed.candidates[0].bounds.width).toBe(100);
  });

  it('requires targetId for highlight decisions', () => {
    const parsed = GuideDecisionSchema.safeParse({
      status: 'in_progress',
      action: 'highlight',
      message: '여기를 눌러보세요.',
      confidence: 0.9,
    });
    expect(parsed.success).toBe(false);
  });

  it('rejects confidence values outside zero and one', () => {
    const parsed = GuideDecisionSchema.safeParse({
      status: 'in_progress',
      action: 'highlight',
      targetId: 'candidate-1',
      message: '여기를 눌러보세요.',
      confidence: 1.1,
    });
    expect(parsed.success).toBe(false);
  });

  it('rejects invalid candidate bounds and session failure counts', () => {
    expect(UiCandidateSchema.safeParse({
      ...candidate,
      bounds: { ...candidate.bounds, width: -1 },
    }).success).toBe(false);
    expect(TaskSessionSchema.safeParse({ ...session, failureCount: -1 }).success).toBe(false);
  });
});
