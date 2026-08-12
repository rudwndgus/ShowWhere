import { z } from 'zod';
import { BoundsSchema, GuideRequestSchema } from '../contracts';

export const BrainModeSchema = z.enum(['legacy', 'v2', 'shadow']);
export const DataAuthoritySchema = z.enum([
  'human_gold',
  'verified_real',
  'synthetic_validated',
  'raw',
  'rejected',
  'legacy',
]);

export const SemanticDecisionSchema = z.object({
  intent: z.string().trim().min(1).max(200),
  taskId: z.string().trim().min(1).max(240),
  stateId: z.string().trim().min(1).max(240),
  nextSemanticAction: z.enum(['navigate', 'select', 'search', 'input', 'confirm', 'explain', 'clarify', 'complete']),
  targetConcept: z.string().trim().min(1).max(240),
  expectedNextState: z.string().trim().min(1).max(240),
  expectedEvidence: z.array(z.string().trim().min(1).max(300)).max(20).default([]),
  confidence: z.number().finite().min(0).max(1),
  needsVision: z.boolean(),
  reasoningMode: z.enum(['off', 'on']).default('off'),
}).strict();

export const MemoryHitSchema = z.object({
  id: z.string().trim().min(1).max(240),
  score: z.number().finite().min(-1).max(1),
  authority: DataAuthoritySchema,
  taskId: z.string().trim().min(1).max(240),
  stateId: z.string().trim().min(1).max(240),
  targetConcept: z.string().trim().min(1).max(240),
  expectedNextState: z.string().trim().min(1).max(240),
  text: z.string().trim().min(1).max(4_000),
}).strict();

export const RerankerScoreSchema = z.object({
  candidateId: z.string().trim().min(1).max(160),
  score: z.number().finite().min(-100).max(100),
}).strict();

export const RerankerResultSchema = z.object({
  scores: z.array(RerankerScoreSchema).max(200),
  selectedId: z.string().trim().min(1).max(160).optional(),
  confidence: z.number().finite().min(0).max(1),
  margin: z.number().finite().min(0).max(200),
  latencyMs: z.number().int().nonnegative(),
  model: z.string().trim().min(1),
}).strict();

export const GroundingResultSchema = z.object({
  point: z.object({ x: z.number().min(0).max(1), y: z.number().min(0).max(1) }).strict().optional(),
  bounds: z.object({
    x: z.number().min(0).max(1),
    y: z.number().min(0).max(1),
    width: z.number().positive().max(1),
    height: z.number().positive().max(1),
  }).strict().optional(),
  label: z.string().trim().min(1).max(160),
  confidence: z.number().min(0).max(1),
  latencyMs: z.number().int().nonnegative(),
  model: z.string().trim().min(1),
}).strict().refine((value) => value.point !== undefined || value.bounds !== undefined, {
  message: 'A grounding result needs a point or bounds.',
});

const CandidateSnapshotSchema = z.object({
  id: z.string(),
  label: z.string().optional(),
  role: z.string(),
}).strict();

export const LearningEventV2Schema = z.object({
  schemaVersion: z.literal('showwhere-learning-event-v2'),
  eventId: z.string().uuid(),
  sessionId: z.string().min(1),
  timestamp: z.string().datetime({ offset: true }),
  source: z.enum(['live', 'manual-test', 'benchmark', 'developer-teaching', 'synthetic']),
  authority: DataAuthoritySchema,
  userQuestion: z.string().max(4_000),
  normalizedIntent: z.string().max(300).optional(),
  taskId: z.string().max(240).optional(),
  stateBefore: z.string().max(240).optional(),
  visibleConcepts: z.array(z.string()).max(300),
  candidates: z.array(CandidateSnapshotSchema).max(200),
  retrievedMemoryIds: z.array(z.string()).max(50),
  retrievalScores: z.array(z.number().finite()).max(50),
  brainModel: z.string().optional(),
  brainOutput: SemanticDecisionSchema.optional(),
  brainConfidence: z.number().min(0).max(1).optional(),
  brainLatencyMs: z.number().int().nonnegative().optional(),
  rerankerModel: z.string().optional(),
  rerankerScores: z.array(RerankerScoreSchema).max(200),
  rerankerSelectedId: z.string().optional(),
  rerankerLatencyMs: z.number().int().nonnegative().optional(),
  visionModel: z.string().optional(),
  visionUsed: z.boolean(),
  visionLatencyMs: z.number().int().nonnegative().optional(),
  selectedSemanticTarget: z.string().optional(),
  selectedCandidate: CandidateSnapshotSchema.optional(),
  selectedBounds: BoundsSchema.optional(),
  expectedNextState: z.string().optional(),
  expectedEvidence: z.array(z.string()).max(20),
  userClickedSuggestedTarget: z.boolean().optional(),
  observedNextState: z.string().optional(),
  transitionMatched: z.boolean().optional(),
  taskContinued: z.boolean().optional(),
  taskCompleted: z.boolean().optional(),
  retryOccurred: z.boolean(),
  fallbackUsed: z.enum(['none', 'memory', 'legacy', 'brain', 'vision']).default('none'),
  developerCorrection: z.string().max(4_000).optional(),
  finalOutcome: z.enum(['pending', 'success', 'failure', 'corrected', 'abandoned']),
  totalLatencyMs: z.number().int().nonnegative(),
  dataQualityStatus: z.enum(['raw', 'scrubbed', 'verified', 'rejected']),
}).strict();

export const TrajectoryV2Schema = z.object({
  schemaVersion: z.literal('showwhere-trajectory-v2'),
  trajectoryId: z.string().uuid(),
  sessionId: z.string().min(1),
  goal: z.string().max(4_000),
  source: LearningEventV2Schema.shape.source,
  authority: DataAuthoritySchema,
  eventIds: z.array(z.string().uuid()).min(1),
  startedAt: z.string().datetime({ offset: true }),
  completedAt: z.string().datetime({ offset: true }).optional(),
  finalOutcome: LearningEventV2Schema.shape.finalOutcome,
  taskCompleted: z.boolean(),
}).strict();

export const BrainV2RequestSchema = z.object({
  guideRequest: GuideRequestSchema,
  memories: z.array(MemoryHitSchema).max(20),
  reasoningMode: z.enum(['off', 'on']),
}).strict();

export type BrainMode = z.infer<typeof BrainModeSchema>;
export type SemanticDecision = z.infer<typeof SemanticDecisionSchema>;
export type MemoryHit = z.infer<typeof MemoryHitSchema>;
export type RerankerResult = z.infer<typeof RerankerResultSchema>;
export type GroundingResult = z.infer<typeof GroundingResultSchema>;
export type LearningEventV2 = z.infer<typeof LearningEventV2Schema>;
export type TrajectoryV2 = z.infer<typeof TrajectoryV2Schema>;
export type BrainV2Request = z.infer<typeof BrainV2RequestSchema>;
