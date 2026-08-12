import { InferenceClient } from '@huggingface/inference';
import { cosineSimilarity } from '../../api/src/providers/HuggingFaceEmbeddingClient';
import { normalizeUiName, semanticLabelFromRules, webSemanticTaxonomy } from '../../../src/web-knowledge';

export interface ModelComparisonResult {
  model: string;
  compatible: boolean;
  qualityScore: number;
  latencyMs?: number;
  error?: string;
}

export interface SemanticLabelResult {
  label: string;
  confidence: number;
  source: 'rule' | 'huggingface' | 'generated';
}

export interface SemanticLabelerOptions {
  token?: string;
  configuredModel?: string;
  candidateModels?: string[];
  minimumScore?: number;
}

function flattenVector(value: unknown): number[] {
  if (!Array.isArray(value) || value.length === 0) throw new Error('No embedding returned.');
  if (value.every((item) => typeof item === 'number')) return value as number[];
  const rows = value.map(flattenVector);
  const width = rows[0]?.length ?? 0;
  if (width === 0 || rows.some((row) => row.length !== width)) throw new Error('Invalid embedding shape.');
  return Array.from({ length: width }, (_, index) => rows.reduce((sum, row) => sum + row[index], 0) / rows.length);
}

function vectorsFromOutput(value: unknown, expected: number): number[][] {
  if (!Array.isArray(value)) throw new Error('Invalid embedding response.');
  if (expected === 1) return [flattenVector(value)];
  if (value.length !== expected) throw new Error(`Expected ${expected} embeddings, received ${value.length}.`);
  return value.map(flattenVector);
}

function compactError(error: unknown): string {
  return (error instanceof Error ? error.message : String(error))
    .replace(/(?:hf_|sk-)[A-Za-z0-9_-]+/gu, '[secret]').slice(0, 240);
}

function fallbackLabel(name: string): string {
  const slug = normalizeUiName(name).replace(/\s+/gu, '_').slice(0, 80);
  return slug ? `site.${slug}` : 'site.unknown';
}

const benchmarkGroups = [
  ['배송 조회', 'track package', 'order tracking'],
  ['주문 내역', 'your orders', 'purchase history'],
  ['계정', 'account & lists', 'my account'],
  ['설정', 'settings', 'preferences'],
];

export class SemanticLabeler {
  private readonly client?: InferenceClient;
  private readonly minimumScore: number;
  private selectedModel?: string;
  private comparisons: ModelComparisonResult[] = [];
  private taxonomyVectors?: Array<{ id: string; vector: number[] }>;

  constructor(private readonly options: SemanticLabelerOptions) {
    this.client = options.token ? new InferenceClient(options.token) : undefined;
    this.minimumScore = options.minimumScore ?? 0.62;
  }

  async initialize(): Promise<void> {
    if (!this.client) return;
    const fallbacks = [
      this.options.configuredModel,
      'ibm-granite/granite-embedding-97m-multilingual-r2',
      'intfloat/multilingual-e5-small',
      'Qwen/Qwen3-Embedding-0.6B',
    ].filter((value): value is string => Boolean(value));
    const discovered = this.options.candidateModels?.length ? [] : await this.discoverCurrentModels();
    const candidates = [...new Set(this.options.candidateModels?.length
      ? this.options.candidateModels : [this.options.configuredModel, ...discovered, ...fallbacks].filter(Boolean) as string[])].slice(0, 4);
    this.comparisons = await Promise.all(candidates.map((model) => this.compareModel(model)));
    this.selectedModel = this.comparisons
      .filter((item) => item.compatible)
      .sort((left, right) => right.qualityScore - left.qualityScore
        || (left.latencyMs ?? Number.MAX_SAFE_INTEGER) - (right.latencyMs ?? Number.MAX_SAFE_INTEGER))[0]?.model;
    if (this.selectedModel) {
      try { await this.prepareTaxonomy(); }
      catch (error) {
        const comparison = this.comparisons.find((item) => item.model === this.selectedModel);
        if (comparison) comparison.error = `taxonomy: ${compactError(error)}`;
        this.selectedModel = undefined;
      }
    }
  }

