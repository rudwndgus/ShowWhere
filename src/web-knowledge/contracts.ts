import { z } from 'zod';

export const WebLocatorHintSchema = z.object({
  role: z.string().min(1).optional(),
  name: z.string().min(1).optional(),
  label: z.string().min(1).optional(),
  placeholder: z.string().min(1).optional(),
  testId: z.string().min(1).optional(),
  href: z.string().min(1).optional(),
  css: z.string().min(1).optional(),
  ordinal: z.number().int().nonnegative().optional(),
}).strict();

export const RawWebElementSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
  normalizedName: z.string(),
  role: z.string().min(1),
  tag: z.string().min(1),
  area: z.string().min(1),
  container: z.string().optional(),
  region: z.enum(['top', 'bottom', 'left', 'right', 'center']),
  href: z.string().optional(),
  inputType: z.string().optional(),
  disabled: z.boolean(),
  locator: WebLocatorHintSchema,
  risk: z.enum(['safe', 'blocked', 'unknown']),
}).strict();

export const CrawlPathStepSchema = z.object({
  elementId: z.string().min(1),
  name: z.string().min(1),
  role: z.string().min(1),
  locator: WebLocatorHintSchema,
}).strict();

export const RawWebStateSchema = z.object({
  id: z.string().min(1),
  url: z.string().min(1),
  title: z.string(),
  depth: z.number().int().nonnegative(),
  path: z.array(CrawlPathStepSchema),
  evidence: z.array(z.string()),
  elements: z.array(RawWebElementSchema),
  observedAt: z.string().datetime(),
}).strict();

export const RawWebTransitionSchema = z.object({
  id: z.string().min(1),
  fromStateId: z.string().min(1),
  toStateId: z.string().min(1).optional(),
  action: CrawlPathStepSchema,
  outcome: z.enum(['navigation', 'state_change', 'popup', 'no_change', 'blocked', 'failed']),
  addedEvidence: z.array(z.string()),
  removedEvidence: z.array(z.string()),
  errorCode: z.string().optional(),
  observedAt: z.string().datetime(),
}).strict();

export const CrawlFailureSchema = z.object({
  stage: z.string().min(1),
  path: z.array(z.string()),
  code: z.string().min(1),
  message: z.string().min(1),
}).strict();

export const RawWebCrawlRunSchema = z.object({
  schemaVersion: z.literal(1),
  runId: z.string().min(1),
  siteId: z.string().min(1),
  domains: z.array(z.string().min(1)).min(1),
  seedUrl: z.string().url(),
  startedAt: z.string().datetime(),
  completedAt: z.string().datetime(),
  crawlerVersion: z.string().min(1),
  locale: z.string().min(1),
  robots: z.object({ respected: z.boolean(), source: z.string().optional() }).strict(),
  limits: z.object({
    maxStates: z.number().int().positive(),
    maxDepth: z.number().int().nonnegative(),
    maxActionsPerState: z.number().int().positive(),
  }).strict(),
  states: z.array(RawWebStateSchema),
  transitions: z.array(RawWebTransitionSchema),
  failures: z.array(CrawlFailureSchema),
}).strict();

export const NormalizedWebElementSchema = z.object({
  id: z.string().min(1),
  names: z.array(z.string().min(1)).min(1),
  normalizedNames: z.array(z.string()),
  role: z.string().min(1),
  areas: z.array(z.string().min(1)),
  regions: z.array(z.enum(['top', 'bottom', 'left', 'right', 'center'])),
  semanticLabel: z.string().min(1),
  semanticConfidence: z.number().min(0).max(1),
  semanticSource: z.enum(['rule', 'huggingface', 'generated']),
  locatorHints: z.array(WebLocatorHintSchema),
  risk: z.enum(['safe', 'blocked', 'unknown']),
}).strict();

export const WebKnowledgeStateSchema = z.object({
  id: z.string().min(1),
  urlPatterns: z.array(z.string()),
  titlePatterns: z.array(z.string()),
  evidence: z.array(z.string()),
  elements: z.array(NormalizedWebElementSchema),
}).strict();

