import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { createHash, timingSafeEqual } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import QRCode from 'qrcode';
import { ZodError } from 'zod';
import type { AiProvider } from '../../../src/guide-api/AiProvider';
import { GUIDE_API_PATH, handleGuideApiRequest } from '../../../src/guide-api/handleGuideApiRequest';
import type { ApiConfig } from './config';
import { centralRecordBatchSchema, CentralKnowledgeStore } from './CentralKnowledgeStore';
import { mobilePage } from './mobilePage';
import { PairingManager } from './PairingManager';
import { OpenAiSpeechTranscriber, type SpeechTranscriber } from './OpenAiSpeechTranscriber';

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

async function readBinaryBody(request: IncomingMessage, maxBytes: number): Promise<Buffer> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    size += buffer.byteLength;
    if (size > maxBytes) throw new Error('request_too_large');
    chunks.push(buffer);
  }
  return Buffer.concat(chunks);
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

type ApiRole = 'anonymous' | 'user' | 'developer' | 'admin';

function requestRole(request: IncomingMessage, config: ApiConfig): ApiRole {
  if (config.security.adminToken && tokenMatches(request, config.security.adminToken)) return 'admin';
  if (config.security.developerToken && tokenMatches(request, config.security.developerToken)) return 'developer';
  if (!config.security.clientToken || tokenMatches(request, config.security.clientToken)) return 'user';
  return 'anonymous';
}

function roleAtLeast(role: ApiRole, required: ApiRole): boolean {
  const rank: Record<ApiRole, number> = { anonymous: 0, user: 1, developer: 2, admin: 3 };
  return rank[role] >= rank[required];
}

