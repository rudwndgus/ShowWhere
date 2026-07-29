import type { NormalizedQuery } from '../types/search';
import { compactText, normalizeComparableText } from './textMatching';

const STOP_PHRASES = [
  '들어가고 싶어', '하고 싶어요', '하고 싶어', '어디 있어', '어디있어',
  '어디에', '어디서', '어디야', '어디', '찾아줘', '보여줘', '알려줘', '눌러줘',
  '누르면', '누르고', '눌러', '하려면', '할래', '버튼', '메뉴', '아이콘',
  '주세요', '해줘', '합니다', '이에요', '예요', '싶어요',
  'where', 'button', 'please', 'show me', 'want to', 'how do i',
  'click', 'open', 'find', 'the', 'is', 'me', 'to', 'how', 'do', 'i', 'go',
];

const ENDINGS = [
  /하고$/u, /하려$/u, /하고싶어$/u, /싶어요$/u, /주세요$/u,
  /해줘$/u, /합니다$/u, /이에요$/u, /예요$/u,
];

export function normalizeQuery(original: string): NormalizedQuery {
  const comparableOriginal = normalizeComparableText(original);
  let normalized = ` ${comparableOriginal} `;
  for (const phrase of [...STOP_PHRASES].sort((a, b) => b.length - a.length)) {
    const cleaned = normalizeComparableText(phrase);
    normalized = normalized.replace(new RegExp(`\\s${escapeRegExp(cleaned)}(?=\\s|$)`, 'gu'), ' ');
  }
  normalized = normalized.replace(/\s+/g, ' ').trim();

  let tokens = normalized.split(' ').filter((token) => token.length > 0);
  tokens = tokens
    .map((token) => {
      const withoutParticle = token.length > 2
        ? token.replace(/(에서|으로|은|는|이|가|을|를|에|로)$/u, '')
        : token;
      return ENDINGS.reduce((current, ending) => current.replace(ending, ''), withoutParticle);
    })
    .filter((token) => token.length > 0);

  if (tokens.length === 0) {
    tokens = comparableOriginal.split(' ').filter(Boolean);
    normalized = comparableOriginal;
  } else {
    normalized = tokens.join(' ');
  }

  return {
    original,
    normalized,
    tokens: [...new Set(tokens)],
    comparableOriginal,
    compactOriginal: compactText(comparableOriginal),
  };
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