export const WebKnowledgeTransitionSchema = z.object({
  id: z.string().min(1),
  fromStateId: z.string().min(1),
  toStateId: z.string().min(1).optional(),
  actionElementId: z.string().min(1),
  actionSemanticLabel: z.string().min(1),
  actionNames: z.array(z.string().min(1)).min(1),
  actionRoles: z.array(z.string().min(1)).min(1),
  outcome: z.enum(['navigation', 'state_change', 'popup', 'no_change', 'blocked', 'failed']),
  observedCount: z.number().int().positive(),
  successRate: z.number().min(0).max(1),
  addedEvidence: z.array(z.string()),
  removedEvidence: z.array(z.string()),
}).strict();

export const WebKnowledgeRouteStepSchema = z.object({
  fromStateId: z.string().min(1),
  toStateId: z.string().min(1).optional(),
  semanticLabel: z.string().min(1),
  names: z.array(z.string().min(1)).min(1),
  roles: z.array(z.string().min(1)).min(1),
}).strict();

export const WebKnowledgeRouteSchema = z.object({
  id: z.string().min(1),
  intentLabel: z.string().min(1),
  aliases: z.array(z.string().min(1)),
  steps: z.array(WebKnowledgeRouteStepSchema).min(1),
  successRate: z.number().min(0).max(1),
}).strict();

export const WebKnowledgeCatalogSchema = z.object({
  schemaVersion: z.literal(1),
  siteId: z.string().min(1),
  displayName: z.string().min(1),
  domains: z.array(z.string().min(1)).min(1),
  generatedAt: z.string().datetime(),
  sourceRunIds: z.array(z.string().min(1)),
  semanticModel: z.object({
    provider: z.enum(['huggingface', 'rules']),
    model: z.string().min(1),
    comparedModels: z.array(z.object({
      model: z.string().min(1),
      compatible: z.boolean(),
      qualityScore: z.number().min(0).max(1),
      latencyMs: z.number().nonnegative().optional(),
      error: z.string().optional(),
    }).strict()),
  }).strict(),
  states: z.array(WebKnowledgeStateSchema),
  transitions: z.array(WebKnowledgeTransitionSchema),
  routes: z.array(WebKnowledgeRouteSchema),
}).strict();

export const CommonWebPatternSchema = z.object({
  id: z.string().min(1),
  intentLabel: z.string().min(1),
  aliases: z.array(z.string().min(1)),
  sequence: z.array(z.string().min(1)).min(1),
  siteCount: z.number().int().positive(),
  confidence: z.number().min(0).max(1),
}).strict();

export const CommonWebPatternsFileSchema = z.object({
  schemaVersion: z.literal(1),
  generatedAt: z.string().datetime(),
  patterns: z.array(CommonWebPatternSchema),
}).strict();

export type WebLocatorHint = z.infer<typeof WebLocatorHintSchema>;
export type RawWebElement = z.infer<typeof RawWebElementSchema>;
export type CrawlPathStep = z.infer<typeof CrawlPathStepSchema>;
export type RawWebState = z.infer<typeof RawWebStateSchema>;
export type RawWebTransition = z.infer<typeof RawWebTransitionSchema>;
export type RawWebCrawlRun = z.infer<typeof RawWebCrawlRunSchema>;
export type NormalizedWebElement = z.infer<typeof NormalizedWebElementSchema>;
export type WebKnowledgeState = z.infer<typeof WebKnowledgeStateSchema>;
export type WebKnowledgeTransition = z.infer<typeof WebKnowledgeTransitionSchema>;
export type WebKnowledgeRouteStep = z.infer<typeof WebKnowledgeRouteStepSchema>;
export type WebKnowledgeRoute = z.infer<typeof WebKnowledgeRouteSchema>;
export type WebKnowledgeCatalog = z.infer<typeof WebKnowledgeCatalogSchema>;
export type CommonWebPattern = z.infer<typeof CommonWebPatternSchema>;
export type CommonWebPatternsFile = z.infer<typeof CommonWebPatternsFileSchema>;
