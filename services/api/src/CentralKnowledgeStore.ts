import { appendFile, mkdir, readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { z } from 'zod';

export const centralRecordKindSchema = z.enum([
  'feedback', 'correction', 'completion', 'status', 'edit',
]);

export const centralRecordInputSchema = z.object({
  id: z.string().trim().min(1).max(200),
  kind: centralRecordKindSchema,
  updatedAt: z.iso.datetime({ offset: true }),
  payload: z.record(z.string(), z.unknown()),
}).strict();

export const centralRecordBatchSchema = z.object({
  records: z.array(centralRecordInputSchema).min(1).max(100),
}).strict();

export type CentralRecordInput = z.infer<typeof centralRecordInputSchema>;
export type CentralRecord = CentralRecordInput & { version: number };
const centralRecordSchema = centralRecordInputSchema.extend({ version: z.number().int().positive() });

export class CentralKnowledgeStore {
  private readonly records = new Map<string, CentralRecord>();
  private cursor = 0;
  private initialization?: Promise<void>;
  private writeChain: Promise<void> = Promise.resolve();
  private readonly path: string;

  constructor(directory: string) {
    this.path = resolve(directory, 'knowledge-events.jsonl');
  }

  async initialize(): Promise<void> {
    this.initialization ??= this.load();
    await this.initialization;
  }

  private async load(): Promise<void> {
    try {
      const content = await readFile(this.path, 'utf8');
      for (const line of content.split(/\r?\n/u)) {
        if (!line.trim()) continue;
        try {
          const parsed = centralRecordSchema.safeParse(JSON.parse(line));
          if (!parsed.success) continue;
          const value = parsed.data;
          this.cursor = Math.max(this.cursor, value.version);
          this.applyLatest(value);
        } catch { /* Ignore a damaged tail record and preserve earlier events. */ }
      }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
    }
  }

  async upsert(inputs: CentralRecordInput[]): Promise<{ cursor: number; accepted: number }> {
    await this.initialize();
    let result = { cursor: this.cursor, accepted: 0 };
    const operation = this.writeChain.then(async () => { result = await this.upsertSerial(inputs); });
    this.writeChain = operation.catch(() => undefined);
    await operation;
    return result;
  }

  private async upsertSerial(inputs: CentralRecordInput[]): Promise<{ cursor: number; accepted: number }> {
    await mkdir(dirname(this.path), { recursive: true });
    let accepted = 0;
    for (const input of inputs) {
      const key = `${input.kind}:${input.id}`;
      const existing = this.records.get(key);
      if (existing && Date.parse(existing.updatedAt) >= Date.parse(input.updatedAt)) continue;
      const record: CentralRecord = { ...input, version: ++this.cursor };
      await appendFile(this.path, `${JSON.stringify(record)}\n`, 'utf8');
      this.records.set(key, record);
      accepted += 1;
    }
    return { cursor: this.cursor, accepted };
  }

  async changesAfter(cursor: number): Promise<{ cursor: number; records: CentralRecord[] }> {
    await this.initialize();
    await this.writeChain;
    return {
      cursor: this.cursor,
      records: [...this.records.values()]
        .filter((record) => record.version > cursor)
        .sort((left, right) => left.version - right.version),
    };
  }

  private applyLatest(record: CentralRecord): void {
    const key = `${record.kind}:${record.id}`;
    const current = this.records.get(key);
    if (!current || record.version > current.version) this.records.set(key, record);
  }
}
