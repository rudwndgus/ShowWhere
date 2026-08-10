import { z } from 'zod';

const completionSchema = z.object({
  choices: z.array(z.object({ message: z.object({ content: z.string() }).passthrough() }).passthrough()).min(1),
}).passthrough();

export interface LearningProviderOptions {
  apiKey: string;
  baseUrl: string;
  timeoutMs: number;
  maxTokens: number;
  retries: number;
  fetchImplementation?: typeof fetch;
  delay?: (milliseconds: number) => Promise<void>;
}

export class LearningProvider {
  private readonly fetchImplementation: typeof fetch;
  private readonly delay: (milliseconds: number) => Promise<void>;

  constructor(private readonly options: LearningProviderOptions) {
    this.fetchImplementation = options.fetchImplementation ?? fetch;
    this.delay = options.delay ?? ((milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)));
  }

  async completeJson<T>(model: string, system: string, user: unknown, schema: z.ZodType<T>): Promise<T> {
    let lastError: unknown;
    for (let attempt = 0; attempt <= this.options.retries; attempt += 1) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), this.options.timeoutMs);
      try {
        const response = await this.fetchImplementation(`${this.options.baseUrl.replace(/\/$/u, '')}/chat/completions`, {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${this.options.apiKey}`,
            'Content-Type': 'application/json',
            'HTTP-Referer': 'https://github.com/rudwndgus/ShowWhere',
            'X-Title': 'ShowWhere Learning Pipeline',
          },
          body: JSON.stringify({
            model,
            temperature: 0.25,
            max_tokens: this.options.maxTokens,
            response_format: { type: 'json_object' },
            messages: [
              { role: 'system', content: system },
              { role: 'user', content: JSON.stringify(user) },
            ],
          }),
          signal: controller.signal,
        });
        if (!response.ok) {
          const retryAfterSeconds = Number(response.headers.get('retry-after'));
          const retryAfterMs = Number.isFinite(retryAfterSeconds) ? Math.min(retryAfterSeconds * 1_000, 30_000) : undefined;
          throw new ProviderHttpError(response.status, retryAfterMs);
        }
        const completion = completionSchema.parse(await response.json());
        const parsed = parseJson(completion.choices[0].message.content);
        return schema.parse(parsed);
      } catch (error) {
        lastError = error;
        if (attempt < this.options.retries) {
          const retryAfterMs = error instanceof ProviderHttpError ? error.retryAfterMs : undefined;
          await this.delay(retryAfterMs ?? 1_000 * (2 ** attempt));
        }
      } finally {
        clearTimeout(timeout);
      }
    }
    throw lastError;
  }
}

class ProviderHttpError extends Error {
  constructor(readonly status: number, readonly retryAfterMs?: number) {
    super(`Provider returned HTTP ${status}.`);
  }
}

function parseJson(content: string): unknown {
  const text = content.trim().replace(/^```(?:json)?\s*/iu, '').replace(/\s*```$/u, '');
  try {
    return JSON.parse(text);
  } catch {
    const start = text.indexOf('{');
    const end = text.lastIndexOf('}');
    if (start < 0 || end <= start) throw new Error('Model did not return JSON.');
    return JSON.parse(text.slice(start, end + 1));
  }
}
