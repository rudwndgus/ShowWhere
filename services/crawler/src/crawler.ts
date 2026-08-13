import { chromium, type Browser, type BrowserContext, type Page } from 'playwright';
import type {
  CrawlPathStep,
  RawWebCrawlRun,
  RawWebElement,
  RawWebState,
  RawWebTransition,
} from '../../../src/web-knowledge';
import { safeUrlForStorage, semanticLabelFromRules, siteIdFromUrl, stableWebId } from '../../../src/web-knowledge';
import { locatePathStep, observeBrowserState } from './browserSnapshot';
import { loadRobotsPolicy, type RobotsPolicy } from './robots';

export interface CrawlOptions {
  url: string;
  maxStates: number;
  maxDepth: number;
  maxActionsPerState: number;
  locale: string;
  headed: boolean;
  browserChannel?: 'msedge' | 'chrome';
  storageStatePath?: string;
  navigationTimeoutMs: number;
  actionDelayMs: number;
}

interface FrontierItem { path: CrawlPathStep[]; depth: number }

function errorDetails(error: unknown): { code: string; message: string } {
  const message = error instanceof Error ? error.message : String(error);
  return {
    code: error instanceof Error ? error.name : 'CrawlError',
    message: message.replace(/(?:hf_|sk-)[A-Za-z0-9_-]+/gu, '[secret]').slice(0, 500),
  };
}

function pathKey(path: CrawlPathStep[]): string {
  return path.map((step) => step.elementId).join('>');
}

function actionPriority(element: RawWebElement): number {
  const areaScore = /navigation|\bnav\b|menu|header|toolbar/u.test(element.area) ? 500 : 0;
  const roleScore = element.role === 'link' ? 400
    : element.role === 'tab' || element.role === 'menuitem' ? 350
      : element.role === 'combobox' ? 300
        : element.role === 'button' ? 200 : 0;
  const riskScore = element.risk === 'safe' ? 100 : 0;
  const semantic = semanticLabelFromRules(element.name);
  const functionalScore = semantic && !['product_detail', 'cart', 'checkout'].includes(semantic) ? 1_200 : 0;
  const href = element.href ?? '';
  const pathScore = /account|order|return|refund|track|address|payment|wallet|prime|member|subscription|security|privacy|language|help|support|contact|device|digital|household|profile|review|notification|communication|gift|registry|password|recommend/iu.test(href)
    ? 900 : 0;
  const productPenalty = /\/dp\/|\/gp\/product\/|\/s\?/iu.test(href) ? 2_000 : 0;
  return areaScore + roleScore + riskScore + functionalScore + pathScore - productPenalty;
}

function canExplore(element: RawWebElement): boolean {
  if (element.disabled || element.risk === 'blocked') return false;
  if (element.risk === 'safe') return true;
  return element.role === 'button' && /navigation|\bnav\b|menu|header|toolbar|dialog/u.test(element.area);
}

async function settle(page: Page, delayMs: number): Promise<void> {
  await page.waitForLoadState('domcontentloaded', { timeout: 4_000 }).catch(() => undefined);
  await page.waitForTimeout(delayMs);
}

async function openAndReplay(
  context: BrowserContext,
  seedUrl: string,
  path: CrawlPathStep[],
  options: CrawlOptions,
): Promise<Page> {
  const page = await context.newPage();
  page.setDefaultTimeout(options.navigationTimeoutMs);
  page.on('dialog', (dialog) => void dialog.dismiss());
  await page.goto(seedUrl, { waitUntil: 'domcontentloaded', timeout: options.navigationTimeoutMs });
  await settle(page, options.actionDelayMs);
  for (const step of path) {
    const locator = await locatePathStep(page, step);
    if (!await locator.isVisible()) throw new Error(`Replay target not visible: ${step.role} ${step.name}`);
    await locator.click({ timeout: options.navigationTimeoutMs });
    await settle(page, options.actionDelayMs);
  }
  return page;
}

async function launchBrowser(options: CrawlOptions): Promise<Browser> {
  try {
    return await chromium.launch({ headless: !options.headed, channel: options.browserChannel });
  } catch (error) {
    if (options.browserChannel) throw error;
    try { return await chromium.launch({ headless: !options.headed, channel: 'msedge' }); }
    catch { throw new Error('Playwright browser is unavailable. Run: npm run crawl:install'); }
  }
}

function allowedTarget(element: RawWebElement, origin: string, robots: RobotsPolicy): boolean {
  if (!element.href) return true;
  try {
    const url = new URL(element.href);
    return url.origin === origin && robots.allows(url.toString());
  } catch { return false; }
}

