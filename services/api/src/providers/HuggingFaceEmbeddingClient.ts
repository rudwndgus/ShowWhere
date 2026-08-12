export interface HuggingFaceEmbeddingOptions {
  token: string;
  model: string;
  baseUrl: string;
  requestTimeoutMs: number;
}

export interface TextEmbeddingProvider {
  embed(texts: string[]): Promise<number[][]>;
}

function meanVector(value: unknown): number[] {
  if (!Array.isArray(value) || value.length === 0) throw new Error('Hugging Face returned no embedding.');
  if (value.every((item) => typeof item === 'number')) return value as number[];
  const rows = value.map(meanVector);
  const width = rows[0]?.length ?? 0;
  if (width === 0 || rows.some((row) => row.length !== width)) throw new Error('Hugging Face returned an invalid embedding shape.');
  return Array.from({ length: width }, (_, index) => rows.reduce((sum, row) => sum + row[index], 0) / rows.length);
}

export function cosineSimilarity(left: number[], right: number[]): number {
  if (left.length === 0 || left.length !== right.length) return -1;
  let dot = 0;
  let leftNorm = 0;
  let rightNorm = 0;
  for (let index = 0; index < left.length; index++) {
    dot += left[index] * right[index];
    leftNorm += left[index] ** 2;
    rightNorm += right[index] ** 2;
  }
  return leftNorm === 0 || rightNorm === 0 ? -1 : dot / Math.sqrt(leftNorm * rightNorm);
}

export class HuggingFaceEmbeddingClient implements TextEmbeddingProvider {
  private readonly cache = new Map<string, number[]>();

  constructor(private readonly options: HuggingFaceEmbeddingOptions) {}

  async embed(texts: string[]): Promise<number[][]> {
    const missing = [...new Set(texts.filter((text) => !this.cache.has(text)))];
    if (missing.length > 0) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), this.options.requestTimeoutMs);
      try {
        const response = await fetch(`${this.options.baseUrl}/${this.options.model}`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${this.options.token}`, 'Content-Type': 'application/json' },
          signal: controller.signal,
          body: JSON.stringify({ inputs: missing, options: { wait_for_model: false } }),
        });
        if (!response.ok) throw new Error(`Hugging Face API ${response.status}: ${(await response.text()).slice(0, 300)}`);
        const raw = await response.json() as unknown;
        const values = missing.length === 1 ? [raw] : raw;
        if (!Array.isArray(values) || values.length !== missing.length)
          throw new Error('Hugging Face returned an unexpected embedding count.');
        missing.forEach((text, index) => this.cache.set(text, meanVector(values[index])));
      } finally {
        clearTimeout(timeout);
      }
    }
    return texts.map((text) => this.cache.get(text)!);
  }
}
