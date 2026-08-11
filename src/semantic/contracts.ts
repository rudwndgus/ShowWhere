import { z } from 'zod';

const semanticId = z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u);
const shortText = z.string().trim().min(1).max(1_000);

export const SemanticConceptRefSchema = z.object({
  conceptId: semanticId,
  role: z.string().trim().min(1).max(100),
  observedLabels: z.array(z.string().trim().min(1).max(300)).max(20),
  enabled: z.boolean(),
}).strict();

export const GuidanceTeachingRecordV2Schema = z.object({
  schemaVersion: z.literal('showwhere-guidance-step-v2'),
  id: semanticId,
  source: z.object({
    userQuestion: shortText,
    currentStepDescription: shortText,
    developerCorrection: shortText,
  }).strict(),
  intent: z.object({
    domain: semanticId,
    action: semanticId,
    object: semanticId,
    language: z.string().trim().min(2).max(35),
    keyPhrases: z.array(z.string().trim().min(1).max(120)).min(1).max(30),
  }).strict(),
  task: z.object({
    taskId: semanticId,
    desiredOutcome: shortText,
  }).strict(),
  state: z.object({
    stateId: semanticId,
    completedStepIds: z.array(semanticId).max(100),
    visibleConcepts: z.array(SemanticConceptRefSchema).max(100),
  }).strict(),
  decision: z.object({
    action: z.enum(['highlight', 'clarify', 'complete', 'explain']),
    target: z.object({
      conceptId: semanticId,
      preferredRoles: z.array(z.string().trim().min(1).max(100)).min(1).max(20),
      confusableNegativeConceptIds: z.array(semanticId).max(30).default([]),
    }).strict().nullable(),
    instruction: shortText,
  }).strict(),
  expectedTransition: z.object({
    nextStateId: semanticId,
    evidenceConcepts: z.array(semanticId).max(30),
  }).strict(),
  successCondition: z.object({
    type: z.enum(['state_reached', 'concept_visible', 'user_confirmed', 'task_complete']),
    stateId: semanticId,
  }).strict(),
  safety: z.object({
    riskLevel: z.enum(['low', 'medium', 'high']),
    confirmationRequired: z.boolean(),
  }).strict(),
  verification: z.object({
    status: z.enum(['draft', 'ai_validated', 'human_verified']),
    sourceType: z.enum(['developer_correction', 'generated', 'migrated', 'real_session']),
    confidence: z.number().finite().min(0).max(1).optional(),
    approvedBy: z.string().trim().min(1).max(160).optional(),
    approvedAtUtc: z.string().datetime().optional(),
    supersedesId: semanticId.optional(),
  }).strict(),
}).strict().superRefine((record, context) => {
  if (record.decision.action === 'highlight' && record.decision.target === null) {
    context.addIssue({ code: 'custom', path: ['decision', 'target'], message: 'Highlight requires a semantic target.' });
  }
  if (record.decision.action !== 'highlight' && record.decision.target !== null) {
    context.addIssue({ code: 'custom', path: ['decision', 'target'], message: 'Only highlight actions may contain a target.' });
  }
  if (record.safety.riskLevel === 'high' && !record.safety.confirmationRequired) {
    context.addIssue({ code: 'custom', path: ['safety', 'confirmationRequired'], message: 'High-risk guidance requires confirmation.' });
  }
  if (record.verification.status === 'human_verified'
      && (!record.verification.approvedBy || !record.verification.approvedAtUtc)) {
    context.addIssue({ code: 'custom', path: ['verification'], message: 'Human-approved Gold requires reviewer and timestamp.' });
  }
});

export const ConceptDictionaryEntrySchema = z.object({
  schemaVersion: z.literal('showwhere-concept-v1'),
  conceptId: semanticId,
  aliases: z.array(z.string().trim().min(1).max(300)).min(1).max(100),
  preferredRoles: z.array(z.string().trim().min(1).max(100)).min(1).max(30),
  relatedConcepts: z.array(semanticId).max(50).default([]),
  description: z.string().trim().min(1).max(1_000),
  platformHints: z.object({
    processNames: z.array(z.string().trim().min(1).max(160)).max(30).default([]),
    automationIds: z.array(z.string().trim().min(1).max(300)).max(30).default([]),
    sourceScopes: z.array(z.string().trim().min(1).max(100)).max(20).default([]),
  }).strict().optional(),
}).strict();

