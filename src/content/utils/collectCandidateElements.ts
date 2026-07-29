import type { CandidateElement } from '../types/search';
import {
  getElementViewportRect,
  isDisabled,
  isElementClickable,
  isElementInViewport,
  isElementVisible,
} from './elementVisibility';
import { getElementSearchText } from './getElementSearchText';

const CANDIDATE_SELECTOR = [
  'button', 'a', 'input', 'textarea', 'select', 'option', 'label', 'summary',
  '[role="button"]', '[role="link"]', '[role="menuitem"]', '[role="tab"]',
  '[role="checkbox"]', '[role="radio"]', '[role="switch"]', '[role="textbox"]',
  '[tabindex]', '[onclick]', '[aria-label]', '[title]',
].join(',');

const MAX_CANDIDATES = 4000;

function belongsToShowWhere(element: HTMLElement): boolean {
  if (element.id === 'showwhere-extension-root') return true;
  const root = element.getRootNode();
  return root instanceof ShadowRoot && (root.host as HTMLElement).id === 'showwhere-extension-root';
}

function collectFromRoot(
  root: Document | ShadowRoot,
  results: CandidateElement[],
  seen: Set<HTMLElement>,
) {
  if (results.length >= MAX_CANDIDATES) return;

  for (const element of root.querySelectorAll<HTMLElement>(CANDIDATE_SELECTOR)) {
    if (results.length >= MAX_CANDIDATES) break;
    if (seen.has(element) || belongsToShowWhere(element)) continue;
    seen.add(element);

    const fields = getElementSearchText(element);
    const rect = getElementViewportRect(element);
    results.push({
      element,
      ...fields,
      role: element.getAttribute('role'),
      tagName: element.tagName.toLocaleLowerCase(),
      inputType: element instanceof HTMLInputElement ? element.type.toLocaleLowerCase() : '',
      isVisible: isElementVisible(element),
      isInViewport: isElementInViewport(element),
      isClickable: isElementClickable(element),
      isDisabled: isDisabled(element),
      rect,
    });
  }

  for (const element of root.querySelectorAll<HTMLElement>('*')) {
    if (results.length >= MAX_CANDIDATES) break;
    if (element.id === 'showwhere-extension-root') continue;
    if (element.shadowRoot) collectFromRoot(element.shadowRoot, results, seen);
    if (element instanceof HTMLIFrameElement) {
      try {
        if (element.contentDocument) collectFromRoot(element.contentDocument, results, seen);
      } catch {
        // Cross-origin frames are intentionally skipped.
      }
    }
  }
}

export function collectCandidateElements(): CandidateElement[] {
  const results: CandidateElement[] = [];
  collectFromRoot(document, results, new Set());
  return results;
}
