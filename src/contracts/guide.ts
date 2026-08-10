import { z } from 'zod';

const attributeValueSchema = z.union([
  z.string(),
  z.number().finite(),
  z.boolean(),
  z.null(),
]);

export const BoundsSchema = z.object({
  x: z.number().finite(),
  y: z.number().finite(),
  width: z.number().finite().nonnegative(),
  height: z.number().finite().nonnegative(),
}).strict();

export const UiCandidateSchema = z.object({
  id: z.string().trim().min(1).max(160),
  label: z.string().trim().min(1).max(500).optional(),
  description: z.string().trim().min(1).max(1_000).optional(),
  role: z.string().trim().min(1).max(100),
  enabled: z.boolean(),
  visible: z.boolean(),
  clickable: z.boolean(),
  bounds: BoundsSchema,
  attributes: z.record(z.string(), attributeValueSchema).optional(),
}).strict();

export const ApplicationContextSchema = z.object({
  platform: z.literal('windows'),
  applicationName: z.string().trim().min(1).max(300),
  windowTitle: z.string().trim().max(500).optional(),
  url: z.string().trim().max(2_048).optional(),
  locale: z.string().trim().max(100).optional(),
}).strict();

export const TaskSessionSchema = z.object({
  sessionId: z.string().trim().min(1).max(160),
  originalUserMessage: z.string().trim().min(1).max(4_000),
  goal: z.string().trim().min(1).max(2_000).optional(),
  mode: z.enum(['guidance', 'troubleshooting', 'unknown']),
  status: z.enum([
    'idle',
    'observing',
    'waiting_for_ai',
    'guiding',
    'waiting_for_user',
    'completed',
    'blocked',
    'cancelled',
  ]),
  currentStep: z.string().trim().min(1).max(1_000).optional(),
  completedSteps: z.array(z.string().trim().min(1).max(1_000)).max(100),
  knownFacts: z.array(z.string().trim().min(1).max(1_000)).max(100),
  expectedChange: z.string().trim().min(1).max(1_000).optional(),
  failureCount: z.number().int().nonnegative().max(100),
}).strict();

export const GuideRequestSchema = z.object({
  session: TaskSessionSchema,
  context: ApplicationContextSchema,
  candidates: z.array(UiCandidateSchema).max(100),
  screenshot: z.string().max(8_000_000).optional(),
  screenshotBounds: BoundsSchema.optional(),
}).strict().superRefine((request, context) => {
  if ((request.screenshot === undefined) !== (request.screenshotBounds === undefined)) {
    context.addIssue({
      code: 'custom',
      path: ['screenshotBounds'],
      message: 'screenshot and screenshotBounds must be supplied together.',
    });
  }
});

export const VisualTargetSchema = z.object({
  x: z.number().finite().min(0).max(1),
  y: z.number().finite().min(0).max(1),
  width: z.number().finite().min(0.005).max(1),
  height: z.number().finite().min(0.005).max(1),
  label: z.string().trim().min(1).max(160),
}).strict().superRefine((target, context) => {
  if (target.x + target.width > 1 || target.y + target.height > 1) {
    context.addIssue({
      code: 'custom',
      message: 'visualTarget must stay inside the normalized screenshot.',
    });
  }
});

export const GuideDecisionSchema = z.object({
  status: z.enum(['in_progress', 'completed', 'needs_clarification', 'blocked']),
  action: z.enum([
    'highlight',
    'highlight_visual',
    'ask_user',
    'explain',
    'request_new_observation',
    'request_vision',
    'request_safe_tool',
  ]),
  targetId: z.string().trim().min(1).max(160).optional(),
  message: z.string().trim().min(1).max(500),
  expectedChange: z.string().trim().min(1).max(1_000).optional(),
  confidence: z.number().finite().min(0).max(1),
  safeToolId: z.string().trim().min(1).max(160).optional(),
  alternativeTargetIds: z.array(z.string().trim().min(1).max(160)).min(2).max(4).optional(),
  visualTarget: VisualTargetSchema.optional(),
}).strict().superRefine((decision, context) => {
  if (decision.action === 'highlight' && !decision.targetId) {
    context.addIssue({
      code: 'custom',
      path: ['targetId'],
      message: 'targetId is required when action is highlight.',
    });
  }
  if (decision.action === 'highlight_visual' && !decision.visualTarget) {
    context.addIssue({
      code: 'custom',
      path: ['visualTarget'],
      message: 'visualTarget is required when action is highlight_visual.',
    });
  }
  if (decision.action === 'request_safe_tool' && !decision.safeToolId) {
    context.addIssue({
      code: 'custom',
      path: ['safeToolId'],
      message: 'safeToolId is required when action requests a safe tool.',
    });
  }
  if (decision.alternativeTargetIds && decision.action !== 'ask_user') {
    context.addIssue({
      code: 'custom',
      path: ['alternativeTargetIds'],
      message: 'alternativeTargetIds are allowed only when action is ask_user.',
    });
  }
  if (decision.alternativeTargetIds
      && new Set(decision.alternativeTargetIds).size !== decision.alternativeTargetIds.length) {
    context.addIssue({
      code: 'custom',
      path: ['alternativeTargetIds'],
      message: 'alternativeTargetIds must be unique.',
    });
  }
});

export type Bounds = z.infer<typeof BoundsSchema>;
export type UiCandidate = z.infer<typeof UiCandidateSchema>;
export type ApplicationContext = z.infer<typeof ApplicationContextSchema>;
export type TaskSession = z.infer<typeof TaskSessionSchema>;
export type GuideRequest = z.infer<typeof GuideRequestSchema>;
export type GuideDecision = z.infer<typeof GuideDecisionSchema>;
export type VisualTarget = z.infer<typeof VisualTargetSchema>;

export const DEFAULT_GUIDE_CONFIDENCE_THRESHOLD = 0.65;

export function targetExistsInRequest(decision: GuideDecision, request: GuideRequest): boolean {
  const candidateIds = new Set(request.candidates.map((candidate) => candidate.id));
  if (decision.action === 'highlight' && !candidateIds.has(decision.targetId ?? '')) return false;
  return decision.alternativeTargetIds?.every((id) => candidateIds.has(id)) ?? true;
}
