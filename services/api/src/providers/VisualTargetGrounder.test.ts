import { describe, expect, it } from 'vitest';
import { guideRequestFixture } from '../testFixtures';
import { groundVisualDecision, matchVisualTargetToCandidate } from './VisualTargetGrounder';

const screenshotBounds = { x: 0, y: 0, width: 1920, height: 1080 };

describe('VisualTargetGrounder', () => {
  it('snaps an approximate visual result to the exact clickable web candidate bounds', () => {
    const exact = {
      ...guideRequestFixture.candidates[0],
      id: 'amazon-account', label: 'Hello, sign in Account & Lists', role: 'button',
      bounds: { x: 1605, y: 18, width: 176, height: 52 },
      attributes: { sourceScope: 'browser_content', processName: 'chrome' },
    };
    const request = { ...guideRequestFixture, screenshotBounds, candidates: [exact] };
    const visualTarget = { x: 0.80, y: 0.01, width: 0.14, height: 0.08, label: 'Hello, sign in Account & Lists' };

    expect(matchVisualTargetToCandidate(request, visualTarget)?.id).toBe('amazon-account');
    expect(groundVisualDecision(request, {
      status: 'in_progress', action: 'highlight_visual', message: '계정을 누르세요.', confidence: 0.96,
      visualTarget,
    })).toMatchObject({ action: 'highlight', targetId: 'amazon-account' });
  });

  it('uses position to distinguish repeated labels without selecting a nearby wrong shortcut', () => {
    const candidates = [
      { ...guideRequestFixture.candidates[0], id: 'header-login', label: 'Sign in', bounds: { x: 1600, y: 20, width: 130, height: 45 }, attributes: { sourceScope: 'browser_content' } },
      { ...guideRequestFixture.candidates[0], id: 'address-login', label: 'Sign in', bounds: { x: 700, y: 500, width: 300, height: 38 }, attributes: { sourceScope: 'browser_content' } },
    ];
    const request = { ...guideRequestFixture, screenshotBounds, candidates };

    expect(matchVisualTargetToCandidate(request, {
      x: 0.82, y: 0.01, width: 0.10, height: 0.06, label: 'Sign in',
    })?.id).toBe('header-login');
  });

  it('keeps a visual box when no accessibility candidate safely matches its label', () => {
    const request = { ...guideRequestFixture, screenshotBounds };
    const decision = {
      status: 'in_progress' as const, action: 'highlight_visual' as const,
      message: '아이콘을 누르세요.', confidence: 0.9,
      visualTarget: { x: 0.4, y: 0.4, width: 0.05, height: 0.05, label: 'Unexposed canvas icon' },
    };

    expect(groundVisualDecision(request, decision)).toEqual(decision);
  });
});