  private async discoverCurrentModels(): Promise<string[]> {
    try {
      const response = await fetch('https://huggingface.co/api/models?pipeline_tag=feature-extraction&inference_provider=all&sort=downloads&direction=-1&limit=100&full=false', {
        signal: AbortSignal.timeout(8_000),
      });
      if (!response.ok) return [];
      const models = await response.json() as Array<{ id?: unknown; tags?: unknown; gated?: unknown; downloads?: unknown }>;
      return models.map((item) => {
        const id = typeof item.id === 'string' ? item.id : '';
        const tags = Array.isArray(item.tags) ? item.tags.filter((tag): tag is string => typeof tag === 'string') : [];
        const searchable = `${id} ${tags.join(' ')}`.toLowerCase();
        const multilingual = /multilingual|cross-lingual|\bko\b|korean|qwen/u.test(searchable);
        const embedding = /sentence-transformers|text-embeddings-inference|feature-extraction/u.test(searchable);
        const sizePreference = /small|mini|97m|0\.6b|base/u.test(searchable) ? 3 : /large|8b|7b/u.test(searchable) ? 0 : 1;
        const downloads = typeof item.downloads === 'number' ? Math.log10(item.downloads + 1) : 0;
        return { id, eligible: Boolean(id) && multilingual && embedding && item.gated !== true, score: sizePreference * 10 + downloads };
      }).filter((item) => item.eligible).sort((left, right) => right.score - left.score).slice(0, 3).map((item) => item.id);
    } catch { return []; }
  }

  get metadata() {
    return {
      provider: this.selectedModel ? 'huggingface' as const : 'rules' as const,
      model: this.selectedModel ?? 'showwhere-web-taxonomy-v1',
      comparedModels: this.comparisons,
    };
  }

  async label(name: string, role: string, area?: string): Promise<SemanticLabelResult> {
    const rule = semanticLabelFromRules(name) ?? semanticLabelFromRules(`${name} ${area ?? ''}`);
    if (rule) return { label: rule, confidence: 0.96, source: 'rule' };
    if (!this.selectedModel || !this.taxonomyVectors) {
      return { label: fallbackLabel(name), confidence: 0.35, source: 'generated' };
    }
    try {
      const [vector] = await this.embed(this.selectedModel, [`query: UI ${role} ${name} ${area ?? ''}`]);
      const ranked = this.taxonomyVectors.map((entry) => ({
        id: entry.id,
        score: cosineSimilarity(vector, entry.vector),
      })).sort((left, right) => right.score - left.score);
      const best = ranked[0];
      const margin = best.score - (ranked[1]?.score ?? 0);
      if (best.score >= this.minimumScore && margin >= 0.015)
        return { label: best.id, confidence: Math.min(0.94, Math.max(0.55, best.score)), source: 'huggingface' };
    } catch { /* Deterministic fallback is intentionally available offline. */ }
    return { label: fallbackLabel(name), confidence: 0.35, source: 'generated' };
  }

  private async compareModel(model: string): Promise<ModelComparisonResult> {
    const started = Date.now();
    try {
      const texts = benchmarkGroups.flatMap((group) => group).map((text) => `query: ${text}`);
      const vectors = await this.embed(model, texts);
      let positive = 0;
      let negative = 0;
      let positives = 0;
      let negatives = 0;
      for (let groupIndex = 0; groupIndex < benchmarkGroups.length; groupIndex++) {
        const start = groupIndex * benchmarkGroups[groupIndex].length;
        for (let left = 0; left < benchmarkGroups[groupIndex].length; left++) {
          for (let right = left + 1; right < benchmarkGroups[groupIndex].length; right++) {
            positive += cosineSimilarity(vectors[start + left], vectors[start + right]);
            positives++;
          }
          const nextGroup = (groupIndex + 1) % benchmarkGroups.length;
          const nextStart = nextGroup * benchmarkGroups[nextGroup].length;
          negative += cosineSimilarity(vectors[start + left], vectors[nextStart]);
          negatives++;
        }
      }
      const separation = (positive / positives) - (negative / negatives);
      return {
        model,
        compatible: true,
        qualityScore: Math.max(0, Math.min(1, 0.5 + separation / 2)),
        latencyMs: Date.now() - started,
      };
    } catch (error) {
      return { model, compatible: false, qualityScore: 0, latencyMs: Date.now() - started, error: compactError(error) };
    }
  }

  private async prepareTaxonomy(): Promise<void> {
    const texts = webSemanticTaxonomy.map((entry) =>
      `passage: ${entry.id} ${entry.description} ${entry.aliases.join(' ')}`);
    const vectors = await this.embed(this.selectedModel!, texts);
    this.taxonomyVectors = webSemanticTaxonomy.map((entry, index) => ({ id: entry.id, vector: vectors[index] }));
  }

  private async embed(model: string, texts: string[]): Promise<number[][]> {
    const response = await this.client!.featureExtraction({
      model,
      provider: 'auto',
      inputs: texts,
      normalize: true,
      truncate: true,
    }, { signal: AbortSignal.timeout(20_000), retry_on_error: false });
    return vectorsFromOutput(response, texts.length);
  }
}
