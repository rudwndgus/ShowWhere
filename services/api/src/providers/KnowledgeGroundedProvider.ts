import { GuideDecisionSchema, type GuideDecision, type GuideRequest } from '../../../../src/contracts';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { SemanticKnowledgeStore } from '../../../../src/semantic/knowledgeStore';
import { resolveSemanticTarget, retrieveSemanticContext } from '../../../../src/semantic/retrieval';

export class KnowledgeGroundedProvider implements AiProvider {
  constructor(
    private readonly inner: AiProvider,
    private readonly store = new SemanticKnowledgeStore(),
  ) {}

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    const [concepts, taskGraphs, gold] = await Promise.all([
      this.store.concepts(), this.store.taskGraphs(), this.store.goldSteps(),
    ]);
    const semanticContext = retrieveSemanticContext(request, concepts, taskGraphs, gold);
    const raw = await this.inner.decideNextAction({ ...request, semanticContext });
    const parsed = GuideDecisionSchema.safeParse(raw);
    if (!parsed.success) return raw;
    const decision = parsed.data;
    if (decision.action !== 'highlight' || !decision.semanticTarget) return decision;

    const candidate = resolveSemanticTarget(decision.semanticTarget, request.candidates, concepts);
    if (!candidate) {
      return {
        status: 'needs_clarification', action: 'ask_user', confidence: 0,
        message: '의미상 맞는 항목은 알겠지만 현재 화면에서 확실한 버튼을 찾지 못했어요. 보이는 항목을 조금 더 알려주세요.',
      } satisfies GuideDecision;
    }
    const currentState = semanticContext.likelyTask?.states.find((state) =>
      state.transitions.some((transition) => transition.targetConceptId === decision.semanticTarget));
    const transition = currentState?.transitions.find((item) => item.targetConceptId === decision.semanticTarget);
    return {
      ...decision,
      targetId: candidate.id,
      expectedStateId: transition?.nextStateId ?? decision.expectedStateId,
      expectedEvidenceConceptIds: transition?.evidenceConceptIds ?? decision.expectedEvidenceConceptIds,
    };
  }
}
