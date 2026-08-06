import type { ApplicationContext } from '../../contracts';

function getSafePageUrl(location: Location): string | undefined {
  try {
    const url = new URL(location.href);
    if (!['http:', 'https:', 'file:'].includes(url.protocol)) return undefined;
    return url.protocol === 'file:' ? `${url.protocol}//${url.pathname}` : `${url.origin}${url.pathname}`;
  } catch {
    return undefined;
  }
}

export function createBrowserApplicationContext(
  document: Document = window.document,
  location: Location = window.location,
): ApplicationContext {
  const hostname = location.hostname.trim();
  const title = document.title.trim();
  const locale = document.documentElement.lang.trim() || navigator.language;
  return {
    platform: 'browser',
    applicationName: hostname || title || 'Browser',
    windowTitle: title || undefined,
    url: getSafePageUrl(location),
    locale: locale || undefined,
  };
}
