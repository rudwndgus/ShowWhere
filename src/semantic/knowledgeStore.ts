import { appendFile, mkdir, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import {
  ConceptDictionaryEntrySchema,
  GuidanceTeachingRecordV2Schema,
  TaskGraphSchema,
  type ConceptDictionaryEntry,
  type GuidanceTeachingRecordV2,
  type TaskGraph,
} from './contracts';

async function readJsonl<T>(filePath: string, parse: (value: unknown) => T): Promise<T[]> {
  try {
    return (await readFile(filePath, 'utf8')).split(/\r?\n/u).filter(Boolean)
      .map((line) => parse(JSON.parse(line) as unknown));
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw error;
  }
}

export class SemanticKnowledgeStore {
  readonly trainingRoot: string;

  constructor(trainingRoot = path.resolve(process.cwd(), 'training')) {
    this.trainingRoot = trainingRoot;
  }

  async concepts(): Promise<ConceptDictionaryEntry[]> {
    return readJsonl(
      path.join(this.trainingRoot, 'concepts', 'concept-dictionary.jsonl'),
      (value) => ConceptDictionaryEntrySchema.parse(value),
    );
  }

  async goldSteps(): Promise<GuidanceTeachingRecordV2[]> {
    return readJsonl(
      path.join(this.trainingRoot, 'gold', 'guidance-steps-v2.jsonl'),
      (value) => GuidanceTeachingRecordV2Schema.parse(value),
    );
  }

  async taskGraphs(): Promise<TaskGraph[]> {
    const directory = path.join(this.trainingRoot, 'knowledge', 'task-playbooks');
    let files: string[];
    try {
      files = (await readdir(directory)).filter((file) => file.endsWith('.json'));
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
      throw error;
    }
    return Promise.all(files.map(async (file) =>
      TaskGraphSchema.parse(JSON.parse(await readFile(path.join(directory, file), 'utf8')) as unknown)));
  }

  async appendDraft(record: GuidanceTeachingRecordV2): Promise<void> {
    const parsed = GuidanceTeachingRecordV2Schema.parse(record);
    const directory = path.join(this.trainingRoot, 'drafts');
    await mkdir(directory, { recursive: true });
    await appendFile(path.join(directory, 'labeling-drafts.jsonl'), `${JSON.stringify(parsed)}\n`, 'utf8');
  }

  async appendGold(record: GuidanceTeachingRecordV2): Promise<void> {
    const parsed = GuidanceTeachingRecordV2Schema.parse(record);
    if (parsed.verification.status !== 'human_verified') {
      throw new Error('Only human-verified records can be saved to Gold.');
    }
    const directory = path.join(this.trainingRoot, 'gold');
    await mkdir(directory, { recursive: true });
    await appendFile(path.join(directory, 'guidance-steps-v2.jsonl'), `${JSON.stringify(parsed)}\n`, 'utf8');
  }
}
