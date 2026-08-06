import {
  GuideDecisionSchema,
  GuideRequestSchema,
  targetExistsInRequest,
  type GuideDecision,
  type GuideRequest,
} from '../../contracts';
import { GUIDE_API_PATH, handleGuideApiRequest } from '../../guide-api/handleGuideApiRequest';
import type { AiProvider } from '../../guide-api/AiProvider';
import { MockAiProvider } from '../../guide-api/MockAiProvider';
import { EXTENSION_MESSAGES, type GuideRuntimeResponse } from '../../shared/messages';

export interface GuideClient {
  decideNextAction(request: GuideRequest): Promise<GuideDecision>;
}

export class GuideServiceError extends Error {
  constructor() {
    super('지금은 안내 서비스에 연결할 수 없어요. 잠시 후 다시 시도해 주세요.');
    this.name = 'GuideServiceError';
  }
}

export class MockGuideClient implements GuideClient {
  readonly #provider: AiProvider;

  constructor(provider: AiProvider = new MockAiProvider()) {
    this.#provider = provider;
  }

  async decideNextAction(request: GuideRequest): Promise<GuideDecision> {
    const validatedRequest = GuideRequestSchema.parse(request);
    const response = await handleGuideApiRequest(
      GUIDE_API_PATH,
      structuredClone(validatedRequest),
      this.#provider,
    );
    return GuideDecisionSchema.parse(response.decision);
  }
}

export interface HttpGuideClientOptions {
  timeoutMs?: number;
  sendMessage?: (message: unknown) => Promise<GuideRuntimeResponse>;
}

export class HttpGuideClient implements GuideClient {
  readonly #timeoutMs: number;
  readonly #sendMessage: (message: unknown) => Promise<GuideRuntimeResponse>;

  constructor(options: HttpGuideClientOptions) {
    this.#timeoutMs = options.timeoutMs ?? 100_000;
    this.#sendMessage = options.sendMessage ?? ((message) =>
      chrome.runtime.sendMessage(message) as Promise<GuideRuntimeResponse>);
  }

  async decideNextAction(request: GuideRequest): Promise<GuideDecision> {
    const validatedRequest = GuideRequestSchema.parse(request);
    let timeout: number | undefined;

    try {
      const timeoutPromise = new Promise<GuideRuntimeResponse>((_resolve, reject) => {
        timeout = window.setTimeout(() => reject(new GuideServiceError()), this.#timeoutMs);
      });
      const response = await Promise.race([
        this.#sendMessage({ type: EXTENSION_MESSAGES.guideRequest, request: validatedRequest }),
        timeoutPromise,
      ]);
      if (!response.ok) throw new GuideServiceError();
      const decision = GuideDecisionSchema.safeParse(response.decision);
      if (!decision.success || !targetExistsInRequest(decision.data, validatedRequest)) {
        throw new GuideServiceError();
      }
      return decision.data;
    } catch (error) {
      if (error instanceof GuideServiceError) throw error;
      throw new GuideServiceError();
    } finally {
      if (timeout !== undefined) window.clearTimeout(timeout);
    }
  }
}

export function createGuideClient(): GuideClient {
  const endpoint = import.meta.env.VITE_SHOWWHERE_GUIDE_API_URL?.trim();
  return endpoint ? new HttpGuideClient({}) : new MockGuideClient();
}
