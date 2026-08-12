const secretPatterns: Array<[RegExp, string]> = [
  [/\b(?:sk|hf|ghp|gho|github_pat)_[A-Za-z0-9_-]{12,}\b/gu, '[REDACTED_TOKEN]'],
  [/\bBearer\s+[A-Za-z0-9._~+/-]+=*\b/giu, 'Bearer [REDACTED_TOKEN]'],
  [/(?:api[_ -]?key|password|passwd|token|secret|cvv)\s*[:=]\s*[^\s,;"']{4,}/giu, '$1=[REDACTED]'],
  [/\b(?:\d[ -]*?){13,19}\b/gu, '[REDACTED_CARD]'],
  [/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/giu, '[REDACTED_EMAIL]'],
];

export function scrubText(value: string): string {
  return secretPatterns.reduce((text, [pattern, replacement]) => text.replace(pattern, replacement), value);
}

export function scrubForLearning<T>(value: T): T {
  if (typeof value === 'string') return scrubText(value) as T;
  if (Array.isArray(value)) return value.map((item) => scrubForLearning(item)) as T;
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, scrubForLearning(item)])) as T;
  }
  return value;
}

export function containsSensitiveMaterial(value: unknown): boolean {
  if (typeof value === 'string') {
    return secretPatterns.some(([pattern]) => {
      pattern.lastIndex = 0;
      return pattern.test(value);
    });
  }
  if (Array.isArray(value)) return value.some(containsSensitiveMaterial);
  if (value !== null && typeof value === 'object') return Object.values(value).some(containsSensitiveMaterial);
  return false;
}
