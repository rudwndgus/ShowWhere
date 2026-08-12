import type {
  CrawlPathStep,
  NormalizedWebElement,
  RawWebCrawlRun,
  WebKnowledgeCatalog,
  WebKnowledgeRoute,
  WebKnowledgeRouteStep,
  WebKnowledgeState,
  WebKnowledgeTransition,
} from '../../../src/web-knowledge';
import {
  normalizeUiName,
  normalizeWebRole,
  semanticAliases,
  stableWebId,
} from '../../../src/web-knowledge';
import type { SemanticLabeler } from './semanticLabeler';

interface LabeledElement {
  rawId: string;
  normalized: NormalizedWebElement;
}
function displayName(siteId: string): string {
  return siteId.split('-').filter(Boolean).map((part) => part[0].toUpperCase() + part.slice(1)).join(' ');
}

function normalizedPathPattern(value: string): string {
  try {
    const url = new URL(value);
    return url.pathname
      .replace(/[0-9a-f]{8}-[0-9a-f-]{27,}/giu, ':id')
      .replace(/\/\d+(?=\/|$)/gu, '/:id') || '/';
  } catch { return value; }
}

async function labelElements(runs: RawWebCrawlRun[], labeler: SemanticLabeler): Promise<Map<string, LabeledElement>> {
  const byFingerprint = new Map<string, { names: Set<string>; roles: Set<string>; areas: Set<string>; regions: Set<NormalizedWebElement['regions'][number]>; hints: NormalizedWebElement['locatorHints']; risks: Set<string>; rawIds: Set<string> }>();
  for (const state of runs.flatMap((run) => run.states)) {
    for (const element of state.elements) {
      const key = `${normalizeWebRole(element.role)}|${normalizeUiName(element.name)}`;
      const group = byFingerprint.get(key) ?? {
        names: new Set(), roles: new Set(), areas: new Set(), regions: new Set(), hints: [], risks: new Set(), rawIds: new Set(),
      };
      group.names.add(element.name);
      group.roles.add(normalizeWebRole(element.role));
      group.areas.add(element.container ?? element.area);
      group.regions.add(element.region);
      group.hints.push(element.locator);
      group.risks.add(element.risk);
      group.rawIds.add(element.id);
      byFingerprint.set(key, group);
    }
  }
  const result = new Map<string, LabeledElement>();
  for (const [fingerprint, group] of byFingerprint) {
    const names = [...group.names].sort();
    const role = [...group.roles][0];
    const semantic = await labeler.label(names[0], role, [...group.areas].join(' '));
    const normalized: NormalizedWebElement = {
      id: stableWebId('web-element', semantic.label, role, fingerprint),
      names,
      normalizedNames: [...new Set(names.map(normalizeUiName))],
      role,
      areas: [...group.areas].filter(Boolean).sort(),
      regions: [...group.regions].sort(),
      semanticLabel: semantic.label,
      semanticConfidence: semantic.confidence,
      semanticSource: semantic.source,
      locatorHints: group.hints.filter((hint, index, values) =>
        values.findIndex((value) => JSON.stringify(value) === JSON.stringify(hint)) === index).slice(0, 8),
      risk: group.risks.has('blocked') ? 'blocked' : group.risks.has('unknown') ? 'unknown' : 'safe',
    };
    for (const rawId of group.rawIds) result.set(rawId, { rawId, normalized });
  }
  return result;
}

function routeStep(pathStep: CrawlPathStep, fromStateId: string, toStateId: string | undefined, elements: Map<string, LabeledElement>): WebKnowledgeRouteStep {
  const element = elements.get(pathStep.elementId)?.normalized;
  return {
    fromStateId,
    ...(toStateId ? { toStateId } : {}),
    semanticLabel: element?.semanticLabel ?? `site.${normalizeUiName(pathStep.name).replace(/\s+/gu, '_')}`,
    names: element?.names ?? [pathStep.name],
    roles: [element?.role ?? pathStep.role],
  };
}

