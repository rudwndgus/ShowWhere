import { describe, expect, it, vi } from 'vitest';
import { guideRequestFixture } from '../../../services/api/src/testFixtures';
import { GuideServiceError, HttpGuideClient } from './GuideClient';

describe('HttpGuideClient', () => {
  it('sends a normalized GuideRequest and accepts a known target ID', async () => {
    const sendMessage = vi.fn(async (_message: unknown) => ({
      ok: true as const,
      decision: {
        status: 'in_progress',
        action: 'highlight',
        targetId: 'candidate-settings',
        message: 'Select Settings.',
        confidence: 0.9,
      },
    }));
    const client = new HttpGuideClient({
      sendMessage,
    });

    const decision = await client.decideNextAction(guideRequestFixture);
    expect(decision.targetId).toBe('candidate-settings');
    expect(sendMessage.mock.calls[0][0]).toEqual({
      type: 'SHOWWHERE_GUIDE_REQUEST',
      request: guideRequestFixture,
    });
  });

  it('rejects a validly shaped decision containing an unknown target ID', async () => {
    const client = new HttpGuideClient({
      sendMessage: async () => ({
        ok: true,
        decision: {
          status: 'in_progress',
          action: 'highlight',
          targetId: 'invented-target',
          message: 'Select this.',
          confidence: 0.99,
        },
      }),
    });

    await expect(client.decideNextAction(guideRequestFixture)).rejects.toBeInstanceOf(GuideServiceError);
  });

  it('does not expose malformed server responses to the UI', async () => {
    const client = new HttpGuideClient({
      sendMessage: async () => ({ ok: false }),
    });

    await expect(client.decideNextAction(guideRequestFixture)).rejects.toThrow(
      '지금은 안내 서비스에 연결할 수 없어요. 잠시 후 다시 시도해 주세요.',
    );
  });
});
