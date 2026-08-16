export function isKoreanRequest(text: string | undefined): boolean {
  return /[\uAC00-\uD7A3]/u.test(text ?? '');
}

export function inRequestLanguage(
  text: string | undefined,
  korean: string,
  english: string,
): string {
  return isKoreanRequest(text) ? korean : english;
}
