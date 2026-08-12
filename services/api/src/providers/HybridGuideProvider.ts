import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import type { GuideDecision, GuideRequest } from '../../../../src/contracts';
import { cosineSimilarity, type TextEmbeddingProvider } from './HuggingFaceEmbeddingClient';
import { describeCandidate, eligibleCandidates, resolveLocally } from './LocalGuideResolver';
import { resolveWindowsKnowledge } from '../windows-knowledge/WindowsKnowledgeResolver';
import { resolveWebKnowledge } from '../web-knowledge/WebKnowledgeResolver';
import type { WebKnowledgeSource } from '../web-knowledge/WebKnowledgeStore';

export interface HybridGuideProviderOptions {
  minScore: number;
  minMargin: number;
  debug?: boolean;
}

export class HybridGuideProvider implements AiProvider {
  constructor(
    private readonly fallback: AiProvider,
    private readonly embeddings?: TextEmbeddingProvider,
    private readonly options: HybridGuideProviderOptions = { minScore: 0.68, minMargin: 0.08 },
    private readonly webKnowledge: WebKnowledgeSource = { catalogs: [], patterns: [] },
  ) {}

  async decideNextAction(request: GuideRequest): Promise<unknown> {
    const windows = resolveWindowsKnowledge(request);
    if (windows) {
      this.log('windows_knowledge', windows.targetId);
      return windows;
    }
    const web = resolveWebKnowledge(request, this.webKnowledge);
    if (web) {
      this.log('web_knowledge', web.targetId);
      return web;
    }
    const local = resolveLocally(request);
    if (local) {
      this.log('local', local.targetId);
      return local;
    }

    if (!this.embeddings) {
      this.log('openai', 'reasoning');
      return this.fallback.decideNextAction(request);
    }

    // Ambiguous requests start both remote paths immediately. Whichever returns a
    // usable answer first wins; a slow HF provider never delays GPT escalation.
    const openai = this.fallback.decideNextAction(request);
    const huggingFace = this.resolveWithEmbeddings(request)
      .catch((error) => {
        this.log('huggingface_fallback', error instanceof Error ? error.message : String(error));
        return undefined;
      });
    const first = await Promise.race([
      openai.then((value) => ({ source: 'openai' as const, value })),
      huggingFace.then((value) => ({ source: 'huggingface' as const, value })),
    ]);
    if (first.source === 'openai') {
      this.log('openai', 'reasoning_parallel');
      return first.value;
    }
    if (first.value) {
      this.log('huggingface', first.value.targetId);
      return first.value;
    }
    this.log('openai', 'reasoning_after_hf_miss');
    return openai;
  }

  private async resolveWithEmbeddings(request: GuideRequest): Promise<GuideDecision | undefined> {
    const candidates = eligibleCandidates(request).slice(0, 48);
    if (candidates.length === 0) return undefined;
    const goal = `사용자 목표: ${request.session.goal ?? request.session.originalUserMessage}`;
    const descriptions = candidates.map((candidate) => `클릭 대상: ${describeCandidate(candidate)}`);
    const [goalVector, ...candidateVectors] = await this.embeddings!.embed([goal, ...descriptions]);
    const ranked = candidates.map((candidate, index) => ({
      candidate,
      score: cosineSimilarity(goalVector, candidateVectors[index]),
    })).sort((left, right) => right.score - left.score);
    const best = ranked[0];
    const margin = best.score - (ranked[1]?.score ?? -1);
    if (best.score < this.options.minScore || margin < this.options.minMargin) return undefined;
    const label = best.candidate.label ?? best.candidate.description ?? best.candidate.role;
    return {
      status: 'in_progress',
      action: 'highlight',
      targetId: best.candidate.id,
      message: `화면의 '${label}'을(를) 눌러주세요.`,
      expectedChange: `'${label}'과 관련된 다음 화면이 열립니다.`,
      confidence: Math.min(0.97, Math.max(0.65, best.score)),
    };
  }

  private log(route: string, detail: unknown): void {
    if (this.options.debug) console.log(`[showwhere:router] route=${route} detail=${String(detail).slice(0, 160)}`);
  }
}
