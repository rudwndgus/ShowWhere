import { describe, expect, it, vi } from 'vitest';
import { loadRobotsPolicy } from './robots';

describe('robots policy', () => {
  it('honors the longest matching allow/disallow rule', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response([
      'User-agent: *',
      'Disallow: /private/',
      'Allow: /private/help',
    ].join('\n')));
    const policy = await loadRobotsPolicy('https://example.com', 'ShowWhereCrawler', fetcher);
    expect(policy.allows('https://example.com/public')).toBe(true);
    expect(policy.allows('https://example.com/private/orders')).toBe(false);
    expect(policy.allows('https://example.com/private/help')).toBe(true);
  });
});
