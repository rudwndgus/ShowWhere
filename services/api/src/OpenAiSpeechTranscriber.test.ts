import { afterEach, describe, expect, it, vi } from 'vitest';
import { OpenAiSpeechTranscriber } from './OpenAiSpeechTranscriber';

afterEach(() => vi.unstubAllGlobals());

describe('OpenAiSpeechTranscriber', () => {
  it('sends audio as multipart form data and returns verbatim text', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      const form = init?.body as FormData;
      expect(form.get('model')).toBe('gpt-transcribe');
      expect(form.get('response_format')).toBe('json');
      expect(form.get('prompt')).toContain('computer guidance request');
      expect(form.getAll('languages[]')).toEqual(['en', 'ko']);
      expect((form.get('file') as File).name).toBe('showwhere-speech.m4a');
      expect(new Headers(init?.headers).get('authorization')).toBe('Bearer server-secret');
      return Response.json({ text: '프린터 연결 상태 확인하고 싶어' });
    });
    vi.stubGlobal('fetch', fetchMock);
    const transcriber = new OpenAiSpeechTranscriber({
      apiKey: 'server-secret', baseUrl: 'https://api.openai.com/v1',
      model: 'gpt-transcribe', requestTimeoutMs: 5_000,
    });

    const result = await transcriber.transcribe(Buffer.alloc(1_024), 'audio/mp4');

    expect(result.text).toBe('프린터 연결 상태 확인하고 싶어');
    expect(result.providerLatencyMs).toBeGreaterThanOrEqual(0);
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('rejects malformed provider output without leaking it to callers', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ unexpected: 'secret text' })));
    const transcriber = new OpenAiSpeechTranscriber({
      apiKey: 'server-secret', baseUrl: 'https://api.openai.com/v1',
      model: 'gpt-4o-transcribe', requestTimeoutMs: 5_000,
    });

    await expect(transcriber.transcribe(Buffer.alloc(1_024), 'audio/webm'))
      .rejects.toThrow('transcription_provider_malformed');
  });

  it('retries one transient provider failure', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('', { status: 503 }))
      .mockResolvedValueOnce(Response.json({ text: 'retry succeeded' }));
    vi.stubGlobal('fetch', fetchMock);
    const transcriber = new OpenAiSpeechTranscriber({
      apiKey: 'server-secret', baseUrl: 'https://api.openai.com/v1',
      model: 'gpt-4o-transcribe', requestTimeoutMs: 5_000,
    });

    await expect(transcriber.transcribe(Buffer.alloc(1_024), 'audio/webm'))
      .resolves.toMatchObject({ text: 'retry succeeded' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
