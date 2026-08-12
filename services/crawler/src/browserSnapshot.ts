import type { Page } from 'playwright';
import type { CrawlPathStep, RawWebElement, RawWebState, WebLocatorHint } from '../../../src/web-knowledge';
import {
  classifyInteractionRisk,
  normalizeUiName,
  normalizeWebRole,
  redactWebText,
  safeUrlForStorage,
  stableWebId,
} from '../../../src/web-knowledge';

interface BrowserElementObservation {
  name: string;
  role: string;
  tag: string;
  area: string;
  container?: string;
  region: RawWebElement['region'];
  href?: string;
  inputType?: string;
  disabled: boolean;
  locator: WebLocatorHint;
}

function isSameOrigin(href: string | undefined, origin: string): boolean {
  if (!href) return true;
  try { return new URL(href).origin === origin; } catch { return false; }
}

export async function observeBrowserState(
  page: Page,
  depth: number,
  path: CrawlPathStep[],
  maximumElements = 250,
): Promise<RawWebState> {
  // tsx/esbuild preserves nested function names with this helper. Playwright
  // serializes only the callback, so make the harmless helper available inside
  // the page before evaluating the self-contained collector.
  await page.evaluate('var __name = globalThis.__name || ((target) => target);');
  const observations = await page.locator([
    'a[href]', 'button', 'input', 'select', 'textarea', 'summary',
    '[role="button"]', '[role="link"]', '[role="menuitem"]', '[role="tab"]',
    '[role="combobox"]', '[role="searchbox"]', '[role="textbox"]',
    '[aria-haspopup="true"]', '[tabindex]:not([tabindex="-1"])',
  ].join(',')).evaluateAll((nodes, limit) => {
    const compact = (value: string | null | undefined, max = 180) =>
      (value ?? '').replace(/\s+/gu, ' ').trim().slice(0, max);
    const implicitRole = (element: Element): string => {
      const tag = element.tagName.toLowerCase();
      const inputType = (element.getAttribute('type') ?? '').toLowerCase();
      if (tag === 'a' && element.hasAttribute('href')) return 'link';
      if (tag === 'button' || tag === 'summary') return 'button';
      if (tag === 'select') return 'combobox';
      if (tag === 'textarea') return 'textbox';
      if (tag === 'input') {
        if (['button', 'submit', 'reset', 'image'].includes(inputType)) return 'button';
        if (inputType === 'checkbox') return 'checkbox';
        if (inputType === 'radio') return 'radio';
        if (inputType === 'range') return 'slider';
        if (inputType === 'search') return 'searchbox';
        return 'textbox';
      }
      return 'control';
    };
    const accessibleName = (element: Element): string => {
      const aria = compact(element.getAttribute('aria-label'));
      if (aria) return aria;
      const labelledBy = element.getAttribute('aria-labelledby');
      if (labelledBy) {
        const text = labelledBy.split(/\s+/u).map((id) => document.getElementById(id)?.textContent ?? '').join(' ');
        if (compact(text)) return compact(text);
      }
      if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
        const labelText = Array.from(element.labels ?? []).map((label) => label.textContent ?? '').join(' ');
        if (compact(labelText)) return compact(labelText);
        if ('placeholder' in element && compact(element.placeholder)) return compact(element.placeholder);
        if (compact(element.value) && ['button', 'submit', 'reset'].includes(element.type)) return compact(element.value);
      }
      const title = compact(element.getAttribute('title'));
      const alt = compact(element.getAttribute('alt'));
      return compact(element.textContent) || title || alt;
    };
    const landmark = (element: Element): { area: string; container?: string } => {
      const parent = element.parentElement?.closest('header,nav,main,aside,footer,dialog,[role="navigation"],[role="main"],[role="dialog"],[role="menu"],[role="toolbar"],[aria-label]');
      if (!parent) return { area: 'document' };
      const role = parent.getAttribute('role') || parent.tagName.toLowerCase();
      const label = compact(parent.getAttribute('aria-label') || parent.getAttribute('title'));
      return { area: role, ...(label ? { container: label } : {}) };
    };
    const seen = new Map<string, number>();
    const result: BrowserElementObservation[] = [];
    for (const element of nodes) {
      if (!(element instanceof HTMLElement) || result.length >= Number(limit)) break;
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      if (style.visibility === 'hidden' || style.display === 'none' || Number(style.opacity) === 0
        || rect.width < 2 || rect.height < 2 || rect.bottom < 0 || rect.right < 0
        || rect.top > innerHeight || rect.left > innerWidth) continue;
      const name = accessibleName(element);
      if (!name) continue;
      const role = compact(element.getAttribute('role')) || implicitRole(element);
      const key = `${role}|${name}`;
      const ordinal = seen.get(key) ?? 0;
      seen.set(key, ordinal + 1);
      const testId = compact(element.getAttribute('data-testid') || element.getAttribute('data-test'));
      const id = compact(element.id);
      const href = element instanceof HTMLAnchorElement ? element.href : undefined;
      const inputType = element instanceof HTMLInputElement ? element.type : undefined;
      const landmarkInfo = landmark(element);
      const centerX = rect.left + rect.width / 2;
      const centerY = rect.top + rect.height / 2;
      const horizontal = centerX < innerWidth * 0.25 ? 'left' : centerX > innerWidth * 0.75 ? 'right' : 'center';
      const region = centerY < innerHeight * 0.22 ? 'top' : centerY > innerHeight * 0.78 ? 'bottom' : horizontal;
      const locator: WebLocatorHint = {
        role,
        name,
        ...(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement
          ? { placeholder: compact(element.getAttribute('placeholder')) || undefined } : {}),
        ...(testId ? { testId } : {}),
        ...(href ? { href } : {}),
        ...(id ? { css: `#${CSS.escape(id)}` } : {}),
        ...(ordinal > 0 ? { ordinal } : {}),
      };
      result.push({
        name, role, tag: element.tagName.toLowerCase(), ...landmarkInfo, region,
        ...(href ? { href } : {}), ...(inputType ? { inputType } : {}),
        disabled: element.matches(':disabled,[aria-disabled="true"]'), locator,
      });
    }
    return result;
  }, maximumElements) as BrowserElementObservation[];

  const origin = new URL(page.url()).origin;
  const elements = observations.map((source) => {
    const safeName = redactWebText(source.name);
    const safeContainer = source.container ? redactWebText(source.container) : undefined;
    const role = normalizeWebRole(source.role);
    const storedHref = source.href ? safeUrlForStorage(source.href) : undefined;
    const locator = {
      ...source.locator,
      role,
      name: safeName,
      ...(storedHref ? { href: storedHref } : {}),
    };
    return {
      id: stableWebId('element', role, normalizeUiName(safeName), source.area, storedHref ?? ''),
      name: safeName,
      normalizedName: normalizeUiName(safeName),
      role,
      tag: source.tag,
      area: source.area,
      ...(safeContainer ? { container: safeContainer } : {}),
      region: source.region,
      ...(storedHref ? { href: storedHref } : {}),
      ...(source.inputType ? { inputType: source.inputType } : {}),
      disabled: source.disabled,
      locator,
      risk: source.disabled || !isSameOrigin(source.href, origin)
        ? 'blocked' as const
        : classifyInteractionRisk(safeName, role, source.inputType, source.href),
    } satisfies RawWebElement;
  });
  const url = safeUrlForStorage(page.url());
  const title = redactWebText((await page.title()).replace(/\s+/gu, ' ').trim().slice(0, 300));
  const evidence = [...new Set([title, ...elements.map((item) => item.name)])].filter(Boolean).slice(0, 60);
  const signature = elements.slice(0, 100).map((item) => `${item.role}:${item.normalizedName}`).sort().join('|');
  return {
    id: stableWebId('state', url, title, signature),
    url,
    title,
    depth,
    path,
    evidence,
    elements,
    observedAt: new Date().toISOString(),
  };
}

export async function locatePathStep(page: Page, step: CrawlPathStep) {
  const hint = step.locator;
  let locator;
  if (hint.testId) locator = page.getByTestId(hint.testId);
  else if (hint.role && hint.name) locator = page.getByRole(hint.role as never, { name: hint.name, exact: true });
  else if (hint.label) locator = page.getByLabel(hint.label, { exact: true });
  else if (hint.placeholder) locator = page.getByPlaceholder(hint.placeholder, { exact: true });
  else if (hint.css) locator = page.locator(hint.css);
  else if (hint.href) locator = page.locator(`a[href="${hint.href.replace(/"/gu, '\\"')}"]`);
  else locator = page.getByText(step.name, { exact: true });
  return locator.nth(hint.ordinal ?? 0);
}
