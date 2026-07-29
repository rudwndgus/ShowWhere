import type { Point } from '../types';

export const ASSISTANT_SIZE = 48;
export const VIEWPORT_MARGIN = 12;

export function clampAssistantPosition(point: Point): Point {
  return {
    x: Math.min(
      Math.max(point.x, VIEWPORT_MARGIN),
      Math.max(VIEWPORT_MARGIN, window.innerWidth - ASSISTANT_SIZE - VIEWPORT_MARGIN),
    ),
    y: Math.min(
      Math.max(point.y, VIEWPORT_MARGIN),
      Math.max(VIEWPORT_MARGIN, window.innerHeight - ASSISTANT_SIZE - VIEWPORT_MARGIN),
    ),
  };
}

export function defaultAssistantPosition(): Point {
  return clampAssistantPosition({
    x: window.innerWidth - ASSISTANT_SIZE - 24,
    y: window.innerHeight - ASSISTANT_SIZE - 24,
  });
}

export interface FloatingPosition extends Point {
  side: 'left' | 'right';
  vertical: 'above' | 'below';
}

export function calculateFloatingPosition(
  anchor: DOMRect,
  width: number,
  height: number,
  gap = 12,
): FloatingPosition {
  const margin = VIEWPORT_MARGIN;
  const leftHasRoom = anchor.left - gap - width >= margin;
  const rightHasRoom = anchor.right + gap + width <= window.innerWidth - margin;
  const side: 'left' | 'right' = leftHasRoom || !rightHasRoom ? 'left' : 'right';
  const idealX = side === 'left' ? anchor.left - width - gap : anchor.right + gap;

  const aboveHasRoom = anchor.top - gap - height >= margin;
  const belowHasRoom = anchor.bottom + gap + height <= window.innerHeight - margin;
  const vertical: 'above' | 'below' = aboveHasRoom || !belowHasRoom ? 'above' : 'below';
  const idealY = vertical === 'above' ? anchor.bottom - height : anchor.top;

  return {
    x: Math.min(Math.max(idealX, margin), Math.max(margin, window.innerWidth - width - margin)),
    y: Math.min(Math.max(idealY, margin), Math.max(margin, window.innerHeight - height - margin)),
    side,
    vertical,
  };
}
