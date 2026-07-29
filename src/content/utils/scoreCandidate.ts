import type {
  CandidateElement,
  NormalizedQuery,
  ScoredCandidate,
  SearchIntent,
} from '../types/search';
import { fuzzyEquals, fuzzyIncludes, normalizeComparableText } from './textMatching';

function inferredRole(candidate: CandidateElement): string {
  if (candidate.role) return candidate.role;
  if (candidate.tagName === 'button') return 'button';
  if (candidate.tagName === 'a') return 'link';
  if (candidate.tagName === 'input' && candidate.inputType === 'search') return 'searchbox';
  if (['input', 'textarea'].includes(candidate.tagName)) return 'textbox';
  return candidate.tagName;
}

function startsWithWord(text: string, token: string): boolean {
  const normalizedText = normalizeComparableText(text);
  const normalizedToken = normalizeComparableText(token);
  return normalizedText.split(' ').some((word) => word.startsWith(normalizedToken));
}

export function scoreCandidate(
  candidate: CandidateElement,
  query: NormalizedQuery,
  intent: SearchIntent | null,
): ScoredCandidate {
  let score = 0;
  let hasTextMatch = false;
  const reasons: string[] = [];
  const phrase = query.normalized || query.comparableOriginal;

  if (phrase && fuzzyEquals(candidate.visibleText, phrase)) {
    score += 100;
    hasTextMatch = true;
    reasons.push('표시 문구 정확히 일치 +100');
  } else if (phrase && fuzzyIncludes(candidate.visibleText, phrase)) {
    score += 80;
    hasTextMatch = true;
    reasons.push('표시 문구에 전체 검색어 포함 +80');
  }

  if (phrase && fuzzyEquals(candidate.ariaLabel, phrase)) {
    score += 90;
    hasTextMatch = true;
    reasons.push('aria-label 정확히 일치 +90');
  } else if (phrase && fuzzyIncludes(candidate.ariaLabel, phrase)) {
    score += 75;
    hasTextMatch = true;
    reasons.push('aria-label에 전체 검색어 포함 +75');
  }

  if (phrase && fuzzyIncludes(candidate.title, phrase)) {
    score += 60;
    hasTextMatch = true;
    reasons.push('title 일치 +60');
  }
  if (phrase && fuzzyIncludes(candidate.placeholder, phrase)) {
    score += 55;
    hasTextMatch = true;
    reasons.push('placeholder 일치 +55');
  }

  for (const token of query.tokens) {
    if (token.length > 1 && fuzzyIncludes(candidate.searchableText, token)) {
      score += 20;
      hasTextMatch = true;
      reasons.push(`검색 토큰 '${token}' 일치 +20`);
      if (startsWithWord(candidate.searchableText, token)) {
        score += 15;
        reasons.push(`단어 시작 '${token}' 일치 +15`);
      }
    }
  }

  if (intent) {
    const matchedTarget = intent.targetTerms.find((term) =>
      fuzzyIncludes(candidate.searchableText, term),
    );
    if (matchedTarget) {
      score += 35;
      hasTextMatch = true;
      reasons.push(`의도 동의어 '${matchedTarget}' 일치 +35`);
    }
  }

  if (!hasTextMatch) {
    return { ...candidate, score: -1000, reasons: ['검색 문구 일치 없음'] };
  }

  const role = inferredRole(candidate);
  if (candidate.tagName === 'button') {
    score += 15;
    reasons.push('button 요소 +15');
  }
  if (candidate.role === 'button') {
    score += 15;
    reasons.push('button 역할 +15');
  }
  if (candidate.tagName === 'a' || candidate.role === 'link') {
    score += 10;
    reasons.push('링크 요소 +10');
  }
  if (candidate.isInViewport) {
    score += 12;
    reasons.push('현재 화면 안 +12');
  } else if (candidate.isVisible) {
    score -= 5;
    reasons.push('현재 화면 밖 -5');
  }
  if (candidate.isVisible) {
    score += 20;
    reasons.push('페이지에 표시됨 +20');
  } else {
    score -= 100;
    reasons.push('숨겨진 요소 -100');
  }
  if (candidate.isClickable) {
    score += 15;
    reasons.push('클릭 가능 +15');
  } else {
    score -= 30;
    reasons.push('클릭할 수 없는 설명 요소 -30');
    if (candidate.visibleText.length > 100) {
      score -= 40;
      reasons.push('긴 비대화형 컨테이너 -40');
    }
  }
  if (candidate.isDisabled) {
    score -= 100;
    reasons.push('비활성 요소 -100');
  }
  if (candidate.rect.width < 16 || candidate.rect.height < 16) {
    score -= 20;
    reasons.push('지나치게 작은 요소 -20');
  }

  if (intent?.preferredRoles?.includes(role)) {
    score += 24;
    reasons.push(`선호 역할 '${role}' +24`);
  }
  if (intent?.preferredRoles?.includes(`input:${candidate.inputType}`)) {
    score += 30;
    reasons.push(`선호 입력 유형 '${candidate.inputType}' +30`);
  }
  if (
    intent?.id === 'SEARCH' &&
    (candidate.inputType === 'search' || candidate.role === 'searchbox')
  ) {
    score += 45;
    reasons.push('검색 입력창 우대 +45');
  }
  if (
    intent?.id === 'SETTINGS' &&
    candidate.ariaLabel &&
    fuzzyIncludes(candidate.ariaLabel, 'settings')
  ) {
    score += 35;
    reasons.push('설정 아이콘 접근성 이름 우대 +35');
  }
  if (
    intent?.id === 'MENU' &&
    ['menu', 'navigation', 'more', '메뉴', '더보기'].some((term) =>
      fuzzyIncludes(candidate.ariaLabel, term),
    )
  ) {
    score += 35;
    reasons.push('메뉴 접근성 이름 우대 +35');
  }

  return { ...candidate, score, reasons };
}
