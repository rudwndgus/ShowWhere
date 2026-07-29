import type { CandidateElement } from '../types/search';

export function createElementLabel(candidate: CandidateElement, index = 0): string {
  const preferred = [
    candidate.ariaLabel,
    candidate.visibleText,
    candidate.title,
    candidate.labelledByText,
    candidate.placeholder,
    candidate.safeValue,
    candidate.alt,
  ].find((value) => value.trim().length > 0);

  if (preferred) return truncateLabel(removeDuplicatePhrase(preferred), 55);
  const number = index + 1;
  if (candidate.tagName === 'a' || candidate.role === 'link') return `링크 ${number}`;
  if (['input', 'textarea', 'select'].includes(candidate.tagName)) return `입력창 ${number}`;
  return `버튼 ${number}`;
}

export function truncateLabel(value: string, maximum: number): string {
  const cleaned = value.replace(/\s+/g, ' ').trim();
  return cleaned.length > maximum ? `${cleaned.slice(0, maximum - 1).trim()}…` : cleaned;
}

function removeDuplicatePhrase(value: string): string {
  const cleaned = value.replace(/\s+/g, ' ').trim();
  const words = cleaned.split(' ');
  if (words.length % 2 === 0) {
    const middle = words.length / 2;
    if (words.slice(0, middle).join(' ') === words.slice(middle).join(' ')) {
      return words.slice(0, middle).join(' ');
    }
  }
  return cleaned.replace(/(.{3,}?)\s+\1$/u, '$1');
}
