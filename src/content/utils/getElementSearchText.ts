import { compactText, normalizeComparableText } from './textMatching';

const MAX_FIELD_LENGTH = 500;

function clean(value: string | null | undefined): string {
  return (value ?? '').replace(/\s+/g, ' ').trim().slice(0, MAX_FIELD_LENGTH);
}

export function getLabelledByText(element: HTMLElement): string {
  const ids = element.getAttribute('aria-labelledby')?.split(/\s+/).filter(Boolean) ?? [];
  return clean(
    ids
      .map((id) => element.ownerDocument.getElementById(id)?.textContent)
      .filter(Boolean)
      .join(' '),
  );
}

export function getSafeValue(element: HTMLElement): string {
  if (!(element instanceof HTMLInputElement)) return '';
  const type = element.type.toLocaleLowerCase();
  return ['button', 'submit', 'reset'].includes(type) ? clean(element.value) : '';
}

export function getSafeHref(element: HTMLElement): string {
  if (!(element instanceof HTMLAnchorElement) || !element.hasAttribute('href')) return '';
  try {
    const url = new URL(element.href, element.ownerDocument.baseURI);
    return clean(`${url.hostname} ${url.pathname}`);
  } catch {
    return clean(element.getAttribute('href'));
  }
}

export function getVisibleText(element: HTMLElement): string {
  const innerText = 'innerText' in element ? element.innerText : '';
  return clean(innerText || element.textContent);
}

export interface ElementSearchFields {
  searchableText: string;
  compactSearchableText: string;
  visibleText: string;
  ariaLabel: string;
  labelledByText: string;
  title: string;
  placeholder: string;
  safeValue: string;
  alt: string;
  name: string;
  id: string;
  className: string;
  href: string;
}

export function getElementSearchText(element: HTMLElement): ElementSearchFields {
  const visibleText = getVisibleText(element);
  const ariaLabel = clean(element.getAttribute('aria-label'));
  const labelledByText = getLabelledByText(element);
  const title = clean(element.getAttribute('title'));
  const placeholder = clean(element.getAttribute('placeholder'));
  const safeValue = getSafeValue(element);
  const alt = clean(element.getAttribute('alt'));
  const name = clean(element.getAttribute('name'));
  const id = clean(element.id);
  const className = clean(typeof element.className === 'string' ? element.className : '');
  const role = clean(element.getAttribute('role'));
  const href = getSafeHref(element);
  const searchableText = normalizeComparableText([
    visibleText,
    ariaLabel,
    labelledByText,
    title,
    placeholder,
    safeValue,
    alt,
    name,
    id,
    className,
    role,
    href,
  ].filter(Boolean).join(' '));

  return {
    searchableText,
    compactSearchableText: compactText(searchableText),
    visibleText,
    ariaLabel,
    labelledByText,
    title,
    placeholder,
    safeValue,
    alt,
    name,
    id,
    className,
    href,
  };
}
