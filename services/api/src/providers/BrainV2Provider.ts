import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import type { GuideDecision, GuideRequest, UiCandidate, VisualTarget } from '../../../../src/contracts';
import type { BrainMode, LearningEventV2, MemoryHit, RerankerResult, SemanticDecision } from '../../../../src/brain-v2/contracts';
import type { CandidateReranker, LearningEventRecorder, OutcomeVerifier, SemanticRetriever, TaskStateReasoner, VisionGrounder } from '../../../../src/brain-v2/interfaces';
import { JsonMemoryProvider } from '../../../../src/brain-v2/localMemory';
import { JsonlLearningEventRecorder } from '../../../../src/brain-v2/learningEventRecorder';
import { EvidenceOutcomeVerifier } from '../../../../src/brain-v2/outcomeVerifier';
import { LocalModelClient } from './LocalModelClient';

export interface BrainV2ProviderOptions {
  mode: BrainMode;
  baseUrl: string;
  timeoutMs: number;
  maxRetries?: number;
  apiToken?: string;
  dataRoot: string;
  memoryReuseThreshold: number;
  rerankThreshold: number;
  legacy: AiProvider;
  reasoner?: TaskStateReasoner;
  reranker?: CandidateReranker;
  grounder?: VisionGrounder;
  memory?: SemanticRetriever;
  recorder?: LearningEventRecorder;
  outcomeVerifier?: OutcomeVerifier;
}

const authorityWeight: Record<MemoryHit['authority'], number> = {
  human_gold: 1,
  verified_real: 0.98,
  synthetic_validated: 0.82,
  legacy: 0.72,
  raw: 0.45,
  rejected: 0,
};

function candidateSnapshot(candidate: UiCandidate) {
  return { id: candidate.id, ...(candidate.label ? { label: candidate.label } : {}), role: candidate.role };
}

function visualTarget(decision: { bounds?: { x: number; y: number; width: number; height: number }; point?: { x: number; y: number }; label: string }): VisualTarget {
  if (decision.bounds) return { ...decision.bounds, label: decision.label };
  const width = 0.04;
  const height = 0.04;
  return {
    x: Math.max(0, Math.min(1 - width, (decision.point?.x ?? 0.5) - width / 2)),
    y: Math.max(0, Math.min(1 - height, (decision.point?.y ?? 0.5) - height / 2)),
    width,
    height,
    label: decision.label,
  };
}

function messageFor(decision: SemanticDecision, candidate?: UiCandidate): string {
  const target = candidate?.label ?? decision.targetConcept;
  if (decision.nextSemanticAction === 'clarify') return `원하시는 작업을 정확히 찾기 위해 ${target}에 대해 조금 더 알려주세요.`;
  if (decision.nextSemanticAction === 'complete') return '요청하신 작업이 완료된 것으로 확인됩니다.';
  return `다음 단계로 '${target}'${candidate?.role === 'Edit' ? '에 입력해 주세요.' : '을(를) 눌러 주세요.'}`;
}

export class BrainV2Provider implements AiProvider {
  private readonly reasoner: TaskStateReasoner;
  private readonly reranker: CandidateReranker;
  private readonly grounder: VisionGrounder;
  private readonly memory: SemanticRetriever;
  private readonly recorder: LearningEventRecorder;
  private readonly outcomeVerifier: OutcomeVerifier;
  private readonly pendingEvents = new Map<string, LearningEventV2>();
  private readonly trajectoryEvents = new Map<string, string[]>();

  constructor(private readonly options: BrainV2ProviderOptions) {
    const local = new LocalModelClient({ baseUrl: options.baseUrl, timeoutMs: options.timeoutMs, maxRetries: options.maxRetries, apiToken: options.apiToken });
    this.reasoner = options.reasoner ?? local;
    this.reranker = options.reranker ?? local;
    this.grounder = options.grounder ?? local;
    this.memory = options.memory ?? new JsonMemoryProvider(resolve(options.dataRoot, 'memory-v2.json'), local);
    this.recorder = options.recorder ?? new JsonlLearningEventRecorder(options.dataRoot);
    this.outcomeVerifier = options.outcomeVerifier ?? new EvidenceOutcomeVerifier();
  }

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    if (this.options.mode === 'shadow') {
      const legacyDecision = await this.options.legacy.decideNextAction(request);
      void this.runV2(request).catch((error) => this.recordFallback(request, error, legacyDecision).catch(() => undefined));
      return legacyDecision;
    }

