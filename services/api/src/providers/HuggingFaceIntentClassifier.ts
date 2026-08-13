import { webSemanticTaxonomy } from '../../../../src/web-knowledge';
import { windowsKnowledgeCatalog } from '../windows-knowledge/WindowsKnowledgeCatalog';
import type { WebKnowledgeSource } from '../web-knowledge/WebKnowledgeStore';
import { cosineSimilarity, type TextEmbeddingProvider } from './HuggingFaceEmbeddingClient';

export interface SemanticIntentMatch {
  domain: 'windows' | 'web';
  intentId: string;
  score: number;
  margin: number;
  alternatives: Array<{
    domain: 'windows' | 'web';
    intentId: string;
    score: number;
  }>;
}

interface IntentPrototype {
  domain: SemanticIntentMatch['domain'];
  intentId: string;
  text: string;
}

export class HuggingFaceIntentClassifier {
  private readonly prototypes: IntentPrototype[];
  private readonly prototypeVectors: Promise<number[][] | undefined>;
  private readonly resultCache = new Map<string, SemanticIntentMatch>();

  constructor(
    private readonly embeddings: TextEmbeddingProvider,
    knowledge: WebKnowledgeSource,
  ) {
    this.prototypes = [
      ...windowsKnowledgeCatalog.map((entry) => ({
        domain: 'windows' as const,
        intentId: entry.id,
        text: `passage: Windows ${entry.kind}: ${entry.intents.join(' | ')}`,
      })),
      ...webSemanticTaxonomy.map((intent) => ({
        domain: 'web' as const,
        intentId: intent.id,
        text: `passage: Website action ${intent.description}: ${intent.aliases.join(' | ')}`,
      })),
      ...knowledge.catalogs.map((catalog) => ({
        domain: 'web' as const,
        intentId: `site:${catalog.siteId}`,
        text: `passage: Website ${catalog.displayName}: ${catalog.domains.join(' | ')}`,
      })),
    ];

    // Warm immutable prototype vectors when the API starts. New questions then
    // embed only one short sentence, while a failed HF provider remains optional.
    this.prototypeVectors = this.embeddings.embed(this.prototypes.map((item) => item.text))
      .catch(() => undefined);
  }

  async ready(): Promise<boolean> {
    return (await this.prototypeVectors) !== undefined;
  }

  async classify(goal: string): Promise<SemanticIntentMatch | undefined> {
    const cached = this.getCached(goal);
    if (cached) return cached;
    const [goalVector, prototypeVectors] = await Promise.all([
      this.embeddings.embed([`query: ${goal}`]).then(([vector]) => vector),
      this.prototypeVectors,
    ]);
    if (!prototypeVectors || prototypeVectors.length !== this.prototypes.length) return undefined;
    const ranked = this.prototypes.map((prototype, index) => ({
      prototype,
      score: cosineSimilarity(goalVector, prototypeVectors[index]),
    })).sort((left, right) => right.score - left.score);
    const best = ranked[0];
    if (!best) return undefined;
    const margin = best.score - (ranked[1]?.score ?? -1);
    const result: SemanticIntentMatch = {
      ...best.prototype,
      score: best.score,
      margin,
      alternatives: ranked.slice(0, 5).map((item) => ({ ...item.prototype, score: item.score })),
    };
    this.resultCache.set(this.cacheKey(goal), result);
    return result;
  }

  getCached(goal: string): SemanticIntentMatch | undefined {
    return this.resultCache.get(this.cacheKey(goal));
  }

  private cacheKey(goal: string): string {
    return goal.normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
  }
}
