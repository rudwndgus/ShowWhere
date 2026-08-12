import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterEach, describe, expect, it } from 'vitest';
import type { GuideRequest } from '../contracts';
import { JsonMemoryProvider } from './localMemory';

const temporaryDirectories: string[] = [];
afterEach(async () => Promise.all(temporaryDirectories.splice(0).map((path) => rm(path, { recursive: true, force: true }))));

const request: GuideRequest = {
  session: { sessionId: 's', originalUserMessage: '프린터 상태 확인', mode: 'guidance', status: 'waiting_for_ai', completedSteps: [], knownFacts: [], failureCount: 0 },
  context: { platform: 'windows', applicationName: 'Windows Start' }, candidates: [],
};

describe('JsonMemoryProvider', () => {
  it('does not treat authority confidence as query similarity', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'showwhere-memory-'));
    temporaryDirectories.push(directory);
    const path = join(directory, 'memory.json');
    await writeFile(path, JSON.stringify({ entries: [
      { id: 'wifi', score: 1, authority: 'human_gold', taskId: 'wifi', stateId: 'start', targetConcept: 'Wi-Fi', expectedNextState: 'wifi', text: 'connect wifi wireless network' },
      { id: 'printer', score: 1, authority: 'verified_real', taskId: 'printer', stateId: 'start', targetConcept: 'Printers', expectedNextState: 'printer', text: '프린터 상태 확인 printer status' },
    ] }), 'utf8');
    const [best, unrelated] = await new JsonMemoryProvider(path).search('프린터 상태 확인', request, 2);
    expect(best.id).toBe('printer');
    expect(best.score).toBeGreaterThan(unrelated.score);
    expect(unrelated.score).toBeLessThan(0.92);
  });
});