export async function normalizeCrawlRuns(runs: RawWebCrawlRun[], labeler: SemanticLabeler): Promise<WebKnowledgeCatalog> {
  if (runs.length === 0) throw new Error('No crawl runs supplied.');
  await labeler.initialize();
  const siteId = runs[0].siteId;
  if (runs.some((run) => run.siteId !== siteId)) throw new Error('All crawl runs must belong to one site.');
  const elements = await labelElements(runs, labeler);
  const statesById = new Map<string, WebKnowledgeState>();
  for (const raw of runs.flatMap((run) => run.states)) {
    const existing = statesById.get(raw.id);
    const normalized: WebKnowledgeState = {
      id: raw.id,
      urlPatterns: [normalizedPathPattern(raw.url)],
      titlePatterns: raw.title ? [raw.title] : [],
      evidence: raw.evidence.slice(0, 60),
      elements: [...new Map(raw.elements.flatMap((item) => {
        const value = elements.get(item.id)?.normalized;
        return value ? [[value.id, value] as const] : [];
      })).values()],
    };
    if (!existing) statesById.set(raw.id, normalized);
    else {
      existing.urlPatterns = [...new Set([...existing.urlPatterns, ...normalized.urlPatterns])];
      existing.titlePatterns = [...new Set([...existing.titlePatterns, ...normalized.titlePatterns])];
      existing.evidence = [...new Set([...existing.evidence, ...normalized.evidence])].slice(0, 80);
      existing.elements = [...new Map([...existing.elements, ...normalized.elements].map((item) => [item.id, item])).values()];
    }
  }

  const transitionGroups = new Map<string, typeof runs[number]['transitions']>();
  for (const transition of runs.flatMap((run) => run.transitions)) {
    const label = elements.get(transition.action.elementId)?.normalized.semanticLabel ?? transition.action.name;
    const key = `${transition.fromStateId}|${transition.toStateId ?? ''}|${label}|${transition.outcome}`;
    const group = transitionGroups.get(key) ?? [];
    group.push(transition);
    transitionGroups.set(key, group);
  }
  const transitions: WebKnowledgeTransition[] = [...transitionGroups.values()].map((group) => {
    const first = group[0];
    const element = elements.get(first.action.elementId)?.normalized;
    const successes = group.filter((item) => ['navigation', 'state_change', 'popup'].includes(item.outcome)).length;
    return {
      id: stableWebId('web-transition', first.fromStateId, first.toStateId ?? '', element?.semanticLabel ?? first.action.name, first.outcome),
      fromStateId: first.fromStateId,
      ...(first.toStateId ? { toStateId: first.toStateId } : {}),
      actionElementId: element?.id ?? first.action.elementId,
      actionSemanticLabel: element?.semanticLabel ?? `site.${normalizeUiName(first.action.name).replace(/\s+/gu, '_')}`,
      actionNames: element?.names ?? [first.action.name],
      actionRoles: [element?.role ?? first.action.role],
      outcome: first.outcome,
      observedCount: group.length,
      successRate: successes / group.length,
      addedEvidence: [...new Set(group.flatMap((item) => item.addedEvidence))].slice(0, 40),
      removedEvidence: [...new Set(group.flatMap((item) => item.removedEvidence))].slice(0, 40),
    };
  });

  const transitionLookup = new Map(transitions.map((item) => [`${item.fromStateId}|${item.actionSemanticLabel}`, item]));
  const routesByKey = new Map<string, WebKnowledgeRoute>();
  for (const run of runs) {
    for (const rawState of run.states.filter((state) => state.path.length > 0)) {
      let currentState = run.states.find((state) => state.depth === 0)?.id ?? rawState.id;
      const steps: WebKnowledgeRouteStep[] = [];
      for (const pathStep of rawState.path) {
        const label = elements.get(pathStep.elementId)?.normalized.semanticLabel ?? `site.${normalizeUiName(pathStep.name).replace(/\s+/gu, '_')}`;
        const transition = transitionLookup.get(`${currentState}|${label}`);
        steps.push(routeStep(pathStep, currentState, transition?.toStateId, elements));
        currentState = transition?.toStateId ?? currentState;
      }
      const intentLabel = steps.at(-1)?.semanticLabel;
      if (!intentLabel) continue;
      const sequence = steps.map((step) => step.semanticLabel).join('>');
      const key = `${intentLabel}|${sequence}`;
      if (!routesByKey.has(key)) routesByKey.set(key, {
        id: stableWebId('web-route', siteId, key),
        intentLabel,
        aliases: [...new Set([intentLabel.replace(/[._]/gu, ' '), ...semanticAliases(intentLabel)])],
        steps,
        successRate: 1,
      });
    }
  }

  return {
    schemaVersion: 1,
    siteId,
    displayName: displayName(siteId),
    domains: [...new Set(runs.flatMap((run) => run.domains))].sort(),
    generatedAt: new Date().toISOString(),
    sourceRunIds: runs.map((run) => run.runId).sort(),
    semanticModel: labeler.metadata,
    states: [...statesById.values()],
    transitions,
    routes: [...routesByKey.values()],
  };
}
