import type { GuideDecision, GuideRequest, UiCandidate } from '../contracts';
import type {
  BrainV2Request,
  GroundingResult,
  LearningEventV2,
  MemoryHit,
  RerankerResult,
  SemanticDecision,
  TrajectoryV2,
} from './contracts';

export interface GuideBrain {
  decideNextAction(request: GuideRequest): Promise<GuideDecision>;
}

export interface SemanticRetriever {
  search(query: string, request: GuideRequest, topK: number): Promise<MemoryHit[]>;
  remember(event: LearningEventV2): Promise<void>;
}

// Stable alias used by future embedded/Qdrant implementations.
export interface MemoryProvider extends SemanticRetriever {
  readonly providerKind?: string;
}

export interface TaskStateReasoner {
  decide(request: BrainV2Request): Promise<{ decision: SemanticDecision; latencyMs: number; model: string }>;
}

export interface CandidateReranker {
  rerank(query: string, candidates: readonly UiCandidate[]): Promise<RerankerResult>;
}

export interface VisionGrounder {
  ground(screenshot: string, targetConcept: string): Promise<GroundingResult>;
}

export interface OutcomeVerifier {
  verify(event: LearningEventV2, nextRequest: GuideRequest): Promise<Partial<LearningEventV2>>;
}

export interface LearningEventRecorder {
  append(event: LearningEventV2): Promise<void>;
  appendTrajectory(trajectory: TrajectoryV2): Promise<void>;
  updateOutcome(eventId: string, patch: Partial<LearningEventV2>): Promise<void>;
}
