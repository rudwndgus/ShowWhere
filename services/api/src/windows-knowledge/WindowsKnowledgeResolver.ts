import type { GuideDecision, GuideRequest, UiCandidate } from '../../../../src/contracts';
import { inRequestLanguage } from '../providers/responseLanguage';
import { eligibleCandidates, meaningfulTokens, normalizeText } from '../providers/LocalGuideResolver';
import { windowsKnowledgeCatalog, type WindowsKnowledgeEntry } from './WindowsKnowledgeCatalog';

export interface WindowsKnowledgeMatch {
  entry: WindowsKnowledgeEntry;
  score: number;
}

function displayLabel(value: string): string {
  const cleaned = value
    .replace(/\s*[-–—]\s*\d+개의?\s+실행\s+중인\s+창\s+고정됨.*$/iu, '')
    .replace(/\s*[-–—]\s*\d+\s+running\s+windows?.*$/iu, '')
    .trim();
  return cleaned || value;
}

const problemPattern = /안\s*(나|돼|되|됨)|못\s|문제|오류|실패|이상|부족|not working|failed|error|cannot|can t|no internet|no sound/u;

function intentScore(goal: string, entry: WindowsKnowledgeEntry): number {
  const normalizedGoal = normalizeText(goal);

  // "login" is shared by every website and must not be treated as a Windows
  // sign-in failure unless the user actually describes a Windows/PIN/password
  // problem. This previously made an Amazon sign-in question open Settings.
  if (entry.id === 'windows.troubleshoot.login'
      && !/(windows|윈도우|pin|핀 번호|비밀번호|password|hello|로그인.{0,8}(안|못|문제|오류|실패))/iu.test(normalizedGoal)) {
    return 0;
  }
  const goalTokens = meaningfulTokens(goal);
  let best = 0;
  for (const intent of entry.intents) {
    const normalizedIntent = normalizeText(intent);
    if (normalizedIntent.length >= 2 && normalizedGoal.includes(normalizedIntent))
      best = Math.max(best, 1_000 + normalizedIntent.length);
    const intentTokens = meaningfulTokens(intent);
    const overlap = intentTokens.filter((token) => goalTokens.some((goalToken) =>
      goalToken.includes(token) || token.includes(goalToken))).length;
    if (intentTokens.length > 0)
      best = Math.max(best, Math.round(600 * overlap / intentTokens.length));
  }
  const isProblem = problemPattern.test(normalizedGoal);
  if (entry.kind === 'troubleshooting' && isProblem) best += 350;
  if (entry.kind === 'setting' && isProblem) best -= 150;
  return best;
}

export function findWindowsKnowledge(goal: string): WindowsKnowledgeMatch | undefined {
  const ranked = windowsKnowledgeCatalog.map((entry) => ({ entry, score: intentScore(goal, entry) }))
    .filter((match) => match.score >= 500)
    .sort((left, right) => right.score - left.score);
  if (ranked.length === 0) return undefined;
  if (ranked[1] && ranked[0].score === ranked[1].score && ranked[0].entry.id !== ranked[1].entry.id) return undefined;
  return ranked[0];
}

function candidateMatchScore(candidate: UiCandidate, aliases: string[]): number {
  const label = normalizeText(candidate.label ?? candidate.description ?? '');
  const container = normalizeText(String(candidate.attributes?.containerLabel ?? ''));
  if (label.length === 0) return 0;
  let score = 0;
  for (const aliasValue of aliases) {
    const alias = normalizeText(aliasValue);
    const aliasMinimum = /[가-힣]/u.test(alias) ? 2 : 3;
    const labelMinimum = /[가-힣]/u.test(label) ? 2 : 3;
    if (label === alias) score = Math.max(score, 1_000 + alias.length);
    else if (alias.length >= aliasMinimum && label.includes(alias)) score = Math.max(score, 800 + alias.length);
    else if (label.length >= labelMinimum && alias.includes(label)) score = Math.max(score, 650 + label.length);
    if (container === alias) score = Math.max(score, 500 + alias.length);
  }
  return score;
}

