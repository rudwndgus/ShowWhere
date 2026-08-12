import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import type { GuideRequest } from '../contracts';
import type { LearningEventV2, MemoryHit } from './contracts';
import type { SemanticRetriever } from './interfaces';
import { scrubForLearning } from './scrubber';

function terms(text: string): Set<string> {
  return new Set(text.toLocaleLowerCase().split(/[^\p{L}\p{N}]+/u).filter((term) => term.length > 1));
}

function similarity(left: string, right: string): number {
  const a = terms(left);
  const b = terms(right);
  if (a.size === 0 || b.size === 0) return 0;
  let overlap = 0;
  for (const term of a) if (b.has(term)) overlap += 1;
  return overlap / Math.sqrt(a.size * b.size);
}

export class JsonMemoryProvider implements SemanticRetriever {
  private entries: Array<MemoryHit & { embedding?: number[] }> | undefined;

  constructor(
    private readonly memoryPath: string,
    private readonly embedder?: { embed(texts: string[]): Promise<number[][]> },
  ) {}

  private async load(): Promise<Array<MemoryHit & { embedding?: number[] }>> {
    if (this.entries) return this.entries;
    try {
      const parsed = JSON.parse(await readFile(this.memoryPath, 'utf8')) as { entries?: Array<MemoryHit & { embedding?: number[] }> } | Array<MemoryHit & { embedding?: number[] }>;
      this.entries = Array.isArray(parsed) ? parsed : parsed.entries ?? [];
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      this.entries = [];
    }
    return this.entries;
  }

  async search(query: string, request: GuideRequest, topK: number): Promise<MemoryHit[]> {
    const context = `${request.context.applicationName} ${request.context.windowTitle ?? ''} ${query}`;
    const entries = await this.load();
    let vectorScores = new Map<string, number>();
    if (this.embedder && entries.length > 0) {
      try {
        const missing = entries.filter((entry) => !entry.embedding);
        const vectors = await this.embedder.embed([context, ...missing.map((entry) => entry.text)]);
        missing.forEach((entry, index) => { entry.embedding = vectors[index + 1]; });
        const queryVector = vectors[0];
        const dot = (left: number[], right: number[]) => left.reduce((sum, value, index) => sum + value * (right[index] ?? 0), 0);
        vectorScores = new Map(entries.map((entry) => [entry.id, entry.embedding ? dot(queryVector, entry.embedding) : 0]));
      } catch {
        vectorScores = new Map();
      }
    }
    return entries
      .map(({ embedding: _embedding, ...entry }) => ({
        ...entry,
        score: Math.max(vectorScores.get(entry.id) ?? 0, similarity(context, entry.text)) * Math.max(0, Math.min(1, entry.score)),
      }))
      .filter((entry) => entry.authority !== 'rejected')
      .sort((a, b) => b.score - a.score)
      .slice(0, topK);
  }

  async remember(event: LearningEventV2): Promise<void> {
    if (!['human_gold', 'verified_real'].includes(event.authority) || event.finalOutcome !== 'success') return;
    const targetConcept = event.selectedSemanticTarget ?? event.selectedCandidate?.label;
    if (!targetConcept) return;
    const entries = await this.load();
    entries.push({
      id: event.eventId,
      score: 1,
      authority: event.authority,
      taskId: event.taskId ?? 'unknown',
      stateId: event.stateBefore ?? 'unknown',
      targetConcept,
      expectedNextState: event.expectedNextState ?? event.observedNextState ?? 'unknown',
      text: [event.userQuestion, targetConcept, event.expectedNextState, event.observedNextState].filter(Boolean).join(' | '),
    });
    await mkdir(dirname(this.memoryPath), { recursive: true });
    const temporary = `${this.memoryPath}.${process.pid}.tmp`;
    await writeFile(temporary, JSON.stringify({ schemaVersion: 1, entries: scrubForLearning(entries) }, null, 2), 'utf8');
    await rename(temporary, this.memoryPath);
  }
}
