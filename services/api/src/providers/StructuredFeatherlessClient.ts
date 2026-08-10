import { z } from 'zod';
import type { ApiConfig } from '../config';

const responseSchema = z.object({
  choices: z.array(z.object({ message: z.object({ content: z.string() }).passthrough() }).passthrough()).min(1),
}).passthrough();

function parseJson(content: string): unknown {
  const cleaned = content.trim().replace(/^```(?:json)?\s*/iu, '').replace(/\s*```$/u, '').trim();
  try { return JSON.parse(cleaned); } catch {
    const start = cleaned.indexOf('{');
    const end = cleaned.lastIndexOf('}');
    if (start < 0 || end <= start) return undefined;
    try { return JSON.parse(cleaned.slice(start, end + 1)); } catch { return undefined; }
  }
}

export class StructuredFeatherlessClient {
  constructor(
    private readonly config: NonNullable<ApiConfig['featherless']>,
    private readonly fetchImplementation: typeof fetch = fetch,
  ) {}

  async completeJson<T>(
    models: readonly string[],
    systemPrompt: string,
    payload: unknown,
    schema: z.ZodType<T>,
    maxTokens = 1_400,
  ): Promise<T> {
    let lastCategory = 'unavailable_model';
    for (const model of models) {
      for (let attempt = 0; attempt <= this.config.maxRetries; attempt += 1) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), this.config.requestTimeoutMs);
        try {
          const response = await this.fetchImplementation(
            `${this.config.baseUrl.replace(/\/$/u, '')}/chat/completions`,
            {
              method: 'POST',
              headers: {
                Authorization: `Bearer ${this.config.apiKey}`,
                'Content-Type': 'application/json',
                'HTTP-Referer': 'https://github.com/rudwndgus/ShowWhere',
                'X-Title': 'ShowWhere',
                'User-Agent': 'ShowWhere/0.2',
              },
              body: JSON.stringify({
                model, temperature: 0, max_tokens: maxTokens,
                response_format: { type: 'json_object' },
                chat_template_kwargs: { enable_thinking: false },
                messages: [
                  { role: 'system', content: systemPrompt },
                  { role: 'user', content: JSON.stringify(payload) },
                ],
              }),
              signal: controller.signal,
            },
          );
          if (!response.ok) {
            lastCategory = response.status === 429 ? 'rate_limit'
              : response.status >= 500 ? 'provider_5xx'
                : response.status === 404 ? 'unavailable_model' : 'provider_rejected';
            if ((response.status === 408 || response.status === 429 || response.status >= 500)
                && attempt < this.config.maxRetries) continue;
            break;
          }
          const envelope = responseSchema.parse(await response.json());
          const parsed = schema.safeParse(parseJson(envelope.choices[0].message.content));
          if (parsed.success) return parsed.data;
          lastCategory = 'malformed_output';
          break;
        } catch {
          lastCategory = controller.signal.aborted ? 'timeout' : 'network';
          if (attempt >= this.config.maxRetries) break;
        } finally {
          clearTimeout(timeout);
        }
      }
    }
    throw new Error(`Structured provider request failed (${lastCategory}).`);
  }
}
