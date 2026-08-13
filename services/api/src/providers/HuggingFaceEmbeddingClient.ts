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
  private readonly inFlight = new Map<string, Promise<number[]>>();

  constructor(private readonly options: HuggingFaceEmbeddingOptions) {}

  async embed(texts: string[]): Promise<number[][]> {
    const unique = [...new Set(texts)];
    const missing = unique.filter((text) => !this.cache.has(text) && !this.inFlight.has(text));
    if (missing.length > 0) {
      // Provider-backed feature extraction is substantially faster and more
      // reliable with small batches. Warm-up chunks run concurrently.
      for (let offset = 0; offset < missing.length; offset += 24) {
        const chunk = missing.slice(offset, offset + 24);
        const batch = this.fetchEmbeddings(chunk);
        chunk.forEach((text, index) => {
          const pending = batch.then((vectors) => vectors[index]);
          this.inFlight.set(text, pending);
          void pending.then(
            () => this.inFlight.delete(text),
            () => this.inFlight.delete(text),
          );
        });
      }
    }
    await Promise.all(unique.map(async (text) => {
      if (this.cache.has(text)) return;
      const vector = await this.inFlight.get(text)!;
      this.cache.set(text, vector);
    }));
    return texts.map((text) => this.cache.get(text)!);
  }

  private async fetchEmbeddings(missing: string[]): Promise<number[][]> {
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
        return missing.map((_, index) => meanVector(values[index]));
      } finally {
        clearTimeout(timeout);
      }
  }
}
