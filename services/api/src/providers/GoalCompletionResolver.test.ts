import { describe, expect, it } from 'vitest';
import type { GuideRequest } from '../../../../src/contracts';
import { guideRequestFixture } from '../testFixtures';
import { resolveGoalCompletion } from './GoalCompletionResolver';

describe('goal completion resolver', () => {
  it('stops after an Amazon login click reaches the sign-in form', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      session: {
        ...guideRequestFixture.session,
        originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?',
        completedSteps: ["웹사이트에서 'Hello, sign in Account & Lists' 항목을 눌러주세요."],
      },
      context: { ...guideRequestFixture.context, applicationName: 'chrome', url: 'https://www.amazon.com/ap/signin' },
      candidates: [{ ...guideRequestFixture.candidates[0], label: 'Email or mobile phone number', role: 'edit' }],
    };
    expect(resolveGoalCompletion(request)?.status).toBe('completed');
  });

  it('does not stop merely because a related login shortcut is visible', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      session: {
        ...guideRequestFixture.session,
        originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?',
        completedSteps: ["'Delivering to Lyndhurst'를 눌러주세요."],
      },
      context: { ...guideRequestFixture.context, applicationName: 'chrome', url: 'https://www.amazon.com/' },
      candidates: [{ ...guideRequestFixture.candidates[0], label: 'Sign in to see your addresses' }],
    };
    expect(resolveGoalCompletion(request)).toBeUndefined();
  });

  it('stops a Windows route only after the final control was clicked and remains visible', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      session: {
        ...guideRequestFixture.session,
        originalUserMessage: '프린터 설정 어디야?', goal: '프린터 설정 어디야?',
        completedSteps: ["Windows에서 '프린터 및 스캐너' 항목을 눌러주세요."],
      },
      candidates: [{ ...guideRequestFixture.candidates[0], label: '프린터 및 스캐너' }],
    };
    expect(resolveGoalCompletion(request)?.status).toBe('completed');
  });
});
