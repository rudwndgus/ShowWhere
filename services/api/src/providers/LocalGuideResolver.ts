import type { GuideDecision, GuideRequest, UiCandidate } from '../../../../src/contracts';
import { inRequestLanguage } from './responseLanguage';

const stopWords = new Set([
  '어디', '어디서', '어떻게', '해줘', '해주세요', '하고', '싶어', '싶어요', '보여줘', '알려줘',
  '윈도우', 'windows', 'window',
  '설정', '변경', '검색', '찾아', '찾기', '열어', '실행', '추가', '삭제', '확인', '상태',
  'where', 'how', 'please', 'show', 'me', 'the', 'a', 'an', 'to', 'in', 'on',
  'settings', 'setting', 'open', 'search', 'find', 'add', 'remove', 'check', 'status',
]);

const unsafeScopes = new Set(['window_chrome', 'browser_chrome', 'windows_window_overview']);
const nonActionRoles = new Set(['window', 'pane', 'group', 'document', 'text', 'image']);

export function normalizeText(value: string): string {
  return value.normalize('NFKC').toLowerCase()
    .replace(/(해주세요|해줘요|해줘|하세요|하고 싶어요|하고 싶어|어디서|어디|어떻게)/gu, ' ')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();
}

export function meaningfulTokens(value: string): string[] {
  return normalizeText(value).split(/\s+/u)
    .map((token) => token.replace(/(에서|으로|로|을|를|이|가|은|는|의)$/u, ''))
    .filter((token) => token.length >= 2 && !stopWords.has(token));
}

function candidateText(candidate: UiCandidate): string {
  return `${candidate.label ?? ''} ${candidate.description ?? ''} ${candidate.role}`;
}

export function eligibleCandidates(request: GuideRequest): UiCandidate[] {
  const goal = normalizeText(request.session.goal ?? request.session.originalUserMessage);
  return request.candidates.filter((candidate) => {
    if (!candidate.visible || !candidate.enabled || !candidate.clickable) return false;
    const scope = String(candidate.attributes?.sourceScope ?? '');
    const process = String(candidate.attributes?.processName ?? '').toLowerCase();
    const text = candidateText(candidate).toLowerCase();
    if (process.includes('showwhere') || text.includes('showwhere')) return false;
    if (nonActionRoles.has(candidate.role.toLowerCase())) return false;
    if (scope === 'browser_chrome' && /주소창|address bar|url bar/u.test(goal)) return true;
    if (scope === 'window_chrome' && /최소화|최대화|닫기|minimize|maximize|close/u.test(goal)) return true;
    return !unsafeScopes.has(scope);
  });
}

export function resolveLocally(request: GuideRequest): GuideDecision | undefined {
  const goal = request.session.goal ?? request.session.originalUserMessage;
  const goalNormalized = normalizeText(goal);
  const goalTokens = meaningfulTokens(goal);

  const ranked = eligibleCandidates(request).map((candidate) => {
    const text = normalizeText(candidateText(candidate));
    const tokenMatches = goalTokens.filter((token) => text.includes(token)).length;
    const exactLabel = candidate.label ? goalNormalized.includes(normalizeText(candidate.label)) : false;
    return { candidate, tokenMatches, exactLabel };
  }).filter((item) => item.exactLabel
      || (goalTokens.length > 0 && item.tokenMatches >= Math.min(2, goalTokens.length)))
    .sort((left, right) => Number(right.exactLabel) - Number(left.exactLabel)
      || right.tokenMatches - left.tokenMatches);

  if (ranked.length === 0) return undefined;
  const best = ranked[0];
  const second = ranked[1];
  if (second && best.exactLabel === second.exactLabel && best.tokenMatches === second.tokenMatches) return undefined;
  const label = best.candidate.label ?? best.candidate.description ?? best.candidate.role;
  const original = request.session.originalUserMessage;
  return {
    status: 'in_progress',
    action: 'highlight',
    targetId: best.candidate.id,
    message: inRequestLanguage(original, `화면의 '${label}'을(를) 눌러주세요.`, `Select '${label}' on the screen.`),
    expectedChange: inRequestLanguage(original, `'${label}' 화면이 열립니다.`, `'${label}' opens.`),
    confidence: best.exactLabel ? 0.98 : 0.9,
  };
}

export function describeCandidate(candidate: UiCandidate): string {
  const scope = String(candidate.attributes?.sourceScope ?? '');
  const container = String(candidate.attributes?.containerLabel ?? '');
  return [candidate.label, candidate.description, candidate.role, container, scope].filter(Boolean).join(' | ');
}
