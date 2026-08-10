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
});
