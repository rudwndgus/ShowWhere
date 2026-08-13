import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { centralRecordBatchSchema, CentralKnowledgeStore } from './CentralKnowledgeStore';

const directories: string[] = [];
afterEach(async () => Promise.all(directories.splice(0).map((path) => rm(path, { recursive: true, force: true }))));

describe('CentralKnowledgeStore', () => {
  it('keeps only the latest update and supports incremental cursors across restarts', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-central-'));
    directories.push(directory);
    const store = new CentralKnowledgeStore(directory);
    const payload = (rating: string) => ({ schemaVersion: 1 as const, id: 'a', createdAtUtc: '2026-08-13T08:00:00.000Z', rating, answerId: 'answer-a', answerText: 'answer' });
    const first = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T10:00:00.000Z', payload: payload('incorrect') }]);
    const duplicate = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T09:00:00.000Z', payload: payload('correct') }]);
    const updated = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T11:00:00.000Z', payload: payload('correct') }]);

    expect(first).toEqual({ cursor: 1, accepted: 1 });
    expect(duplicate).toEqual({ cursor: 1, accepted: 0 });
    expect(updated).toEqual({ cursor: 2, accepted: 1 });
    expect((await store.changesAfter(1)).records[0]?.payload).toEqual(payload('correct'));

    const restored = new CentralKnowledgeStore(directory);
    const all = await restored.changesAfter(0);
    expect(all.cursor).toBe(2);
    expect(all.records).toHaveLength(1);
    expect(all.records[0]?.version).toBe(2);
  });

  it('rejects mismatched IDs and unverified Gold records at the runtime boundary', async () => {
    const common = { schemaVersion: 1, createdAtUtc: '2026-08-13T08:00:00.000Z' };
    expect(centralRecordBatchSchema.safeParse({ records: [{
      id: 'envelope', kind: 'feedback', updatedAt: '2026-08-13T10:00:00.000Z',
      payload: { ...common, id: 'different', rating: 'correct', answerId: 'a', answerText: 'ok' },
    }] }).success).toBe(false);
    expect(centralRecordBatchSchema.safeParse({ records: [{
      id: 'correction', kind: 'correction', updatedAt: '2026-08-13T10:00:00.000Z',
      payload: { ...common, id: 'correction', originalGoal: 'g', effectiveGoal: 'g', context: {}, developerVerified: false },
    }] }).success).toBe(false);
    expect(centralRecordBatchSchema.safeParse({ records: [{
      id: 'health-check', kind: 'status', updatedAt: '2026-08-13T10:00:00.000Z',
      payload: { ...common, id: 'health-check', feedbackId: '__central_sync_health__', active: true },
    }] }).success).toBe(false);
  });
});
