import { describe, expect, it } from 'vitest';
import { guideRequestFixture } from '../testFixtures';
import { createUiTarsMessages, isUiTarsModel, parseUiTarsDecision } from './uiTarsAdapter';

const screenshotRequest = {
  ...guideRequestFixture,
  screenshot: 'data:image/jpeg;base64,abc',
  screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
};

describe('uiTarsAdapter', () => {
  it('recognizes UI-TARS model identifiers', () => {
    expect(isUiTarsModel('ByteDance-Seed/UI-TARS-1.5-7B')).toBe(true);
    expect(isUiTarsModel('Qwen/Qwen3-VL-30B-A3B-Instruct')).toBe(false);
  });

  it('uses the native action protocol and complete screenshot', () => {
    const messages = createUiTarsMessages(screenshotRequest);
    expect(messages[0].content).toContain("Action: click(start_box='(x,y)')");
    expect(messages[1].content[1]).toEqual({
      type: 'image_url',
      image_url: { url: screenshotRequest.screenshot },
    });
  });

  it('parses a normalized start_box point', () => {
    const decision = parseUiTarsDecision(
      "Thought: The Settings icon is visible.\nAction: click(start_box='(554,148)')",
      screenshotRequest,
    );
    expect(decision).toMatchObject({
      action: 'highlight_visual',
      visualTarget: { x: 0.534, y: 0.118, width: 0.04, height: 0.06 },
    });
  });

  it('parses the point tag used by newer UI-TARS prompts', () => {
    const decision = parseUiTarsDecision(
      "Thought: click Settings\nAction: click(point='<point>554 148</point>')",
      screenshotRequest,
    );
    expect(decision?.visualTarget).toMatchObject({ x: 0.534, y: 0.118 });
  });

  it('parses a bounding box and physical-pixel fallback coordinates', () => {
    const decision = parseUiTarsDecision(
      "Thought: click Settings\nAction: click(start_box='[1056,216,1152,324]')",
      screenshotRequest,
    );
    const target = decision?.visualTarget;
    expect(target).toBeDefined();
    expect(target?.x).toBeCloseTo(0.55);
    expect(target?.y).toBeCloseTo(0.2);
    expect(target?.width).toBeCloseTo(0.05);
    expect(target?.height).toBeCloseTo(0.1);
  });

  it('does not invent a target when UI-TARS asks the user', () => {
    expect(parseUiTarsDecision(
      'Thought: no safe target is visible\nAction: call_user()',
      screenshotRequest,
    )).toBeUndefined();
  });
});