export const TaskTransitionSchema = z.object({
  transitionId: semanticId,
  action: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  targetConceptId: semanticId.nullable(),
  nextStateId: semanticId,
  evidenceConceptIds: z.array(semanticId).max(30),
  instruction: shortText,
  riskLevel: z.enum(['low', 'medium', 'high']),
  confirmationRequired: z.boolean(),
}).strict().superRefine((transition, context) => {
  if (transition.action === 'highlight' && transition.targetConceptId === null) {
    context.addIssue({ code: 'custom', path: ['targetConceptId'], message: 'Highlight transition requires a semantic target.' });
  }
  if (transition.riskLevel === 'high' && !transition.confirmationRequired) {
    context.addIssue({ code: 'custom', path: ['confirmationRequired'], message: 'High-risk transition requires confirmation.' });
  }
});

export const TaskGraphSchema = z.object({
  schemaVersion: z.literal('showwhere-task-graph-v1'),
  taskId: semanticId,
  intent: z.object({ domain: semanticId, action: semanticId, object: semanticId }).strict(),
  desiredOutcome: shortText,
  initialStateIds: z.array(semanticId).min(1),
  terminalStateIds: z.array(semanticId).min(1),
  states: z.array(z.object({
    stateId: semanticId,
    evidenceConceptIds: z.array(semanticId).max(30),
    transitions: z.array(TaskTransitionSchema),
  }).strict()).min(1),
}).strict().superRefine((graph, context) => {
  const stateIds = new Set(graph.states.map((state) => state.stateId));
  for (const stateId of [...graph.initialStateIds, ...graph.terminalStateIds]) {
    if (!stateIds.has(stateId)) context.addIssue({ code: 'custom', message: `Unknown graph state: ${stateId}` });
  }
  for (const state of graph.states) {
    for (const transition of state.transitions) {
      if (!stateIds.has(transition.nextStateId)) {
        context.addIssue({ code: 'custom', path: ['states'], message: `Transition points to unknown state: ${transition.nextStateId}` });
      }
    }
  }
});

export const TeachingInputSchema = z.object({
  userQuestion: shortText,
  currentStepDescription: shortText,
  developerCorrection: shortText,
  context: z.object({
    platform: z.string().trim().min(1),
    applicationName: z.string().trim().min(1),
    windowTitle: z.string().trim().nullable().optional(),
    url: z.string().trim().nullable().optional(),
    locale: z.string().trim().nullable().optional(),
  }).strict().nullable().optional(),
  candidates: z.array(z.object({
    label: z.string().trim().min(1),
    role: z.string().trim().min(1),
    enabled: z.boolean(),
  }).strict()).max(100).default([]),
}).strict();

export const TeachingValidationResultSchema = z.object({
  valid: z.boolean(),
  confidence: z.number().finite().min(0).max(1),
  issues: z.array(z.object({
    code: semanticId,
    path: z.string(),
    message: z.string().min(1).max(500),
    severity: z.enum(['error', 'warning']),
  }).strict()),
  shortReason: z.string().min(1).max(500),
}).strict();

export const SemanticKnowledgeContextSchema = z.object({
  likelyTask: TaskGraphSchema.nullable(),
  relevantConcepts: z.array(ConceptDictionaryEntrySchema).max(12),
  relatedGoldSteps: z.array(GuidanceTeachingRecordV2Schema).max(5),
}).strict();

export type GuidanceTeachingRecordV2 = z.infer<typeof GuidanceTeachingRecordV2Schema>;
export type ConceptDictionaryEntry = z.infer<typeof ConceptDictionaryEntrySchema>;
export type TaskGraph = z.infer<typeof TaskGraphSchema>;
export type TeachingInput = z.infer<typeof TeachingInputSchema>;
export type TeachingValidationResult = z.infer<typeof TeachingValidationResultSchema>;
export type SemanticKnowledgeContext = z.infer<typeof SemanticKnowledgeContextSchema>;
