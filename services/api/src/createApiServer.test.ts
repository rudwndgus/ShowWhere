import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import { WebSocket } from 'ws';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
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
  centralDataDirectory: 'data/test-central',
  pairingTtlSeconds: 60,
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
const temporaryDirectories: string[] = [];

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) =>
    new Promise<void>((resolve) => server.close(() => resolve()))));
  await Promise.all(temporaryDirectories.splice(0).map((path) => rm(path, { recursive: true, force: true })));
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
    expect(await response.json()).toEqual({ status: 'ok', services: { guide: true, sync: true, pairing: true } });
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

describe('central knowledge API', () => {
  it('allows every client to pull but only developer/admin roles to upload', async () => {
    const centralDataDirectory = await mkdtemp(join(tmpdir(), 'showwhere-api-central-'));
    temporaryDirectories.push(centralDataDirectory);
    const securedConfig: ApiConfig = {
      ...config,
      centralDataDirectory,
      security: {
        ...config.security,
        clientToken: 'user-client-token-at-least-24-characters',
        developerToken: 'developer-token-at-least-24-characters',
      },
    };
    const guide = await listenWithConfig(securedConfig, { async decideNextAction() { return {}; } });
    const base = guide.replace('/api/guide', '');
    const body = JSON.stringify({ records: [{
      id: 'feedback-1', kind: 'feedback', updatedAt: '2026-08-13T12:00:00.000Z', payload: { rating: 'correct' },
    }] });
    const userUpload = await fetch(`${base}/api/knowledge/records`, {
      method: 'POST', headers: { authorization: 'Bearer user-client-token-at-least-24-characters', 'content-type': 'application/json' }, body,
    });
    const developerUpload = await fetch(`${base}/api/knowledge/records`, {
      method: 'POST', headers: { authorization: 'Bearer developer-token-at-least-24-characters', 'content-type': 'application/json' }, body,
    });
    const pull = await fetch(`${base}/api/knowledge/sync?cursor=0`, {
      headers: { authorization: 'Bearer user-client-token-at-least-24-characters' },
    });

    expect(userUpload.status).toBe(403);
    expect(developerUpload.status).toBe(200);
    expect(pull.status).toBe(200);
    expect((await pull.json() as { records: unknown[] }).records).toHaveLength(1);
  });
});

describe('mobile pairing API', () => {
  it('requires the QR token and six-digit code once, then relays messages one-to-one', async () => {
    const guide = await listen({ async decideNextAction() { return {}; } });
    const base = guide.replace('/api/guide', '');
    const createdResponse = await fetch(`${base}/api/pairing/sessions`, { method: 'POST' });
    const created = await createdResponse.json() as {
      sessionId: string; desktopSecret: string; pairingToken: string; code: string; qrDataUrl: string;
    };
    expect(created.code).toMatch(/^\d{6}$/u);
    expect(created.qrDataUrl).toMatch(/^data:image\/png;base64,/u);
    expect((await fetch(`${base}/api/pairing/claim`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ pairingToken: created.pairingToken, code: '999999' }),
    })).status).toBe(400);
    const claimResponse = await fetch(`${base}/api/pairing/claim`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ pairingToken: created.pairingToken, code: created.code }),
    });
    const claim = await claimResponse.json() as { sessionId: string; mobileSecret: string };
    expect(claimResponse.status).toBe(200);
    expect((await fetch(`${base}/api/pairing/claim`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ pairingToken: created.pairingToken, code: created.code }),
    })).status).toBe(400);

    const wsBase = base.replace('http:', 'ws:');
    const desktop = new WebSocket(`${wsBase}/api/pairing/ws?role=desktop&sessionId=${created.sessionId}&secret=${created.desktopSecret}`);
    const mobile = new WebSocket(`${wsBase}/api/pairing/ws?role=mobile&sessionId=${claim.sessionId}&secret=${claim.mobileSecret}`);
    await Promise.all([desktop, mobile].map((socket) => new Promise<void>((resolve, reject) => {
      socket.once('open', resolve); socket.once('error', reject);
    })));
    const relayed = new Promise<Record<string, unknown>>((resolve) => {
      desktop.on('message', (data) => {
        const value = JSON.parse(data.toString()) as Record<string, unknown>;
        if (value.type === 'user_message') resolve(value);
      });
    });
    mobile.send(JSON.stringify({ type: 'user_message', id: 'm1', text: '프린터 설정 어디야?' }));
    await expect(relayed).resolves.toMatchObject({ id: 'm1', text: '프린터 설정 어디야?' });
    desktop.close(); mobile.close();
  });
});
