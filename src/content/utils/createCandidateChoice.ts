import type { CandidateChoice, ScoredCandidate } from '../types/search';
import { createElementLabel } from './createElementLabel';
import { DEBUG_MATCHING } from './debug';
import { resolveClickableTarget } from './resolveClickableTarget';

const elementIds = new WeakMap<HTMLElement, string>();
let sequence = 0;

export function getStableElementId(element: HTMLElement): string {
  const existing = elementIds.get(element);
  if (existing) return existing;
  const id = `sw-candidate-${++sequence}`;
  elementIds.set(element, id);
  return id;
}

export function createCandidateChoice(candidate: ScoredCandidate, index = 0): CandidateChoice {
  const label = createElementLabel(candidate, index);
  const resolved = resolveClickableTarget(candidate.element, label);
  const choice: CandidateChoice = {
    id: getStableElementId(candidate.element),
    label,
    candidate,
    target: candidate.element,
    resolvedTarget: resolved.target,
    actionType: resolved.actionType,
    score: candidate.score,
    optionButtonFound: resolved.optionButtonFound,
  };
  if (DEBUG_MATCHING) {
    console.debug('[ShowWhere] candidate choice', {
      id: choice.id,
      label: choice.label,
      score: choice.score,
      actionType: choice.actionType,
      optionButtonFound: choice.optionButtonFound,
      target: choice.target,
      resolvedTarget: choice.resolvedTarget,
    });
  }
  return choice;
}
