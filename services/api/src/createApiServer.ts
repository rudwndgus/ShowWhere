import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { createHash, timingSafeEqual } from 'node:crypto';
import type { AiProvider } from '../../../src/guide-api/AiProvider';
import { GUIDE_API_PATH, handleGuideApiRequest } from '../../../src/guide-api/handleGuideApiRequest';
import type { ApiConfig } from './config';

async function readJsonBody(request: IncomingMessage, maxBytes: number): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;

  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    size += buffer.byteLength;
    if (size > maxBytes) throw new Error('request_too_large');
    chunks.push(buffer);
  }

  return JSON.parse(Buffer.concat(chunks).toString('utf8')) as unknown;
}

function sendJson(response: ServerResponse, status: number, body: unknown): void {
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
    'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
  });
  response.end(JSON.stringify(body));
}

function tokenMatches(request: IncomingMessage, expected: string | undefined): boolean {
  if (!expected) return true;
  const authorization = request.headers.authorization;
  const supplied = authorization?.startsWith('Bearer ') ? authorization.slice(7).trim() : '';
  const expectedHash = createHash('sha256').update(expected).digest();
  const suppliedHash = createHash('sha256').update(supplied).digest();
  return timingSafeEqual(expectedHash, suppliedHash);
}

function clientAddress(request: IncomingMessage, trustProxy: boolean): string {
  if (trustProxy) {
    const forwarded = request.headers['x-forwarded-for'];
    const first = (Array.isArray(forwarded) ? forwarded[0] : forwarded)?.split(',')[0]?.trim();
    if (first) return first;
  }
  return request.socket.remoteAddress ?? 'unknown';
}

class FixedWindowRateLimiter {
  private readonly buckets = new Map<string, { startedAt: number; requests: number }>();

  constructor(private readonly windowMs: number, private readonly maximum: number) {}

  allow(key: string, now = Date.now()): boolean {
    const bucket = this.buckets.get(key);
    if (!bucket || now - bucket.startedAt >= this.windowMs) {
      this.buckets.set(key, { startedAt: now, requests: 1 });
      if (this.buckets.size > 10_000) this.prune(now);
      return true;
    }
    if (bucket.requests >= this.maximum) return false;
    bucket.requests += 1;
    return true;
  }

  private prune(now: number): void {
    for (const [key, bucket] of this.buckets)
      if (now - bucket.startedAt >= this.windowMs) this.buckets.delete(key);
  }
}

export function createApiServer(config: ApiConfig, provider: AiProvider) {
  const limiter = new FixedWindowRateLimiter(
    config.security.rateLimitWindowMs,
    config.security.rateLimitMaxRequests,
  );
  return createServer(async (request, response) => {
    const requestStartedAt = performance.now();
    const url = new URL(request.url ?? '/', 'http://localhost');
    if (request.method === 'GET' && url.pathname === '/health') {
      sendJson(response, 200, { status: 'ok' });
      return;
    }
    if (request.method !== 'POST' || url.pathname !== GUIDE_API_PATH) {
      sendJson(response, 404, { message: '안내 경로를 찾을 수 없어요.' });
      return;
    }
    if (!tokenMatches(request, config.security.clientToken)) {
      sendJson(response, 401, {
        status: 'blocked', action: 'explain', message: 'ShowWhere 서버 인증에 실패했어요. 최신 배포본을 사용해 주세요.', confidence: 1,
      });
      return;
    }
    if (!limiter.allow(clientAddress(request, config.security.trustProxy))) {
      response.setHeader('Retry-After', String(Math.ceil(config.security.rateLimitWindowMs / 1_000)));
      sendJson(response, 429, {
        status: 'blocked', action: 'explain', message: '요청이 너무 많아요. 잠시 후 다시 시도해 주세요.', confidence: 1,
      });
      return;
    }

    let body: unknown;
    try {
      body = await readJsonBody(request, config.maxRequestBytes);
    } catch (error) {
      const status = error instanceof Error && error.message === 'request_too_large' ? 413 : 400;
      sendJson(response, status, { message: '화면 정보를 확인할 수 없어요. 다시 시도해 주세요.' });
      return;
    }

    const result = await handleGuideApiRequest(url.pathname, body, provider);
    if (config.debug) {
      const candidates = typeof body === 'object' && body !== null && 'candidates' in body
        && Array.isArray(body.candidates) ? body.candidates : [];
      const selected = candidates.find((candidate) =>
        typeof candidate === 'object' && candidate !== null && 'id' in candidate
        && candidate.id === result.decision.targetId);
      const selectedLabel = typeof selected === 'object' && selected !== null && 'label' in selected
        && typeof selected.label === 'string'
        ? selected.label.replace(/\s+/gu, ' ').slice(0, 100)
        : 'none';
      console.log(
        `[showwhere:api] candidates=${candidates.length} action=${result.decision.action}`
        + ` target=${result.decision.targetId ?? 'none'} label=${JSON.stringify(selectedLabel)}`
        + ` message=${JSON.stringify(result.decision.message.replace(/\s+/gu, ' ').slice(0, 180))}`
        + ` duration_ms=${Math.round(performance.now() - requestStartedAt)}`,
      );
      const intentCandidates = candidates.filter((candidate) => {
        if (typeof candidate !== 'object' || candidate === null) return false;
        const item = candidate as { label?: unknown; description?: unknown };
        return /sign|login|account|address|location|deliver|로그인|계정|주소|위치|배송/iu.test(
          `${typeof item.label === 'string' ? item.label : ''} ${typeof item.description === 'string' ? item.description : ''}`,
        );
      }).slice(0, 20).map((candidate) => {
        const item = candidate as { id?: unknown; label?: unknown; description?: unknown; attributes?: { sourceScope?: unknown } };
        return {
          id: item.id,
          label: typeof item.label === 'string' ? item.label.slice(0, 140) : null,
          description: typeof item.description === 'string' ? item.description.slice(0, 100) : null,
          scope: item.attributes?.sourceScope,
        };
      });
      if (intentCandidates.length > 0)
        console.log(`[showwhere:api:intent-candidates] ${JSON.stringify(intentCandidates)}`);
    }
    sendJson(response, result.status, result.decision);
  });
}
