import type { GuideRequest } from '../../../src/contracts';

export const guideRequestFixture: GuideRequest = {
  session: {
    sessionId: 'session-1',
    originalUserMessage: 'Open settings',
    goal: 'Open settings',
    mode: 'guidance',
    status: 'waiting_for_ai',
    completedSteps: [],
    knownFacts: [],
    failureCount: 0,
  },
  context: {
    platform: 'windows',
    applicationName: 'SystemSettings',
    windowTitle: 'Settings',
  },
  candidates: [{
    id: 'candidate-settings',
    label: 'Settings',
    role: 'button',
    enabled: true,
    visible: true,
    clickable: true,
    bounds: { x: 10, y: 10, width: 120, height: 40 },
  }],
};
