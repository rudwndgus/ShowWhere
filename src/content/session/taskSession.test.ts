import { describe, expect, it } from 'vitest';
import { createTaskSession, transitionTaskSession } from './taskSession';

describe('task session transitions', () => {
  it('tracks guidance and completed steps without declaring the whole task complete', () => {
    const created = createTaskSession('로그인하고 싶어요', () => 'session-1');
    const observing = transitionTaskSession(created, { type: 'observation_started' });
    const waiting = transitionTaskSession(observing, { type: 'ai_requested' });
    const guiding = transitionTaskSession(waiting, {
      type: 'guidance_ready',
      message: '로그인 버튼을 눌러보세요.',
      expectedChange: '로그인 화면이 열립니다.',
    });
    const afterClick = transitionTaskSession(guiding, { type: 'step_completed' });
    expect(guiding.status).toBe('guiding');
    expect(afterClick.status).toBe('observing');
    expect(afterClick.completedSteps).toEqual(['로그인 버튼을 눌러보세요.']);
    expect(afterClick.expectedChange).toBeUndefined();
  });
});
