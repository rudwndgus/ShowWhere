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
});
