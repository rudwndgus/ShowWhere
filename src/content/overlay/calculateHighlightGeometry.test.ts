import { describe, expect, it } from 'vitest';
import { getViewportScrollDirection } from './calculateHighlightGeometry';

describe('getViewportScrollDirection', () => {
  it('points down for a target below the viewport', () => {
    expect(getViewportScrollDirection({ top: 900, bottom: 940 }, 800)).toBe('down');
  });

  it('points up for a target above the viewport', () => {
    expect(getViewportScrollDirection({ top: -80, bottom: -20 }, 800)).toBe('up');
  });

  it('returns no direction once the target is visible', () => {
    expect(getViewportScrollDirection({ top: 200, bottom: 240 }, 800)).toBeNull();
  });
});
