import { describe, expect, it } from 'vitest';
import type { GuideRequest } from '../contracts';
import type { AiProvider } from './AiProvider';
import { GUIDE_API_PATH, handleGuideApiRequest } from './handleGuideApiRequest';

const request: GuideRequest = {
  session: {
    sessionId: 'session-1',
    originalUserMessage: '로그인하고 싶어요',
    goal: '로그인',
    mode: 'guidance',
    status: 'waiting_for_ai',
    completedSteps: [],
    knownFacts: [],
    failureCount: 0,
  },
  context: {
    platform: 'browser',
    applicationName: 'example.com',
    url: 'https://example.com/login',
  },
  candidates: [{
    id: 'candidate-1',
    label: '로그인',
    role: 'button',
    enabled: true,
    visible: true,
    clickable: true,
    bounds: { x: 10, y: 20, width: 100, height: 40 },
    attributes: { localScore: 150, localRank: 1 },
  }],
};

describe('mock /api/guide', () => {
  it('returns a deterministic known target', async () => {
    const response = await handleGuideApiRequest(GUIDE_API_PATH, request);
    expect(response.status).toBe(200);
    expect(response.decision.action).toBe('highlight');
    expect(response.decision.targetId).toBe('candidate-1');
  });

  it('selects a deterministic Windows UI Automation candidate in mock mode', async () => {
    const windowsRequest: GuideRequest = {
      ...request,
      context: {
        platform: 'windows',
        applicationName: 'notepad',
        windowTitle: 'Untitled - Notepad',
        locale: 'en-US',
      },
      candidates: [
        { ...request.candidates[0], id: 'windows-first', attributes: { automationId: 'FileButton' } },
        { ...request.candidates[0], id: 'windows-second', attributes: { automationId: 'EditArea' } },
      ],
    };

    const response = await handleGuideApiRequest(GUIDE_API_PATH, windowsRequest);

    expect(response.status).toBe(200);
    expect(response.decision.action).toBe('highlight');
    expect(response.decision.targetId).toBe('windows-first');
  });

  it('rejects a target ID that was not supplied by the platform', async () => {
    const provider: AiProvider = {
      async decideNextAction() {
        return {
          status: 'in_progress',
          action: 'highlight',
          targetId: 'invented-coordinate-target',
          message: '여기를 눌러보세요.',
          confidence: 0.99,
        };
      },
    };
    const response = await handleGuideApiRequest(GUIDE_API_PATH, request, provider);
    expect(response.status).toBe(422);
    expect(response.errorCode).toBe('unknown_target_id');
    expect(response.decision.action).toBe('ask_user');
    expect(response.decision.targetId).toBeUndefined();
  });

  it('rejects clarification alternatives that were not supplied by the platform', async () => {
    const provider: AiProvider = {
      async decideNextAction() {
        return {
          status: 'needs_clarification',
          action: 'ask_user',
          alternativeTargetIds: ['candidate-1', 'invented-target'],
          message: '어느 항목인가요?',
          confidence: 0.4,
        };
      },
    };

    const response = await handleGuideApiRequest(GUIDE_API_PATH, request, provider);

    expect(response.status).toBe(422);
    expect(response.errorCode).toBe('unknown_target_id');
    expect(response.decision.alternativeTargetIds).toBeUndefined();
  });

  it('converts low-confidence highlights into clarification', async () => {
    const provider: AiProvider = {
      async decideNextAction() {
        return {
          status: 'in_progress',
          action: 'highlight',
          targetId: 'candidate-1',
          message: '아마 여기일 거예요.',
          confidence: 0.2,
        };
      },
    };
    const response = await handleGuideApiRequest(GUIDE_API_PATH, request, provider);
    expect(response.errorCode).toBe('low_confidence');
    expect(response.decision.action).toBe('ask_user');
  });

  it('returns a safe fallback for malformed provider output', async () => {
    const provider: AiProvider = { async decideNextAction() { return { targetId: 42 }; } };
    const response = await handleGuideApiRequest(GUIDE_API_PATH, request, provider);
    expect(response.status).toBe(502);
    expect(response.errorCode).toBe('malformed_decision');
    expect(response.decision.action).toBe('ask_user');
  });
});