export async function crawlWebsite(options: CrawlOptions): Promise<RawWebCrawlRun> {
  const seed = new URL(options.url);
  if (!['http:', 'https:'].includes(seed.protocol)) throw new Error('Only http:// and https:// URLs are supported.');
  const startedAt = new Date().toISOString();
  const siteId = siteIdFromUrl(seed.toString());
  const runId = `${startedAt.replace(/[-:.TZ]/gu, '').slice(0, 14)}-${stableWebId('run', crypto.randomUUID()).slice(4, 12)}`;
  const robots = await loadRobotsPolicy(seed.toString());
  if (!robots.allows(seed.toString())) throw new Error(`robots.txt disallows crawling ${seed.pathname}`);
  const browser = await launchBrowser(options);
  const context = await browser.newContext({
    locale: options.locale,
    storageState: options.storageStatePath,
    acceptDownloads: false,
    userAgent: `ShowWhereCrawler/1.0 ${await browser.version()}`,
  });
  const states = new Map<string, RawWebState>();
  const transitions: RawWebTransition[] = [];
  const failures: RawWebCrawlRun['failures'] = [];
  const queuedPaths = new Set<string>(['']);
  const frontier: FrontierItem[] = [{ path: [], depth: 0 }];

  try {
    while (frontier.length > 0 && states.size < options.maxStates) {
      const item = frontier.shift()!;
      let statePage: Page | undefined;
      let state: RawWebState;
      try {
        statePage = await openAndReplay(context, seed.toString(), item.path, options);
        if (new URL(statePage.url()).origin !== seed.origin || !robots.allows(statePage.url())) continue;
        state = await observeBrowserState(statePage, item.depth, item.path);
      } catch (error) {
        const details = errorDetails(error);
        failures.push({ stage: 'replay', path: item.path.map((step) => step.name), ...details });
        continue;
      } finally {
        await statePage?.close().catch(() => undefined);
      }
      if (!states.has(state.id)) states.set(state.id, state);
      if (item.depth >= options.maxDepth) continue;
      const actions = state.elements.filter((element) => canExplore(element)
          && allowedTarget(element, seed.origin, robots))
        .sort((left, right) => actionPriority(right) - actionPriority(left)
          || left.normalizedName.localeCompare(right.normalizedName))
        .slice(0, options.maxActionsPerState);

      for (const element of actions) {
        if (states.size >= options.maxStates) break;
        const action: CrawlPathStep = {
          elementId: element.id,
          name: element.name,
          role: element.role,
          locator: element.locator,
        };
        let page: Page | undefined;
        const observedAt = new Date().toISOString();
        try {
          page = await openAndReplay(context, seed.toString(), item.path, options);
          const popupPromise = page.waitForEvent('popup', { timeout: 1_500 }).catch(() => undefined);
          const locator = await locatePathStep(page, action);
          if (!await locator.isVisible()) throw new Error(`Action target not visible: ${action.name}`);
          await locator.click({ timeout: options.navigationTimeoutMs });
          const popup = await popupPromise;
          const targetPage = popup ?? page;
          await settle(targetPage, options.actionDelayMs);
          const targetOrigin = new URL(targetPage.url()).origin;
          if (targetOrigin !== seed.origin || !robots.allows(targetPage.url())) {
            transitions.push({
              id: stableWebId('transition', state.id, element.id, 'blocked'),
              fromStateId: state.id, action, outcome: 'blocked', addedEvidence: [], removedEvidence: [], observedAt,
            });
            await popup?.close().catch(() => undefined);
            continue;
          }
          const nextPath = [...item.path, action];
          const nextState = await observeBrowserState(targetPage, item.depth + 1, nextPath);
          const beforeEvidence = new Set(state.evidence);
          const afterEvidence = new Set(nextState.evidence);
          const addedEvidence = nextState.evidence.filter((value) => !beforeEvidence.has(value)).slice(0, 30);
          const removedEvidence = state.evidence.filter((value) => !afterEvidence.has(value)).slice(0, 30);
          const outcome = popup ? 'popup'
            : nextState.url !== state.url ? 'navigation'
              : nextState.id !== state.id ? 'state_change' : 'no_change';
          transitions.push({
            id: stableWebId('transition', state.id, element.id, nextState.id, outcome),
            fromStateId: state.id,
            ...(outcome !== 'no_change' ? { toStateId: nextState.id } : {}),
            action, outcome, addedEvidence, removedEvidence, observedAt,
          });
          if (outcome !== 'no_change' && !states.has(nextState.id) && states.size < options.maxStates) {
            states.set(nextState.id, nextState);
            const key = pathKey(nextPath);
            if (item.depth + 1 < options.maxDepth && !queuedPaths.has(key)) {
              queuedPaths.add(key);
              frontier.push({ path: nextPath, depth: item.depth + 1 });
            }
          }
          await popup?.close().catch(() => undefined);
        } catch (error) {
          const details = errorDetails(error);
          failures.push({ stage: 'action', path: [...item.path.map((step) => step.name), action.name], ...details });
          transitions.push({
            id: stableWebId('transition', state.id, element.id, 'failed'),
            fromStateId: state.id, action, outcome: 'failed', addedEvidence: [], removedEvidence: [],
            errorCode: details.code, observedAt,
          });
        } finally {
          await page?.close().catch(() => undefined);
        }
      }
    }
  } finally {
    await context.close();
    await browser.close();
  }

  return {
    schemaVersion: 1,
    runId,
    siteId,
    domains: [seed.hostname.toLowerCase()],
    seedUrl: safeUrlForStorage(seed.toString()),
    startedAt,
    completedAt: new Date().toISOString(),
    crawlerVersion: '1.0.0',
    locale: options.locale,
    robots: { respected: robots.respected, ...(robots.source ? { source: robots.source } : {}) },
    limits: {
      maxStates: options.maxStates,
      maxDepth: options.maxDepth,
      maxActionsPerState: options.maxActionsPerState,
    },
    states: [...states.values()],
    transitions,
    failures,
  };
}