    try {
      return await this.runV2(request);
    } catch (error) {
      const legacyDecision = await this.options.legacy.decideNextAction(request);
      await this.recordFallback(request, error, legacyDecision).catch(() => undefined);
      return legacyDecision;
    }
  }

  private async recordFallback(request: GuideRequest, error: unknown, rawDecision: unknown): Promise<void> {
    const legacyDecision = rawDecision as Partial<GuideDecision>;
    const candidate = request.candidates.find((item) => item.id === legacyDecision.targetId);
    const event: LearningEventV2 = {
      schemaVersion: 'showwhere-learning-event-v2', eventId: randomUUID(), sessionId: request.session.sessionId,
      timestamp: new Date().toISOString(), source: 'live', authority: 'raw',
      userQuestion: request.session.originalUserMessage,
      stateBefore: `${request.context.applicationName}.${request.context.windowTitle ?? ''}`.slice(0, 240),
      visibleConcepts: request.candidates.map((item) => item.label ?? item.role),
      candidates: request.candidates.map(candidateSnapshot), retrievedMemoryIds: [], retrievalScores: [],
      rerankerScores: [], visionUsed: false,
      selectedCandidate: candidate ? candidateSnapshot(candidate) : undefined,
      selectedBounds: candidate?.bounds, expectedEvidence: [], retryOccurred: true,
      fallbackUsed: 'legacy', failureReason: error instanceof Error ? error.message.slice(0, 1_000) : String(error).slice(0, 1_000),
      finalOutcome: 'pending', totalLatencyMs: 0, dataQualityStatus: 'scrubbed',
    };
    await this.recorder.append(event);
    if (legacyDecision.status === 'in_progress') this.pendingEvents.set(request.session.sessionId, event);
  }

  private async runV2(request: GuideRequest): Promise<GuideDecision> {
    const previous = this.pendingEvents.get(request.session.sessionId);
    if (previous) {
      const outcome = await this.outcomeVerifier.verify(previous, request);
      await this.recorder.updateOutcome(previous.eventId, outcome).catch(() => undefined);
      const verified = { ...previous, ...outcome } as LearningEventV2;
      await this.memory.remember(verified).catch(() => undefined);
      this.pendingEvents.delete(request.session.sessionId);
    }
    const started = performance.now();
    const eventId = randomUUID();
    const query = `${request.session.originalUserMessage}\n${request.session.currentStep ?? ''}`.trim();
    const memories = await this.memory.search(query, request, 8);
    const bestMemory = memories[0];
    let semantic: SemanticDecision;
    let brainModel: string | undefined;
    let brainLatencyMs: number | undefined;
    let fallbackUsed: LearningEventV2['fallbackUsed'] = 'brain';

    if (bestMemory && bestMemory.score * authorityWeight[bestMemory.authority] >= this.options.memoryReuseThreshold) {
      semantic = {
        intent: query.slice(0, 200),
        taskId: bestMemory.taskId,
        stateId: bestMemory.stateId,
        nextSemanticAction: 'select',
        targetConcept: bestMemory.targetConcept,
        expectedNextState: bestMemory.expectedNextState,
        expectedEvidence: [],
        confidence: Math.min(1, bestMemory.score * authorityWeight[bestMemory.authority]),
        needsVision: false,
        reasoningMode: 'off',
      };
      fallbackUsed = 'memory';
    } else {
      const result = await this.reasoner.decide({
        guideRequest: request,
        memories,
        reasoningMode: request.session.failureCount > 0 ? 'on' : 'off',
      });
      semantic = result.decision;
      brainModel = result.model;
      brainLatencyMs = result.latencyMs;
    }

    if (semantic.nextSemanticAction === 'clarify') {
      const decision: GuideDecision = { status: 'needs_clarification', action: 'ask_user', message: messageFor(semantic), confidence: semantic.confidence };
      await this.record(request, eventId, memories, semantic, decision, started, { brainModel, brainLatencyMs, fallbackUsed });
      return decision;
    }
    if (semantic.nextSemanticAction === 'complete') {
      const decision: GuideDecision = { status: 'completed', action: 'explain', message: messageFor(semantic), confidence: semantic.confidence };
      await this.record(request, eventId, memories, semantic, decision, started, { brainModel, brainLatencyMs, fallbackUsed });
      return decision;
    }

    const eligible = request.candidates.filter((candidate) => candidate.visible && candidate.enabled && candidate.clickable);
    let rerankerResult: RerankerResult | undefined;
    if (eligible.length > 0) {
      rerankerResult = await this.reranker.rerank(`${query}\nTarget: ${semantic.targetConcept}`, eligible);
      const candidate = eligible.find((item) => item.id === rerankerResult?.selectedId);
      if (candidate && rerankerResult.confidence >= this.options.rerankThreshold) {
        const decision: GuideDecision = {
          status: 'in_progress', action: 'highlight', targetId: candidate.id,
          message: messageFor(semantic, candidate), expectedChange: semantic.expectedNextState,
          confidence: Math.min(semantic.confidence, rerankerResult.confidence),
        };
        await this.record(request, eventId, memories, semantic, decision, started, { brainModel, brainLatencyMs, fallbackUsed, rerankerResult, candidate });
        return decision;
      }
    }

    if (request.screenshot && (semantic.needsVision || eligible.length === 0 || (rerankerResult?.confidence ?? 0) < this.options.rerankThreshold)) {
      const grounded = await this.grounder.ground(request.screenshot, semantic.targetConcept);
      if (grounded.confidence >= this.options.rerankThreshold) {
        fallbackUsed = 'vision';
        const decision: GuideDecision = {
          status: 'in_progress', action: 'highlight_visual', visualTarget: visualTarget(grounded),
          message: messageFor(semantic), expectedChange: semantic.expectedNextState,
          confidence: Math.min(semantic.confidence, grounded.confidence),
        };
        await this.record(request, eventId, memories, semantic, decision, started, { brainModel, brainLatencyMs, fallbackUsed, rerankerResult, vision: grounded });
        return decision;
      }
    }

    throw new Error('Brain v2 could not identify a sufficiently reliable target.');
  }

  private async record(
    request: GuideRequest,
    eventId: string,
    memories: MemoryHit[],
    semantic: SemanticDecision,
    decision: GuideDecision,
    started: number,
    details: {
      brainModel?: string; brainLatencyMs?: number; fallbackUsed: LearningEventV2['fallbackUsed'];
      rerankerResult?: Awaited<ReturnType<CandidateReranker['rerank']>>; candidate?: UiCandidate;
      vision?: Awaited<ReturnType<VisionGrounder['ground']>>;
    },
  ): Promise<void> {
    const event: LearningEventV2 = {
      schemaVersion: 'showwhere-learning-event-v2', eventId, sessionId: request.session.sessionId,
      timestamp: new Date().toISOString(), source: 'live', authority: 'raw',
      userQuestion: request.session.originalUserMessage, normalizedIntent: semantic.intent,
      taskId: semantic.taskId, stateBefore: semantic.stateId,
      visibleConcepts: request.candidates.map((candidate) => candidate.label ?? candidate.role).slice(0, 300),
      candidates: request.candidates.map(candidateSnapshot),
      retrievedMemoryIds: memories.map((memory) => memory.id), retrievalScores: memories.map((memory) => memory.score),
      brainModel: details.brainModel, brainOutput: semantic, brainConfidence: semantic.confidence,
      brainLatencyMs: details.brainLatencyMs, rerankerModel: details.rerankerResult?.model,
      rerankerScores: details.rerankerResult?.scores ?? [], rerankerSelectedId: details.rerankerResult?.selectedId,
      rerankerLatencyMs: details.rerankerResult?.latencyMs,
      visionModel: details.vision?.model, visionUsed: details.vision !== undefined, visionLatencyMs: details.vision?.latencyMs,
      selectedSemanticTarget: semantic.targetConcept,
      selectedCandidate: details.candidate ? candidateSnapshot(details.candidate) : undefined,
      selectedBounds: details.candidate?.bounds,
      expectedNextState: semantic.expectedNextState, expectedEvidence: semantic.expectedEvidence,
      retryOccurred: request.session.failureCount > 0, fallbackUsed: details.fallbackUsed,
      finalOutcome: 'pending', totalLatencyMs: Math.round(performance.now() - started), dataQualityStatus: 'scrubbed',
    };
    await this.recorder.append(event).catch(() => undefined);
    const eventIds = [...(this.trajectoryEvents.get(request.session.sessionId) ?? []), event.eventId];
    this.trajectoryEvents.set(request.session.sessionId, eventIds);
    if (decision.status === 'in_progress') this.pendingEvents.set(request.session.sessionId, event);
    if (decision.status === 'completed' || decision.status === 'blocked') {
      await this.recorder.appendTrajectory({
        schemaVersion: 'showwhere-trajectory-v2', trajectoryId: randomUUID(),
        sessionId: request.session.sessionId, goal: request.session.goal ?? request.session.originalUserMessage,
        source: 'live', authority: 'raw', eventIds,
        startedAt: event.timestamp, completedAt: new Date().toISOString(),
        finalOutcome: decision.status === 'completed' ? 'pending' : 'failure', taskCompleted: false,
      }).catch(() => undefined);
      this.trajectoryEvents.delete(request.session.sessionId);
    }
  }
}
