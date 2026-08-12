import 'dotenv/config';
import { resolve } from 'node:path';
import { crawlWebsite, type CrawlOptions } from './crawler';
import { normalizeCrawlRuns } from './normalizer';
import { SemanticLabeler } from './semanticLabeler';
import {
  knowledgePaths,
  loadCatalogs,
  loadRawRuns,
  rebuildCommonPatterns,
  saveCatalog,
  saveRawRun,
} from './storage';
import { siteIdFromUrl } from '../../../src/web-knowledge';

interface CliArguments {
  command: 'crawl' | 'normalize' | 'patterns';
  url?: string;
  site?: string;
  maxStates: number;
  maxDepth: number;
  maxActions: number;
  locale: string;
  headed: boolean;
  channel?: 'msedge' | 'chrome';
  storageState?: string;
  delayMs: number;
  timeoutMs: number;
}

function positiveInteger(value: string | undefined, name: string, fallback: number, minimum = 1): number {
  if (value === undefined) return fallback;
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum) throw new Error(`${name} must be an integer >= ${minimum}.`);
  return parsed;
}

function parseArguments(argv: string[]): CliArguments {
  const argumentsByName = new Map<string, string>();
  const flags = new Set<string>();
  let command: CliArguments['command'] = 'crawl';
  for (let index = 0; index < argv.length; index++) {
    const value = argv[index];
    if (['crawl', 'normalize', 'patterns'].includes(value)) {
      command = value as CliArguments['command'];
      continue;
    }
    if (!value.startsWith('--')) continue;
    const [name, inline] = value.slice(2).split('=', 2);
    if (inline !== undefined) argumentsByName.set(name, inline);
    else if (argv[index + 1] && !argv[index + 1].startsWith('--')) argumentsByName.set(name, argv[++index]);
    else flags.add(name);
  }
  const channel = argumentsByName.get('channel');
  if (channel && channel !== 'msedge' && channel !== 'chrome') throw new Error('--channel must be msedge or chrome.');
  const browserChannel = channel === 'msedge' || channel === 'chrome' ? channel : undefined;
  return {
    command,
    url: argumentsByName.get('url'),
    site: argumentsByName.get('site'),
    maxStates: positiveInteger(argumentsByName.get('max-states'), '--max-states', 30),
    maxDepth: positiveInteger(argumentsByName.get('max-depth'), '--max-depth', 3, 0),
    maxActions: positiveInteger(argumentsByName.get('max-actions'), '--max-actions', 12),
    locale: argumentsByName.get('locale') ?? 'ko-KR',
    headed: flags.has('headed'),
    channel: browserChannel,
    storageState: argumentsByName.get('storage-state'),
    delayMs: positiveInteger(argumentsByName.get('delay-ms'), '--delay-ms', 900, 100),
    timeoutMs: positiveInteger(argumentsByName.get('timeout-ms'), '--timeout-ms', 15_000, 1_000),
  };
}

function usage(): string {
  return [
    'ShowWhere web navigation crawler',
    '',
    '  npm run crawl -- --url https://example.com',
    '  npm run crawl -- --url https://example.com --headed --max-depth 3 --max-states 30',
    '  npm run crawl:normalize -- --site example-com',
    '',
    'Options: --max-states, --max-depth, --max-actions, --locale, --headed,',
    '         --channel msedge|chrome, --storage-state <private-file>, --delay-ms, --timeout-ms',
  ].join('\n');
}

async function buildKnowledge(siteId: string): Promise<void> {
  const paths = knowledgePaths();
  const runs = await loadRawRuns(paths, siteId);
  if (runs.length === 0) throw new Error(`No raw crawl runs found for ${siteId}.`);
  const candidateModels = process.env.CRAWLER_HF_MODEL_CANDIDATES?.split(',').map((value) => value.trim()).filter(Boolean);
  const labeler = new SemanticLabeler({
    token: process.env.HF_TOKEN,
    configuredModel: process.env.CRAWLER_HF_EMBEDDING_MODEL ?? process.env.HF_EMBEDDING_MODEL,
    candidateModels,
  });
  const catalog = await normalizeCrawlRuns(runs, labeler);
  const path = await saveCatalog(paths, catalog);
  const patterns = await rebuildCommonPatterns(paths, await loadCatalogs(paths));
  console.log(`Knowledge: ${path}`);
  console.log(`Normalized ${catalog.states.length} states, ${catalog.transitions.length} transitions, ${catalog.routes.length} routes.`);
  console.log(`Semantic labeling: ${catalog.semanticModel.provider} ${catalog.semanticModel.model}`);
  console.log(`Common patterns: ${patterns.patterns.length}`);
}

async function main(): Promise<void> {
  const args = parseArguments(process.argv.slice(2));
  if (args.command === 'patterns') {
    const paths = knowledgePaths();
    const patterns = await rebuildCommonPatterns(paths, await loadCatalogs(paths));
    console.log(`Rebuilt ${patterns.patterns.length} common patterns.`);
    return;
  }
  if (args.command === 'normalize') {
    const siteId = args.site ?? (args.url ? siteIdFromUrl(args.url) : undefined);
    if (!siteId) throw new Error(`${usage()}\n\n--site or --url is required.`);
    await buildKnowledge(siteId);
    return;
  }
  if (!args.url) throw new Error(`${usage()}\n\n--url is required.`);
  const options: CrawlOptions = {
    url: args.url,
    maxStates: args.maxStates,
    maxDepth: args.maxDepth,
    maxActionsPerState: args.maxActions,
    locale: args.locale,
    headed: args.headed,
    browserChannel: args.channel,
    storageStatePath: args.storageState ? resolve(args.storageState) : undefined,
    navigationTimeoutMs: args.timeoutMs,
    actionDelayMs: args.delayMs,
  };
  console.log(`Crawling ${args.url} (states=${args.maxStates}, depth=${args.maxDepth}, actions=${args.maxActions})`);
  const run = await crawlWebsite(options);
  const path = await saveRawRun(knowledgePaths(), run);
  console.log(`Raw crawl: ${path}`);
  console.log(`Collected ${run.states.length} states, ${run.transitions.length} transitions; ${run.failures.length} failures.`);
  await buildKnowledge(run.siteId);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
