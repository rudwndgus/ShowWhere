import { randomUUID } from 'node:crypto';
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

const LABEL_PROMPT = `You label one ShowWhere guidance step. ShowWhere guides but never clicks.
Return one JSON object matching schemaVersion showwhere-guidance-step-v2. Infer semantic intent separately from task workflow. Use only supplied task IDs and concept IDs unless no applicable ID exists. Never include screen coordinates, HWNDs, secrets, personal data, or chain-of-thought. The source fields must match the developer input. verification must be draft/developer_correction. A highlight decision needs one semantic concept target, an expected next state, evidence concepts, and a success condition. High-risk consequential actions require confirmation.`;

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
      draft = await this.#client.completeJson(
        this.#router.reasoning(), LABEL_PROMPT,
        {
          developerInput: input,
          availableConcepts: concepts.map((concept) => ({ conceptId: concept.conceptId, aliases: concept.aliases, preferredRoles: concept.preferredRoles })),
          availableTasks: taskGraphs.map((task) => ({
            taskId: task.taskId, intent: task.intent, desiredOutcome: task.desiredOutcome,
            states: task.states.map((state) => ({ stateId: state.stateId, transitions: state.transitions })),
          })),
        },
        GuidanceTeachingRecordV2Schema,
      );
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
    return validateTeachingRecord(value, { concepts, taskGraphs, existingGold });
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
