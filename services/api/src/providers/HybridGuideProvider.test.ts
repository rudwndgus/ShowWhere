import { describe, expect, it, vi } from 'vitest';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import type { WebKnowledgeCatalog } from '../../../../src/web-knowledge';
import { guideRequestFixture } from '../testFixtures';
import { HybridGuideProvider } from './HybridGuideProvider';

describe('hybrid guide provider', () => {
  it('uses the local route before calling GPT', async () => {
    const fallback: AiProvider = { decideNextAction: vi.fn() };
    const provider = new HybridGuideProvider(fallback);
    const decision = await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: 'Open Settings', goal: 'Open Settings' },
    }) as { targetId?: string };
    expect(decision.targetId).toBe('candidate-settings');
    expect(fallback.decideNextAction).not.toHaveBeenCalled();
  });

  it('falls back to GPT when local evidence is insufficient', async () => {
    const expected = { status: 'needs_clarification', action: 'ask_user', message: '질문', confidence: 0.5 };
    const fallback: AiProvider = { decideNextAction: vi.fn().mockResolvedValue(expected) };
    const provider = new HybridGuideProvider(fallback);
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: '무언가 도와줘', goal: '무언가 도와줘' },
    });
    expect(result).toBe(expected);
    expect(fallback.decideNextAction).toHaveBeenCalledOnce();
  });

  it('routes known Windows settings before local, Hugging Face, or GPT', async () => {
    const fallback: AiProvider = { decideNextAction: vi.fn() };
    const provider = new HybridGuideProvider(fallback);
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: '프린터 설정 어디야?', goal: '프린터 설정 어디야?' },
      candidates: [{ ...guideRequestFixture.candidates[0], id: 'printers', label: '프린터 및 스캐너' }],
    }) as { targetId?: string };
    expect(result.targetId).toBe('printers');
    expect(fallback.decideNextAction).not.toHaveBeenCalled();
  });

  it('does not let a stalled Hugging Face request delay an available GPT answer', async () => {
    const expected = { status: 'needs_clarification', action: 'ask_user', message: '무엇을 찾을까요?', confidence: 0.8 };
    const fallback: AiProvider = { decideNextAction: vi.fn().mockResolvedValue(expected) };
    const stalledEmbeddings = { embed: vi.fn(() => new Promise<number[][]>(() => {})) };
    const provider = new HybridGuideProvider(fallback, stalledEmbeddings);
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: '그거 해줘', goal: '그거 해줘' },
    });
    expect(result).toBe(expected);
  });

  it('can return a confident HF target while GPT is still reasoning', async () => {
    const fallback: AiProvider = { decideNextAction: vi.fn(() => new Promise(() => {})) };
    const embeddings = { embed: vi.fn().mockResolvedValue([[1, 0], [1, 0]]) };
    const provider = new HybridGuideProvider(fallback, embeddings);
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      session: { ...guideRequestFixture.session, originalUserMessage: '그 기능', goal: '그 기능' },
    }) as { targetId?: string };
    expect(result.targetId).toBe('candidate-settings');
  });

  it('prefers collected website knowledge over a possible Windows match', async () => {
    const fallback: AiProvider = { decideNextAction: vi.fn() };
    const catalog: WebKnowledgeCatalog = {
      schemaVersion: 1,
      siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'],
      generatedAt: '2026-08-13T00:00:00.000Z', sourceRunIds: ['run-1'],
      semanticModel: { provider: 'rules', model: 'taxonomy', comparedModels: [] },
      transitions: [], routes: [],
      states: [{
        id: 'home', urlPatterns: ['/'], titlePatterns: ['Amazon.com'], evidence: [],
        elements: [{
          id: 'known-login', names: ['Hello, sign in Account & Lists'],
          normalizedNames: ['hello sign in account lists'], role: 'link', areas: ['Primary'], regions: ['top'],
          semanticLabel: 'login', semanticConfidence: 0.96, semanticSource: 'rule',
          locatorHints: [{ name: 'Hello, sign in Account & Lists' }], risk: 'safe',
        }],
      }],
    };
    const provider = new HybridGuideProvider(fallback, undefined, undefined, { catalogs: [catalog], patterns: [] });
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Amazon.com', url: 'https://www.amazon.com/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '아마존에서 로그인 어디서 해?', goal: '아마존에서 로그인 어디서 해?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'amazon-login', label: 'Hello, sign in Account & Lists', role: 'link',
        attributes: { sourceScope: 'browser_content' },
      }, {
        ...guideRequestFixture.candidates[0], id: 'settings', label: '설정',
        attributes: { sourceScope: 'windows_taskbar' },
      }],
    }) as { targetId?: string };
    expect(result.targetId).toBe('amazon-login');
    expect(fallback.decideNextAction).not.toHaveBeenCalled();
  });

  it('keeps a Windows printer intent while Chrome is showing Amazon', async () => {
    const fallback: AiProvider = { decideNextAction: vi.fn() };
    const provider = new HybridGuideProvider(fallback, undefined, undefined, {
      catalogs: [{
        schemaVersion: 1, siteId: 'amazon-com', displayName: 'Amazon Com', domains: ['www.amazon.com'],
        generatedAt: '2026-08-13T00:00:00.000Z', sourceRunIds: ['run-1'],
        semanticModel: { provider: 'rules', model: 'taxonomy', comparedModels: [] },
        states: [], transitions: [], routes: [],
      }],
      patterns: [],
    });
    const result = await provider.decideNextAction({
      ...guideRequestFixture,
      context: { ...guideRequestFixture.context, applicationName: 'chrome', windowTitle: 'Amazon.com', url: 'https://www.amazon.com/' },
      session: { ...guideRequestFixture.session, originalUserMessage: '프린터 설정은 어디서 해?', goal: '프린터 설정은 어디서 해?' },
      candidates: [{
        ...guideRequestFixture.candidates[0], id: 'settings', label: '설정',
        attributes: { sourceScope: 'windows_taskbar' },
      }, {
        ...guideRequestFixture.candidates[0], id: 'amazon-search', label: 'Search Amazon', role: 'searchbox',
        attributes: { sourceScope: 'browser_content' },
      }],
    }) as { targetId?: string };
    expect(result.targetId).toBe('settings');
    expect(fallback.decideNextAction).not.toHaveBeenCalled();
  });
});
