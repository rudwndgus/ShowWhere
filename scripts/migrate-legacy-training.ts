import { constants } from 'node:fs';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createLegacyMigrationDraft } from '../src/semantic/legacyMigration';

const trainingRoot = path.resolve(process.cwd(), 'training');
const legacyRoot = path.join(trainingRoot, 'legacy', 'v1');
await mkdir(legacyRoot, { recursive: true });

const legacyFiles = [
  'answer-feedback.jsonl', 'corrections.jsonl', 'e2e-windows-sessions.jsonl',
  'e2e-batch-runs.jsonl', 'feedback-exclusions.jsonl', 'windows-e2e-cases.json',
];
const copied: string[] = [];
for (const file of legacyFiles) {
  try {
    await copyFile(path.join(trainingRoot, file), path.join(legacyRoot, file), constants.COPYFILE_EXCL);
    copied.push(file);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'EEXIST' && (error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
  }
}

let corrections: unknown[] = [];
try {
  corrections = (await readFile(path.join(trainingRoot, 'corrections.jsonl'), 'utf8'))
    .split(/\r?\n/u).filter(Boolean).map((line) => JSON.parse(line) as unknown);
} catch (error) {
  if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
}
const drafts = corrections.map(createLegacyMigrationDraft).filter((draft) => draft !== undefined);
await writeFile(
  path.join(legacyRoot, 'semantic-review-queue.jsonl'),
  drafts.map((draft) => JSON.stringify(draft)).join('\n') + (drafts.length ? '\n' : ''),
  'utf8',
);
await writeFile(path.join(legacyRoot, 'migration-manifest.json'), `${JSON.stringify({
  schemaVersion: 1,
  migratedAtUtc: new Date().toISOString(),
  immutableSourceCopiesCreated: copied,
  sourceCorrectionCount: corrections.length,
  semanticDraftCount: drafts.length,
  promotedToGold: 0,
  policy: 'Every legacy semantic draft requires explicit developer review in Teaching Mode.',
}, null, 2)}\n`, 'utf8');
console.log(`Legacy preserved. semantic_drafts=${drafts.length} gold_promotions=0`);
