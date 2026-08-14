import { appendFile, mkdir, readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { z } from 'zod';

export const centralRecordKindSchema = z.enum([
  'feedback', 'correction', 'completion', 'status', 'edit',
]);

const centralContextSchema = z.object({
  platform: z.literal('windows'),
  applicationName: z.string().trim().min(1).max(300),
}).passthrough();

const centralPayloadSchemas = {
  feedback: z.object({
    rating: z.enum(['correct', 'incorrect', 'completed']),
    answerId: z.string().trim().min(1),
    answerText: z.string().trim().min(1),
  }).passthrough(),
  correction: z.object({
    originalGoal: z.string().trim().min(1),
    effectiveGoal: z.string().trim().min(1),
    context: centralContextSchema,
    developerVerified: z.literal(true),
    humanGold: z.object({
      identity: z.string().trim().min(1),
      siteOrApplication: z.string().trim().min(1),
      normalizedIntent: z.string().trim().min(1),
      semanticState: z.string().trim().min(1),
      targetConcept: z.string().trim().min(1),
      targetAliases: z.array(z.string()),
      action: z.string().trim().min(1),
      developerVerified: z.literal(true),
      confidence: z.number().min(0).max(1),
      successfulUses: z.number().int().nonnegative(),
      independentVerificationCount: z.number().int().positive(),
      lastVerifiedAt: z.iso.datetime({ offset: true }),
      verificationKeys: z.array(z.string()).min(1),
    }).passthrough().optional(),
  }).passthrough(),
  completion: z.object({
    originalGoal: z.string().trim().min(1),
    effectiveGoal: z.string().trim().min(1),
    context: centralContextSchema,
    visibleEvidence: z.array(z.string()),
    learningLabels: z.object({
      taskId: z.string().trim().min(1),
      stateId: z.string().trim().min(1),
      outcomeLabel: z.string().trim().min(1),
      authority: z.literal('human_gold'),
    }).passthrough(),
    developerVerified: z.literal(true),
  }).passthrough(),
  status: z.object({ feedbackId: z.string().trim().min(1), active: z.boolean() }).passthrough(),
  edit: z.object({
    feedbackId: z.string().trim().min(1),
    rating: z.enum(['correct', 'incorrect', 'completed']),
    goal: z.string().trim().min(1),
    answer: z.string().trim().min(1),
  }).passthrough(),
} satisfies Record<z.infer<typeof centralRecordKindSchema>, z.ZodType>;

export const centralRecordInputSchema = z.object({
  id: z.string().trim().min(1).max(200),
  kind: centralRecordKindSchema,
  updatedAt: z.iso.datetime({ offset: true }),
  payload: z.record(z.string(), z.unknown()),
}).strict().superRefine((record, context) => {
  const payload = record.payload;
  const base = z.object({
    schemaVersion: z.literal(1),
    id: z.string().trim().min(1).max(200),
    createdAtUtc: z.iso.datetime({ offset: true }),
  }).passthrough().safeParse(payload);
  if (!base.success) {
    context.addIssue({ code: 'custom', path: ['payload'], message: 'Invalid learning record metadata.' });
    return;
  }
  if (base.data.id !== record.id) {
    context.addIssue({ code: 'custom', path: ['payload', 'id'], message: 'Payload ID must match the envelope ID.' });
  }
  const feedbackId = payload.feedbackId;
  if (typeof feedbackId === 'string' && feedbackId.startsWith('__'))
    context.addIssue({ code: 'custom', path: ['payload', 'feedbackId'], message: 'Reserved feedback IDs cannot be synchronized.' });
  const kindResult = centralPayloadSchemas[record.kind].safeParse(payload);
  if (!kindResult.success)
    context.addIssue({ code: 'custom', path: ['payload'], message: `Invalid ${record.kind} learning record.` });
});

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
