import { describe, expect, it } from 'vitest';
import { SemanticKnowledgeStore } from '../../../../src/semantic/knowledgeStore';
import type { ConceptDictionaryEntry, TaskGraph } from '../../../../src/semantic/contracts';
import { TeachingService } from './TeachingService';

const graph: TaskGraph = {
  schemaVersion: 'showwhere-task-graph-v1', taskId: 'windows.printer.check_status', intent: { domain: 'windows', action: 'check', object: 'printer_connection' }, desiredOutcome: 'Check printer', initialStateIds: ['start'], terminalStateIds: ['settings'], states: [
    { stateId: 'start', evidenceConceptIds: ['windows.settings'], transitions: [{ transitionId: 'open', action: 'highlight', targetConceptId: 'windows.settings', nextStateId: 'settings', evidenceConceptIds: ['windows.settings'], instruction: 'Open Settings', riskLevel: 'low', confirmationRequired: false }] },
    { stateId: 'settings', evidenceConceptIds: ['windows.settings'], transitions: [] },
  ],
};
const concept: ConceptDictionaryEntry = { schemaVersion: 'showwhere-concept-v1', conceptId: 'windows.settings', aliases: ['Settings'], preferredRoles: ['button'], relatedConcepts: [], description: 'Settings' };

function store() {
  const drafts: unknown[] = []; const gold: unknown[] = [];
  return { store: {
    async concepts() { return [concept]; }, async taskGraphs() { return [graph]; }, async goldSteps() { return []; },
    async appendDraft(value: unknown) { drafts.push(value); }, async appendGold(value: unknown) { gold.push(value); },
  } as unknown as SemanticKnowledgeStore, drafts, gold };
}

describe('TeachingService', () => {
  it('keeps AI/mock analysis as an editable draft and only saves human-approved Gold', async () => {
    const data = store();
    const service = new TeachingService({ aiMode: 'mock', host: '127.0.0.1', port: 0, maxRequestBytes: 1_000_000, debug: false }, data.store);
    const draft = await service.analyze({ userQuestion: '프린터 상태 확인', currentStepDescription: 'start', developerCorrection: '설정을 안내', candidates: [] });
    expect(draft.verification.status).toBe('draft');
    expect(data.drafts).toHaveLength(1);
    const validation = await service.validate(draft);
    expect(validation.valid).toBe(true);
    const approved = await service.saveApproved(draft, 'developer');
    expect(approved.verification.status).toBe('human_verified');
    expect(data.gold).toHaveLength(1);
  });

  it('uses a compact AI proposal and assembles a trusted editable Semantic v2 draft', async () => {
    const data = store();
    let providerRequestBody = '';
    const fetchImplementation: typeof fetch = async (_input, init) => {
      providerRequestBody = String(init?.body ?? '');
      return new Response(JSON.stringify({
      choices: [{ message: { content: JSON.stringify({
        intent: { domain: 'windows', action: 'check', object: 'printer_connection', language: 'ko-KR', keyPhrases: ['프린터 상태'] },
        taskId: 'windows.printer.check_status',
        desiredOutcome: 'View printer status',
        stateId: 'start',
        action: 'highlight',
        targetConceptId: 'windows.settings',
        instruction: '먼저 설정을 열어보세요.',
        nextStateId: 'settings',
        evidenceConceptIds: ['windows.settings'],
        successConditionType: 'state_reached',
        riskLevel: 'low',
        confirmationRequired: false,
        confidence: 0.94,
      }) } }],
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    };
    const service = new TeachingService({
      aiMode: 'featherless', host: '127.0.0.1', port: 0, maxRequestBytes: 1_000_000, debug: false,
      featherless: {
        apiKey: 'test-key', baseUrl: 'https://example.test/v1', model: 'deepseek-test',
        visionModels: ['vision-test'], reasoningModel: 'deepseek-test', learningJudgeModels: [],
        requestTimeoutMs: 1_000, maxRetries: 0, retryBaseDelayMs: 0, maxTokens: 256,
        enableThinking: false, debug: false,
      },
    }, data.store, fetchImplementation);

    const draft = await service.analyze({
      userQuestion: '프린터 상태를 보고 싶어',
      currentStepDescription: '시작 메뉴가 열려 있음',
      developerCorrection: '볼륨이 아니라 설정을 눌러야 함',
      context: {
        platform: 'windows', applicationName: 'chrome',
        windowTitle: 'Reports - Google Chrome', url: 'https://internal.example/reports', locale: null,
      },
      candidates: [{ label: 'Settings', role: 'button', enabled: true }],
    });

    expect(draft.source).toEqual({
      userQuestion: '프린터 상태를 보고 싶어',
      currentStepDescription: '시작 메뉴가 열려 있음',
      developerCorrection: '볼륨이 아니라 설정을 눌러야 함',
    });
    expect(draft.decision.target?.conceptId).toBe('windows.settings');
    expect(draft.state.visibleConcepts[0]).toMatchObject({ conceptId: 'windows.settings', observedLabels: ['Settings'] });
    expect(draft.verification).toMatchObject({ status: 'draft', sourceType: 'developer_correction', confidence: 0.94 });
    expect(providerRequestBody).not.toContain('https://internal.example/reports');
    expect(data.drafts).toHaveLength(1);
  });

  it('allows a human-reviewed developer correction to introduce a new task while keeping semantic validation', async () => {
    const data = store();
    const service = new TeachingService({ aiMode: 'mock', host: '127.0.0.1', port: 0, maxRequestBytes: 1_000_000, debug: false }, data.store);
    const draft = await service.analyze({
      userQuestion: '프린터 상태 확인', currentStepDescription: 'start',
      developerCorrection: '설정을 안내', candidates: [],
    });
    const newTaskDraft = {
      ...draft,
      task: { taskId: 'windows.display.adjust_brightness', desiredOutcome: 'Adjust display brightness' },
    };

    await expect(service.validate(newTaskDraft)).resolves.toMatchObject({ valid: true });
    await expect(service.saveApproved(newTaskDraft, 'developer')).resolves.toMatchObject({
      task: { taskId: 'windows.display.adjust_brightness' },
      verification: { status: 'human_verified' },
    });
  });
});
