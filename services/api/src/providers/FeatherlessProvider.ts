import { z } from 'zod';
import { GuideDecisionSchema, type GuideRequest } from '../../../../src/contracts';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { createGuideMessages } from './guidePrompt';

const completionResponseSchema = z.object({
  choices: z.array(z.object({
    message: z.object({
      content: z.string(),
    }).passthrough(),
  }).passthrough()).min(1),
}).passthrough();

export interface FeatherlessProviderOptions {
  apiKey: string;
  baseUrl: string;
  model: string;
  requestTimeoutMs: number;
  maxRetries: number;
  retryBaseDelayMs: number;
  maxTokens: number;
  fetchImplementation?: typeof fetch;
  delay?: (milliseconds: number) => Promise<void>;
}

class ProviderRequestError extends Error {
  constructor(readonly retryable: boolean) {
    super('Featherless provider request failed.');
  }
}

async function isRetryableProviderResponse(response: Response): Promise<boolean> {
  if (response.status === 408 || response.status === 429 || response.status >= 500) return true;
  if (response.status !== 400) return false;

  try {
    const body = (await response.text()).toLowerCase();
    return body.includes('model is busy')
      || body.includes('try again later')
      || body.includes('temporarily unavailable')
      || body.includes('overloaded');
  } catch {
    return false;
  }
}

function parseJsonObject(content: string): unknown {
  const trimmed = content.trim();
  const withoutFence = trimmed
    .replace(/^```(?:json)?\s*/iu, '')
    .replace(/\s*```$/u, '')
    .trim();

  try {
    return JSON.parse(withoutFence);
  } catch {
    const firstBrace = withoutFence.indexOf('{');
    const lastBrace = withoutFence.lastIndexOf('}');
    if (firstBrace < 0 || lastBrace <= firstBrace) return undefined;
    try {
      return JSON.parse(withoutFence.slice(firstBrace, lastBrace + 1));
    } catch {
      return undefined;
    }
  }
}

function defaultDelay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

export class FeatherlessProvider implements AiProvider {
  readonly #options: FeatherlessProviderOptions;
  readonly #fetch: typeof fetch;
  readonly #delay: (milliseconds: number) => Promise<void>;

  constructor(options: FeatherlessProviderOptions) {
    this.#options = options;
    this.#fetch = options.fetchImplementation ?? fetch;
    this.#delay = options.delay ?? defaultDelay;
  }

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    for (let responseAttempt = 0; responseAttempt < 2; responseAttempt += 1) {
      const content = await this.#requestCompletion(request, responseAttempt === 1);
      const parsed = GuideDecisionSchema.safeParse(parseJsonObject(content));
      if (parsed.success) return parsed.data;
    }

    throw new ProviderRequestError(false);
  }

  async #requestCompletion(request: GuideRequest, repairMalformedResponse: boolean): Promise<string> {
    const endpoint = `${this.#options.baseUrl.replace(/\/$/u, '')}/chat/completions`;

    for (let attempt = 0; attempt <= this.#options.maxRetries; attempt += 1) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), this.#options.requestTimeoutMs);

      try {
        const response = await this.#fetch(endpoint, {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${this.#options.apiKey}`,
            'Content-Type': 'application/json',
            'HTTP-Referer': 'https://github.com/rudwndgus/ShowWhere',
            'X-Title': 'ShowWhere',
          },
          body: JSON.stringify({
            model: this.#options.model,
            messages: createGuideMessages(request, repairMalformedResponse),
            temperature: 0,
            max_tokens: this.#options.maxTokens,
          }),
          signal: controller.signal,
        });

        if (!response.ok) {
          throw new ProviderRequestError(await isRetryableProviderResponse(response));
        }

        const body = completionResponseSchema.safeParse(await response.json());
        if (!body.success) throw new ProviderRequestError(false);
        return body.data.choices[0].message.content;
      } catch (error) {
        const retryable = error instanceof ProviderRequestError ? error.retryable : true;
        if (!retryable || attempt >= this.#options.maxRetries) {
          throw new ProviderRequestError(false);
        }
        await this.#delay(this.#options.retryBaseDelayMs * (2 ** attempt));
      } finally {
        clearTimeout(timeout);
      }
    }

    throw new ProviderRequestError(false);
  }
}
