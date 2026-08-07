import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ScoredCandidate } from '../types/search';
import { isMeaningfulRankedCandidate, normalizeBrowserCandidates } from './normalizeBrowserCandidates';

function makeCandidate(element: HTMLElement, overrides: Partial<ScoredCandidate> = {}): ScoredCandidate {
  return {
    element,
    searchableText: 'login',
    compactSearchableText: 'login',
    visibleText: 'Login',
    ariaLabel: '',
    labelledByText: '',
    title: '',
    placeholder: '',
    safeValue: '',
    alt: '',
    name: '',
    id: '',
    className: '',
    role: null,
    href: '',
    inputType: '',
    tagName: 'button',
    isVisible: true,
    isInViewport: true,
    isClickable: true,
    isDisabled: false,
    rect: new DOMRect(0, 0, 100, 40),
    score: 100,
    reasons: [],
    ...overrides,
  };
}

describe('browser candidate normalization', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  it('filters hidden, disabled, and low-scoring candidates', () => {
    const element = document.createElement('button');
    expect(isMeaningfulRankedCandidate(makeCandidate(element))).toBe(true);
    expect(isMeaningfulRankedCandidate(makeCandidate(element, { isVisible: false }))).toBe(false);
    expect(isMeaningfulRankedCandidate(makeCandidate(element, { isDisabled: true }))).toBe(false);
    expect(isMeaningfulRankedCandidate(makeCandidate(element, { score: 49 }))).toBe(false);
  });

  it('deduplicates nested candidates that resolve to the same clickable target', () => {
    const button = document.createElement('button');
    const label = document.createElement('span');
    label.textContent = 'Login';
    button.append(label);
    document.body.append(button);
    vi.spyOn(button, 'getBoundingClientRect').mockReturnValue(new DOMRect(10, 10, 100, 40));
    vi.spyOn(label, 'getBoundingClientRect').mockReturnValue(new DOMRect(20, 20, 50, 20));
    const records = normalizeBrowserCandidates([
      makeCandidate(button),
      makeCandidate(label, { tagName: 'span', isClickable: false, score: 90 }),
    ]);
    expect(records).toHaveLength(1);
    expect(records[0].target).toBe(button);
    expect(records[0].candidate.bounds.width).toBe(100);
  });

  it('keeps visible semantic fallback elements for AI even when local text matching is weak', () => {
    const element = document.createElement('button');
    element.textContent = 'My Tickets';
    document.body.append(element);
    vi.spyOn(element, 'getBoundingClientRect').mockReturnValue(new DOMRect(10, 1200, 120, 40));

    const records = normalizeBrowserCandidates([
      makeCandidate(element, {
        searchableText: 'my tickets',
        visibleText: 'My Tickets',
        isInViewport: false,
        score: -1000,
      }),
    ]);

    expect(records).toHaveLength(1);
    expect(records[0].candidate.label).toContain('My Tickets');
    expect(records[0].candidate.attributes?.inViewport).toBe(false);
  });
});
