import { afterEach, describe, expect, it, vi } from 'vitest';
import { guideRequestFixture } from '../testFixtures';
import { OpenAiGuideProvider } from './OpenAiGuideProvider';

afterEach(() => vi.unstubAllGlobals());

describe('OpenAiGuideProvider', () => {
  it('sends the full screenshot with store disabled and parses strict output', async () => {
    let body: Record<string, unknown> | undefined;
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit) => {
      body = JSON.parse(String(init.body)) as Record<string, unknown>;
      return new Response(JSON.stringify({
        output: [{ content: [{ type: 'output_text', text: JSON.stringify({
          status: 'in_progress', action: 'highlight', targetId: 'save-button',
          message: '저장 버튼을 누르세요.', expectedChange: null, confidence: 0.97,
          alternativeTargetIds: null, visualTarget: null,
        }) }] }],
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }));
    const provider = new OpenAiGuideProvider({
      apiKey: 'secret', fastModel: 'gpt-5.6-luna', model: 'gpt-5.6-terra',
      strongModel: 'gpt-5.6-sol', baseUrl: 'https://api.openai.com/v1',
      requestTimeoutMs: 5_000, maxRetries: 0,
    });

    const decision = await provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    expect(decision.targetId).toBe('save-button');
    expect(body?.store).toBe(false);
    expect(body?.model).toBe('gpt-5.6-luna');
    expect(body?.reasoning).toEqual({ effort: 'none' });
    expect(JSON.stringify(body)).toContain('data:image/jpeg;base64,abc');
    expect(JSON.stringify(body)).toContain('json_schema');
    expect(JSON.stringify(body)).toContain('Missing, hidden, or not-yet-visible controls are navigation problems');
    expect(JSON.stringify(body)).toContain('"detail":"auto"');
    expect(JSON.stringify(body)).not.toContain('localScore');
  });

  it('uses Terra for an ambiguous candidate set', async () => {
    let body: Record<string, unknown> | undefined;
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit) => {
      body = JSON.parse(String(init.body)) as Record<string, unknown>;
      return new Response(JSON.stringify({
        output: [{ content: [{ type: 'output_text', text: JSON.stringify({
          status: 'needs_clarification', action: 'ask_user', targetId: null,
          message: '어떤 작업을 원하시나요?', expectedChange: null, confidence: 0.5,
          alternativeTargetIds: ['first', 'second'], visualTarget: null,
        }) }] }],
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }));
    const provider = new OpenAiGuideProvider({
      apiKey: 'secret', fastModel: 'gpt-5.6-luna', model: 'gpt-5.6-terra',
      strongModel: 'gpt-5.6-sol', baseUrl: 'https://api.openai.com/v1',
      requestTimeoutMs: 5_000, maxRetries: 0,
    });

    await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: '도와줘', goal: '도와줘' },
      candidates: [
        { ...guideRequestFixture.candidates[0], id: 'first', label: '계정' },
        { ...guideRequestFixture.candidates[0], id: 'second', label: '시스템' },
      ],
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    expect(body?.model).toBe('gpt-5.6-terra');
    expect(body?.reasoning).toEqual({ effort: 'low' });
  });

  it('reserves Sol for visual-only screens with no live candidates', async () => {
    let body: Record<string, unknown> | undefined;
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit) => {
      body = JSON.parse(String(init.body)) as Record<string, unknown>;
      return new Response(JSON.stringify({
        output: [{ content: [{ type: 'output_text', text: JSON.stringify({
          status: 'completed', action: 'explain', targetId: null,
          message: '작업이 완료되었습니다.', expectedChange: null, confidence: 0.95,
          alternativeTargetIds: null, visualTarget: null,
        }) }] }],
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }));
    const provider = new OpenAiGuideProvider({
      apiKey: 'secret', fastModel: 'gpt-5.6-luna', model: 'gpt-5.6-terra',
      strongModel: 'gpt-5.6-sol', baseUrl: 'https://api.openai.com/v1',
      requestTimeoutMs: 5_000, maxRetries: 0,
    });

    await provider.decideNextAction({
      ...guideRequestFixture,
      candidates: [],
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    expect(body?.model).toBe('gpt-5.6-sol');
    expect(body?.reasoning).toEqual({ effort: 'low' });
  });

  it('does not retry when the account has no API credits', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({
      error: { message: 'You have no credits remaining.', code: 'credit_balance_exhausted' },
    }), { status: 429, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);
    const provider = new OpenAiGuideProvider({
      apiKey: 'secret', model: 'gpt-5.6', baseUrl: 'https://api.openai.com/v1',
      requestTimeoutMs: 5_000, maxRetries: 2,
    });

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    })).rejects.toThrow('credit_balance_exhausted');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
