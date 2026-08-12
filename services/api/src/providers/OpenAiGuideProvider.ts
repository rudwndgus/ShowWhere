import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { GuideDecisionSchema, type GuideDecision, type GuideRequest } from '../../../../src/contracts';

export interface OpenAiGuideProviderOptions {
  apiKey: string;
  model: string;
  baseUrl: string;
  requestTimeoutMs: number;
  maxRetries: number;
}

const decisionJsonSchema = {
  type: 'object',
  additionalProperties: false,
  required: ['status', 'action', 'targetId', 'message', 'expectedChange', 'confidence', 'alternativeTargetIds', 'visualTarget'],
  properties: {
    status: { type: 'string', enum: ['in_progress', 'completed', 'needs_clarification', 'blocked'] },
    action: { type: 'string', enum: ['highlight', 'highlight_visual', 'ask_user', 'explain', 'request_new_observation'] },
    targetId: { type: ['string', 'null'] },
    message: { type: 'string' },
    expectedChange: { type: ['string', 'null'] },
    confidence: { type: 'number', minimum: 0, maximum: 1 },
    alternativeTargetIds: { type: ['array', 'null'], items: { type: 'string' }, minItems: 2, maxItems: 4 },
    visualTarget: {
      anyOf: [
        { type: 'null' },
        {
          type: 'object', additionalProperties: false,
          required: ['x', 'y', 'width', 'height', 'label'],
          properties: {
            x: { type: 'number', minimum: 0, maximum: 1 },
            y: { type: 'number', minimum: 0, maximum: 1 },
            width: { type: 'number', minimum: 0.005, maximum: 1 },
            height: { type: 'number', minimum: 0.005, maximum: 1 },
            label: { type: 'string' },
          },
        },
      ],
    },
  },
} as const;

const systemPrompt = `You are the visual decision brain of ShowWhere, a Windows assistant.
Given the user's original goal, progress, current app metadata, UI Automation candidates, and a FULL desktop screenshot, decide exactly ONE next step.

Rules:
- You are responsible for advancing the user's whole goal step by step, not merely identifying a control already named in the question.
- Treat questions such as "where is ...?", "how do I ...?", and troubleshooting questions as requests for interactive guidance unless the user explicitly asks for text-only information.
- A clear goal with a known destination is NOT ambiguous. Never ask the user where a standard Windows feature, app, setting, or website control is located. Use your knowledge plus the current screen to choose the best visible entry point and keep navigating.
- Use ask_user only when the USER'S INTENT has two or more materially different meanings that would lead to different outcomes. Missing, hidden, or not-yet-visible controls are navigation problems, not reasons to ask the user.
- If the final destination is not visible yet, choose the safest visible entry point that moves toward it (for example an already-open relevant app/category, or the taskbar Start/Search entry point). After the user clicks it, the next request will contain the changed screen and completed-step history.
- Never repeat a control recorded in completedSteps unless the screen proves the previous click did not take effect.
- First decide whether the user's goal is already complete from visible evidence. If complete: status=completed, action=explain, no target.
- Understand the destination and scope. A website search belongs inside that website, never in the browser address bar unless the user explicitly asks for web/navigation search.
- Select the most direct visible control that advances the goal. Do not select window chrome (back/minimize/maximize/close) unless explicitly requested.
- Prefer action=highlight with a candidate targetId only when the candidate label, role, app/scope, and screenshot all agree.
- If the right control is visible in pixels but absent/unsafe in candidates, use highlight_visual with one tight normalized box around only that clickable control.
- Coordinates are fractions of the entire supplied screenshot. Never use a whole window, panel, card, or guessed off-screen location.
- If intent has multiple materially different meanings, ask one concise Korean clarification question. Do not guess.
- If the goal cannot yet be completed, give only the immediate next click. Do not keep guiding after completion.
- Write the user-facing message in natural, concise Korean and name the visible target.
- Confidence must reflect visual evidence. Below 0.65, ask for clarification or a new observation instead of pointing.
- ShowWhere's own panel, bubble, tooltip, and existing overlay are never valid targets.`;

function compactRequest(request: GuideRequest) {
  return {
    session: request.session,
    context: request.context,
    candidates: request.candidates.map((candidate) => ({
      id: candidate.id,
      label: candidate.label ?? null,
      description: candidate.description ?? null,
      role: candidate.role,
      bounds: candidate.bounds,
      attributes: candidate.attributes ?? null,
    })),
  };
}

function extractOutputText(response: unknown): string {
  if (typeof response !== 'object' || response === null) throw new Error('OpenAI returned an invalid response.');
  const direct = (response as { output_text?: unknown }).output_text;
  if (typeof direct === 'string' && direct.length > 0) return direct;
  const output = (response as { output?: unknown }).output;
  if (!Array.isArray(output)) throw new Error('OpenAI response did not contain output.');
  for (const item of output) {
    if (typeof item !== 'object' || item === null || !Array.isArray((item as { content?: unknown }).content)) continue;
    for (const content of (item as { content: unknown[] }).content) {
      if (typeof content === 'object' && content !== null && typeof (content as { text?: unknown }).text === 'string')
        return (content as { text: string }).text;
    }
  }
  throw new Error('OpenAI response did not contain text.');
}

function removeNulls(value: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== null));
}

function shouldRetry(error: unknown): boolean {
  if (!(error instanceof Error)) return true;
  return !/credit_balance_exhausted|insufficient_quota|invalid_api_key|401|403/iu.test(error.message);
}

export class OpenAiGuideProvider implements AiProvider {
  constructor(private readonly options: OpenAiGuideProviderOptions) {}

  async decideNextAction(request: GuideRequest): Promise<GuideDecision> {
    if (!request.screenshot) throw new Error('A full desktop screenshot is required for GPT guidance.');
    let lastError: unknown;
    for (let attempt = 0; attempt <= this.options.maxRetries; attempt++) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), this.options.requestTimeoutMs);
      try {
        const response = await fetch(`${this.options.baseUrl}/responses`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${this.options.apiKey}`, 'Content-Type': 'application/json' },
          signal: controller.signal,
          body: JSON.stringify({
            model: this.options.model,
            store: false,
            reasoning: { effort: 'low' },
            max_output_tokens: 700,
            input: [
              { role: 'system', content: [{ type: 'input_text', text: systemPrompt }] },
              { role: 'user', content: [
                { type: 'input_text', text: JSON.stringify(compactRequest(request)) },
                { type: 'input_image', image_url: request.screenshot, detail: 'high' },
              ] },
            ],
            text: { format: { type: 'json_schema', name: 'showwhere_next_action', strict: true, schema: decisionJsonSchema } },
          }),
        });
        if (!response.ok) throw new Error(`OpenAI API ${response.status}: ${(await response.text()).slice(0, 500)}`);
        const parsed = JSON.parse(extractOutputText(await response.json())) as Record<string, unknown>;
        return GuideDecisionSchema.parse(removeNulls(parsed));
      } catch (error) {
        lastError = error;
        if (attempt < this.options.maxRetries && shouldRetry(error))
          await new Promise((resolve) => setTimeout(resolve, 250 * (attempt + 1)));
        else
          break;
      } finally {
        clearTimeout(timeout);
      }
    }
    throw lastError instanceof Error ? lastError : new Error('OpenAI request failed.');
  }
}
