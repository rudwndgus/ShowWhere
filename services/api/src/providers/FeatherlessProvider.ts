import { z } from 'zod';
import { GuideDecisionSchema, type GuideRequest } from '../../../../src/contracts';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { createGuideMessages } from './guidePrompt';
import { createUiTarsMessages, isUiTarsModel, parseUiTarsDecision } from './uiTarsAdapter';
import { ModelRouter } from './ModelRouter';

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
  guideFallbackModel?: string;
  visionModels: readonly string[];
  fastModel?: string;
  reasoningModel?: string;
  embeddingModel?: string;
  learningGeneratorModel?: string;
  learningJudgeModels?: readonly string[];
  requestTimeoutMs: number;
  maxRetries: number;
  retryBaseDelayMs: number;
  maxTokens: number;
  enableThinking: boolean;
  debug: boolean;
  fetchImplementation?: typeof fetch;
  delay?: (milliseconds: number) => Promise<void>;
}

export type ProviderErrorCategory =
  | 'timeout'
  | 'rate_limit'
  | 'busy'
  | 'provider_5xx'
  | 'malformed_output'
  | 'unavailable_model'
  | 'network'
  | 'unknown';

class ProviderRequestError extends Error {
  constructor(readonly category: ProviderErrorCategory, readonly retryable: boolean) {
    super('Featherless provider request failed.');
  }
}

