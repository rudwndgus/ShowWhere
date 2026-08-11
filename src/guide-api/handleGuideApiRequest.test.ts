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
    platform: 'windows',
    applicationName: 'SystemSettings',
    windowTitle: 'Settings',
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

  it('rejects the Chrome address bar for an explicit YouTube Music search', async () => {
    const scopedRequest: GuideRequest = {
      ...request,
      session: {
        ...request.session,
        originalUserMessage: '유튜브 뮤직에서 아이유 노래 찾아줘',
        goal: '유튜브 뮤직에서 음악 검색',
      },
      context: { platform: 'windows', applicationName: 'chrome', windowTitle: 'YouTube Music - Google Chrome' },
      candidates: [{
        ...request.candidates[0],
        id: 'chrome-search',
        label: 'Address and search bar',
        role: 'edit',
        attributes: { sourceScope: 'browser_chrome' },
      }],
    };
    const provider: AiProvider = { async decideNextAction() {
      return { status: 'in_progress', action: 'highlight', targetId: 'chrome-search', message: '검색하세요.', confidence: 0.98 };
    } };

    const response = await handleGuideApiRequest(GUIDE_API_PATH, scopedRequest, provider);

    expect(response.decision.action).toBe('request_vision');
    expect(response.decision.targetId).toBeUndefined();
  });

  it('asks the user when site search and web search are both plausible', async () => {
    const scopedRequest: GuideRequest = {
      ...request,
      session: { ...request.session, originalUserMessage: '검색해줘', goal: '검색' },
      context: { platform: 'windows', applicationName: 'chrome', windowTitle: 'Example - Google Chrome' },
      candidates: [
        { ...request.candidates[0], id: 'chrome-search', label: 'Address and search bar', role: 'edit', attributes: { sourceScope: 'browser_chrome' } },
        { ...request.candidates[0], id: 'site-search', label: 'Search', role: 'edit', attributes: { sourceScope: 'browser_content', containerLabel: 'Example' } },
      ],
    };
    const provider: AiProvider = { async decideNextAction() {
      return { status: 'in_progress', action: 'highlight', targetId: 'chrome-search', message: '검색하세요.', confidence: 0.98 };
    } };

    const response = await handleGuideApiRequest(GUIDE_API_PATH, scopedRequest, provider);

    expect(response.decision.action).toBe('ask_user');
    expect(response.decision.alternativeTargetIds).toEqual(['chrome-search', 'site-search']);
  });

  it('uses vision instead of asking whether a named app search box is visible', async () => {
    const scopedRequest: GuideRequest = {
      ...request,
      session: {
        ...request.session,
        originalUserMessage: '유튜브 뮤직에서 아이유 노래 찾아줘',
        goal: '유튜브 뮤직에서 음악 검색',
      },
      context: { platform: 'windows', applicationName: 'chrome', windowTitle: 'YouTube Music - Google Chrome' },
      candidates: [{
        ...request.candidates[0],
        id: 'chrome-search',
        label: 'Address and search bar',
        role: 'edit',
        attributes: { sourceScope: 'browser_chrome' },
      }],
    };
    const provider: AiProvider = { async decideNextAction() {
      return { status: 'needs_clarification', action: 'ask_user', message: '검색창이 보이나요?', confidence: 0.7 };
    } };

    const response = await handleGuideApiRequest(GUIDE_API_PATH, scopedRequest, provider);

    expect(response.decision.action).toBe('request_vision');
  });
});
