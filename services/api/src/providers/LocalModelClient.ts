import type { GuideRequest, UiCandidate } from '../../../../src/contracts';
import {
  GroundingResultSchema,
  RerankerResultSchema,
  SemanticDecisionSchema,
  type GroundingResult,
  type MemoryHit,
  type RerankerResult,
  type SemanticDecision,
} from '../../../../src/brain-v2/contracts';
import type { CandidateReranker, TaskStateReasoner, VisionGrounder } from '../../../../src/brain-v2/interfaces';

export interface LocalModelClientOptions {
  baseUrl: string;
  timeoutMs: number;
  maxRetries?: number;
  apiToken?: string;
}

export class LocalModelClient implements TaskStateReasoner, CandidateReranker, VisionGrounder {
  constructor(private readonly options: LocalModelClientOptions) {}

  private async post(path: string, body: unknown): Promise<unknown> {
    let lastError: unknown;
    for (let attempt = 0; attempt <= (this.options.maxRetries ?? 1); attempt += 1) {
      try {
        const response = await fetch(`${this.options.baseUrl.replace(/\/$/u, '')}${path}`, {
          method: 'POST',
          headers: {
            'content-type': 'application/json',
            ...(this.options.apiToken ? { authorization: `Bearer ${this.options.apiToken}` } : {}),
          },
          body: JSON.stringify(body),
          signal: AbortSignal.timeout(this.options.timeoutMs),
        });
        if (response.ok) return response.json();
        const error = new Error(`Local AI ${path} failed with HTTP ${response.status}.`);
        if (response.status < 500 && response.status !== 429) throw error;
        lastError = error;
      } catch (error) {
        lastError = error;
      }
      if (attempt < (this.options.maxRetries ?? 1)) {
        await new Promise((done) => setTimeout(done, 150 * (attempt + 1)));
      }
    }
    throw lastError instanceof Error ? lastError : new Error(`Local AI ${path} failed.`);
  }

  async health(): Promise<unknown> {
    const response = await fetch(`${this.options.baseUrl.replace(/\/$/u, '')}/health`, {
      headers: this.options.apiToken ? { authorization: `Bearer ${this.options.apiToken}` } : {},
      signal: AbortSignal.timeout(Math.min(this.options.timeoutMs, 3_000)),
    });
    if (!response.ok) throw new Error(`Local AI health check failed with HTTP ${response.status}.`);
    return response.json();
  }

  async decide(input: { guideRequest: GuideRequest; memories: MemoryHit[]; reasoningMode: 'off' | 'on' }) {
    const started = performance.now();
    const raw = await this.post('/brain/decide', input);
    const envelope = raw as { decision?: unknown; model?: string; latencyMs?: number };
    return {
      decision: SemanticDecisionSchema.parse(envelope.decision ?? raw),
      model: envelope.model ?? 'domyn/Domyn-Small-v1.0',
      latencyMs: envelope.latencyMs ?? Math.round(performance.now() - started),
    };
  }

  async rerank(query: string, candidates: readonly UiCandidate[]): Promise<RerankerResult> {
    return RerankerResultSchema.parse(await this.post('/rerank', {
      query,
      candidates: candidates.map(({ id, label, description, role }) => ({ id, label, description, role })),
    }));
  }

  async ground(screenshot: string, targetConcept: string): Promise<GroundingResult> {
    return GroundingResultSchema.parse(await this.post('/ground', { screenshot, targetConcept }));
  }

  async embed(texts: string[]): Promise<number[][]> {
    const result = await this.post('/memory/embed', { texts }) as { embeddings?: unknown };
    if (!Array.isArray(result.embeddings)) throw new Error('Local embedding response is malformed.');
    return result.embeddings as number[][];
  }
}
