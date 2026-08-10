import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { AiProvider } from '../../../src/guide-api/AiProvider';
import { GUIDE_API_PATH, handleGuideApiRequest } from '../../../src/guide-api/handleGuideApiRequest';
import type { ApiConfig } from './config';
import type { TeachingService } from './teaching/TeachingService';

export const TEACHING_ANALYZE_PATH = '/api/teaching/analyze';
export const TEACHING_VALIDATE_PATH = '/api/teaching/validate';
export const TEACHING_GOLD_PATH = '/api/teaching/gold';

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
  });
  response.end(JSON.stringify(body));
}

export function createApiServer(config: ApiConfig, provider: AiProvider, teaching?: TeachingService) {
  return createServer(async (request, response) => {
    const requestStartedAt = performance.now();
    const url = new URL(request.url ?? '/', 'http://localhost');
    const isTeachingRoute = [TEACHING_ANALYZE_PATH, TEACHING_VALIDATE_PATH, TEACHING_GOLD_PATH].includes(url.pathname);
    if (request.method !== 'POST' || (url.pathname !== GUIDE_API_PATH && !isTeachingRoute)) {
      sendJson(response, 404, { message: '안내 경로를 찾을 수 없어요.' });
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

    if (isTeachingRoute) {
      if (!teaching) {
        sendJson(response, 503, { message: '개발자 학습 서비스를 사용할 수 없습니다.' });
        return;
      }
      try {
        if (url.pathname === TEACHING_ANALYZE_PATH) {
          sendJson(response, 200, await teaching.analyze(body));
          return;
        }
        if (url.pathname === TEACHING_VALIDATE_PATH) {
          sendJson(response, 200, await teaching.validate(body));
          return;
        }
        const envelope = body && typeof body === 'object' && !Array.isArray(body)
          ? body as { record?: unknown; approvedBy?: unknown } : {};
        if (!envelope.record) {
          sendJson(response, 400, { message: '승인할 학습 데이터가 없습니다.' });
          return;
        }
        sendJson(response, 200, await teaching.saveApproved(
          envelope.record,
          typeof envelope.approvedBy === 'string' ? envelope.approvedBy : 'developer',
        ));
        return;
      } catch {
        sendJson(response, url.pathname === TEACHING_GOLD_PATH ? 422 : 502, {
          message: url.pathname === TEACHING_GOLD_PATH
            ? '검증 오류를 수정한 뒤 다시 승인해 주세요.'
            : '라벨링 결과를 준비하지 못했어요. 잠시 후 다시 시도해 주세요.',
        });
        return;
      }
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
        + ` duration_ms=${Math.round(performance.now() - requestStartedAt)}`,
      );
    }
    sendJson(response, result.status, result.decision);
  });
}
