import { describe, expect, it, vi } from 'vitest';
import { guideRequestFixture } from '../testFixtures';
import { FeatherlessProvider, type FeatherlessProviderOptions } from './FeatherlessProvider';

const validDecision = {
  status: 'in_progress',
  action: 'highlight',
  targetId: 'candidate-settings',
  message: 'Select Settings.',
  expectedChange: 'Settings opens.',
  confidence: 0.94,
};

function completion(content: string, status = 200): Response {
  return new Response(JSON.stringify({ choices: [{ message: { content } }] }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function options(overrides: Partial<FeatherlessProviderOptions> = {}): FeatherlessProviderOptions {
  return {
    apiKey: 'test-key',
    baseUrl: 'https://provider.example/v1',
    model: 'configured-guide-model',
    visionModels: ['configured-vision-model'],
    requestTimeoutMs: 1_000,
    maxRetries: 1,
    retryBaseDelayMs: 0,
    maxTokens: 800,
    enableThinking: false,
    debug: false,
    ...overrides,
  };
}

describe('FeatherlessProvider', () => {
  it('uses the configured endpoint, bearer token, and model', async () => {
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    const [url, init] = fetchImplementation.mock.calls[0];
    expect(url).toBe('https://provider.example/v1/chat/completions');
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer test-key');
    expect(JSON.parse(String(init?.body)).model).toBe('configured-guide-model');
    expect(JSON.parse(String(init?.body)).chat_template_kwargs).toEqual({ enable_thinking: false });
    expect(JSON.parse(String(init?.body)).response_format).toEqual({ type: 'json_object' });
  });

  it('falls back to the next configured vision model when the preferred model is unavailable', async () => {
    const visualDecision = {
      status: 'in_progress',
      action: 'highlight_visual',
      message: 'Select Settings.',
      confidence: 0.92,
      visualTarget: { x: 0.5, y: 0.2, width: 0.08, height: 0.06, label: 'Settings' },
    };
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(visualDecision)))
      .mockResolvedValueOnce(completion('{}', 503));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      maxRetries: 0,
      visionModels: ['preferred-vision-model', 'fallback-vision-model'],
    }));

    await provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    expect(JSON.parse(String(fetchImplementation.mock.calls[0][1]?.body)).model).toBe('preferred-vision-model');
    expect(JSON.parse(String(fetchImplementation.mock.calls[1][1]?.body)).model).toBe('fallback-vision-model');
  });

  it('falls back when a vision model describes the target without returning coordinates', async () => {
    const visualDecision = {
      status: 'in_progress',
      action: 'highlight_visual',
      message: '설정 아이콘을 누르세요.',
      confidence: 0.94,
      visualTarget: { x: 0.56, y: 0.24, width: 0.04, height: 0.06, label: '설정' },
    };
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(visualDecision)))
      .mockResolvedValueOnce(completion(JSON.stringify({
        status: 'needs_clarification',
        action: 'ask_user',
        message: '고정됨에 있는 설정 아이콘을 누르세요.',
        confidence: 0.8,
      })));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      visionModels: ['descriptive-model', 'grounding-model'],
    }));

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    })).resolves.toMatchObject({ action: 'highlight_visual', visualTarget: { label: '설정' } });
    expect(JSON.parse(String(fetchImplementation.mock.calls[0][1]?.body)).model).toBe('descriptive-model');
    expect(JSON.parse(String(fetchImplementation.mock.calls[1][1]?.body)).model).toBe('grounding-model');
  });

  it('uses the configured vision model and image content for screenshot requests', async () => {
    const visualDecision = {
      status: 'in_progress',
      action: 'highlight_visual',
      message: 'Select Settings.',
      confidence: 0.92,
      visualTarget: { x: 0.5, y: 0.2, width: 0.08, height: 0.06, label: 'Settings' },
    };
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(visualDecision)));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });

    const body = JSON.parse(String(fetchImplementation.mock.calls[0][1]?.body));
    expect(body.model).toBe('configured-vision-model');
    expect(body.messages.at(-1).content[1]).toEqual({
      type: 'image_url',
      image_url: { url: 'data:image/jpeg;base64,abc' },
    });
  });

  it('uses native UI-TARS actions without forcing the JSON response format', async () => {
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) => completion(
      "Thought: Settings is visible.\nAction: click(start_box='(580,270)')",
    ));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      visionModels: ['ByteDance-Seed/UI-TARS-1.5-7B'],
    }));

    const decision = await provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });
    expect(decision).toMatchObject({ action: 'highlight_visual' });
    const visualTarget = (decision as { visualTarget: { x: number; y: number } }).visualTarget;
    expect(visualTarget.x).toBeCloseTo(0.56);
    expect(visualTarget.y).toBeCloseTo(0.24);

    const body = JSON.parse(String(fetchImplementation.mock.calls[0][1]?.body));
    expect(body.response_format).toBeUndefined();
    expect(body.chat_template_kwargs).toBeUndefined();
    expect(body.messages[0].content).toContain('native UI-TARS format');
  });

  it('falls back when UI-TARS cannot produce a native click action', async () => {
    const visualDecision = {
      status: 'in_progress',
      action: 'highlight_visual',
      message: '설정을 누르세요.',
      confidence: 0.93,
      visualTarget: { x: 0.55, y: 0.2, width: 0.05, height: 0.08, label: '설정' },
    };
    const fetchImplementation = vi.fn(async () => completion(JSON.stringify(visualDecision)))
      .mockResolvedValueOnce(completion('Thought: target is unclear\nAction: call_user()'));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      visionModels: ['ByteDance-Seed/UI-TARS-1.5-7B', 'Qwen/Qwen3-VL-30B-A3B-Instruct'],
    }));

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    })).resolves.toMatchObject({ action: 'highlight_visual' });
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
  });

  it('retries a bounded transient provider failure', async () => {
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(completion('{}', 503))
      .mockResolvedValueOnce(completion(JSON.stringify(validDecision)));
    const delay = vi.fn(async () => undefined);
    const provider = new FeatherlessProvider(options({ fetchImplementation, delay }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    expect(delay).toHaveBeenCalledTimes(1);
  });

  it('retries Featherless model-busy responses even when they use HTTP 400', async () => {
    const busyResponse = new Response(JSON.stringify({
      error: { message: 'This model is busy, please try again later.' },
    }), { status: 400, headers: { 'Content-Type': 'application/json' } });
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(busyResponse)
      .mockResolvedValueOnce(completion(JSON.stringify(validDecision)));
    const delay = vi.fn(async () => undefined);
    const provider = new FeatherlessProvider(options({ fetchImplementation, delay }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    expect(delay).toHaveBeenCalledTimes(1);
  });

  it('makes one repair request after malformed model output', async () => {
    const fetchImplementation = vi.fn(async (_input: Parameters<typeof fetch>[0], _init?: RequestInit) =>
      completion(JSON.stringify(validDecision)))
      .mockResolvedValueOnce(completion('not JSON'))
      .mockResolvedValueOnce(completion(`\`\`\`json\n${JSON.stringify(validDecision)}\n\`\`\``));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toEqual(validDecision);
    expect(fetchImplementation).toHaveBeenCalledTimes(2);
    const secondBody = JSON.parse(String(fetchImplementation.mock.calls[1][1]?.body));
    expect(secondBody.messages.some((item: { content: string }) =>
      item.content.includes('previous response was invalid'))).toBe(true);
  });

  it('safely defaults missing confidence for ask-user vision responses', async () => {
    const fetchImplementation = vi.fn(async () => completion(JSON.stringify({
      status: 'needs_clarification',
      action: 'ask_user',
      message: '화면에서 찾지 못했어요.',
    })));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toMatchObject({
      action: 'ask_user',
      confidence: 0,
    });
  });

  it('normalizes harmless UI-TARS point and optional-field variations', async () => {
    const fetchImplementation = vi.fn(async () => completion(JSON.stringify({
      status: 'in_progress',
      action: 'highlight_visual',
      targetId: null,
      alternativeTargetIds: null,
      expectedChange: null,
      message: '설정을 누르세요.',
      confidence: 0.9,
      visualTarget: { x: 0.58, y: 0.27, width: 0, height: 0, label: '설정' },
    })));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    })).resolves.toMatchObject({
      action: 'highlight_visual',
      visualTarget: { x: 0.565, y: 0.25, width: 0.03, height: 0.04, label: '설정' },
    });
    expect(fetchImplementation).toHaveBeenCalledTimes(1);
  });

  it('promotes a confident UI-TARS visual target from ask-user to a visible highlight', async () => {
    const fetchImplementation = vi.fn(async () => completion(JSON.stringify({
      status: 'needs_clarification',
      action: 'ask_user',
      message: '고정됨의 설정 아이콘을 누르세요.',
      confidence: 0.91,
      visualTarget: { x: 0.57, y: 0.25, width: 0.03, height: 0.05, label: '설정' },
    })));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    })).resolves.toMatchObject({
      status: 'in_progress',
      action: 'highlight_visual',
      confidence: 0.91,
      visualTarget: { label: '설정' },
    });
  });

  it('does not promote an ambiguous visual target with explicit alternatives', async () => {
    const fetchImplementation = vi.fn(async () => completion(JSON.stringify({
      status: 'needs_clarification',
      action: 'ask_user',
      message: '어느 설정인가요?',
      confidence: 0.9,
      alternativeTargetIds: ['candidate-settings', 'candidate-other'],
      visualTarget: { x: 0.57, y: 0.25, width: 0.03, height: 0.05, label: '설정' },
    })));
    const provider = new FeatherlessProvider(options({ fetchImplementation }));

    await expect(provider.decideNextAction({
      ...guideRequestFixture,
      candidates: [
        guideRequestFixture.candidates[0],
        { ...guideRequestFixture.candidates[0], id: 'candidate-other' },
      ],
    })).resolves.toMatchObject({ action: 'ask_user' });
  });

  it('times out without surfacing credentials or raw provider errors', async () => {
    const fetchImplementation = vi.fn((_url: Parameters<typeof fetch>[0], init?: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new Error('raw secret test-key')));
      }));
    const provider = new FeatherlessProvider(options({
      fetchImplementation,
      requestTimeoutMs: 5,
      maxRetries: 0,
    }));

    const promise = provider.decideNextAction(guideRequestFixture);
    await expect(promise).rejects.toThrow('Featherless provider request failed.');
    await expect(promise).rejects.not.toThrow('test-key');
  });
});
