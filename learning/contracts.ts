import { z } from 'zod';

export const RiskLevelSchema = z.enum(['low', 'medium', 'high']);

export const SeedExampleSchema = z.object({
  id: z.string().regex(/^[a-z0-9_]+$/u),
  version: z.string().min(1),
  category: z.string().min(1),
  goal: z.string().min(1),
  exampleUserMessages: z.array(z.string().min(1)).min(2),
  initialContext: z.object({
    platform: z.enum(['windows', 'web']),
    application: z.string().min(1),
    screenState: z.string().min(1),
  }).strict(),
  decisionRules: z.array(z.string().min(1)).min(2),
  possibleCandidates: z.array(z.string().min(1)).min(1),
  correctNextAction: z.object({
    target: z.string().min(1).nullable(),
    instruction: z.string().min(1),
    mode: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  }).strict(),
  expectedChange: z.string().min(1),
  successCondition: z.string().min(1),
  riskLevel: RiskLevelSchema,
  requiresConfirmation: z.boolean(),
  tags: z.array(z.string().min(1)).default([]),
}).strict().superRefine((seed, context) => {
  if (seed.correctNextAction.mode === 'highlight'
      && !seed.possibleCandidates.includes(seed.correctNextAction.target ?? '')) {
    context.addIssue({
      code: 'custom',
      path: ['correctNextAction', 'target'],
      message: 'A highlighted target must exist in possibleCandidates.',
    });
  }
  if (seed.riskLevel === 'high' && !seed.requiresConfirmation) {
    context.addIssue({
      code: 'custom',
      path: ['requiresConfirmation'],
      message: 'High-risk examples must require explicit confirmation.',
    });
  }
});

export const CandidateSchema = z.object({
  id: z.string().min(1),
  label: z.string().min(1),
  role: z.string().min(1),
  enabled: z.boolean(),
  visible: z.boolean(),
  description: z.string().optional(),
}).strict();

export const ScenarioSchema = z.object({
  schemaVersion: z.literal('showwhere-scenario-v1'),
  scenarioId: z.string().min(1),
  seedId: z.string().min(1),
  goal: z.string().min(1),
  userMessage: z.string().min(1),
  context: z.object({
    platform: z.string().min(1),
    application: z.string().min(1),
    screenState: z.string().min(1),
    locale: z.string().min(1),
    previousSteps: z.array(z.string()),
  }).strict(),
  candidates: z.array(CandidateSchema).min(1),
  proposedAction: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  proposedCorrectTargetId: z.string().min(1).nullable(),
  instruction: z.string().min(1),
  expectedChange: z.string().min(1),
  successCondition: z.string().min(1),
  difficulty: z.enum(['easy', 'medium', 'hard']),
  ambiguity: z.number().min(0).max(1),
  riskLevel: RiskLevelSchema,
  requiresConfirmation: z.boolean(),
  generatorModel: z.string().min(1),
  generationTimestamp: z.string().datetime(),
  provenance: z.enum(['synthetic', 'human']),
}).strict().superRefine((scenario, context) => {
  const candidateIds = new Set(scenario.candidates.map((candidate) => candidate.id));
  if (scenario.proposedAction === 'highlight'
      && !candidateIds.has(scenario.proposedCorrectTargetId ?? '')) {
    context.addIssue({ code: 'custom', path: ['proposedCorrectTargetId'], message: 'Target does not exist.' });
  }
  if (scenario.proposedAction !== 'highlight' && scenario.proposedCorrectTargetId !== null) {
    context.addIssue({ code: 'custom', path: ['proposedCorrectTargetId'], message: 'Non-highlight actions cannot have a target.' });
  }
});

export const JudgeResultSchema = z.object({
  selectedTargetId: z.string().min(1).nullable(),
  action: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  verdict: z.enum(['valid', 'invalid', 'ambiguous']),
  confidence: z.number().min(0).max(1),
  shortReason: z.string().min(1).max(500),
  safetyConcern: z.boolean(),
}).strict();

export const JudgedScenarioSchema = z.object({
  scenario: ScenarioSchema,
  judgments: z.array(z.object({
    model: z.string().min(1),
    latencyMs: z.number().nonnegative(),
    result: JudgeResultSchema,
  }).strict()).min(1),
  disposition: z.enum(['auto_accept', 'human_review', 'reject']),
  dispositionReason: z.string().min(1),
  evaluatedAt: z.string().datetime(),
}).strict();

export const BenchmarkCaseSchema = z.object({
  id: z.string().min(1),
  seedId: z.string().min(1),
  category: z.string().min(1),
  goal: z.string().min(1),
  userMessage: z.string().min(1),
  context: z.object({ application: z.string(), screenState: z.string() }).strict(),
  candidates: z.array(CandidateSchema).min(1),
  expectedAction: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  expectedTargetId: z.string().nullable(),
  riskLevel: RiskLevelSchema,
  requiresConfirmation: z.boolean(),
}).strict();

export const RealSessionFeedbackSchema = z.object({
  schemaVersion: z.literal('showwhere-session-feedback-v1'),
  id: z.string().min(1),
  normalizedGoal: z.string().min(1),
  platform: z.string().min(1),
  applicationType: z.string().min(1),
  candidateRoles: z.array(z.string()),
  selectedTargetRole: z.string().nullable(),
  userClickedSelectedTarget: z.boolean().nullable(),
  expectedChangeOccurred: z.boolean().nullable(),
  retryRequested: z.boolean(),
  guidanceMarkedWrong: z.boolean(),
  taskCompleted: z.boolean().nullable(),
  stepCount: z.number().int().nonnegative(),
  model: z.string().min(1),
  confidence: z.number().min(0).max(1),
  latencyMs: z.number().nonnegative(),
  explicitCollectionConsent: z.literal(true),
}).strict();

export const HumanReviewDecisionSchema = z.object({
  scenarioId: z.string().min(1),
  action: z.enum(['accept', 'reject', 'correct', 'mark_ambiguous']),
  correctedTargetId: z.string().min(1).nullable(),
  rewrittenInstruction: z.string().min(1).nullable(),
  seedRuleToAdd: z.string().min(1).nullable(),
  reviewerComment: z.string().max(2_000),
  reviewedAt: z.string().datetime(),
}).strict();

export type SeedExample = z.infer<typeof SeedExampleSchema>;
export type Scenario = z.infer<typeof ScenarioSchema>;
export type JudgeResult = z.infer<typeof JudgeResultSchema>;
export type JudgedScenario = z.infer<typeof JudgedScenarioSchema>;
export type BenchmarkCase = z.infer<typeof BenchmarkCaseSchema>;
export type HumanReviewDecision = z.infer<typeof HumanReviewDecisionSchema>;
