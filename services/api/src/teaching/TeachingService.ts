import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { ApiConfig } from '../config';
import { ModelRouter } from '../providers/ModelRouter';
import { StructuredFeatherlessClient } from '../providers/StructuredFeatherlessClient';
import {
  GuidanceTeachingRecordV2Schema,
  TeachingInputSchema,
  type GuidanceTeachingRecordV2,
  type TeachingInput,
  type TeachingValidationResult,
} from '../../../../src/semantic/contracts';
import { SemanticKnowledgeStore } from '../../../../src/semantic/knowledgeStore';
import { validateTeachingRecord } from '../../../../src/semantic/validation';

const LabelProposalSchema = z.object({
  intent: z.object({
    domain: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
    action: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
    object: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
    language: z.string().trim().min(2).max(35),
    keyPhrases: z.array(z.string().trim().min(1).max(120)).min(1).max(20),
  }).strict(),
  taskId: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
  desiredOutcome: z.string().trim().min(1).max(1_000),
  stateId: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
  action: z.enum(['highlight', 'clarify', 'complete', 'explain']),
  targetConceptId: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u).nullable(),
  instruction: z.string().trim().min(1).max(1_000),
  nextStateId: z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u),
  evidenceConceptIds: z.array(z.string().trim().min(1).max(160).regex(/^[a-z0-9][a-z0-9._-]*$/u)).max(20),
  successConditionType: z.enum(['state_reached', 'concept_visible', 'user_confirmed', 'task_complete']),
  riskLevel: z.enum(['low', 'medium', 'high']),
  confirmationRequired: z.boolean(),
  confidence: z.number().finite().min(0).max(1),
}).strict().superRefine((proposal, context) => {
  if (proposal.action === 'highlight' && proposal.targetConceptId === null) {
    context.addIssue({ code: 'custom', path: ['targetConceptId'], message: 'Highlight needs a target concept.' });
  }
  if (proposal.action !== 'highlight' && proposal.targetConceptId !== null) {
    context.addIssue({ code: 'custom', path: ['targetConceptId'], message: 'Only highlight may have a target concept.' });
  }
  if (proposal.riskLevel === 'high' && !proposal.confirmationRequired) {
    context.addIssue({ code: 'custom', path: ['confirmationRequired'], message: 'High risk requires confirmation.' });
  }
});

const LABEL_PROMPT = `You are the semantic labeling brain for one ShowWhere developer correction.
ShowWhere guides one step at a time and never clicks. Return JSON only, with exactly this compact shape:
{"intent":{"domain":"semantic_id","action":"semantic_id","object":"semantic_id","language":"ko-KR","keyPhrases":["phrase"]},"taskId":"semantic_id","desiredOutcome":"text","stateId":"semantic_id","action":"highlight|clarify|complete|explain","targetConceptId":"semantic_id or null","instruction":"friendly one-step instruction","nextStateId":"semantic_id","evidenceConceptIds":["semantic_id"],"successConditionType":"state_reached|concept_visible|user_confirmed|task_complete","riskLevel":"low|medium|high","confirmationRequired":false,"confidence":0.0}
Use lowercase dotted semantic IDs. Infer the user's real intent from the question and developer correction. Prefer supplied task, state, and concept IDs when their meanings fit. If no task ID fits, create a precise new task ID. If no concept ID fits, create a precise new concept ID. stateId describes the screen before the suggested action; nextStateId describes the screen immediately after that one action. Never skip an intermediate screen. For example, highlighting Start from another app leads to windows.start.menu, not directly to Settings. Treat the developer correction as authoritative. Never include source text, candidates, coordinates, HWNDs, secrets, personal data, explanations, markdown, or chain-of-thought. A highlight must have one target concept. Other actions must use null. High-risk actions require confirmation.`;

