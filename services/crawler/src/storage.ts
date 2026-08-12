import { appendFile, mkdir, readFile, readdir, rename, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import {
  CommonWebPatternsFileSchema,
  RawWebCrawlRunSchema,
  WebKnowledgeCatalogSchema,
  type CommonWebPattern,
  type CommonWebPatternsFile,
  type RawWebCrawlRun,
  type WebKnowledgeCatalog,
} from '../../../src/web-knowledge';

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

export async function saveRawRun(paths: KnowledgePaths, run: RawWebCrawlRun): Promise<string> {
  const validated = RawWebCrawlRunSchema.parse(run);
  const path = join(paths.raw, validated.siteId, `${validated.runId}.json`);
  await writeJsonAtomic(path, validated);
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
  for (const name of names.filter((value) => value.endsWith('.json')).sort()) {
    result.push(RawWebCrawlRunSchema.parse(JSON.parse(await readFile(join(directory, name), 'utf8'))));
  }
  return result;
}

export async function saveCatalog(paths: KnowledgePaths, catalog: WebKnowledgeCatalog): Promise<string> {
  const validated = WebKnowledgeCatalogSchema.parse(catalog);
  const normalizedPath = join(paths.normalized, `${validated.siteId}.json`);
  const catalogPath = join(paths.catalogs, `${validated.siteId}.json`);
  await writeJsonAtomic(normalizedPath, validated);
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

export async function loadCatalogs(paths: KnowledgePaths): Promise<WebKnowledgeCatalog[]> {
  let names: string[];
  try { names = await readdir(paths.catalogs); } catch { return []; }
  const catalogs: WebKnowledgeCatalog[] = [];
  for (const name of names.filter((value) => value.endsWith('.json')).sort()) {
    catalogs.push(WebKnowledgeCatalogSchema.parse(JSON.parse(await readFile(join(paths.catalogs, name), 'utf8'))));
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
