import type { GuideDecision, GuideRequest } from '../../../../src/contracts';
import { normalizeUiName, semanticAliases, semanticLabelFromRules } from '../../../../src/web-knowledge';
import { findWindowsKnowledge } from '../windows-knowledge/WindowsKnowledgeResolver';

function visibleState(request: GuideRequest): string {
  return normalizeUiName([
    request.context.applicationName,
    request.context.windowTitle,
    request.context.url,
    ...request.candidates.filter((candidate) => candidate.visible)
      .flatMap((candidate) => [candidate.label, candidate.description]),
  ].filter(Boolean).join(' '));
}

function completedState(request: GuideRequest): string {
  return normalizeUiName(request.session.completedSteps.join(' '));
}

function containsAlias(value: string, aliases: string[]): boolean {
  return aliases.some((alias) => {
    const normalized = normalizeUiName(alias);
    return normalized.length >= 2 && value.includes(normalized);
  });
}

export function resolveGoalCompletion(request: GuideRequest): GuideDecision | undefined {
  if (request.session.completedSteps.length === 0) return undefined;
  const goal = request.session.goal ?? request.session.originalUserMessage;
  const completed = completedState(request);
  const visible = visibleState(request);

  const windows = findWindowsKnowledge(goal)?.entry;
  if (windows) {
    const finalAliases = windows.route.at(-1) ?? [];
    if (containsAlias(completed, finalAliases) && containsAlias(visible, finalAliases)) {
      return {
        status: 'completed', action: 'explain',
        message: '원하는 Windows 화면 또는 설정에 도착했습니다.',
        expectedChange: `${windows.id} 목표가 완료되었습니다.`, confidence: 0.99,
      };
    }
    return undefined;
  }

  const intent = semanticLabelFromRules(goal);
  if (!intent) return undefined;
  const aliases = semanticAliases(intent);
  if (!containsAlias(completed, aliases)) return undefined;

  if (intent === 'login') {
    const atSignInPage = /\/ap\/(?:signin|register)|[/?#](?:signin|login)(?:[/?#]|$)/iu.test(
      request.context.url ?? '',
    ) || ((/password|비밀번호/u.test(visible)) && /email|phone|이메일|휴대폰|sign in|로그인/u.test(visible));
    if (!atSignInPage) return undefined;
  } else if (!containsAlias(visible, aliases)) {
    return undefined;
  }

  return {
    status: 'completed', action: 'explain',
    message: '요청한 화면에 도착했습니다. 안내를 종료합니다.',
    expectedChange: `${intent} 목표가 완료되었습니다.`, confidence: 0.97,
  };
}
