import type { ApplicationContext, UiCandidate } from '../../contracts';
import type { CandidateChoice, SearchOutcome } from '../types/search';
import { rankCandidates } from '../utils/rankCandidates';
import { BrowserCandidateRegistry, type BrowserCandidateRecord } from './BrowserCandidateRegistry';
import { createBrowserApplicationContext } from './createApplicationContext';
import { normalizeBrowserCandidates } from './normalizeBrowserCandidates';

export interface BrowserObservation {
  context: ApplicationContext;
  candidates: UiCandidate[];
  outcome: SearchOutcome;
  records: BrowserCandidateRecord[];
  bestChoice: CandidateChoice | null;
  alternativeChoices: CandidateChoice[];
  candidateSignature: string;
}

export function observeBrowserInterface(
  query: string,
  registry: BrowserCandidateRegistry,
): BrowserObservation {
  const outcome = rankCandidates(query);
  const records = normalizeBrowserCandidates(outcome.ranked);
  registry.replace(records);
  const recordByElement = new Map(
    records.map((record) => [record.choice.candidate.element, record] as const),
  );
  const bestChoice = outcome.best
    ? recordByElement.get(outcome.best.element)?.choice ?? null
    : null;
  const alternativeChoices = outcome.alternatives
    .map((candidate) => recordByElement.get(candidate.element)?.choice)
    .filter((choice): choice is CandidateChoice => choice !== undefined)
    .slice(0, 3);
  return {
    context: createBrowserApplicationContext(),
    candidates: records.map((record) => record.candidate),
    outcome,
    records,
    bestChoice,
    alternativeChoices,
    candidateSignature: alternativeChoices.map((choice) => choice.id).join('|'),
  };
}
