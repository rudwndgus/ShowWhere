import type { CandidateActionType } from '../types/search';
import { DEBUG_MATCHING } from './debug';

const CLICKABLE_SELECTOR = [
  'button', 'a[href]', 'input[type="button"]', 'input[type="submit"]',
  '[role="button"]', '[role="link"]', '[role="menuitem"]', '[role="option"]',
  '[role="tab"]', '[onclick]', '[tabindex="0"]',
].join(',');

const OPTION_SELECTOR = [
  'button[aria-label*="option" i]', 'button[aria-label*="options" i]',
  'button[aria-label*="more" i]', 'button[aria-label*="menu" i]',
  'button[aria-label*="action" i]', '[role="button"][aria-label*="option" i]',
  '[role="button"][aria-label*="more" i]', 'button[title*="option" i]',
  'button[title*="more" i]', 'button[title*="menu" i]',
  'button[aria-label*="옵션"]', 'button[aria-label*="더보기"]',
  'button[aria-label*="메뉴"]', '[role="button"][aria-label*="옵션"]',
  'button[title*="옵션"]', 'button[title*="더보기"]',
].join(',');

export interface ResolvedTarget {
  target: HTMLElement;
  actionType: CandidateActionType;
  optionButtonFound: boolean;
}

function isTooLarge(element: HTMLElement): boolean {
  const rect = element.getBoundingClientRect();
  return (
    rect.width >= window.innerWidth * 0.9 ||
    rect.height >= window.innerHeight * 0.5 ||
    ['BODY', 'HTML', 'MAIN', 'SECTION'].includes(element.tagName)
  );
}

function isClickable(element: HTMLElement): boolean {
  if (element.matches(CLICKABLE_SELECTOR)) return true;
  const view = element.ownerDocument.defaultView ?? window;
  return view.getComputedStyle(element).cursor === 'pointer';
}

function inferActionType(label: string): CandidateActionType {
  const normalized = label.toLocaleLowerCase();
  if (
    /(option|options|more|menu|actions|옵션|더보기|점 세 개)/u.test(normalized) ||
    /^[.…•⋮⋯\s]{3,}$/u.test(normalized)
  ) return 'options';
  if (/(open|열기|열어)/u.test(normalized)) return 'open';
  if (/(select|선택)/u.test(normalized)) return 'select';
  return 'click';
}

function findFallbackRow(element: HTMLElement, label: string): HTMLElement | null {
  const meaningfulLabel = label
    .replace(/(대화\s*)?(옵션|option|options|more|menu|actions|열기|open)/giu, ' ')
    .replace(/\s+/g, ' ')
    .trim();
  let row: HTMLElement | null = element;
  for (let depth = 0; row && depth < 5; depth += 1, row = row.parentElement) {
    if (isTooLarge(row)) break;
    const rect = row.getBoundingClientRect();
    const rowText = (row.innerText || row.textContent || '').replace(/\s+/g, ' ').trim();
    const hasExpectedName = !meaningfulLabel || rowText.includes(meaningfulLabel);
    const looksLikeRow =
      /(conversation|dialog|chat|message|item|entry|row|대화)/iu.test(row.className) ||
      row.children.length >= 2;
    if (
      hasExpectedName && looksLikeRow &&
      rect.width >= 80 && rect.height >= 28 &&
      rect.width < window.innerWidth * 0.9 && rect.height < window.innerHeight * 0.5
    ) return row;
  }
  return null;
}

function findOptionButton(element: HTMLElement): HTMLElement | null {
  if (element.matches(OPTION_SELECTOR)) return element;
  let row: HTMLElement | null = element;
  for (let depth = 0; row && depth <= 5; depth += 1, row = row.parentElement) {
    if (isTooLarge(row)) break;
    const explicit = row.querySelector<HTMLElement>(OPTION_SELECTOR);
    if (explicit) return explicit;

    const rowRect = row.getBoundingClientRect();
    const smallButtons = Array.from(row.querySelectorAll<HTMLElement>(CLICKABLE_SELECTOR))
      .filter((button) => {
        const rect = button.getBoundingClientRect();
        return (
          rect.width >= 20 && rect.width <= 50 && rect.height >= 20 && rect.height <= 50 &&
          rect.left >= rowRect.left + rowRect.width * 0.55
        );
      });
    if (smallButtons.length > 0) return smallButtons[smallButtons.length - 1];
  }
  return null;
}

export function resolveClickableTarget(element: HTMLElement, label: string): ResolvedTarget {
  const actionType = inferActionType(label);
  const optionButton = actionType === 'options' || /대화.*(열기|옵션)/u.test(label)
    ? findOptionButton(element)
    : null;
  if (optionButton) {
    const result = { target: optionButton, actionType: 'options' as const, optionButtonFound: true };
    debugResolution(element, result, label);
    return result;
  }

  if (actionType === 'options') {
    const fallbackRow = findFallbackRow(element, label);
    if (fallbackRow) {
      const result = { target: fallbackRow, actionType, optionButtonFound: false };
      debugResolution(element, result, label);
      return result;
    }
  }

  let current: HTMLElement | null = element;
  let depth = 0;
  while (current && depth <= 5) {
    if (isClickable(current) && !isTooLarge(current)) {
      const result = { target: current, actionType, optionButtonFound: false };
      debugResolution(element, result, label);
      return result;
    }
    current = current.parentElement;
    depth += 1;
  }

  const result = { target: element, actionType, optionButtonFound: false };
  debugResolution(element, result, label);
  return result;
}

function debugResolution(original: HTMLElement, result: ResolvedTarget, label: string) {
  if (!DEBUG_MATCHING) return;
  console.log('[ShowWhere] target resolution', {
    original,
    resolvedTarget: result.target,
    originalRect: original.getBoundingClientRect(),
    finalRect: result.target.getBoundingClientRect(),
    actionType: result.actionType,
    optionButtonFound: result.optionButtonFound,
    label,
  });
}
