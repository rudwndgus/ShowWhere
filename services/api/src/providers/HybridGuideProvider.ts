import { createHash } from 'node:crypto';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { GuideDecisionSchema, type GuideDecision, type GuideRequest } from '../../../../src/contracts';
import type { TextEmbeddingProvider } from './HuggingFaceEmbeddingClient';
import { resolveLocally } from './LocalGuideResolver';
import { resolveWindowsKnowledge, resolveWindowsKnowledgeEntry } from '../windows-knowledge/WindowsKnowledgeResolver';
import { windowsKnowledgeCatalog } from '../windows-knowledge/WindowsKnowledgeCatalog';
import { resolveWebKnowledge } from '../web-knowledge/WebKnowledgeResolver';
import type { WebKnowledgeSource } from '../web-knowledge/WebKnowledgeStore';
import { classifyRequestIntent } from './RequestIntentRouter';
import { HuggingFaceIntentClassifier, type SemanticIntentMatch } from './HuggingFaceIntentClassifier';
import { resolveGoalCompletion } from './GoalCompletionResolver';

export interface HybridGuideProviderOptions {
  minScore: number;
  minMargin: number;
  debug?: boolean;
}

export class HybridGuideProvider implements AiProvider {
  private readonly intentClassifier?: HuggingFaceIntentClassifier;
  private readonly remoteDecisionCache = new Map<string, { expiresAt: number; decision: GuideDecision }>();
  private readonly inFlightRemoteDecisions = new Map<string, Promise<unknown>>();

  constructor(
    private readonly fallback: AiProvider,
    embeddings?: TextEmbeddingProvider,
    private readonly options: HybridGuideProviderOptions = { minScore: 0.68, minMargin: 0.08 },
    private readonly webKnowledge: WebKnowledgeSource = { catalogs: [], patterns: [] },
  ) {
    this.intentClassifier = embeddings
      ? new HuggingFaceIntentClassifier(embeddings, webKnowledge)
      : undefined;
    if (this.intentClassifier && this.options.debug) {
      const startedAt = performance.now();
      void this.intentClassifier.ready().then((ready) => this.log(
        'huggingface_warmup',
        `${ready ? 'ready' : 'unavailable'}:${Math.round(performance.now() - startedAt)}ms`,
      ));
    }
  }

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    const completed = resolveGoalCompletion(request);
    if (completed) {
      this.log('completed', completed.expectedChange);
      return completed;
    }
    const intent = classifyRequestIntent(request, this.webKnowledge);
    this.log('intent', `${intent.domain}:${intent.reason}:${intent.siteId ?? '-'}`);

    if (intent.domain === 'web') {
      const web = resolveWebKnowledge(request, this.webKnowledge);
      if (web) {
        this.log('web_knowledge', web.targetId);
        return web;
      }
      return this.resolveRemotely(request);
    }

    if (intent.domain === 'windows') {
      const windows = resolveWindowsKnowledge(request);
      if (windows) {
        this.log('windows_knowledge', windows.targetId);
        return windows;
      }
      return this.resolveRemotely(request);
    }

    // An exact control explicitly named by the user is itself a high-confidence
    // intent signal and does not need a remote classification round trip.
    const direct = resolveLocally(request);
    if (direct) {
      this.log('direct_explicit_target', direct.targetId);
      return direct;
    }

