import { afterEach, describe, expect, it, vi } from 'vitest';
import { cosineSimilarity, HuggingFaceEmbeddingClient } from './HuggingFaceEmbeddingClient';

afterEach(() => vi.unstubAllGlobals());

describe('HuggingFaceEmbeddingClient', () => {
  it('batches embeddings and caches repeated text locally', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify([[1, 0], [0, 1]]), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const client = new HuggingFaceEmbeddingClient({
      token: 'secret', model: 'test/model', baseUrl: 'https://router.example/models', requestTimeoutMs: 1_000,
    });

    expect(await client.embed(['프린터', '소리'])).toEqual([[1, 0], [0, 1]]);
    expect(await client.embed(['프린터'])).toEqual([[1, 0]]);
    expect(fetchMock).toHaveBeenCalledOnce();
    const headers = (fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer secret');
  });

  it('calculates cosine similarity safely', () => {
    expect(cosineSimilarity([1, 0], [1, 0])).toBe(1);
    expect(cosineSimilarity([1, 0], [0, 1])).toBe(0);
    expect(cosineSimilarity([], [])).toBe(-1);
  });
});
