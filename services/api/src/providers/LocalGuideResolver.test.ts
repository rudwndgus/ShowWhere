import { describe, expect, it } from 'vitest';
import type { GuideRequest, UiCandidate } from '../../../../src/contracts';
import { eligibleCandidates, resolveLocally } from './LocalGuideResolver';

function candidate(id: string, label: string, sourceScope = 'settings'): UiCandidate {
  return {
    id, label, role: 'button', enabled: true, visible: true, clickable: true,
    bounds: { x: 0, y: 0, width: 100, height: 30 }, attributes: { sourceScope },
  };
}

function request(goal: string, candidates: UiCandidate[]): GuideRequest {
  return {
    session: { sessionId: 's', originalUserMessage: goal, goal, mode: 'guidance', status: 'waiting_for_ai', completedSteps: [], knownFacts: [], failureCount: 0 },
    context: { platform: 'windows', applicationName: 'Settings' }, candidates,
  };
}

describe('local guide resolver', () => {
  it('resolves an unambiguous visible semantic target without AI', () => {
    const decision = resolveLocally(request('윈도우 프린터 설정은 어디서 해?', [
      candidate('sound', '소리'), candidate('printers', '프린터 및 스캐너'),
    ]));
    expect(decision?.targetId).toBe('printers');
  });

  it('does not guess when equally matching targets exist', () => {
    expect(resolveLocally(request('검색', [candidate('a', '검색'), candidate('b', '검색')]))).toBeUndefined();
  });

  it('rejects browser chrome unless the user explicitly requests it', () => {
    expect(eligibleCandidates(request('유튜브 뮤직에서 노래 검색', [candidate('address', '검색', 'browser_chrome')]))).toHaveLength(0);
    expect(eligibleCandidates(request('크롬 주소창 표시', [candidate('address', '주소창', 'browser_chrome')]))).toHaveLength(1);
  });

  it('never treats a whole window as the clickable answer', () => {
    const wholeWindow = { ...candidate('window', '설정', 'windows_window_overview'), role: 'window' };
    const decision = resolveLocally(request('프린터 설정 어디야?', [
      wholeWindow,
      candidate('printers', '프린터 및 스캐너'),
    ]));
    expect(decision?.targetId).toBe('printers');
  });
});