async function classifyProviderResponse(response: Response): Promise<ProviderRequestError> {
  if (response.status === 408) return new ProviderRequestError('timeout', true);
  if (response.status === 429) return new ProviderRequestError('rate_limit', true);
  if (response.status === 404) return new ProviderRequestError('unavailable_model', false);
  if (response.status >= 500) return new ProviderRequestError('provider_5xx', true);
  if (response.status !== 400) return new ProviderRequestError('unknown', false);

  try {
    const body = (await response.text()).toLowerCase();
    const busy = body.includes('model is busy')
      || body.includes('try again later')
      || body.includes('temporarily unavailable')
      || body.includes('overloaded');
    return new ProviderRequestError(busy ? 'busy' : 'unknown', busy);
  } catch {
    return new ProviderRequestError('unknown', false);
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

function applySafeDecisionDefaults(value: unknown): unknown {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return value;
  const decision = { ...(value as Record<string, unknown>) };
  const rawVisualTarget = decision.visualTarget;
  for (const field of ['targetId', 'visualTarget', 'safeToolId', 'alternativeTargetIds', 'expectedChange']) {
    if (decision[field] === null) delete decision[field];
  }
  const normalizedVisualTarget = normalizeVisualTarget(rawVisualTarget);
  const confidence = Number(decision.confidence);
  const hasAlternatives = Array.isArray(decision.alternativeTargetIds)
    && decision.alternativeTargetIds.length >= 2;
  if (decision.action === 'ask_user'
      && normalizedVisualTarget
      && !hasAlternatives
      && (!Number.isFinite(confidence) || confidence >= 0.65)) {
    decision.status = 'in_progress';
    decision.action = 'highlight_visual';
    decision.visualTarget = normalizedVisualTarget;
    decision.confidence = Number.isFinite(confidence) ? confidence : 0.72;
  }
  if (decision.confidence === undefined
      && ['ask_user', 'explain', 'request_new_observation', 'request_vision'].includes(String(decision.action))) {
    decision.confidence = 0;
  }
  if (decision.action !== 'highlight') delete decision.targetId;
  if (decision.action !== 'highlight_visual') delete decision.visualTarget;
  if (decision.action !== 'request_safe_tool') delete decision.safeToolId;
  if (decision.action !== 'ask_user') delete decision.alternativeTargetIds;
  if (decision.alternativeTargetIds !== undefined
      && (!Array.isArray(decision.alternativeTargetIds)
        || decision.alternativeTargetIds.length < 2
        || decision.alternativeTargetIds.length > 4)) {
    delete decision.alternativeTargetIds;
  }
  if (decision.expectedChange !== undefined && typeof decision.expectedChange !== 'string') {
    delete decision.expectedChange;
  }
  if (decision.action === 'highlight_visual'
      && normalizedVisualTarget) decision.visualTarget = normalizedVisualTarget;
  return decision;
}

function normalizeVisualTarget(value: unknown): Record<string, unknown> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
  const source = value as Record<string, unknown>;
  let x = Number(source.x);
  let y = Number(source.y);
  let width = Number(source.width);
  let height = Number(source.height);
  const label = typeof source.label === 'string' ? source.label.trim() : '';
  if (![x, y, width, height].every(Number.isFinite) || !label || x < 0 || y < 0 || x > 1 || y > 1) {
    return undefined;
  }

  if (width < 0.005) {
    width = 0.03;
    x = Math.max(0, Math.min(1 - width, x - width / 2));
  } else {
    width = Math.min(width, 1 - x);
  }
  if (height < 0.005) {
    height = 0.04;
    y = Math.max(0, Math.min(1 - height, y - height / 2));
  } else {
    height = Math.min(height, 1 - y);
  }
  if (width < 0.005 || height < 0.005) return undefined;
  return { x, y, width, height, label };
}

function defaultDelay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

export class FeatherlessProvider implements AiProvider {
  readonly #options: FeatherlessProviderOptions;
  readonly #fetch: typeof fetch;
  readonly #delay: (milliseconds: number) => Promise<void>;
  readonly #router: ModelRouter;

  constructor(options: FeatherlessProviderOptions) {
    this.#options = options;
    this.#fetch = options.fetchImplementation ?? fetch;
    this.#delay = options.delay ?? defaultDelay;
    this.#router = new ModelRouter({
      guideModel: options.model,
      guideFallbackModel: options.guideFallbackModel,
      reasoningModel: options.reasoningModel,
      visionModels: options.visionModels,
      fastModel: options.fastModel,
      embeddingModel: options.embeddingModel,
      learningGeneratorModel: options.learningGeneratorModel,
      learningJudgeModels: options.learningJudgeModels,
    });
  }

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    const models = request.screenshot ? this.#router.vision() : this.#router.guide();
    for (const model of models) {
      try {
        const nativeUiTars = Boolean(request.screenshot) && isUiTarsModel(model);
        const responseAttempts = nativeUiTars ? 1 : 2;
        for (let responseAttempt = 0; responseAttempt < responseAttempts; responseAttempt += 1) {
          const retryLimit = this.#options.maxRetries;
          const content = await this.#requestCompletion(
            request,
            responseAttempt === 1,
            model,
            retryLimit,
            nativeUiTars,
          );
          if (nativeUiTars) {
            const nativeDecision = parseUiTarsDecision(content, request);
            if (nativeDecision) {
              this.#log(`native_grounding model=${model} action=${nativeDecision.action}`);
              return nativeDecision;
            }
            this.#log(`native_grounding_invalid model=${model} trying_fallback=${model !== models.at(-1)}`);
            break;
          }
          const normalized = applySafeDecisionDefaults(parseJsonObject(content));
          const parsed = GuideDecisionSchema.safeParse(normalized);
          if (parsed.success) {
            const isInconclusiveVisionDecision = request.screenshot
              && ['ask_user', 'request_vision', 'request_new_observation'].includes(parsed.data.action);
            if (isInconclusiveVisionDecision && model !== models.at(-1)) {
              this.#log(`vision_inconclusive model=${model} action=${parsed.data.action} trying_fallback=true`);
              break;
            }
            return parsed.data;
          }
          const action = normalized && typeof normalized === 'object' && !Array.isArray(normalized)
            ? String((normalized as Record<string, unknown>).action ?? 'missing')
            : 'missing';
          this.#log(`invalid_decision model=${model} action=${action} issues=${parsed.error.issues.map((issue) => issue.path.join('.') || 'root').join(',')}`);
        }
      } catch (error) {
        if (model === models.at(-1)) throw error;
        const category = error instanceof ProviderRequestError ? error.category : 'unknown';
        this.#log(`model_failed model=${model} category=${category} fallback_used=true`);
      }
    }

    throw new ProviderRequestError('malformed_output', false);
  }

  async #requestCompletion(
    request: GuideRequest,
    repairMalformedResponse: boolean,
    model: string,
    retryLimit: number,
    nativeUiTars: boolean,
  ): Promise<string> {
    const endpoint = `${this.#options.baseUrl.replace(/\/$/u, '')}/chat/completions`;

    for (let attempt = 0; attempt <= retryLimit; attempt += 1) {
      const startedAt = performance.now();
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
            'User-Agent': 'ShowWhere/0.2',
          },
          body: JSON.stringify({
            model,
            messages: nativeUiTars
              ? createUiTarsMessages(request)
              : createGuideMessages(request, repairMalformedResponse),
            temperature: 0,
            max_tokens: this.#options.maxTokens,
            ...(!nativeUiTars ? {
              chat_template_kwargs: {
                enable_thinking: this.#options.enableThinking,
              },
              response_format: { type: 'json_object' },
            } : {}),
          }),
          signal: controller.signal,
        });

        if (!response.ok) {
          throw await classifyProviderResponse(response);
        }

        const body = completionResponseSchema.safeParse(await response.json());
        if (!body.success) throw new ProviderRequestError('malformed_output', false);
        this.#log(`completed model=${model} attempt=${attempt + 1} duration_ms=${Math.round(performance.now() - startedAt)}`);
        return body.data.choices[0].message.content;
      } catch (error) {
        const aborted = controller.signal.aborted;
        const category: ProviderErrorCategory = aborted
          ? 'timeout'
          : error instanceof ProviderRequestError ? error.category : 'network';
        const retryable = aborted || (error instanceof ProviderRequestError ? error.retryable : true);
        this.#log(`failed model=${model} attempt=${attempt + 1} duration_ms=${Math.round(performance.now() - startedAt)} category=${category} retryable=${retryable}`);
        if (!retryable || attempt >= retryLimit) {
          throw new ProviderRequestError(category, false);
        }
        await this.#delay(this.#options.retryBaseDelayMs * (2 ** attempt));
      } finally {
        clearTimeout(timeout);
      }
    }

    throw new ProviderRequestError('unknown', false);
  }

  #log(message: string): void {
    if (!this.#options.debug) return;
    console.log(`[showwhere:ai] ${message}`);
  }
}
