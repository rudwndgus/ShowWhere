import type { GuideDecision, GuideRequest, UiCandidate } from '../../../../src/contracts';
import { eligibleCandidates, meaningfulTokens, normalizeText } from '../providers/LocalGuideResolver';
import { windowsKnowledgeCatalog, type WindowsKnowledgeEntry } from './WindowsKnowledgeCatalog';

export interface WindowsKnowledgeMatch {
  entry: WindowsKnowledgeEntry;
  score: number;
}

const problemPattern = /안\s*(나|돼|되|됨)|못\s|문제|오류|실패|이상|부족|not working|failed|error|cannot|can t|no internet|no sound/u;

function intentScore(goal: string, entry: WindowsKnowledgeEntry): number {
  const normalizedGoal = normalizeText(goal);
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
    if (label === alias) score = Math.max(score, 1_000 + alias.length);
    else if (alias.length >= 3 && label.includes(alias)) score = Math.max(score, 800 + alias.length);
    else if (label.length >= 3 && alias.includes(label)) score = Math.max(score, 650 + label.length);
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
  return undefined;
}

export function resolveWindowsKnowledge(request: GuideRequest): GuideDecision | undefined {
  const goal = request.session.goal ?? request.session.originalUserMessage;
  const match = findWindowsKnowledge(goal);
  if (!match) return undefined;
  const candidate = nextCandidate(request, match.entry);
  if (!candidate) return undefined;
  const label = candidate.label ?? candidate.description ?? candidate.role;
  const diagnostic = match.entry.kind === 'troubleshooting'
    && match.entry.route.slice(match.entry.navigationDepth).some((step) => step.includes(label));
  return {
    status: 'in_progress',
    action: 'highlight',
    targetId: candidate.id,
    message: diagnostic
      ? `원인을 확인하려면 '${label}' 항목을 눌러주세요.`
      : `Windows에서 '${label}' 항목을 눌러주세요.`,
    expectedChange: diagnostic
      ? `${match.entry.id} 문제 해결을 위한 다음 상태를 확인합니다.`
      : `${match.entry.id} 설정 화면으로 이동합니다.`,
    confidence: 0.99,
  };
}
