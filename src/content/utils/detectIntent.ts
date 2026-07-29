import { INTENT_DICTIONARY } from '../data/intentDictionary';
import type { NormalizedQuery, SearchIntent } from '../types/search';
import { fuzzyEquals, fuzzyIncludes } from './textMatching';

export function detectIntent(query: NormalizedQuery): SearchIntent | null {
  const ranked = INTENT_DICTIONARY.map((intent) => {
    let score = 0;
    for (const phrase of intent.userPhrases) {
      if (fuzzyEquals(query.normalized, phrase) || fuzzyEquals(query.comparableOriginal, phrase)) {
        score = Math.max(score, 120 + phrase.length);
      } else if (
        fuzzyIncludes(query.normalized, phrase) ||
        fuzzyIncludes(query.comparableOriginal, phrase)
      ) {
        score = Math.max(score, 70 + phrase.length);
      }
    }
    return { intent, score };
  }).sort((a, b) => b.score - a.score);

  return ranked[0]?.score > 0 ? ranked[0].intent : null;
}
