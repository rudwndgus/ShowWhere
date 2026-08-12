import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, normalize, resolve } from 'node:path';
import { crawlWebsite } from './crawler';
import { normalizeCrawlRuns } from './normalizer';
import { SemanticLabeler } from './semanticLabeler';

const fixtureRoot = resolve(process.cwd(), 'fixtures', 'web-crawler-site');
const contentTypes: Record<string, string> = { '.html': 'text/html; charset=utf-8', '.txt': 'text/plain; charset=utf-8' };
const server = createServer(async (request, response) => {
  const pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://127.0.0.1').pathname);
  const relative = normalize(pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, ''));
  const path = resolve(fixtureRoot, relative);
  if (!path.startsWith(fixtureRoot)) { response.writeHead(403).end(); return; }
  try {
    const contents = await readFile(path);
    response.writeHead(200, { 'Content-Type': contentTypes[extname(path)] ?? 'application/octet-stream' });
    response.end(contents);
  } catch { response.writeHead(404).end(); }
});

try {
  await new Promise<void>((resolveListening) => server.listen(0, '127.0.0.1', resolveListening));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Smoke server did not start.');
  const run = await crawlWebsite({
    url: `http://127.0.0.1:${address.port}/index.html`,
    maxStates: 12,
    maxDepth: 4,
    maxActionsPerState: 8,
    locale: 'en-US',
    headed: false,
    navigationTimeoutMs: 8_000,
    actionDelayMs: 150,
  });
  const catalog = await normalizeCrawlRuns([run], new SemanticLabeler({}));
  const trackingRoutes = catalog.routes.filter((route) => route.intentLabel === 'order_tracking').length;
  console.log(JSON.stringify({
    states: run.states.length,
    transitions: run.transitions.length,
    failures: run.failures.length,
    ...(run.failures.length > 0 ? { failureDetails: run.failures } : {}),
    routes: catalog.routes.length,
    trackingRoutes,
  }));
  if (run.states.length < 4 || run.transitions.length < 3 || trackingRoutes < 1)
    throw new Error('Crawler smoke test did not discover the expected navigation graph.');
} finally {
  await new Promise<void>((resolveClosed) => server.close(() => resolveClosed()));
}
