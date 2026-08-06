import type { UiCandidate } from '../../contracts';
import type { ScoredCandidate } from '../types/search';
import { createCandidateChoice } from '../utils/createCandidateChoice';
import { getElementViewportRect, isElementClickable } from '../utils/elementVisibility';
import { MINIMUM_MATCH_SCORE } from '../utils/rankCandidates';
import type { BrowserCandidateRecord } from './BrowserCandidateRegistry';

export const MAX_GUIDE_CANDIDATES = 25;

export function isMeaningfulRankedCandidate(candidate: ScoredCandidate): boolean {
  return (
    candidate.isVisible &&
    !candidate.isDisabled &&
    candidate.score >= MINIMUM_MATCH_SCORE &&
    (
      candidate.isClickable ||
      ['input', 'textarea', 'select', 'option', 'label'].includes(candidate.tagName) ||
      ['textbox', 'checkbox', 'radio', 'switch', 'tab'].includes(candidate.role ?? '')
    )
  );
}

function inferRole(candidate: ScoredCandidate): string {
  if (candidate.role) return candidate.role;
  if (candidate.tagName === 'button') return 'button';
  if (candidate.tagName === 'a') return 'link';
  if (candidate.tagName === 'input' && candidate.inputType === 'search') return 'searchbox';
  if (['input', 'textarea'].includes(candidate.tagName)) return 'textbox';
  return candidate.tagName;
}

function getHrefType(element: HTMLElement): string | null {
  if (!(element instanceof HTMLAnchorElement)) return null;
  const rawHref = element.getAttribute('href');
  if (!rawHref) return null;
  if (rawHref.startsWith('#')) return 'same-page';
  if (rawHref.startsWith('mailto:')) return 'email';
  if (rawHref.startsWith('tel:')) return 'telephone';
  try {
    const url = new URL(element.href, element.ownerDocument.baseURI);
    return url.origin === element.ownerDocument.location.origin ? 'internal' : 'external';
  } catch {
    return 'other';
  }
}

function concise(value: string, maximum = 240): string | undefined {
  const cleaned = value.replace(/\s+/g, ' ').trim();
  if (!cleaned) return undefined;
  return cleaned.length > maximum ? `${cleaned.slice(0, maximum - 1)}…` : cleaned;
}

function toUiCandidate(
  candidate: ScoredCandidate,
  choice: ReturnType<typeof createCandidateChoice>,
  rank: number,
): UiCandidate {
  const target = choice.resolvedTarget.isConnected ? choice.resolvedTarget : choice.target;
  const rect = getElementViewportRect(target);
  const hrefType = getHrefType(candidate.element);
  const description = concise(candidate.visibleText) === choice.label
    ? undefined
    : concise(candidate.visibleText);
  return {
    id: choice.id,
    label: choice.label,
    description,
    role: inferRole(candidate),
    enabled: !candidate.isDisabled,
    visible: candidate.isVisible,
    clickable: isElementClickable(target) || candidate.isClickable,
    bounds: {
      x: rect.x,
      y: rect.y,
      width: rect.width,
      height: rect.height,
    },
    attributes: {
      tagName: candidate.tagName,
      inputType: candidate.inputType || null,
      ariaLabel: concise(candidate.ariaLabel) ?? null,
      title: concise(candidate.title) ?? null,
      placeholder: concise(candidate.placeholder) ?? null,
      hrefType,
      inViewport: candidate.isInViewport,
      actionType: choice.actionType,
      optionButtonFound: choice.optionButtonFound,
      localScore: candidate.score,
      localRank: rank + 1,
    },
  };
}

export function normalizeBrowserCandidates(
  ranked: ScoredCandidate[],
  maximum = MAX_GUIDE_CANDIDATES,
): BrowserCandidateRecord[] {
  const records: BrowserCandidateRecord[] = [];
  const seenTargets = new Set<HTMLElement>();
  for (const candidate of ranked) {
    if (records.length >= maximum) break;
    if (!isMeaningfulRankedCandidate(candidate)) continue;
    const choice = createCandidateChoice(candidate, records.length);
    const target = choice.resolvedTarget.isConnected ? choice.resolvedTarget : choice.target;
    if (seenTargets.has(target)) continue;
    seenTargets.add(target);
    records.push({
      candidate: toUiCandidate(candidate, choice, records.length),
      choice,
      target,
    });
  }
  return records;
}
