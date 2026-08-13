import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { CentralKnowledgeStore } from './CentralKnowledgeStore';

const directories: string[] = [];
afterEach(async () => Promise.all(directories.splice(0).map((path) => rm(path, { recursive: true, force: true }))));

describe('CentralKnowledgeStore', () => {
  it('keeps only the latest update and supports incremental cursors across restarts', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-central-'));
    directories.push(directory);
    const store = new CentralKnowledgeStore(directory);
    const first = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T10:00:00.000Z', payload: { rating: 'incorrect' } }]);
    const duplicate = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T09:00:00.000Z', payload: { rating: 'correct' } }]);
    const updated = await store.upsert([{ id: 'a', kind: 'feedback', updatedAt: '2026-08-13T11:00:00.000Z', payload: { rating: 'correct' } }]);

    expect(first).toEqual({ cursor: 1, accepted: 1 });
    expect(duplicate).toEqual({ cursor: 1, accepted: 0 });
    expect(updated).toEqual({ cursor: 2, accepted: 1 });
    expect((await store.changesAfter(1)).records[0]?.payload).toEqual({ rating: 'correct' });

    const restored = new CentralKnowledgeStore(directory);
    const all = await restored.changesAfter(0);
    expect(all.cursor).toBe(2);
    expect(all.records).toHaveLength(1);
    expect(all.records[0]?.version).toBe(2);
  });
});
