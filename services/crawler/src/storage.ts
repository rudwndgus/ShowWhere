import { appendFile, mkdir, readFile, readdir, rename, unlink, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { gzip, gunzip } from 'node:zlib';
import {
  CommonWebPatternsFileSchema,
  RawWebCrawlRunSchema,
  WebKnowledgeCatalogSchema,
  type CommonWebPattern,
  type CommonWebPatternsFile,
  type RawWebCrawlRun,
  type WebKnowledgeCatalog,
} from '../../../src/web-knowledge';

const gzipAsync = promisify(gzip);
const gunzipAsync = promisify(gunzip);

export interface KnowledgePaths {
  root: string;
  raw: string;
  normalized: string;
  catalogs: string;
  patterns: string;
  training: string;
}
export function knowledgePaths(projectRoot = process.cwd()): KnowledgePaths {
  const root = resolve(projectRoot, 'knowledge', 'web');
  return {
    root,
    raw: join(root, 'raw'),
    normalized: join(root, 'normalized'),
    catalogs: join(root, 'catalogs'),
    patterns: join(root, 'patterns'),
    training: join(root, 'training'),
  };
}

async function writeJsonAtomic(path: string, value: unknown): Promise<void> {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.tmp`;
  await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  await rename(temporary, path);
}

async function writeJsonGzipAtomic(path: string, value: unknown): Promise<void> {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.tmp`;
  const contents = Buffer.from(`${JSON.stringify(value)}\n`, 'utf8');
  await writeFile(temporary, await gzipAsync(contents, { level: 9 }));
  await rename(temporary, path);
}

async function readJson(path: string): Promise<unknown> {
  const contents = await readFile(path);
  const decoded = path.endsWith('.gz') ? await gunzipAsync(contents) : contents;
  return JSON.parse(decoded.toString('utf8')) as unknown;
}

export async function saveRawRun(paths: KnowledgePaths, run: RawWebCrawlRun): Promise<string> {
  const validated = RawWebCrawlRunSchema.parse(run);
  const path = join(paths.raw, validated.siteId, `${validated.runId}.json.gz`);
  await writeJsonGzipAtomic(path, validated);
  await appendTrainingEvent(paths, {
    schemaVersion: 1,
    type: 'crawl_run',
    createdAt: validated.completedAt,
    siteId: validated.siteId,
    runId: validated.runId,
    states: validated.states.length,
    transitions: validated.transitions.length,
    failures: validated.failures.length,
  });
  return path;
}

export async function loadRawRuns(paths: KnowledgePaths, siteId: string): Promise<RawWebCrawlRun[]> {
  const directory = join(paths.raw, siteId);
  let names: string[];
  try { names = await readdir(directory); } catch { return []; }
  const result: RawWebCrawlRun[] = [];
  const byRunId = new Map<string, RawWebCrawlRun>();
  for (const name of names.filter((value) => value.endsWith('.json') || value.endsWith('.json.gz')).sort()) {
    const run = RawWebCrawlRunSchema.parse(await readJson(join(directory, name)));
    byRunId.set(run.runId, run);
  }
  result.push(...byRunId.values());
  return result.sort((left, right) => left.runId.localeCompare(right.runId));
}

export async function saveCatalog(paths: KnowledgePaths, catalog: WebKnowledgeCatalog): Promise<string> {
  const validated = WebKnowledgeCatalogSchema.parse(catalog);
  const normalizedPath = join(paths.normalized, `${validated.siteId}.json.gz`);
  const catalogPath = join(paths.catalogs, `${validated.siteId}.json`);
  await writeJsonGzipAtomic(normalizedPath, validated);
  await writeJsonAtomic(catalogPath, validated);
  await appendTrainingEvent(paths, {
    schemaVersion: 1,
    type: 'knowledge_build',
    createdAt: validated.generatedAt,
    siteId: validated.siteId,
    sourceRunIds: validated.sourceRunIds,
    semanticModel: validated.semanticModel.model,
    states: validated.states.length,
    transitions: validated.transitions.length,
    routes: validated.routes.length,
  });
  return catalogPath;
}

export async function compressKnowledgeArtifacts(paths: KnowledgePaths): Promise<{ files: number; beforeBytes: number; afterBytes: number }> {
  let files = 0;
  let beforeBytes = 0;
  let afterBytes = 0;
  const directories = [paths.raw, paths.normalized];
  for (const root of directories) {
    const pending = [root];
    while (pending.length > 0) {
      const directory = pending.pop()!;
      let entries;
      try { entries = await readdir(directory, { withFileTypes: true }); } catch { continue; }
      for (const entry of entries) {
        const path = join(directory, entry.name);
        if (entry.isDirectory()) { pending.push(path); continue; }
        if (!entry.isFile() || !entry.name.endsWith('.json')) continue;
        const original = await readFile(path);
        const parsed = JSON.parse(original.toString('utf8')) as unknown;
        const compressedPath = `${path}.gz`;
        await writeJsonGzipAtomic(compressedPath, parsed);
        await readJson(compressedPath);
        const compressed = await readFile(compressedPath);
        await unlink(path);
        files++;
        beforeBytes += original.byteLength;
        afterBytes += compressed.byteLength;
      }
    }
  }
  return { files, beforeBytes, afterBytes };
}

export async function loadCatalogs(paths: KnowledgePaths): Promise<WebKnowledgeCatalog[]> {
  let names: string[];
  try { names = await readdir(paths.catalogs); } catch { return []; }
  const catalogs: WebKnowledgeCatalog[] = [];
  for (const name of names.filter((value) => value.endsWith('.json')).sort()) {
    catalogs.push(WebKnowledgeCatalogSchema.parse(await readJson(join(paths.catalogs, name))));
  }
  return catalogs;
}

export async function rebuildCommonPatterns(paths: KnowledgePaths, catalogs: WebKnowledgeCatalog[]): Promise<CommonWebPatternsFile> {
  const groups = new Map<string, { sites: Set<string>; aliases: Set<string>; scores: number[] }>();
  for (const catalog of catalogs) {
    for (const route of catalog.routes) {
      const sequence = route.steps.map((step) => step.semanticLabel).join('>');
      const key = `${route.intentLabel}|${sequence}`;
      const group = groups.get(key) ?? { sites: new Set(), aliases: new Set(), scores: [] };
      group.sites.add(catalog.siteId);
      route.aliases.forEach((alias) => group.aliases.add(alias));
      group.scores.push(route.successRate);
      groups.set(key, group);
    }
  }
  const patterns: CommonWebPattern[] = [...groups.entries()].map(([key, group]) => {
    const [intentLabel, sequenceText] = key.split('|');
    const siteCount = group.sites.size;
    return {
      id: `common.${intentLabel}.${sequenceText.replace(/[^a-z0-9]+/giu, '.')}`,
      intentLabel,
      aliases: [...group.aliases].sort(),
      sequence: sequenceText.split('>'),
      siteCount,
      confidence: Math.min(0.95, (group.scores.reduce((sum, value) => sum + value, 0) / group.scores.length)
        * (siteCount >= 3 ? 1 : siteCount === 2 ? 0.85 : 0.55)),
    };
  }).sort((left, right) => right.siteCount - left.siteCount || right.confidence - left.confidence);
  const output = CommonWebPatternsFileSchema.parse({ schemaVersion: 1, generatedAt: new Date().toISOString(), patterns });
  await writeJsonAtomic(join(paths.patterns, 'common.json'), output);
  return output;
}

async function appendTrainingEvent(paths: KnowledgePaths, value: unknown): Promise<void> {
  const path = join(paths.training, 'pipeline-events.jsonl');
  await mkdir(dirname(path), { recursive: true });
  await appendFile(path, `${JSON.stringify(value)}\n`, 'utf8');
}