    return this.resolveRemotely(request);
  }

  private async resolveRemotely(request: GuideRequest): Promise<unknown> {
    const cacheKey = remoteDecisionCacheKey(request);
    const cached = this.remoteDecisionCache.get(cacheKey);
    if (cached && cached.expiresAt > Date.now()) {
      this.log('verified_decision_cache', cached.decision.targetId ?? cached.decision.action);
      return cached.decision;
    }
    if (cached) this.remoteDecisionCache.delete(cacheKey);

    const existing = this.inFlightRemoteDecisions.get(cacheKey);
    if (existing) {
      this.log('verified_decision_inflight', 'joined');
      return existing;
    }

    const pending = this.resolveRemotelyUncached(request).then((value) => {
      const parsed = GuideDecisionSchema.safeParse(value);
      if (parsed.success && canReuseDecision(parsed.data, request)) {
        if (this.remoteDecisionCache.size >= 128)
          this.remoteDecisionCache.delete(this.remoteDecisionCache.keys().next().value!);
        this.remoteDecisionCache.set(cacheKey, {
          expiresAt: Date.now() + 120_000,
          decision: parsed.data,
        });
      }
      return value;
    }).finally(() => this.inFlightRemoteDecisions.delete(cacheKey));
    this.inFlightRemoteDecisions.set(cacheKey, pending);
    return pending;
  }

  private async resolveRemotelyUncached(request: GuideRequest): Promise<unknown> {
    if (!this.intentClassifier) {
      this.log('openai', 'reasoning');
      return this.fallback.decideNextAction(request);
    }

    const goal = request.session.goal ?? request.session.originalUserMessage;
    const cachedIntent = this.intentClassifier.getCached(goal);
    if (cachedIntent) {
      const cached = this.resolveIntentMatch(request, cachedIntent);
      if (cached) {
        this.log('huggingface_cached_intent', cached.targetId);
        return cached;
      }
    }

    // A new ambiguous intent starts HF intent classification and GPT vision together.
    // HF can win only after mapping the intent through a deterministic domain
    // route; raw semantic similarity is never allowed to point at a UI control.
    const openai = this.fallback.decideNextAction(request)
      .then((value) => ({ value, error: undefined as unknown }))
      .catch((error: unknown) => ({ value: undefined, error }));
    const huggingFace = this.resolveWithIntentEmbeddings(request).catch((error) => {
      this.log('huggingface_fallback', error instanceof Error ? error.message : String(error));
      return undefined;
    });
    const first = await Promise.race([
      openai.then((result) => ({ source: 'openai' as const, ...result })),
      huggingFace.then((value) => ({ source: 'huggingface' as const, value })),
    ]);
    if (first.source === 'openai') {
      if (first.error) {
        const hfValue = await huggingFace;
        if (hfValue) return hfValue;
        throw first.error;
      }
      this.log('openai', 'reasoning_parallel');
      return first.value;
    }
    if (first.value) {
      this.log('huggingface', first.value.targetId);
      return first.value;
    }
    this.log('openai', 'reasoning_after_hf_miss');
    const openaiResult = await openai;
    if (openaiResult.error) throw openaiResult.error;
    return openaiResult.value;
  }

  private async resolveWithIntentEmbeddings(request: GuideRequest): Promise<GuideDecision | undefined> {
    if (!this.intentClassifier) return undefined;
    const goal = request.session.goal ?? request.session.originalUserMessage;
    const intent = await this.intentClassifier.classify(goal);
    if (!intent) return undefined;
    this.log('huggingface_intent', `${intent.domain}:${intent.intentId}:${intent.score.toFixed(3)}:${intent.margin.toFixed(3)}`);
    return this.resolveIntentMatch(request, intent);
  }

  private resolveIntentMatch(request: GuideRequest, intent: SemanticIntentMatch): GuideDecision | undefined {
    if (intent.score < this.options.minScore) return undefined;
    if (intent.margin < this.options.minMargin) {
      const nearBest = intent.alternatives.filter((item) => intent.score - item.score <= 0.035);
      const decisions = nearBest.map((item) => this.resolveSemanticIntent(request, item)).filter(Boolean) as GuideDecision[];
      const targetIds = new Set(decisions.map((decision) => `${decision.action}:${decision.targetId ?? ''}`));
      if (decisions.length < 2 || targetIds.size !== 1) return undefined;
      this.log('huggingface_consensus', `${nearBest.length}:${decisions[0].targetId}`);
      return { ...decisions[0], confidence: Math.min(0.92, Math.max(0.7, intent.score)) };
    }

    return this.resolveSemanticIntent(request, intent);
  }

  private resolveSemanticIntent(
    request: GuideRequest,
    intent: Pick<SemanticIntentMatch, 'domain' | 'intentId' | 'score'>,
  ): GuideDecision | undefined {
    if (intent.domain === 'windows') {
      const entry = windowsKnowledgeCatalog.find((item) => item.id === intent.intentId);
      return entry
        ? resolveWindowsKnowledgeEntry(request, entry, Math.min(0.96, Math.max(0.7, intent.score)))
        : undefined;
    }
    if (intent.intentId.startsWith('site:')) return undefined;
    return resolveWebKnowledge(request, this.webKnowledge, intent.intentId);
  }

  private log(route: string, detail: unknown): void {
    if (this.options.debug)
      console.log(`[showwhere:router] route=${route} detail=${String(detail).slice(0, 160)}`);
  }
}

function remoteDecisionCacheKey(request: GuideRequest): string {
  const hash = createHash('sha256');
  hash.update(JSON.stringify({
    session: {
      originalUserMessage: request.session.originalUserMessage,
      goal: request.session.goal,
      mode: request.session.mode,
      currentStep: request.session.currentStep,
      completedSteps: request.session.completedSteps,
      knownFacts: request.session.knownFacts,
      expectedChange: request.session.expectedChange,
      failureCount: request.session.failureCount,
    },
    context: request.context,
    candidates: request.candidates,
    screenshotBounds: request.screenshotBounds,
  }));
  hash.update('|screenshot|');
  hash.update(request.screenshot ?? '');
  return hash.digest('hex');
}

function canReuseDecision(decision: GuideDecision, request: GuideRequest): boolean {
  const candidateIds = new Set(request.candidates.map((candidate) => candidate.id));
  if (decision.action === 'highlight' && (!decision.targetId || !candidateIds.has(decision.targetId)))
    return false;
  if (decision.alternativeTargetIds?.some((id) => !candidateIds.has(id))) return false;
  return true;
}
