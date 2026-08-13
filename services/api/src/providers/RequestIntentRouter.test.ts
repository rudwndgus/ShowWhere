import { describe, expect, it } from 'vitest';
import type { GuideRequest } from '../../../../src/contracts';
import type { WebKnowledgeCatalog } from '../../../../src/web-knowledge';
import { guideRequestFixture } from '../testFixtures';
import { classifyRequestIntent } from './RequestIntentRouter';

const amazon: WebKnowledgeCatalog = {
  schemaVersion: 1,
  siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'],
  generatedAt: '2026-08-13T00:00:00.000Z', sourceRunIds: ['run-1'],
  semanticModel: { provider: 'rules', model: 'taxonomy', comparedModels: [] },
  states: [], transitions: [], routes: [],
};

function request(goal: string): GuideRequest {
  return {
    ...guideRequestFixture,
    context: {
      ...guideRequestFixture.context,
      applicationName: 'chrome',
      windowTitle: 'Amazon.com',
      url: 'https://www.amazon.com/',
    },
    session: { ...guideRequestFixture.session, originalUserMessage: goal, goal },
  };
}

describe('request intent router', () => {
  it('classifies a printer question as Windows even while Amazon is foreground', () => {
    expect(classifyRequestIntent(request('프린터 설정은 어디서 해?'), { catalogs: [amazon], patterns: [] }))
      .toMatchObject({ domain: 'windows', reason: 'windows_intent' });
  });

  it('classifies an explicitly named Amazon action as web before screen grounding', () => {
    expect(classifyRequestIntent(request('아마존에서 로그인 어디서 해?'), { catalogs: [amazon], patterns: [] }))
      .toMatchObject({ domain: 'web', reason: 'explicit_site', siteId: 'amazon-com' });
  });

  it('does not let the active website decide an otherwise ambiguous intent', () => {
    expect(classifyRequestIntent(request('로그인은 어디서 해?'), { catalogs: [amazon], patterns: [] }))
      .toMatchObject({ domain: 'unknown', reason: 'ambiguous' });
  });
});