function bestCandidate(candidates: UiCandidate[], aliases: string[]): UiCandidate | undefined {
  const ranked = candidates.map((candidate) => ({ candidate, score: candidateMatchScore(candidate, aliases) }))
    .filter((item) => item.score > 0)
    .sort((left, right) => right.score - left.score
      || left.candidate.bounds.width * left.candidate.bounds.height
        - right.candidate.bounds.width * right.candidate.bounds.height);
  if (ranked.length === 0) return undefined;
  if (ranked[1] && ranked[0].score === ranked[1].score) return undefined;
  return ranked[0].candidate;
}

function stepWasCompleted(request: GuideRequest, aliases: string[]): boolean {
  const history = normalizeText(request.session.completedSteps.join(' '));
  return aliases.some((alias) => {
    const normalized = normalizeText(alias);
    return normalized.length >= 2 && history.includes(normalized);
  });
}

function nextCandidate(request: GuideRequest, entry: WindowsKnowledgeEntry): UiCandidate | undefined {
  const candidates = eligibleCandidates(request);
  if (entry.kind === 'troubleshooting') {
    for (let index = entry.navigationDepth; index < entry.route.length; index++) {
      if (stepWasCompleted(request, entry.route[index])) continue;
      const candidate = bestCandidate(candidates, entry.route[index]);
      if (candidate) return candidate;
    }
  }
  for (let index = entry.navigationDepth - 1; index >= 0; index--) {
    const candidate = bestCandidate(candidates, entry.route[index]);
    if (candidate) return candidate;
  }
  const startWasOpened = stepWasCompleted(request, ['시작', '시작 메뉴', 'start'])
    || /startmenuexperiencehost|searchhost|start menu/u.test(normalizeText(
      `${request.context.applicationName} ${request.context.windowTitle ?? ''}`,
    ));
  if (startWasOpened) {
    // Search is a last-resort entry path only after the direct destination,
    // Settings app, and Start controls were all absent from the live candidates.
    return bestCandidate(candidates, [
      '검색 상자', '검색창', '앱, 설정 및 문서 검색',
      'Search box', 'Type here to search', 'Search for apps, settings, and documents',
    ]);
  }
  return undefined;
}

export function resolveWindowsKnowledgeEntry(
  request: GuideRequest,
  entry: WindowsKnowledgeEntry,
  confidence = 0.99,
): GuideDecision | undefined {
  const candidate = nextCandidate(request, entry);
  if (!candidate) return undefined;
  const label = displayLabel(candidate.label ?? candidate.description ?? candidate.role);
  const diagnostic = entry.kind === 'troubleshooting'
    && entry.route.slice(entry.navigationDepth).some((step) => step.includes(label));
  const original = request.session.originalUserMessage;
  return {
    status: 'in_progress',
    action: 'highlight',
    targetId: candidate.id,
    message: diagnostic
      ? inRequestLanguage(original, `원인을 확인하려면 '${label}' 항목을 눌러주세요.`, `Select '${label}' to check the cause.`)
      : /검색|search/iu.test(label)
        ? inRequestLanguage(original, `'${label}'을 누른 다음 찾을 설정 이름을 입력해 주세요.`, `Select '${label}', then enter the setting name.`)
        : inRequestLanguage(original, `Windows에서 '${label}' 항목을 눌러주세요.`, `Select '${label}' in Windows.`),
    expectedChange: diagnostic
      ? inRequestLanguage(original, `${entry.id} 문제 해결을 위한 다음 상태를 확인합니다.`, `The next ${entry.id} troubleshooting state opens.`)
      : inRequestLanguage(original, `${entry.id} 설정 화면으로 이동합니다.`, `The ${entry.id} settings screen opens.`),
    confidence,
  };
}

export function resolveWindowsKnowledge(request: GuideRequest): GuideDecision | undefined {
  const goal = request.session.goal ?? request.session.originalUserMessage;
  const match = findWindowsKnowledge(goal);
  return match ? resolveWindowsKnowledgeEntry(request, match.entry) : undefined;
}
