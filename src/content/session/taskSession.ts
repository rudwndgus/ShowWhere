import type { TaskSession } from '../../contracts';

export type TaskSessionEvent =
  | { type: 'observation_started' }
  | { type: 'ai_requested' }
  | { type: 'guidance_ready'; message: string; expectedChange?: string }
  | { type: 'waiting_for_user'; message?: string }
  | { type: 'step_completed'; fact?: string }
  | { type: 'completed'; message?: string }
  | { type: 'failed'; message?: string }
  | { type: 'blocked'; message?: string }
  | { type: 'cancelled' };

export function inferTaskMode(message: string): TaskSession['mode'] {
  return /(안\s*돼|안됨|오류|에러|문제|못\s|보이지|실패|stuck|error|problem|not showing)/iu.test(message)
    ? 'troubleshooting'
    : 'guidance';
}

export function createTaskSession(
  originalUserMessage: string,
  createId: () => string = createSessionId,
): TaskSession {
  const cleaned = originalUserMessage.trim();
  return {
    sessionId: createId(),
    originalUserMessage: cleaned,
    goal: cleaned,
    mode: inferTaskMode(cleaned),
    status: 'idle',
    completedSteps: [],
    knownFacts: [],
    failureCount: 0,
  };
}

function createSessionId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `session-${Date.now()}-${Math.random().toString(36).slice(2, 12)}`;
}

export function transitionTaskSession(
  session: TaskSession,
  event: TaskSessionEvent,
): TaskSession {
  switch (event.type) {
    case 'observation_started':
      return { ...session, status: 'observing' };
    case 'ai_requested':
      return { ...session, status: 'waiting_for_ai' };
    case 'guidance_ready':
      return {
        ...session,
        status: 'guiding',
        currentStep: event.message,
        expectedChange: event.expectedChange,
      };
    case 'waiting_for_user':
      return {
        ...session,
        status: 'waiting_for_user',
        currentStep: event.message ?? session.currentStep,
      };
    case 'step_completed': {
      const completedSteps = session.currentStep && !session.completedSteps.includes(session.currentStep)
        ? [...session.completedSteps, session.currentStep]
        : session.completedSteps;
      const knownFacts = event.fact && !session.knownFacts.includes(event.fact)
        ? [...session.knownFacts, event.fact]
        : session.knownFacts;
      return {
        ...session,
        status: 'observing',
        currentStep: undefined,
        expectedChange: undefined,
        completedSteps,
        knownFacts,
      };
    }
    case 'completed':
      return {
        ...session,
        status: 'completed',
        currentStep: event.message ?? session.currentStep,
        expectedChange: undefined,
      };
    case 'failed':
      return {
        ...session,
        status: 'observing',
        currentStep: event.message ?? session.currentStep,
        failureCount: session.failureCount + 1,
      };
    case 'blocked':
      return {
        ...session,
        status: 'blocked',
        currentStep: event.message ?? session.currentStep,
        expectedChange: undefined,
        failureCount: session.failureCount + 1,
      };
    case 'cancelled':
      return { ...session, status: 'cancelled', expectedChange: undefined };
  }
}
