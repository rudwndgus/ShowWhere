import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { z } from 'zod';

export const learningRoot = path.resolve(process.cwd(), 'learning');
export const dataRoot = path.join(learningRoot, 'data');

export async function readJson<T>(filePath: string, schema: z.ZodType<T>): Promise<T> {
  return schema.parse(JSON.parse(await readFile(filePath, 'utf8')));
}

export async function readJsonl<T>(filePath: string, schema: z.ZodType<T>): Promise<T[]> {
  const text = await readFile(filePath, 'utf8').catch((error: NodeJS.ErrnoException) => {
    if (error.code === 'ENOENT') return '';
    throw error;
  });
  return text.split(/\r?\n/u).filter(Boolean).map((line) => schema.parse(JSON.parse(line)));
}

export async function writeJson(filePath: string, value: unknown): Promise<void> {
  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

export async function writeJsonl(filePath: string, values: readonly unknown[]): Promise<void> {
  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, values.map((value) => JSON.stringify(value)).join('\n') + (values.length ? '\n' : ''), 'utf8');
}

export function stableHash(value: unknown): string {
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

export function scenarioFingerprint(value: {
  seedId: string;
  goal: string;
  context: { screenState: string };
  candidates: Array<{ label: string; role: string }>;
  proposedCorrectTargetId: string | null;
}): string {
  const normalize = (text: string) => text.toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
  return stableHash({
    seedId: value.seedId,
    goal: normalize(value.goal),
    state: normalize(value.context.screenState),
    candidates: value.candidates.map((candidate) => [normalize(candidate.label), candidate.role]).sort(),
    target: value.proposedCorrectTargetId,
  });
}
