import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
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
  });
  response.end(JSON.stringify(body));
}

export function createApiServer(config: ApiConfig, provider: AiProvider) {
  return createServer(async (request, response) => {
    const requestStartedAt = performance.now();
    const url = new URL(request.url ?? '/', 'http://localhost');
    if (request.method !== 'POST' || url.pathname !== GUIDE_API_PATH) {
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
    }
    sendJson(response, result.status, result.decision);
  });
}
