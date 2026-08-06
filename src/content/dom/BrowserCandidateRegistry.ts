import type { UiCandidate } from '../../contracts';
import type { CandidateChoice } from '../types/search';

export interface BrowserCandidateRecord {
  candidate: UiCandidate;
  choice: CandidateChoice;
  target: HTMLElement;
}

export class BrowserCandidateRegistry {
  readonly #records = new Map<string, BrowserCandidateRecord>();

  replace(records: BrowserCandidateRecord[]): void {
    this.#records.clear();
    for (const record of records) this.#records.set(record.candidate.id, record);
  }

  clear(): void {
    this.#records.clear();
  }

  has(candidateId: string): boolean {
    return this.#records.has(candidateId);
  }

  resolveTarget(candidateId: string): HTMLElement | null {
    const target = this.#records.get(candidateId)?.target;
    return target?.isConnected ? target : null;
  }

  getChoice(candidateId: string): CandidateChoice | null {
    return this.#records.get(candidateId)?.choice ?? null;
  }

  getChoices(candidateIds: string[]): CandidateChoice[] {
    return candidateIds
      .map((candidateId) => this.getChoice(candidateId))
      .filter((choice): choice is CandidateChoice => choice !== null);
  }
}
