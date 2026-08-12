import type { GuideDecision, GuideRequest, UiCandidate } from '../../../../src/contracts';
import {
  meaningfulWebTokens,
  normalizeUiName,
  semanticAliases,
  type CommonWebPattern,
  type WebKnowledgeCatalog,
  type WebKnowledgeRoute,
  type WebKnowledgeRouteStep,
} from '../../../../src/web-knowledge';
import { eligibleCandidates } from '../providers/LocalGuideResolver';
import type { WebKnowledgeSource } from './WebKnowledgeStore';

interface RankedTarget { candidate: UiCandidate; score: number; step: WebKnowledgeRouteStep; route: WebKnowledgeRoute }

function browserCandidates(request: GuideRequest): UiCandidate[] {
  return eligibleCandidates(request).filter((candidate) =>
    String(candidate.attributes?.sourceScope ?? '') === 'browser_content');
}
function catalogMatchesContext(catalog: WebKnowledgeCatalog, request: GuideRequest): boolean {
  if (request.context.url) {
    try {
      const host = new URL(request.context.url).hostname.replace(/^www\./u, '').toLowerCase();
      return catalog.domains.some((domain) => host === domain.replace(/^www\./u, '').toLowerCase()
        || host.endsWith(`.${domain.replace(/^www\./u, '').toLowerCase()}`));
    } catch { return false; }
  }
  const title = normalizeUiName(request.context.windowTitle ?? '');
  return title.includes(normalizeUiName(catalog.displayName)) || title.includes(normalizeUiName(catalog.siteId));
}

function phraseScore(goal: string, phrases: string[]): number {
  const normalizedGoal = normalizeUiName(goal);
  const goalTokens = meaningfulWebTokens(goal);
  let best = 0;
  for (const phraseValue of phrases) {
    const phrase = normalizeUiName(phraseValue);
    if (!phrase) continue;
    if (normalizedGoal === phrase) best = Math.max(best, 1_200 + phrase.length);
    else if (phrase.length >= 3 && normalizedGoal.includes(phrase)) best = Math.max(best, 1_000 + phrase.length);
    const tokens = meaningfulWebTokens(phrase);
    const overlap = tokens.filter((token) => goalTokens.some((goalToken) =>
      goalToken === token || goalToken.includes(token) || token.includes(goalToken))).length;
    if (tokens.length > 0) best = Math.max(best, Math.round(750 * overlap / tokens.length));
  }
  return best;
}

function routeScore(goal: string, route: WebKnowledgeRoute): number {
  return phraseScore(goal, [route.intentLabel.replace(/[._]/gu, ' '), ...route.aliases, ...semanticAliases(route.intentLabel)]);
}

function candidateScore(candidate: UiCandidate, step: WebKnowledgeRouteStep): number {
  const label = candidate.label ?? candidate.description ?? '';
  const normalized = normalizeUiName(label);
  const aliases = [...step.names, ...semanticAliases(step.semanticLabel)];
  let score = phraseScore(label, aliases);
  if (step.roles.some((role) => normalizeUiName(role) === normalizeUiName(candidate.role))) score += 140;
  const container = normalizeUiName(String(candidate.attributes?.containerLabel ?? ''));
  if (container && aliases.some((alias) => container.includes(normalizeUiName(alias)))) score += 80;
  if (normalized && step.names.some((name) => normalizeUiName(name) === normalized)) score += 300;
  return score;
}

function selectFromRoutes(goal: string, routes: WebKnowledgeRoute[], candidates: UiCandidate[]): RankedTarget | undefined {
  const rankedRoutes = routes.map((route) => ({ route, score: routeScore(goal, route) }))
    .filter((item) => item.score >= 500)
    .sort((left, right) => right.score - left.score || right.route.successRate - left.route.successRate)
    .slice(0, 5);
  const targets: RankedTarget[] = [];
  for (const rankedRoute of rankedRoutes) {
    rankedRoute.route.steps.forEach((step, index) => {
      for (const candidate of candidates) {
        const score = candidateScore(candidate, step) + Math.min(160, index * 30)
          + Math.round(rankedRoute.route.successRate * 100);
        if (score > 0) targets.push({ candidate, score, step, route: rankedRoute.route });
      }
    });
  }
  targets.sort((left, right) => right.score - left.score);
  const best = targets[0];
  if (!best || best.score < 1_050) return undefined;
  const second = targets.find((item) => item.candidate.id !== best.candidate.id);
  if (second && best.score - second.score < 100) return undefined;
  return best;
}

function patternRoutes(patterns: CommonWebPattern[], goal: string): WebKnowledgeRoute[] {
  return patterns.filter((pattern) => pattern.siteCount >= 2 && pattern.confidence >= 0.6
      && phraseScore(goal, [pattern.intentLabel.replace(/[._]/gu, ' '), ...pattern.aliases, ...semanticAliases(pattern.intentLabel)]) >= 500)
    .map((pattern) => ({
      id: pattern.id,
      intentLabel: pattern.intentLabel,
      aliases: pattern.aliases,
      successRate: pattern.confidence,
      steps: pattern.sequence.map((semanticLabel, index) => ({
        fromStateId: `common-${index}`,
        semanticLabel,
        names: semanticAliases(semanticLabel).length > 0 ? semanticAliases(semanticLabel) : [semanticLabel.replace(/[._]/gu, ' ')],
        roles: ['button', 'link', 'menuitem', 'tab'],
      })),
    }));
}

export function resolveWebKnowledge(request: GuideRequest, knowledge: WebKnowledgeSource): GuideDecision | undefined {
  const candidates = browserCandidates(request);
  if (candidates.length === 0) return undefined;
  const goal = request.session.goal ?? request.session.originalUserMessage;
  const catalogs = knowledge.catalogs.filter((catalog) => catalogMatchesContext(catalog, request));
  let target = selectFromRoutes(goal, catalogs.flatMap((catalog) => catalog.routes), candidates);
  let source = 'site';
  if (!target) {
    target = selectFromRoutes(goal, patternRoutes(knowledge.patterns, goal), candidates);
    source = 'common';
  }
  if (!target) return undefined;
  const label = target.candidate.label ?? target.candidate.description ?? target.candidate.role;
  return {
    status: 'in_progress',
    action: 'highlight',
    targetId: target.candidate.id,
    message: `웹사이트에서 '${label}' 항목을 눌러주세요.`,
    expectedChange: `${target.step.semanticLabel} 기능으로 이동합니다.`,
    confidence: source === 'site' ? 0.97 : 0.82,
  };
}
