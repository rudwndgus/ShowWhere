import { describe, expect, test, vi } from 'vitest';
import { z } from 'zod';
import { LearningProvider } from './provider';

describe('LearningProvider', () => {
  test('honors Retry-After for a rate-limited request', async () => {
    const fetchImplementation = vi.fn()
      .mockResolvedValueOnce(new Response('busy', { status: 429, headers: { 'Retry-After': '2' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ choices: [{ message: { content: '{"ok":true}' } }] }), { status: 200 }));
    const delay = vi.fn().mockResolvedValue(undefined);
    const provider = new LearningProvider({
      apiKey: 'test', baseUrl: 'https://example.test/v1', timeoutMs: 1_000,
      maxTokens: 256, retries: 1, fetchImplementation, delay,
    });
    await expect(provider.completeJson('model', 'system', {}, z.object({ ok: z.literal(true) }))).resolves.toEqual({ ok: true });
    expect(delay).toHaveBeenCalledWith(2_000);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
  });
});
