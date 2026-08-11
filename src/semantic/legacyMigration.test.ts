import { describe, expect, it } from 'vitest';
import { createLegacyMigrationDraft } from './legacyMigration';

describe('legacy migration', () => {
  it('extracts semantic intent but never marks legacy correction as Gold', () => {
    const draft = createLegacyMigrationDraft({
      schemaVersion: 1, id: 'legacy-1', originalGoal: '프린터 상태 확인', correctedIntent: '프린터 설정으로 안내',
      context: { applicationName: 'explorer' }, selectedBounds: { x: 10, y: 20, width: 30, height: 40 },
    });
    expect(draft?.proposedTaskId).toBe('windows.printer.check_status');
    expect(draft?.status).toBe('needs_human_review');
    expect(JSON.stringify(draft)).not.toContain('selectedBounds');
  });
});
