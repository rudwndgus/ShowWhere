import { describe, expect, it } from 'vitest';
import { GuidanceTeachingRecordV2Schema, TaskGraphSchema } from './contracts';

const validRecord = {
  schemaVersion: 'showwhere-guidance-step-v2', id: 'printer.start.settings',
  source: { userQuestion: '프린터 상태를 확인해', currentStepDescription: '시작 메뉴가 열림', developerCorrection: '설정을 먼저 안내' },
  intent: { domain: 'windows', action: 'check', object: 'printer_connection', language: 'ko-KR', keyPhrases: ['프린터', '상태'] },
  task: { taskId: 'windows.printer.check_status', desiredOutcome: '프린터 상태 확인' },
  state: { stateId: 'windows.start.menu', completedStepIds: [], visibleConcepts: [] },
  decision: { action: 'highlight', target: { conceptId: 'windows.settings', preferredRoles: ['button'], confusableNegativeConceptIds: [] }, instruction: '설정을 눌러보세요.' },
  expectedTransition: { nextStateId: 'windows.settings.home', evidenceConcepts: ['windows.settings.bluetooth_devices'] },
  successCondition: { type: 'state_reached', stateId: 'windows.settings.home' },
  safety: { riskLevel: 'low', confirmationRequired: false },
  verification: { status: 'draft', sourceType: 'developer_correction', confidence: 0.9 },
};

describe('Semantic v2 contracts', () => {
  it('accepts one coordinate-free semantic guidance step', () => {
    expect(GuidanceTeachingRecordV2Schema.parse(validRecord).decision.target?.conceptId).toBe('windows.settings');
  });

  it('rejects coordinate fields and high-risk actions without confirmation', () => {
    expect(GuidanceTeachingRecordV2Schema.safeParse({ ...validRecord, x: 532 }).success).toBe(false);
    expect(GuidanceTeachingRecordV2Schema.safeParse({
      ...validRecord, safety: { riskLevel: 'high', confirmationRequired: false },
    }).success).toBe(false);
  });

  it('rejects task graph transitions to unknown states', () => {
    expect(TaskGraphSchema.safeParse({
      schemaVersion: 'showwhere-task-graph-v1', taskId: 'test.task',
      intent: { domain: 'test', action: 'check', object: 'thing' }, desiredOutcome: 'Test',
      initialStateIds: ['start'], terminalStateIds: ['done'],
      states: [
        { stateId: 'start', evidenceConceptIds: [], transitions: [{ transitionId: 'go', action: 'highlight', targetConceptId: 'thing', nextStateId: 'missing', evidenceConceptIds: [], instruction: 'Go', riskLevel: 'low', confirmationRequired: false }] },
        { stateId: 'done', evidenceConceptIds: [], transitions: [] },
      ],
    }).success).toBe(false);
  });
});
