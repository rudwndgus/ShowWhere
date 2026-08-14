import { createHash } from 'node:crypto';
import { webSemanticTaxonomy } from './taxonomy';

const roleAliases: Record<string, string> = {
  a: 'link', anchor: 'link', hyperlink: 'link', input: 'textbox', edit: 'textbox',
  select: 'combobox', option: 'option', menuitemcheckbox: 'menuitem', menuitemradio: 'menuitem',
};

const stopWords = new Set([
  'the', 'a', 'an', 'your', 'my', 'to', 'of', 'and', 'for', 'in', 'on',
  '내', '나의', '를', '을', '의', '에', '에서', '하기', '보기',
]);

export function normalizeUiName(value: string): string {
  return value.normalize('NFKC').toLowerCase()
    .replace(/[\u200B-\u200D\uFEFF]/gu, '')
    .replace(/\(\s*\d+\s*\)|\[\s*\d+\s*\]|\b\d+\s+(items?|notifications?|messages?)\b/giu, ' ')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
    .replace(/\s+/gu, ' ');
}

export function redactWebText(value: string): string {
  return value
    .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/giu, '[email]')
    .replace(/\b\d{3}-\d{7}-\d{7}\b/gu, '[order-id]')
    .replace(/(?:\+?\d[\s().-]*){9,}/gu, '[phone]')
    .replace(/\b(?:ending\s+in|last\s+four|끝자리)\s*\d{4}\b/giu, '[payment]')
    .replace(/\b\d{1,6}\s+[\p{L}0-9.' -]{2,60}\s(?:street|st|avenue|ave|road|rd|boulevard|blvd|lane|ln|drive|dr|court|ct|way)\b/giu, '[address]')
    .replace(/\b(deliver(?:ing)?\s+to)\s+[\p{L}\p{N}.' -]{1,80}(?=\s+(?:update|change)\s+location\b)/giu, '$1 [location]')
    .replace(/\b(?:hello|hi|deliver(?:ing)?\s+to)\s*,?\s+[\p{L}][\p{L}.' -]{1,60}(?=\s*(?:account|$|[|,;]))/giu, (match) => {
      const prefix = match.match(/^(hello|hi|deliver(?:ing)?\s+to)/iu)?.[0] ?? 'Account';
      return `${prefix} [name]${/\s$/u.test(match) ? ' ' : ''}`;
    })
    .replace(/\b(?:\d[ -]*?){13,19}\b/gu, '[number]')
    .replace(/\b[0-9a-f]{8}-[0-9a-f-]{27,}\b/giu, '[id]')
    .replace(/\baccount\s+for\s+[^|,;]{2,80}$/iu, 'Account')
    .trim();
}

export function meaningfulWebTokens(value: string): string[] {
  return normalizeUiName(value).split(/\s+/u)
    .filter((token) => token.length >= 2 && !stopWords.has(token));
}

export function normalizeWebRole(value: string): string {
  const role = normalizeUiName(value).replace(/\s+/gu, '');
  return roleAliases[role] ?? (role || 'control');
}

export function stableWebId(prefix: string, ...parts: string[]): string {
  const hash = createHash('sha256').update(parts.join('|')).digest('hex').slice(0, 20);
  return `${prefix}-${hash}`;
}

export function semanticLabelFromRules(value: string): string | undefined {
  const normalized = normalizeUiName(value);
  if (!normalized) return undefined;

  // Resolve action phrases before generic nouns. Controls such as
  // "Hello, sign in Account & Lists" contain both "sign in" and "account";
  // the action is login, while account is merely the surrounding destination.
  if (/(?:sign out|log out|logout|로그아웃)/u.test(normalized)) return 'logout';
  if (/(?:sign in|log in|login|로그인)/u.test(normalized)) return 'login';
  let best: { id: string; score: number } | undefined;
  for (const label of webSemanticTaxonomy) {
    for (const aliasValue of label.aliases) {
      const alias = normalizeUiName(aliasValue);
      let score = 0;
      if (normalized === alias) score = 1_000 + alias.length;
      else if (alias.length >= 3 && normalized.includes(alias)) score = 700 + alias.length;
      else {
        const aliasTokens = meaningfulWebTokens(alias);
        const nameTokens = meaningfulWebTokens(normalized);
        const overlap = aliasTokens.filter((token) => nameTokens.some((name) => name === token)).length;
        if (aliasTokens.length > 0) score = Math.round(500 * overlap / aliasTokens.length);
      }
      if (!best || score > best.score) best = { id: label.id, score };
    }
  }
  return best && best.score >= 500 ? best.id : undefined;
}

export function semanticAliases(labelId: string): string[] {
  return webSemanticTaxonomy.find((item) => item.id === labelId)?.aliases ?? [];
}

const dangerousPattern = /delete|remove|erase|destroy|buy\s*now|purchase|checkout|place\s*order|pay\b|submit|send\b|post\b|publish|confirm\s*order|sign\s*out|log\s*out|unsubscribe|cancel\s*(order|subscription)|withdraw|transfer|\uc0ad제|\uc81c거|\uad6c매|\uacb0제|\uc8fc문\s*\ud655정|\uc81c출|\uc804송|\ubc1c\uc1a1|\uac8c\uc2dc|\ub85c그아웃|\uad6c독\s*\ud574지|\uc8fc문\s*\ucde8소|\uc1a1금/u;

export function classifyInteractionRisk(name: string, role: string, inputType?: string, href?: string): 'safe' | 'blocked' | 'unknown' {
  const searchable = normalizeUiName(`${name} ${href ?? ''}`);
  if (dangerousPattern.test(searchable)) return 'blocked';
  if (href && /^(mailto:|tel:|javascript:)/iu.test(href)) return 'blocked';
  if (['password', 'file'].includes((inputType ?? '').toLowerCase())) return 'blocked';
  if (['checkbox', 'radio', 'switch', 'slider'].includes(role)) return 'blocked';
  if (['link', 'tab', 'menuitem', 'combobox'].includes(role)) return 'safe';
  if (role === 'button' && (inputType ?? 'button').toLowerCase() !== 'submit') return 'unknown';
  return 'blocked';
}

export function safeUrlForStorage(value: string): string {
  try {
    const url = new URL(value);
    url.username = '';
    url.password = '';
    url.search = '';
    url.hash = '';
    url.pathname = url.pathname
      .replace(/\b\d{3}-\d{7}-\d{7}\b/gu, ':id')
      .replace(/\b[0-9a-f]{8}-[0-9a-f-]{27,}\b/giu, ':id')
      .replace(/\/(?=\d{8,}(?:\/|$))\d+/gu, '/:id');
    return url.toString();
  } catch {
    return value.split(/[?#]/u)[0];
  }
}

export function siteIdFromUrl(value: string): string {
  const host = new URL(value).hostname.toLowerCase().replace(/^www\./u, '');
  return host.replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '') || 'website';
}
