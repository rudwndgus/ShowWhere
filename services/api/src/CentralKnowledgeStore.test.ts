import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
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
    expect(centralRecordBatchSchema.safeParse({ records: [{
      id: 'bad-gold', kind: 'correction', updatedAt: '2026-08-13T10:00:00.000Z',
      payload: {
        ...common, id: 'bad-gold', originalGoal: 'printer settings', effectiveGoal: 'printer settings',
        context: { platform: 'windows', applicationName: 'ApplicationFrameHost' }, developerVerified: true,
        issueTags: ['wrong_target'], learningLabels: { outcomeLabel: 'wrong_target' },
        humanGold: {
          identity: 'bad', siteOrApplication: 'applicationframehost', normalizedIntent: 'printer.settings',
          semanticState: 'application', targetConcept: 'start', targetAliases: ['Start'], action: 'highlight',
          developerVerified: true, confidence: 0.98, successfulUses: 1, independentVerificationCount: 1,
          lastVerifiedAt: '2026-08-13T08:00:00.000Z', verificationKeys: ['one'],
        },
      },
    }] }).success).toBe(false);
  });

  it('propagates a revoked correction as the latest central tombstone', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-central-revoke-'));
    directories.push(directory);
    const store = new CentralKnowledgeStore(directory);
    const base = {
      schemaVersion: 1 as const, id: 'correction-a', createdAtUtc: '2026-08-13T08:00:00.000Z',
      originalGoal: 'printer settings', effectiveGoal: 'printer settings',
      context: { platform: 'windows' as const, applicationName: 'ApplicationFrameHost' }, developerVerified: true as const,
    };
    await store.upsert([{ id: 'correction-a', kind: 'correction', updatedAt: '2026-08-13T10:00:00.000Z', payload: base }]);
    await store.upsert([{ id: 'correction-a', kind: 'correction', updatedAt: '2026-08-13T11:00:00.000Z', payload: {
      ...base, knowledgeStatus: 'revoked', invalidReason: 'wrong_target_without_correct_next_step',
    } }]);

    const synced = await store.changesAfter(0);
    expect(synced.records).toHaveLength(1);
    expect(synced.records[0]?.payload).toMatchObject({ knowledgeStatus: 'revoked' });
  });

  it('migrates legacy invalid Gold to one durable tombstone on startup', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-central-legacy-'));
    directories.push(directory);
    await mkdir(directory, { recursive: true });
    const legacy = {
      id: 'legacy-bad', kind: 'correction', version: 7, updatedAt: '2026-08-13T10:00:00.000Z',
      payload: {
        schemaVersion: 1, id: 'legacy-bad', createdAtUtc: '2026-08-13T08:00:00.000Z',
        originalGoal: 'printer settings', effectiveGoal: 'printer settings',
        context: { platform: 'windows', applicationName: 'ApplicationFrameHost' }, developerVerified: true,
        issueTags: ['wrong_target'], learningLabels: { outcomeLabel: 'wrong_target', authority: 'human_gold' },
        humanGold: {
          identity: 'legacy', siteOrApplication: 'applicationframehost', normalizedIntent: 'printer.settings',
          semanticState: 'application', targetConcept: 'start', targetAliases: ['Start'], action: 'highlight',
          developerVerified: true, confidence: 0.98, successfulUses: 1, independentVerificationCount: 1,
          lastVerifiedAt: '2026-08-13T08:00:00.000Z', verificationKeys: ['legacy'],
        },
      },
    };
    const path = join(directory, 'knowledge-events.jsonl');
    await writeFile(path, `${JSON.stringify(legacy)}\n`, 'utf8');

    const first = new CentralKnowledgeStore(directory);
    const migrated = await first.changesAfter(0);
    expect(migrated.cursor).toBe(8);
    expect(migrated.records).toHaveLength(1);
    expect(migrated.records[0]?.payload).toMatchObject({ knowledgeStatus: 'revoked' });
    expect(migrated.records[0]?.payload).not.toHaveProperty('humanGold');
    const afterFirstStart = await readFile(path, 'utf8');

    const restarted = new CentralKnowledgeStore(directory);
    expect((await restarted.changesAfter(0)).cursor).toBe(8);
    expect(await readFile(path, 'utf8')).toBe(afterFirstStart);
  });

  it('revokes an older conflicting Gold learned on the same screen', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-central-conflict-'));
    directories.push(directory);
    const store = new CentralKnowledgeStore(directory);
    const correction = (id: string, targetConcept: string) => ({
      schemaVersion: 1 as const, id, createdAtUtc: '2026-08-13T08:00:00.000Z',
      originalGoal: 'printer settings', effectiveGoal: 'printer settings', snapshotHash: 'same-screen',
      context: { platform: 'windows' as const, applicationName: 'ApplicationFrameHost' }, developerVerified: true as const,
      issueTags: ['positive_feedback'], learningLabels: { outcomeLabel: 'correct_target' },
      correctTarget: { label: targetConcept },
      humanGold: {
        identity: id, siteOrApplication: 'applicationframehost', normalizedIntent: 'printer.settings',
        semanticState: 'foreground_application', targetConcept, targetAliases: [targetConcept], action: 'highlight',
        developerVerified: true as const, confidence: 0.98, successfulUses: 1, independentVerificationCount: 1,
        lastVerifiedAt: '2026-08-13T08:00:00.000Z', verificationKeys: [id],
      },
    });
    await store.upsert([{ id: 'old', kind: 'correction', updatedAt: '2026-08-13T10:00:00.000Z', payload: correction('old', 'start-button') }]);
    await store.upsert([{ id: 'new', kind: 'correction', updatedAt: '2026-08-13T11:00:00.000Z', payload: correction('new', 'printers-scanners') }]);

    const records = (await store.changesAfter(0)).records;
    expect(records).toHaveLength(2);
    expect(records.find((item) => item.id === 'old')?.payload).toMatchObject({ knowledgeStatus: 'revoked' });
    expect(records.find((item) => item.id === 'new')?.payload).toHaveProperty('humanGold');
  });
});
