import { describe, expect, it, vi } from 'vitest';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
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
});