function sendHtml(response: ServerResponse, body: string): void {
  response.writeHead(200, {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'Permissions-Policy': 'camera=(self), microphone=(self), geolocation=()',
    'Content-Security-Policy': "default-src 'self'; img-src 'self' data:; style-src 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; media-src blob:",
  });
  response.end(body);
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

export function createApiServer(
  config: ApiConfig,
  provider: AiProvider,
  speechTranscriber: SpeechTranscriber = new OpenAiSpeechTranscriber(config.openai),
) {
  const limiter = new FixedWindowRateLimiter(
    config.security.rateLimitWindowMs,
    config.security.rateLimitMaxRequests,
  );
  const pairingCreateLimiter = new FixedWindowRateLimiter(60_000, 20);
  const pairingClaimLimiter = new FixedWindowRateLimiter(60_000, 20);
  const speechLimiter = new FixedWindowRateLimiter(60_000, 20);
  const knowledge = new CentralKnowledgeStore(config.centralDataDirectory);
  const pairing = new PairingManager(config.pairingTtlSeconds * 1_000);
  const server = createServer(async (request, response) => {
    const requestStartedAt = performance.now();
    const url = new URL(request.url ?? '/', 'http://localhost');
    if (request.method === 'GET' && url.pathname === '/health') {
      sendJson(response, 200, { status: 'ok', services: { guide: true, sync: true, pairing: true } });
      return;
    }
    if (request.method === 'GET' && (url.pathname === '/mobile' || url.pathname === '/mobile/')) {
      sendHtml(response, mobilePage);
      return;
    }
    if (request.method === 'GET' && url.pathname === '/mobile/manifest.webmanifest') {
      response.writeHead(200, {
        'Content-Type': 'application/manifest+json; charset=utf-8',
        'Cache-Control': 'public, max-age=3600',
        'X-Content-Type-Options': 'nosniff',
      });
      response.end(JSON.stringify({
        name: 'ShowWhere', short_name: 'ShowWhere', start_url: '/mobile/', scope: '/mobile/',
        display: 'standalone', background_color: '#f6f3f3', theme_color: '#472323',
        icons: [{ src: '/mobile/gorilla.png', sizes: 'any', type: 'image/png', purpose: 'any maskable' }],
      }));
      return;
    }
    if (request.method === 'GET' && url.pathname === '/mobile/jsqr.js') {
      try {
        const script = await readFile(resolve('node_modules/jsqr/dist/jsQR.js'));
        response.writeHead(200, {
          'Content-Type': 'text/javascript; charset=utf-8',
          'Cache-Control': 'public, max-age=86400',
          'X-Content-Type-Options': 'nosniff',
        });
        response.end(script);
      } catch { sendJson(response, 404, { message: 'QR 판독기를 찾을 수 없습니다.' }); }
      return;
    }
    if (request.method === 'GET' && url.pathname === '/mobile/gorilla.png') {
      try {
        const image = await readFile(resolve('apps/windows/ShowWhere.Desktop/Assets/Assistant/monkey-sit.png'));
        response.writeHead(200, { 'Content-Type': 'image/png', 'Cache-Control': 'public, max-age=86400', 'X-Content-Type-Options': 'nosniff' });
        response.end(image);
      } catch { sendJson(response, 404, { message: '이미지를 찾을 수 없습니다.' }); }
      return;
    }
    if (request.method === 'POST' && url.pathname === '/api/pairing/claim') {
      if (!pairingClaimLimiter.allow(clientAddress(request, config.security.trustProxy))) {
        response.setHeader('Retry-After', '60');
        sendJson(response, 429, { message: '연결 시도가 너무 많아요. 잠시 후 다시 시도해 주세요.' });
        return;
      }
      try {
        const body = await readJsonBody(request, 8_192) as { pairingToken?: unknown; code?: unknown };
        if (typeof body.pairingToken !== 'string' || typeof body.code !== 'string') throw new Error('invalid');
        const claimed = pairing.claim(body.pairingToken, body.code);
        if (!claimed) { sendJson(response, 400, { message: '인증번호가 다르거나 연결 시간이 만료되었습니다.' }); return; }
        sendJson(response, 200, claimed);
      } catch { sendJson(response, 400, { message: '연결 정보를 확인해 주세요.' }); }
      return;
    }
    if (request.method === 'POST' && url.pathname === '/api/mobile/transcribe') {
      const sessionId = url.searchParams.get('sessionId') ?? '';
      const supplied = request.headers['x-showwhere-pairing-secret'];
      const mobileSecret = Array.isArray(supplied) ? supplied[0] : supplied ?? '';
      if (!pairing.authorizeConnectedMobile(sessionId, mobileSecret)) {
        sendJson(response, 401, { message: 'PC 연결을 다시 확인해 주세요.' });
        return;
      }
      if (!speechLimiter.allow(`${clientAddress(request, config.security.trustProxy)}:${sessionId}`)) {
        sendJson(response, 429, { message: '음성 요청이 너무 많아요. 잠시 후 다시 시도해 주세요.' });
        return;
      }
      const contentType = request.headers['content-type']?.split(';')[0]?.trim().toLowerCase() ?? '';
      if (!['audio/mp4', 'audio/x-m4a', 'audio/webm', 'audio/ogg', 'audio/mpeg', 'audio/mp3', 'audio/wav', 'audio/x-wav'].includes(contentType)) {
        sendJson(response, 415, { message: '이 휴대폰의 음성 형식을 처리할 수 없어요.' });
        return;
      }
      try {
        const audio = await readBinaryBody(request, 5_000_000);
        if (audio.byteLength < 512) {
          sendJson(response, 400, { message: '음성이 너무 짧아요. 조금 더 길게 말해 주세요.' });
          return;
        }
        const transcription = await speechTranscriber.transcribe(audio, contentType);
        if (!transcription.text) {
          sendJson(response, 422, { message: '음성을 듣지 못했어요. 다시 말해 주세요.' });
          return;
        }
        if (config.debug)
          console.log(`[showwhere:stt] duration_ms=${transcription.providerLatencyMs} bytes=${audio.byteLength}`);
        sendJson(response, 200, transcription);
      } catch (error) {
        const status = error instanceof Error && error.message === 'request_too_large' ? 413 : 502;
        console.error(`[showwhere:stt] failed=${status}`);
        sendJson(response, status, {
          message: status === 413 ? '음성이 너무 길어요. 짧게 나누어 말해 주세요.' : '음성을 변환하지 못했어요. 다시 시도해 주세요.',
        });
      }
      return;
    }

    const role = requestRole(request, config);
    if (request.method === 'POST' && url.pathname === '/api/pairing/sessions') {
      if (!pairingCreateLimiter.allow(clientAddress(request, config.security.trustProxy))) {
        response.setHeader('Retry-After', '60');
        sendJson(response, 429, { message: '새 연결 요청이 너무 많아요. 잠시 후 다시 시도해 주세요.' });
        return;
      }
      if (!roleAtLeast(role, 'user')) { sendJson(response, 401, { message: '인증이 필요합니다.' }); return; }
      const protocol = request.headers['x-forwarded-proto']?.toString().split(',')[0] ?? 'http';
      const host = request.headers['x-forwarded-host']?.toString().split(',')[0] ?? request.headers.host ?? 'localhost';
      const baseUrl = config.publicBaseUrl ?? `${protocol}://${host}`;
      const session = pairing.create(baseUrl);
      if (!session) {
        sendJson(response, 503, { message: '현재 연결이 많아요. 잠시 후 다시 시도해 주세요.' });
        return;
      }
      const qrDataUrl = await QRCode.toDataURL(session.mobileUrl, { width: 420, margin: 2, errorCorrectionLevel: 'M' });
      sendJson(response, 201, { ...session, qrDataUrl });
      return;
    }
    if (request.method === 'DELETE' && url.pathname.startsWith('/api/pairing/sessions/')) {
      if (!roleAtLeast(role, 'user')) { sendJson(response, 401, { message: '인증이 필요합니다.' }); return; }
      const suppliedSecret = request.headers['x-showwhere-pairing-secret'];
      const desktopSecret = Array.isArray(suppliedSecret) ? suppliedSecret[0] : suppliedSecret ?? '';
      const disconnected = pairing.disconnect(
        decodeURIComponent(url.pathname.slice('/api/pairing/sessions/'.length)),
        desktopSecret,
      );
      if (!disconnected) {
        sendJson(response, 404, { message: '이미 종료되었거나 유효하지 않은 연결입니다.' });
        return;
      }
      sendJson(response, 200, { disconnected: true });
      return;
    }
    if (request.method === 'GET' && url.pathname === '/api/knowledge/sync') {
      if (!roleAtLeast(role, 'user')) { sendJson(response, 401, { message: '인증이 필요합니다.' }); return; }
      const cursor = Number(url.searchParams.get('cursor') ?? '0');
      if (!Number.isSafeInteger(cursor) || cursor < 0) { sendJson(response, 400, { message: '올바르지 않은 동기화 버전입니다.' }); return; }
      try { sendJson(response, 200, await knowledge.changesAfter(cursor)); }
      catch {
        console.error('[showwhere:central] read_failed');
        sendJson(response, 503, { message: '중앙 학습 데이터를 잠시 불러올 수 없습니다. 로컬 안내는 계속 사용할 수 있습니다.' });
      }
      return;
    }
    if (request.method === 'POST' && url.pathname === '/api/knowledge/records') {
      if (!roleAtLeast(role, 'developer')) { sendJson(response, 403, { message: '개발자 권한이 필요합니다.' }); return; }
      try {
        const parsed = centralRecordBatchSchema.parse(await readJsonBody(request, config.maxRequestBytes));
        sendJson(response, 200, await knowledge.upsert(parsed.records));
      } catch (error) {
        if (error instanceof ZodError) sendJson(response, 400, { message: '학습 데이터 형식을 확인해 주세요.' });
        else {
          console.error('[showwhere:central] write_failed');
          sendJson(response, 503, { message: '중앙 저장을 잠시 사용할 수 없습니다. 데이터는 로컬에서 보존됩니다.' });
        }
      }
      return;
    }
    if (request.method !== 'POST' || url.pathname !== GUIDE_API_PATH) {
      sendJson(response, 404, { message: '안내 경로를 찾을 수 없어요.' });
      return;
    }
    if (!roleAtLeast(role, 'user')) {
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
  server.on('upgrade', (request, socket, head) => {
    if (!pairing.handleUpgrade(request, socket, head)) socket.destroy();
  });
  server.on('close', () => pairing.close());
  return server;
}
