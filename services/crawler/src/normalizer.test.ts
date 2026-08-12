import { describe, expect, it } from 'vitest';
import { normalizeCrawlRuns } from './normalizer';
import { SemanticLabeler } from './semanticLabeler';
import type { RawWebCrawlRun } from '../../../src/web-knowledge';

const accountLocator = { role: 'link', name: 'Account' };
const ordersLocator = { role: 'link', name: 'Your Orders' };

const run: RawWebCrawlRun = {
  schemaVersion: 1,
  runId: 'run-1',
  siteId: 'shop-example',
  domains: ['shop.example'],
  seedUrl: 'https://shop.example/',
  startedAt: '2026-08-12T00:00:00.000Z',
  completedAt: '2026-08-12T00:01:00.000Z',
  crawlerVersion: '1.0.0',
  locale: 'ko-KR',
  robots: { respected: true },
  limits: { maxStates: 10, maxDepth: 2, maxActionsPerState: 5 },
  failures: [],
  states: [{
    id: 'home-state', url: 'https://shop.example/', title: 'Shop', depth: 0, path: [],
    evidence: ['Shop', 'Account'], observedAt: '2026-08-12T00:00:01.000Z',
    elements: [{
      id: 'account-element', name: 'Account', normalizedName: 'account', role: 'link', tag: 'a',
      area: 'navigation', region: 'top', disabled: false, locator: accountLocator, risk: 'safe',
    }],
  }, {
    id: 'account-state', url: 'https://shop.example/account', title: 'Account', depth: 1,
    path: [{ elementId: 'account-element', name: 'Account', role: 'link', locator: accountLocator }],
    evidence: ['Account', 'Your Orders'], observedAt: '2026-08-12T00:00:10.000Z',
    elements: [{
      id: 'orders-element', name: 'Your Orders', normalizedName: 'your orders', role: 'link', tag: 'a',
      area: 'main', region: 'center', disabled: false, locator: ordersLocator, risk: 'safe',
    }],
  }, {
    id: 'orders-state', url: 'https://shop.example/orders', title: 'Orders', depth: 2,
    path: [
      { elementId: 'account-element', name: 'Account', role: 'link', locator: accountLocator },
      { elementId: 'orders-element', name: 'Your Orders', role: 'link', locator: ordersLocator },
    ],
    evidence: ['Orders'], observedAt: '2026-08-12T00:00:20.000Z', elements: [],
  }],
  transitions: [{
    id: 'transition-account', fromStateId: 'home-state', toStateId: 'account-state',
    action: { elementId: 'account-element', name: 'Account', role: 'link', locator: accountLocator },
    outcome: 'navigation', addedEvidence: ['Your Orders'], removedEvidence: [], observedAt: '2026-08-12T00:00:10.000Z',
  }, {
    id: 'transition-orders', fromStateId: 'account-state', toStateId: 'orders-state',
    action: { elementId: 'orders-element', name: 'Your Orders', role: 'link', locator: ordersLocator },
    outcome: 'navigation', addedEvidence: ['Orders'], removedEvidence: [], observedAt: '2026-08-12T00:00:20.000Z',
  }],
};

describe('web crawl normalizer', () => {
  it('creates semantic transitions and a reusable navigation route offline', async () => {
    const catalog = await normalizeCrawlRuns([run], new SemanticLabeler({}));
    expect(catalog.semanticModel.provider).toBe('rules');
    expect(catalog.transitions.map((item) => item.actionSemanticLabel)).toEqual(['account', 'order_history']);
    expect(catalog.routes.some((route) =>
      route.steps.map((step) => step.semanticLabel).join('>') === 'account>order_history')).toBe(true);
  });
});
