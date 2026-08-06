import { describe, expect, it, vi } from 'vitest';
import { guideRequestFixture } from '../testFixtures';
import { FeatherlessProvider, type FeatherlessProviderOptions } from './FeatherlessProvider';

const validDecision = {
  status: 'in_progress',
  action: 'highlight',
  targetId: 'candidate-settings',
  message: 'Select Settings.',
  expectedChange: 'Settings opens.',
  confidence: 0.94,
};

function completion(content: string, status = 200): Response {
  return new Response(JSON.stringify({ choices: [{ message: { content } }] }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function options(overrides: Partial<FeatherlessProviderOptions> = {}): FeatherlessProviderOptions {
  return {
    apiKey: 'test-key',
    baseUrl: 'https://provider.example/v1',
    model: 'configured-guide-model',
    requestTimeoutMs: 1_000,
    maxRetries: 1,
    retryBaseDelayMs: 0,
    maxTokens: 800,
    ...overrides,
  };
}

describe('FeatherlessProvider', () => {
  it('uses the configured endpoint, bearer token, and model', async () => {
    const fetchImplementation = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    const [url, init] = fetchImplementation.mock.calls[0];
    expect(url).toBe('https://provider.example/v1/chat/completions');
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer test-key');
    expect(JSON.parse(String(init?.body)).model).toBe('configured-guide-model');
  });

  it('retries a bounded transient provider failure', async () => {
    const fetchImplementation = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(completion('{}', 503))
      .mockResolvedValueOnce(completion(JSON.stringify(validDecision)));
    const delay = vi.fn(async () => undefined);
    const provider = new FeatherlessProvider(options({ fetchImplementation, delay }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    expect(delay).toHaveBeenCalledTimes(1);
  });

  it('retries Featherless model-busy responses even when they use HTTP 400', async () => {
    const busyResponse = new Response(JSON.stringify({
      error: { message: 'This model is busy, please try again later.' },
    }), { status: 400, headers: { 'Content-Type': 'application/json' } });
    const fetchImplementation = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(busyResponse)
      .mockResolvedValueOnce(completion(JSON.stringify(validDecision)));
    const delay = vi.fn(async () => undefined);
    const provider = new FeatherlessProvider(options({ fetchImplementation, delay }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    expect(delay).toHaveBeenCalledTimes(1);
  });

  it('makes one repair request after malformed model output', async () => {
    const fetchImplementation = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(completion('not JSON'))
      .mockResolvedValueOnce(completion(`\`\`\`json\n${JSON.stringify(validDecision)}\n\`\`\``));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    const secondBody = JSON.parse(String(fetchImplementation.mock.calls[1][1]?.body));
    expect(secondBody.messages.some((item: { content: string }) =>
      item.content.includes('previous response was invalid'))).toBe(true);
  });

  it('times out without surfacing credentials or raw provider errors', async () => {
    const fetchImplementation = vi.fn((_url: RequestInfo | URL, init?: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('raw secret test-key')));
      }));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      requestTimeoutMs: 5,
      maxRetries: 0,
    }));

    const promise = provider.decideNextAction(guideRequestFixture);
    await expect(promise).rejects.toThrow('Featherless provider request failed.');
    await expect(promise).rejects.not.toThrow('test-key');
  });
});
