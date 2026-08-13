import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { GuideDecisionSchema, type GuideDecision, type GuideRequest } from '../../../../src/contracts';
import { normalizeUiName, semanticLabelFromRules } from '../../../../src/web-knowledge';
import { groundVisualDecision } from './VisualTargetGrounder';

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
- Separate the user's FINAL INTENT from controls that merely contain related words. For a generic website login request, choose the site's canonical account/sign-in control (for example Amazon's "Hello, sign in Account & Lists"). Never choose delivery-location, address, shipping, or other contextual "sign in to ..." shortcuts unless the user explicitly asked about that context.
- For Windows settings tasks, navigation priority is mandatory: (1) the final settings control if visible, (2) a visible/running Settings app or Settings icon, (3) Start, and only then (4) Windows Search. Never choose or instruct typing into Search while a direct Settings control/icon is visible anywhere in the screenshot.
- When a direct Windows Settings icon/control is clearly visible in pixels but absent from candidates, use highlight_visual around that icon instead of choosing a Search candidate.
- Select the most direct visible control that advances the goal. Do not select window chrome (back/minimize/maximize/close) unless explicitly requested.
- Prefer action=highlight with a candidate targetId only when the candidate label, role, app/scope, and screenshot all agree.
- If the right control is represented by a candidate, always return highlight with its targetId so ShowWhere can use the live clickable rectangle. Use highlight_visual only when no matching candidate exists.
- If the right control is visible in pixels but absent/unsafe in candidates, use highlight_visual with one tight normalized box around only that clickable control.
- Coordinates are fractions of the entire supplied screenshot. Never use a whole window, panel, card, or guessed off-screen location.
- If intent has multiple materially different meanings, ask one concise Korean clarification question. Do not guess.
- If the goal cannot yet be completed, give only the immediate next click. Do not keep guiding after completion.
- Write the user-facing message in natural, concise Korean and name the visible target.
- Confidence must reflect visual evidence. Below 0.65, ask for clarification or a new observation instead of pointing.
- ShowWhere's own panel, bubble, tooltip, and existing overlay are never valid targets.`;

function candidateScore(request: GuideRequest, index: number): number {
  const candidate = request.candidates[index];
  const intent = `${request.session.originalUserMessage} ${request.session.goal ?? ''}`.toLowerCase();
  const searchable = `${candidate.label ?? ''} ${candidate.description ?? ''} ${candidate.role}`.toLowerCase();
  const intentWords = intent.split(/[^\p{L}\p{N}]+/u).filter((word) => word.length >= 2);
  const directMatches = intentWords.filter((word) => searchable.includes(word) || intent.includes(searchable.trim())).length;
  const intentSemantic = semanticLabelFromRules(intent);
  const candidateSemantic = semanticLabelFromRules(searchable);
  const semanticMatch = intentSemantic && candidateSemantic === intentSemantic ? 5_000 : 0;
  const normalizedCandidate = normalizeUiName(searchable);
  const contextualLoginPenalty = intentSemantic === 'login'
    && !/address|location|delivery|shipping|주소|위치|배송/u.test(normalizeUiName(intent))
    && /address|location|delivery|shipping|주소|위치|배송/u.test(normalizedCandidate)
    ? 12_000 : 0;
  const canonicalLoginBonus = intentSemantic === 'login'
    && (/^(sign in|log in|login|로그인)(?: link| button)?$/u.test(normalizedCandidate)
      || /hello.*sign in.*account|sign in.*account.*lists/u.test(normalizedCandidate))
    ? 4_000 : 0;
  const scope = String(candidate.attributes?.sourceScope ?? '');
  const globalEntryScore = scope === 'windows_taskbar' ? 600
    : scope === 'windows_window_overview' ? 450
      : 0;
  return directMatches * 2_000 + semanticMatch + canonicalLoginBonus
    - contextualLoginPenalty + globalEntryScore + Math.max(0, 250 - index);
}

function selectCandidates(request: GuideRequest) {
  if (request.candidates.length <= 32) return request.candidates;
  return request.candidates
    .map((candidate, index) => ({ candidate, index, score: candidateScore(request, index) }))
    .sort((left, right) => right.score - left.score || left.index - right.index)
    .slice(0, 32)
    .map(({ candidate }) => candidate);
}

function compactRequest(request: GuideRequest) {
  const attributeKeys = [
    'automationId', 'className', 'controlType', 'processName',
    'sourceScope', 'containerLabel',
  ] as const;
  return {
    session: request.session,
    context: request.context,
    candidates: selectCandidates(request).map((candidate) => ({
      id: candidate.id,
      label: candidate.label?.slice(0, 160) ?? null,
      description: candidate.description?.slice(0, 80) ?? null,
      role: candidate.role,
      bounds: Object.fromEntries(Object.entries(candidate.bounds).map(([key, value]) => [key, Math.round(value)])),
      enabled: candidate.enabled,
      clickable: candidate.clickable,
      attributes: candidate.attributes
        ? Object.fromEntries(attributeKeys.flatMap((key) => {
          const value = candidate.attributes?.[key];
          return typeof value === 'string' && value.length > 0 ? [[key, value.slice(0, 120)]] : [];
        }))
        : null,
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
  return !/credit_balance_exhausted|insufficient_quota|invalid_api_key|401|403|request too large/iu.test(error.message);
}

function retryDelay(error: unknown, attempt: number): number {
  const match = error instanceof Error ? /try again in ([\d.]+)s/iu.exec(error.message) : null;
  return match ? Math.min(20_000, Math.ceil(Number(match[1]) * 1_000) + 100) : 250 * (attempt + 1);
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
            max_output_tokens: 300,
            input: [
              { role: 'system', content: [{ type: 'input_text', text: systemPrompt }] },
              { role: 'user', content: [
                { type: 'input_text', text: JSON.stringify(compactRequest(request)) },
                // Auto preserves enough source detail for small controls. Whenever UIA exposes
                // the control, the model's visual box is snapped back to its live click bounds.
                { type: 'input_image', image_url: request.screenshot, detail: 'auto' },
              ] },
            ],
            text: { format: { type: 'json_schema', name: 'showwhere_next_action', strict: true, schema: decisionJsonSchema } },
          }),
        });
        if (!response.ok) throw new Error(`OpenAI API ${response.status}: ${(await response.text()).slice(0, 500)}`);
        const parsed = JSON.parse(extractOutputText(await response.json())) as Record<string, unknown>;
        return groundVisualDecision(request, GuideDecisionSchema.parse(removeNulls(parsed)));
      } catch (error) {
        lastError = error;
        if (attempt < this.options.maxRetries && shouldRetry(error))
          await new Promise((resolve) => setTimeout(resolve, retryDelay(error, attempt)));
        else
          break;
      } finally {
        clearTimeout(timeout);
      }
    }
    throw lastError instanceof Error ? lastError : new Error('OpenAI request failed.');
  }
}
