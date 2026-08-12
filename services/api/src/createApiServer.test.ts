import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import type { AiProvider } from '../../../src/guide-api/AiProvider';
import type { ApiConfig } from './config';
import { createApiServer } from './createApiServer';
import { guideRequestFixture } from './testFixtures';

const config: ApiConfig = {
  aiMode: 'mock',
  brainMode: 'legacy',
  host: '127.0.0.1',
  port: 0,
  maxRequestBytes: 100_000,
  debug: false,
  brainV2: {
    baseUrl: 'http://127.0.0.1:8790',
    modelRoot: 'C:\\ShowWhere_Models',
    dataRoot: 'data/brain-v2',
    timeoutMs: 30_000,
    maxRetries: 1,
    memoryReuseThreshold: 0.92,
    rerankThreshold: 0.62,
  },
};

const servers: ReturnType<typeof createApiServer>[] = [];

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) =>
    new Promise<void>((resolve) => server.close(() => resolve()))));
});

async function listen(provider: AiProvider): Promise<string> {
  const server = createApiServer(config, provider);
  servers.push(server);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address() as AddressInfo;
  return `http://127.0.0.1:${address.port}/api/guide`;
}

describe('POST /api/guide', () => {
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
