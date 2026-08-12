import { describe, expect, it } from 'vitest';
import type { GuideRequest, UiCandidate } from '../../../../src/contracts';
import { windowsKnowledgeCatalog } from './WindowsKnowledgeCatalog';
import { findWindowsKnowledge, resolveWindowsKnowledge } from './WindowsKnowledgeResolver';

function candidate(id: string, label: string, sourceScope = 'settings'): UiCandidate {
  return {
    id, label, role: 'button', enabled: true, visible: true, clickable: true,
    bounds: { x: 10, y: 10, width: 180, height: 44 }, attributes: { sourceScope },
  };
}

function request(goal: string, candidates: UiCandidate[], completedSteps: string[] = []): GuideRequest {
  return {
    session: { sessionId: 'windows', originalUserMessage: goal, goal, mode: 'guidance', status: 'waiting_for_ai', completedSteps, knownFacts: [], failureCount: 0 },
    context: { platform: 'windows', applicationName: 'SystemSettings', windowTitle: '설정' }, candidates,
  };
}

describe('Windows knowledge catalog', () => {
  it('contains broad settings and troubleshooting coverage without coordinates', () => {
    expect(windowsKnowledgeCatalog.length).toBeGreaterThanOrEqual(60);
    expect(windowsKnowledgeCatalog.filter((entry) => entry.kind === 'troubleshooting').length).toBeGreaterThanOrEqual(20);
    expect(windowsKnowledgeCatalog.every((entry) => entry.msSettingsUri?.startsWith('ms-settings:'))).toBe(true);
    expect(JSON.stringify(windowsKnowledgeCatalog)).not.toMatch(/"x"|"y"|bounds/iu);
  });

  it('distinguishes a printer settings request from printer troubleshooting', () => {
    expect(findWindowsKnowledge('프린터 설정 어디야?')?.entry.id).toBe('windows.devices.printers');
    expect(findWindowsKnowledge('프린터가 안 돼서 인쇄를 못해')?.entry.id).toBe('windows.troubleshoot.printer');
  });

  it('opens the Settings entry point from the desktop without a remote model', () => {
    const decision = resolveWindowsKnowledge(request('프린터 설정 어디야?', [
      candidate('start', '시작', 'windows_taskbar'), candidate('settings', '설정', 'windows_taskbar'),
    ]));
    expect(decision?.targetId).toBe('settings');
  });

  it('chooses the final printer row when Settings is already open', () => {
    const decision = resolveWindowsKnowledge(request('인쇄 장치 설정 보여줘', [
      candidate('system', '시스템'), candidate('devices', 'Bluetooth 및 장치'),
      candidate('printers', '프린터 및 스캐너'),
    ]));
    expect(decision?.targetId).toBe('printers');
  });

  it('guides no-sound troubleshooting in state order', () => {
    const home = resolveWindowsKnowledge(request('소리가 왜 안 나와?', [
      candidate('system', '시스템'), candidate('sound', '소리'),
    ]));
    expect(home?.targetId).toBe('sound');

    const soundPage = resolveWindowsKnowledge(request('소리가 왜 안 나와?', [
      candidate('output', '출력 장치'), candidate('mixer', '볼륨 믹서'),
    ], ["사용자가 '소리' 컨트롤을 클릭함."]));
    expect(soundPage?.targetId).toBe('output');
  });

  it('does not claim unrelated app questions as Windows settings knowledge', () => {
    expect(findWindowsKnowledge('유튜브 뮤직에서 아이유 노래 검색해줘')).toBeUndefined();
  });

  it('maps every canonical intent back to its own unique entry', () => {
    for (const entry of windowsKnowledgeCatalog)
      expect(findWindowsKnowledge(entry.intents[0])?.entry.id, entry.id).toBe(entry.id);
  });
});
