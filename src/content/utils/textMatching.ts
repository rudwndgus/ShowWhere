export function normalizeComparableText(value: string): string {
  return value
    .toLocaleLowerCase()
    .normalize('NFKC')
    .replace(/[\u2010-\u2015_-]+/g, ' ')
    .replace(/[^\p{L}\p{N}\s]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function compactText(value: string): string {
  return normalizeComparableText(value).replace(/\s+/g, '');
}

export function simpleForms(value: string): string[] {
  const normalized = normalizeComparableText(value);
  const compact = compactText(value);
  const singular = normalized.endsWith('s') && normalized.length > 3
    ? normalized.slice(0, -1)
    : normalized;
  return [...new Set([normalized, compact, singular, singular.replace(/\s+/g, '')].filter(Boolean))];
}

export function fuzzyIncludes(haystack: string, needle: string): boolean {
  if (!needle) return false;
  const haystackForms = simpleForms(haystack);
  const needleForms = simpleForms(needle);
  return haystackForms.some((left) => needleForms.some((right) => left.includes(right)));
}

export function fuzzyEquals(left: string, right: string): boolean {
  const leftForms = simpleForms(left);
  const rightForms = simpleForms(right);
  return leftForms.some((leftForm) => rightForms.includes(leftForm));
}
