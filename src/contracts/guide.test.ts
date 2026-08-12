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
        platform: 'windows',
        applicationName: 'SystemSettings',
        windowTitle: 'Settings',
      },
      candidates: [candidate],
    });
    expect(parsed.candidates[0].bounds.width).toBe(100);
  });

  it('accepts complex Windows screens without exceeding the shared candidate limit', () => {
    const base = {
      session,
      context: { platform: 'windows' as const, applicationName: 'chrome' },
    };
    expect(GuideRequestSchema.safeParse({
      ...base,
      candidates: Array.from({ length: 220 }, (_, index) => ({ ...candidate, id: `candidate-${index}` })),
    }).success).toBe(true);
    expect(GuideRequestSchema.safeParse({
      ...base,
      candidates: Array.from({ length: 251 }, (_, index) => ({ ...candidate, id: `candidate-${index}` })),
    }).success).toBe(false);
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

  it('requires normalized visual bounds for visual highlight decisions', () => {
    expect(GuideDecisionSchema.safeParse({
      status: 'in_progress',
      action: 'highlight_visual',
      message: '설정을 누르세요.',
      confidence: 0.9,
      visualTarget: { x: 0.5, y: 0.2, width: 0.08, height: 0.06, label: '설정' },
    }).success).toBe(true);
    expect(GuideDecisionSchema.safeParse({
      status: 'in_progress',
      action: 'highlight_visual',
      message: '설정을 누르세요.',
      confidence: 0.9,
      visualTarget: { x: 1.1, y: 0.2, width: 0.08, height: 0.06, label: '설정' },
    }).success).toBe(false);
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

  it('validates unique ask-user alternatives and rejects alternatives for other actions', () => {
    expect(GuideDecisionSchema.safeParse({
      status: 'needs_clarification',
      action: 'ask_user',
      message: '어느 항목인가요?',
      confidence: 0.4,
      alternativeTargetIds: ['candidate-1', 'candidate-2'],
    }).success).toBe(true);
    expect(GuideDecisionSchema.safeParse({
      status: 'in_progress',
      action: 'highlight',
      targetId: 'candidate-1',
      message: '여기를 누르세요.',
      confidence: 0.9,
      alternativeTargetIds: ['candidate-1', 'candidate-2'],
    }).success).toBe(false);
  });
});
