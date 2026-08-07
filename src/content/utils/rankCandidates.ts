import type { ScoredCandidate, SearchOutcome } from '../types/search';
import { collectCandidateElements } from './collectCandidateElements';
import { createElementLabel } from './createElementLabel';
import { detectIntent } from './detectIntent';
import { normalizeQuery } from './normalizeQuery';
import { scoreCandidate } from './scoreCandidate';
import { DEBUG_MATCHING } from './debug';

export { DEBUG_MATCHING } from './debug';
export const MINIMUM_MATCH_SCORE = 50;
const AMBIGUITY_GAP = 12;

export function rankCandidates(originalQuery: string): SearchOutcome {
  const query = normalizeQuery(originalQuery);
  const intent = detectIntent(query);
  const ranked = collectCandidateElements()
    .filter((candidate) =>
      candidate.isClickable ||
      ['input', 'textarea', 'select', 'option', 'label'].includes(candidate.tagName) ||
      ['textbox', 'checkbox', 'radio', 'switch', 'tab'].includes(candidate.role ?? '')
    )
    .map((candidate) => scoreCandidate(candidate, query, intent))
    .filter((candidate) => candidate.isVisible && !candidate.isDisabled)
    .sort((a, b) => b.score - a.score);

  const locallyMatched = ranked.filter((candidate) => candidate.score >= MINIMUM_MATCH_SCORE);
  const best = locallyMatched[0] ?? null;
  const alternatives = best
    ? locallyMatched.filter((candidate) => best.score - candidate.score <= AMBIGUITY_GAP).slice(0, 3)
    : [];
  const ambiguous = alternatives.length > 1;

  if (DEBUG_MATCHING) {
    debugMatching(query.original, query.normalized, query.tokens, intent?.id ?? null, ranked, best);
  }

  return { query, intent, ranked, best, alternatives, ambiguous };
}

function debugMatching(
  original: string,
  normalized: string,
  tokens: string[],
  intent: string | null,
  ranked: ScoredCandidate[],
  best: ScoredCandidate | null,
) {
  console.group('[ShowWhere] matching debug');
  console.log('원본 질문:', original);
  console.log('정규화된 질문:', normalized);
  console.log('감지된 의도:', intent);
  console.log('검색 토큰:', tokens);
  console.table(
    ranked.slice(0, 10).map((candidate, index) => ({
      rank: index + 1,
      label: createElementLabel(candidate, index),
      tag: candidate.tagName,
      role: candidate.role,
      score: candidate.score,
      reasons: candidate.reasons.join(', '),
    })),
  );
  console.log('최종 선택 요소:', best?.element ?? null);
  console.groupEnd();
}
