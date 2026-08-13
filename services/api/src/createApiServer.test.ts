import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import type { AiProvider } from '../../../src/guide-api/AiProvider';
import type { ApiConfig } from './config';
import { createApiServer } from './createApiServer';
import { guideRequestFixture } from './testFixtures';

const config: ApiConfig = {
  host: '127.0.0.1',
  port: 0,
  maxRequestBytes: 100_000,
  webKnowledgeDirectory: 'knowledge/web',
  debug: false,
  security: {
    rateLimitWindowMs: 60_000,
    rateLimitMaxRequests: 20,
    trustProxy: false,
  },
  openai: {
    apiKey: 'test-key',
    fastModel: 'gpt-5.6-luna',
    model: 'gpt-5.6-terra',
    strongModel: 'gpt-5.6-sol',
    baseUrl: 'https://api.openai.com/v1',
    requestTimeoutMs: 30_000,
    maxRetries: 1,
  },
};

const servers: ReturnType<typeof createApiServer>[] = [];

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) =>
    new Promise<void>((resolve) => server.close(() => resolve()))));
});

async function listen(provider: AiProvider): Promise<string> {
  return listenWithConfig(config, provider);
}

async function listenWithConfig(serverConfig: ApiConfig, provider: AiProvider): Promise<string> {
  const server = createApiServer(serverConfig, provider);
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address() as AddressInfo;
  return `http://127.0.0.1:${address.port}/api/guide`;
}

describe('GET /health', () => {
  it('reports readiness without calling the AI provider', async () => {
    let providerCalls = 0;
    const guideEndpoint = await listen({
      async decideNextAction() {
        providerCalls += 1;
        return {};
      },
    });
    const response = await fetch(guideEndpoint.replace('/api/guide', '/health'));

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ status: 'ok' });
    expect(providerCalls).toBe(0);
  });
});

describe('POST /api/guide', () => {
  it('requires the configured client bearer token', async () => {
    const securedConfig = { ...config, security: { ...config.security, clientToken: 'test-client-token-at-least-24-characters' } };
    const endpoint = await listenWithConfig(securedConfig, { async decideNextAction() { return {}; } });

    const unauthorized = await fetch(endpoint, { method: 'POST', body: '{}' });
    const authorized = await fetch(endpoint, {
      method: 'POST',
      headers: { Authorization: 'Bearer test-client-token-at-least-24-characters', 'Content-Type': 'application/json' },
      body: '{}',
    });

    expect(unauthorized.status).toBe(401);
    expect(authorized.status).toBe(400);
  });

  it('limits repeated requests before invoking the provider', async () => {
    let providerCalls = 0;
    const limitedConfig = { ...config, security: { ...config.security, rateLimitMaxRequests: 1 } };
    const endpoint = await listenWithConfig(limitedConfig, {
      async decideNextAction() { providerCalls += 1; return {}; },
    });
    const options = { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(guideRequestFixture) };

    await fetch(endpoint, options);
    const limited = await fetch(endpoint, options);

    expect(limited.status).toBe(429);
    expect(limited.headers.get('retry-after')).toBe('60');
    expect(providerCalls).toBe(1);
  });

  it('returns only a safe decision when a provider throws a secret-bearing error', async () => {
    const endpoint = await listen({
      async decideNextAction() {
        throw new Error('upstream payload and provider-secret-do-not-leak');
      },
    });
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(guideRequestFixture),
    });
    const text = await response.text();

    expect(response.status).toBe(502);
    expect(text).not.toContain('provider-secret-do-not-leak');
    expect(JSON.parse(text).action).toBe('ask_user');
  });
});
