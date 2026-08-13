import type { GuideDecision, GuideRequest, UiCandidate } from '../../../../src/contracts';
import {
  meaningfulWebTokens,
  normalizeUiName,
  semanticAliases,
  semanticLabelFromRules,
  type CommonWebPattern,
  type WebKnowledgeCatalog,
  type WebKnowledgeRoute,
  type WebKnowledgeRouteStep,
} from '../../../../src/web-knowledge';
import { eligibleCandidates } from '../providers/LocalGuideResolver';
import type { WebKnowledgeSource } from './WebKnowledgeStore';

interface RankedTarget { candidate: UiCandidate; score: number; step: WebKnowledgeRouteStep; route: WebKnowledgeRoute }

const localizedSiteAliases: Record<string, string[]> = {
  'amazon-com': ['amazon', '아마존'],
};

function browserCandidates(request: GuideRequest): UiCandidate[] {
  return eligibleCandidates(request).filter((candidate) =>
    String(candidate.attributes?.sourceScope ?? '') === 'browser_content');
}
export function catalogMentionedInGoal(catalog: WebKnowledgeCatalog, request: GuideRequest): boolean {
  const goal = normalizeUiName(request.session.goal ?? request.session.originalUserMessage);
  const siteNames = [
    catalog.displayName,
    catalog.siteId,
    ...(localizedSiteAliases[catalog.siteId] ?? []),
    ...catalog.domains.flatMap((domain) => {
      const host = domain.replace(/^www\./u, '').toLowerCase();
      return [host, host.split('.')[0]];
    }),
  ].map(normalizeUiName).filter((value) => value.length >= 3);
  return siteNames.some((name) => goal.includes(name));
}

