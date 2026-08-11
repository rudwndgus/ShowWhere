import { describe, expect, it } from 'vitest';
import type { GuideRequest } from '../contracts';
import type { ConceptDictionaryEntry, GuidanceTeachingRecordV2, TaskGraph } from './contracts';
import { expectedTransitionMatches, resolveSemanticTarget, retrieveSemanticContext } from './retrieval';
import { validateTeachingRecord } from './validation';

const settingsConcept: ConceptDictionaryEntry = {
  schemaVersion: 'showwhere-concept-v1', conceptId: 'windows.settings', aliases: ['Settings', '설정'], preferredRoles: ['button'], relatedConcepts: [], description: 'Windows Settings',
};
const graph: TaskGraph = {
  schemaVersion: 'showwhere-task-graph-v1', taskId: 'windows.printer.check_status',
  intent: { domain: 'windows', action: 'check', object: 'printer_connection' }, desiredOutcome: '프린터 상태 확인',
  initialStateIds: ['windows.start.menu'], terminalStateIds: ['windows.settings.home'],
  states: [
    { stateId: 'windows.start.menu', evidenceConceptIds: ['windows.settings'], transitions: [{ transitionId: 'open_settings', action: 'highlight', targetConceptId: 'windows.settings', nextStateId: 'windows.settings.home', evidenceConceptIds: ['windows.settings.bluetooth_devices'], instruction: '설정을 누르세요.', riskLevel: 'low', confirmationRequired: false }] },
    { stateId: 'windows.settings.home', evidenceConceptIds: ['windows.settings.bluetooth_devices'], transitions: [] },
  ],
};
const record: GuidanceTeachingRecordV2 = {
  schemaVersion: 'showwhere-guidance-step-v2', id: 'printer.settings.step',
  source: { userQuestion: '프린터 어디서 확인해?', currentStepDescription: '시작 메뉴', developerCorrection: '설정 안내' },
  intent: { domain: 'windows', action: 'check', object: 'printer_connection', language: 'ko-KR', keyPhrases: ['프린터'] },
  task: { taskId: graph.taskId, desiredOutcome: graph.desiredOutcome },
  state: { stateId: 'windows.start.menu', completedStepIds: [], visibleConcepts: [{ conceptId: 'windows.settings', role: 'button', observedLabels: ['Settings'], enabled: true }] },
  decision: { action: 'highlight', target: { conceptId: 'windows.settings', preferredRoles: ['button'], confusableNegativeConceptIds: [] }, instruction: '설정을 눌러보세요.' },
  expectedTransition: { nextStateId: 'windows.settings.home', evidenceConcepts: ['windows.settings.bluetooth_devices'] },
  successCondition: { type: 'state_reached', stateId: 'windows.settings.home' }, safety: { riskLevel: 'low', confirmationRequired: false },
  verification: { status: 'draft', sourceType: 'developer_correction' },
};
const request: GuideRequest = {
  session: { sessionId: 's', originalUserMessage: '프린터 상태 어디서 봐?', mode: 'guidance', status: 'observing', completedSteps: [], knownFacts: [], failureCount: 0 },
  context: { platform: 'windows', applicationName: 'explorer' },
  candidates: [{ id: 'settings', label: 'Settings', role: 'button', enabled: true, visible: true, clickable: true, bounds: { x: 0, y: 0, width: 10, height: 10 } }],
};

describe('semantic knowledge', () => {
  it('resolves a semantic target against current candidates without coordinates as knowledge', () => {
    expect(resolveSemanticTarget('windows.settings', request.candidates, [settingsConcept])?.id).toBe('settings');
  });

  it('retrieves only relevant task/concept/gold context', () => {
    const context = retrieveSemanticContext(request, [settingsConcept], [graph], [record]);
    expect(context.likelyTask?.taskId).toBe(graph.taskId);
    expect(context.relatedGoldSteps).toHaveLength(1);
  });

  it('detects duplicate Gold and validates expected transition evidence', () => {
    const result = validateTeachingRecord({ ...record, id: 'other' }, { concepts: [settingsConcept, { ...settingsConcept, conceptId: 'windows.settings.bluetooth_devices' }], taskGraphs: [graph], existingGold: [record] });
    expect(result.valid).toBe(false);
    expect(result.issues.some((issue) => issue.code === 'duplicate_gold')).toBe(true);
    expect(expectedTransitionMatches(['windows.settings'], request.candidates, [settingsConcept])).toBe(true);
  });
});