function normalizeLabel(value: string): string {
  return value.toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

export class TeachingService {
  readonly #store: SemanticKnowledgeStore;
  readonly #config: ApiConfig;
  readonly #router?: ModelRouter;
  readonly #client?: StructuredFeatherlessClient;

  constructor(config: ApiConfig, store = new SemanticKnowledgeStore(), fetchImplementation?: typeof fetch) {
    this.#config = config;
    this.#store = store;
    if (config.featherless) {
      this.#router = new ModelRouter({
        guideModel: config.featherless.model,
        guideFallbackModel: config.featherless.guideFallbackModel,
        reasoningModel: config.featherless.reasoningModel,
        visionModels: config.featherless.visionModels,
        fastModel: config.featherless.fastModel,
        embeddingModel: config.featherless.embeddingModel,
        learningGeneratorModel: config.featherless.learningGeneratorModel,
        learningJudgeModels: config.featherless.learningJudgeModels,
      });
      this.#client = new StructuredFeatherlessClient(config.featherless, fetchImplementation);
    }
  }

  async analyze(value: unknown): Promise<GuidanceTeachingRecordV2> {
    const input = TeachingInputSchema.parse(value);
    const [concepts, taskGraphs] = await Promise.all([this.#store.concepts(), this.#store.taskGraphs()]);
    let draft: GuidanceTeachingRecordV2;
    if (this.#config.aiMode === 'featherless' && this.#router && this.#client) {
      const proposal = await this.#client.completeJson(
        this.#router.reasoning(), LABEL_PROMPT,
        {
          developerInput: {
            userQuestion: input.userQuestion,
            currentStepDescription: input.currentStepDescription,
            developerCorrection: input.developerCorrection,
            context: input.context ? {
              platform: input.context.platform,
              applicationName: input.context.applicationName,
              windowTitle: input.context.windowTitle,
              locale: input.context.locale,
            } : undefined,
            candidates: input.candidates,
          },
          availableConcepts: concepts.map((concept) => ({ conceptId: concept.conceptId, aliases: concept.aliases, preferredRoles: concept.preferredRoles })),
          availableTasks: taskGraphs.map((task) => ({
            taskId: task.taskId, intent: task.intent, desiredOutcome: task.desiredOutcome,
            states: task.states.map((state) => ({ stateId: state.stateId, transitions: state.transitions })),
          })),
        },
        LabelProposalSchema,
        900,
      );
      const visibleConcepts = concepts.flatMap((concept) => {
        const matches = input.candidates.filter((candidate) => {
          const label = normalizeLabel(candidate.label);
          return concept.aliases.some((alias) => {
            const normalizedAlias = normalizeLabel(alias);
            return label === normalizedAlias || label.includes(normalizedAlias) || normalizedAlias.includes(label);
          });
        });
        if (matches.length === 0) return [];
        return [{
          conceptId: concept.conceptId,
          role: matches[0].role,
          observedLabels: [...new Set(matches.map((candidate) => candidate.label))].slice(0, 20),
          enabled: matches.some((candidate) => candidate.enabled),
        }];
      });
      const targetConcept = proposal.targetConceptId === null
        ? undefined
        : concepts.find((concept) => concept.conceptId === proposal.targetConceptId);
      draft = GuidanceTeachingRecordV2Schema.parse({
        schemaVersion: 'showwhere-guidance-step-v2',
        id: 'draft.placeholder',
        source: {
          userQuestion: input.userQuestion,
          currentStepDescription: input.currentStepDescription,
          developerCorrection: input.developerCorrection,
        },
        intent: proposal.intent,
        task: { taskId: proposal.taskId, desiredOutcome: proposal.desiredOutcome },
        state: { stateId: proposal.stateId, completedStepIds: [], visibleConcepts },
        decision: {
          action: proposal.action,
          target: proposal.action === 'highlight' ? {
            conceptId: proposal.targetConceptId!,
            preferredRoles: targetConcept?.preferredRoles ?? ['button', 'link', 'menuitem', 'listitem'],
            confusableNegativeConceptIds: [],
          } : null,
          instruction: proposal.instruction,
        },
        expectedTransition: {
          nextStateId: proposal.nextStateId,
          evidenceConcepts: proposal.evidenceConceptIds,
        },
        successCondition: { type: proposal.successConditionType, stateId: proposal.nextStateId },
        safety: { riskLevel: proposal.riskLevel, confirmationRequired: proposal.confirmationRequired },
        verification: { status: 'draft', sourceType: 'developer_correction', confidence: proposal.confidence },
      });
    } else {
      draft = this.#mockDraft(input, taskGraphs);
    }
    const safeDraft = GuidanceTeachingRecordV2Schema.parse({
      ...draft,
      id: `draft.${randomUUID().toLowerCase()}`,
      source: {
        userQuestion: input.userQuestion,
        currentStepDescription: input.currentStepDescription,
        developerCorrection: input.developerCorrection,
      },
      verification: { status: 'draft', sourceType: 'developer_correction', confidence: draft.verification.confidence },
    });
    await this.#store.appendDraft(safeDraft);
    return safeDraft;
  }

  async validate(value: unknown): Promise<TeachingValidationResult> {
    const [concepts, taskGraphs, existingGold] = await Promise.all([
      this.#store.concepts(), this.#store.taskGraphs(), this.#store.goldSteps(),
    ]);
    const parsed = GuidanceTeachingRecordV2Schema.safeParse(value);
    const isDeveloperCorrection = parsed.success
      && parsed.data.verification.sourceType === 'developer_correction';
    return validateTeachingRecord(value, {
      concepts,
      taskGraphs,
      existingGold,
      allowNewTaskId: isDeveloperCorrection,
    });
  }

  async saveApproved(value: unknown, approvedBy: string): Promise<GuidanceTeachingRecordV2> {
    const draft = GuidanceTeachingRecordV2Schema.parse(value);
    const validation = await this.validate(draft);
    if (!validation.valid) throw new Error('Gold validation failed.');
    const approved = GuidanceTeachingRecordV2Schema.parse({
      ...draft,
      verification: {
        ...draft.verification,
        status: 'human_verified',
        sourceType: draft.verification.sourceType,
        approvedBy: approvedBy.trim() || 'developer',
        approvedAtUtc: new Date().toISOString(),
      },
    });
    await this.#store.appendGold(approved);
    return approved;
  }

  #mockDraft(input: TeachingInput, tasks: Awaited<ReturnType<SemanticKnowledgeStore['taskGraphs']>>): GuidanceTeachingRecordV2 {
    const text = `${input.userQuestion} ${input.currentStepDescription} ${input.developerCorrection}`.toLocaleLowerCase();
    const task = text.includes('프린터') || text.includes('printer')
      ? tasks.find((item) => item.taskId === 'windows.printer.check_status')
      : text.includes('wi-fi') || text.includes('wifi') || text.includes('와이파이') || text.includes('인터넷')
        ? tasks.find((item) => item.taskId === 'windows.network.check_wifi_status')
        : tasks.find((item) => item.taskId === 'shopping.track_order');
    if (!task) throw new Error('No task graph is available for mock labeling.');
    const state = task.states.find((item) => input.currentStepDescription.toLocaleLowerCase().includes(item.stateId.split('.').at(-1) ?? ''))
      ?? task.states.find((item) => item.transitions.length > 0)!;
    const transition = state.transitions[0];
    return {
      schemaVersion: 'showwhere-guidance-step-v2', id: 'draft.placeholder',
      source: input,
      intent: { ...task.intent, language: input.context?.locale ?? 'ko-KR', keyPhrases: input.userQuestion.split(/\s+/u).filter(Boolean).slice(0, 12) },
      task: { taskId: task.taskId, desiredOutcome: task.desiredOutcome },
      state: { stateId: state.stateId, completedStepIds: [], visibleConcepts: [] },
      decision: transition.action === 'highlight' ? {
        action: 'highlight', target: { conceptId: transition.targetConceptId!, preferredRoles: ['button', 'link', 'menuitem'], confusableNegativeConceptIds: [] }, instruction: transition.instruction,
      } : { action: transition.action, target: null, instruction: transition.instruction },
      expectedTransition: { nextStateId: transition.nextStateId, evidenceConcepts: transition.evidenceConceptIds },
      successCondition: { type: 'state_reached', stateId: transition.nextStateId },
      safety: { riskLevel: transition.riskLevel, confirmationRequired: transition.confirmationRequired },
      verification: { status: 'draft', sourceType: 'developer_correction', confidence: 0.6 },
    };
  }
}
