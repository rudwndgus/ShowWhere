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
      apiKey: 'secret', model: 'gpt-5.6', baseUrl: 'https://api.openai.com/v1',
      requestTimeoutMs: 5_000, maxRetries: 0,
    });

    const decision = await provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    expect(decision.targetId).toBe('save-button');
    expect(body?.store).toBe(false);
    expect(JSON.stringify(body)).toContain('data:image/jpeg;base64,abc');
    expect(JSON.stringify(body)).toContain('json_schema');
  });
});