export function catalogMatchesContext(catalog: WebKnowledgeCatalog, request: GuideRequest): boolean {
  if (request.context.url) {
    try {
      const host = new URL(request.context.url).hostname.replace(/^www\./u, '').toLowerCase();
      return catalog.domains.some((domain) => host === domain.replace(/^www\./u, '').toLowerCase()
        || host.endsWith(`.${domain.replace(/^www\./u, '').toLowerCase()}`));
    } catch { return false; }
  }
  const title = normalizeUiName(request.context.windowTitle ?? '');
  if (title.includes(normalizeUiName(catalog.displayName)) || title.includes(normalizeUiName(catalog.siteId))) return true;

  // Some browsers do not expose the address bar URL through UI Automation.
  // In that case an explicitly named site in the user's goal is still strong
  // enough context, but only while a browser is the foreground application.
  const application = normalizeUiName(request.context.applicationName);
  if (!/chrome|edge|firefox|brave|opera|vivaldi|arc/u.test(application)) return false;
  return catalogMentionedInGoal(catalog, request);
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

function isContextQualifiedGoal(goal: string, intent: string): boolean {
  const normalized = normalizeUiName(goal);
  if (intent === 'login')
    return /address|location|delivery|shipping|주소|위치|배송/u.test(normalized);
  return false;
}

function isCandidateCompatibleWithIntent(goal: string, intent: string, candidate: UiCandidate): boolean {
  if (intent !== 'login' || isContextQualifiedGoal(goal, intent)) return true;
  const text = normalizeUiName(`${candidate.label ?? ''} ${candidate.description ?? ''}`);

  // These are contextual shortcuts whose primary job is changing an address
  // or delivery location. They may eventually authenticate the user, but are
  // not the canonical answer to a bare "where do I log in?" request.
  if (/address|location|delivery|shipping|주소|위치|배송/u.test(text)) return false;
  return /sign in|log in|login|account lists|로그인/u.test(text);
}

function canonicalCandidateBonus(intent: string, candidate: UiCandidate): number {
  if (intent !== 'login') return 0;
  const label = normalizeUiName(candidate.label ?? '');
  const automationId = normalizeUiName(String(candidate.attributes?.automationId ?? ''));
  let score = 0;
  if (/^(sign in|log in|login|로그인)$/u.test(label)) score += 700;
  if (/hello.*sign in.*account|sign in.*account.*lists|account.*lists/u.test(label)) score += 650;
  if (/nav link accountlist|nav link account list/u.test(automationId)) score += 900;
  return score;
}

function statesMatchingCurrentPage(catalog: WebKnowledgeCatalog, request: GuideRequest) {
  let path: string | undefined;
  if (request.context.url) {
    try { path = new URL(request.context.url).pathname.replace(/\/$/u, '') || '/'; }
    catch { /* The title match below remains available. */ }
  }
  const title = normalizeUiName(request.context.windowTitle ?? '');
  if (path !== undefined) {
    const pathMatches = catalog.states.filter((state) => state.urlPatterns.some((pattern) =>
      (pattern.replace(/\/$/u, '') || '/') === path));
    if (pathMatches.length > 0) return pathMatches;
  }
  const titleMatches = catalog.states.filter((state) =>
    title.length > 0 && state.titlePatterns.some((pattern) => title.includes(normalizeUiName(pattern))));
  return titleMatches.length > 0 ? titleMatches : catalog.states;
}

function selectFromRoutes(
  goal: string,
  routes: WebKnowledgeRoute[],
  candidates: UiCandidate[],
  currentStateIds?: Set<string>,
): RankedTarget | undefined {
  const rankedRoutes = routes.map((route) => ({ route, score: routeScore(goal, route) }))
    .filter((item) => item.score >= 500)
    .sort((left, right) => right.score - left.score || right.route.successRate - left.route.successRate)
    .slice(0, 5);
  const targets: RankedTarget[] = [];
  for (const rankedRoute of rankedRoutes) {
    rankedRoute.route.steps.forEach((step, index) => {
      if (currentStateIds && !currentStateIds.has(step.fromStateId)) return;
      for (const candidate of candidates) {
        const score = candidateScore(candidate, step) - Math.min(160, index * 30)
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

function selectFromKnownElements(
  request: GuideRequest,
  goal: string,
  catalogs: WebKnowledgeCatalog[],
  candidates: UiCandidate[],
): RankedTarget | undefined {
  const targets: RankedTarget[] = [];
  const requestedIntent = semanticLabelFromRules(goal);
  for (const catalog of catalogs) {
    for (const state of statesMatchingCurrentPage(catalog, request)) {
      for (const element of state.elements) {
        const effectiveElementIntent = semanticLabelFromRules(element.names.join(' ')) ?? element.semanticLabel;
        const intent = phraseScore(goal, [
          effectiveElementIntent.replace(/[._]/gu, ' '),
          ...semanticAliases(effectiveElementIntent),
        ]);
        if (intent < 500) continue;
        const step: WebKnowledgeRouteStep = {
          fromStateId: state.id,
          semanticLabel: effectiveElementIntent,
          names: element.names,
          roles: [element.role],
        };
        const route: WebKnowledgeRoute = {
          id: `element-${element.id}`,
          intentLabel: element.semanticLabel,
          aliases: semanticAliases(element.semanticLabel),
          steps: [step],
          successRate: element.semanticConfidence,
        };
        for (const candidate of candidates) {
          if (requestedIntent && !isCandidateCompatibleWithIntent(goal, requestedIntent, candidate)) continue;
          const match = candidateScore(candidate, step);
          if (match > 0) targets.push({
            candidate,
            step,
            route,
            score: intent + match + Math.round(element.semanticConfidence * 100)
              + canonicalCandidateBonus(requestedIntent ?? effectiveElementIntent, candidate),
          });
        }
      }
    }
  }
  targets.sort((left, right) => right.score - left.score
    || left.candidate.bounds.y - right.candidate.bounds.y
    || left.candidate.bounds.x - right.candidate.bounds.x);
  const best = targets[0];
  if (!best || best.score < 1_550) return undefined;

  // Multiple visible controls can legitimately perform the same action (for
  // example Amazon's header sign-in link and its sign-in flyout button). They
  // are not an ambiguity that should force GPT or an unrelated Windows route.
  const second = targets.find((item) => item.candidate.id !== best.candidate.id);
  if (second && best.score - second.score < 100
      && second.step.semanticLabel !== best.step.semanticLabel) return undefined;
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

export function resolveWebKnowledge(
  request: GuideRequest,
  knowledge: WebKnowledgeSource,
  resolvedIntent?: string,
): GuideDecision | undefined {
  const originalGoal = request.session.goal ?? request.session.originalUserMessage;
  const goal = resolvedIntent
    ? `${resolvedIntent.replace(/[._]/gu, ' ')} ${semanticAliases(resolvedIntent).join(' ')}`
    : originalGoal;
  const requestedIntent = semanticLabelFromRules(goal);
  const candidates = browserCandidates(request).filter((candidate) =>
    !requestedIntent || isCandidateCompatibleWithIntent(goal, requestedIntent, candidate));
  if (candidates.length === 0) return undefined;
  const catalogs = knowledge.catalogs.filter((catalog) => catalogMatchesContext(catalog, request));
  // A crawl may observe a useful control without producing a transition route
  // (login pages are a common example because authentication is intentionally
  // not automated). Use the normalized element knowledge before route search.
  let target = selectFromKnownElements(request, goal, catalogs, candidates);
  let source = 'site';
  if (!target) {
    const currentStateIds = new Set(catalogs.flatMap((catalog) =>
      statesMatchingCurrentPage(catalog, request).map((state) => state.id)));
    target = selectFromRoutes(
      goal,
      catalogs.flatMap((catalog) => catalog.routes),
      candidates,
      currentStateIds.size > 0 ? currentStateIds : undefined,
    );
  }
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
