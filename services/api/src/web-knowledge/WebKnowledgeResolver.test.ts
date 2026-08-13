import { describe, expect, it } from 'vitest';
import type { WebKnowledgeCatalog } from '../../../../src/web-knowledge';
import type { GuideRequest } from '../../../../src/contracts';
import { guideRequestFixture } from '../testFixtures';
import { resolveWebKnowledge } from './WebKnowledgeResolver';

const catalog: WebKnowledgeCatalog = {
  schemaVersion: 1,
  siteId: 'shop-example',
  displayName: 'Shop Example',
  domains: ['shop.example'],
  generatedAt: '2026-08-12T00:00:00.000Z',
  sourceRunIds: ['run-1'],
  semanticModel: { provider: 'rules', model: 'taxonomy', comparedModels: [] },
  states: [], transitions: [],
  routes: [{
    id: 'tracking-route',
    intentLabel: 'order_tracking',
    aliases: ['배송 조회', '내 배송'],
    successRate: 1,
    steps: [{ fromStateId: 'home', toStateId: 'account', semanticLabel: 'account', names: ['Account'], roles: ['link'] },
      { fromStateId: 'account', semanticLabel: 'order_history', names: ['Your Orders'], roles: ['link'] }],
  }],
};

describe('web knowledge resolver', () => {
  it('selects the known next website control without calling a model', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'msedge', windowTitle: 'Shop Example', url: 'https://shop.example/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '내 배송 어디까지 왔어?', goal: '내 배송 어디까지 왔어?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'account', label: 'Account', role: 'link',
        attributes: { sourceScope: 'browser_content', containerLabel: 'Navigation' },
      }, {
        ...guideRequestFixture.candidates[0], id: 'search', label: 'Search', role: 'button',
        attributes: { sourceScope: 'browser_content' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [catalog], patterns: [] })?.targetId).toBe('account');
  });

  it('does not reuse a site catalog on a different domain', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'msedge', url: 'https://other.example/' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'account', label: 'Account', role: 'link',
        attributes: { sourceScope: 'browser_content' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [catalog], patterns: [] })).toBeUndefined();
  });

  it('uses a normalized login element even when the crawl has no login route', () => {
    const amazonCatalog: WebKnowledgeCatalog = {
      ...catalog,
      siteId: 'amazon-com',
      displayName: 'Amazon Com',
      domains: ['www.amazon.com'],
      routes: [],
      states: [{
        id: 'amazon-home',
        urlPatterns: ['/'],
        titlePatterns: ['Amazon.com'],
        evidence: ['Hello, sign in Account & Lists'],
        elements: [{
          id: 'amazon-sign-in',
          names: ['Hello, sign in Account & Lists'],
          normalizedNames: ['hello sign in account lists'],
          role: 'link',
          areas: ['Primary'],
          regions: ['top'],
          semanticLabel: 'login',
          semanticConfidence: 0.96,
          semanticSource: 'rule',
          locatorHints: [{ role: 'link', name: 'Hello, sign in Account & Lists', href: 'https://www.amazon.com/ap/signin' }],
          risk: 'safe',
        }],
      }],
    };
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Amazon.com', url: 'https://www.amazon.com/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?' },
      candidates: [{
        ...guideRequestFixture.candidates[0],
        id: 'live-amazon-sign-in',
        label: 'Hello, sign in Account & Lists',
        role: 'link',
        attributes: { sourceScope: 'browser_content', containerLabel: 'Amazon.com' },
      }, {
        ...guideRequestFixture.candidates[0],
        id: 'taskbar-settings',
        label: '설정',
        role: 'button',
        attributes: { sourceScope: 'windows_taskbar' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [amazonCatalog], patterns: [] })?.targetId)
      .toBe('live-amazon-sign-in');
  });

  it('can identify an explicitly named site when the browser hides its URL', () => {
    const amazonCatalog: WebKnowledgeCatalog = {
      ...catalog,
      siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'], routes: [],
      states: [{
        id: 'amazon-home', urlPatterns: ['/'], titlePatterns: [], evidence: [],
        elements: [{
          id: 'amazon-sign-in', names: ['Sign in'], normalizedNames: ['sign in'], role: 'link',
          areas: ['Primary'], regions: ['top'], semanticLabel: 'login', semanticConfidence: 0.96,
          semanticSource: 'rule', locatorHints: [{ name: 'Sign in' }], risk: 'safe',
        }],
      }],
    };
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Google Chrome', url: undefined },
      session: { ...guideRequestFixture.session, originalUserMessage: '아마존 로그인 어디야?', goal: '아마존 로그인 어디야?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'sign-in', label: 'Sign in', role: 'link',
        attributes: { sourceScope: 'browser_content' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [amazonCatalog], patterns: [] })?.targetId).toBe('sign-in');
  });

  it('prefers the canonical Amazon login over location and address sign-in shortcuts', () => {
    const amazonCatalog: WebKnowledgeCatalog = {
      ...catalog,
      siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'], routes: [],
      states: [{
        id: 'amazon-home', urlPatterns: ['/'], titlePatterns: ['Amazon.com'], evidence: [],
        elements: [{
          id: 'direct-login', names: ['Sign in'], normalizedNames: ['sign in'], role: 'link',
          areas: ['Primary'], regions: ['top'], semanticLabel: 'login', semanticConfidence: 0.96,
          semanticSource: 'rule', locatorHints: [{ href: 'https://www.amazon.com/ap/signin' }], risk: 'safe',
        }, {
          id: 'location-login', names: ['Sign in to update your location'],
          normalizedNames: ['sign in to update your location'], role: 'button', areas: ['Choose your location'],
          regions: ['center'], semanticLabel: 'login', semanticConfidence: 0.96,
          semanticSource: 'rule', locatorHints: [], risk: 'unknown',
        }],
      }],
    };
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Amazon.com', url: 'https://www.amazon.com/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'delivery-location',
        label: 'Delivering to Lyndhurst Location', description: 'Sign in to see your addresses', role: 'button',
        attributes: { sourceScope: 'browser_content' },
      }, {
        ...guideRequestFixture.candidates[0], id: 'address-login', label: 'Sign in to see your addresses', role: 'button',
        attributes: { sourceScope: 'browser_content' },
      }, {
        ...guideRequestFixture.candidates[0], id: 'header-login', label: 'Hello, sign in Account & Lists', role: 'link',
        attributes: { sourceScope: 'browser_content', automationId: 'nav-link-accountList' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [amazonCatalog], patterns: [] })?.targetId).toBe('header-login');
  });

  it('refuses a contextual address login when the canonical login control is missing', () => {
    const contextualCatalog: WebKnowledgeCatalog = {
      ...catalog,
      siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'], routes: [],
      states: [{
        id: 'amazon-home', urlPatterns: ['/'], titlePatterns: ['Amazon.com'], evidence: [],
        elements: [{
          id: 'location-login', names: ['Sign in to update your location'],
          normalizedNames: ['sign in to update your location'], role: 'button', areas: ['Choose your location'],
          regions: ['center'], semanticLabel: 'login', semanticConfidence: 0.96,
          semanticSource: 'rule', locatorHints: [], risk: 'unknown',
        }],
      }],
    };
    const request: GuideRequest = {
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Amazon.com', url: 'https://www.amazon.com/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'address-login', label: 'Sign in to see your addresses', role: 'button',
        attributes: { sourceScope: 'browser_content' },
      }],
    };
    expect(resolveWebKnowledge(request, { catalogs: [contextualCatalog], patterns: [] })).toBeUndefined();
  });
});
