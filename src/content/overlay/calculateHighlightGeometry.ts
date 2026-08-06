import { getElementViewportRect } from '../utils/elementVisibility';

export interface HighlightGeometry {
  left: number;
  top: number;
  width: number;
  height: number;
  labelLeft: number;
  labelTop: number;
  borderRadius: number;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), Math.max(minimum, maximum));
}

export function calculateHighlightGeometry(target: HTMLElement): HighlightGeometry {
  const rect = getElementViewportRect(target);
  const padding = 4;
  const edge = 4;
  const viewportWidth = window.innerWidth;
  const viewportHeight = window.innerHeight;
  const maximumWidth = Math.min(viewportWidth - edge * 2, viewportWidth * 0.88);
  const maximumHeight = Math.min(viewportHeight - edge * 2, viewportHeight * 0.48);
  const width = clamp(Math.max(28, rect.width + padding * 2), 28, maximumWidth);
  const height = clamp(Math.max(28, rect.height + padding * 2), 28, maximumHeight);
  const centerX = rect.left + rect.width / 2;
  const centerY = rect.top + rect.height / 2;
  const left = clamp(centerX - width / 2, edge, viewportWidth - width - edge);
  const top = clamp(centerY - height / 2, edge, viewportHeight - height - edge);
  const labelWidth = 154;
  const labelLeft = Math.min(
    Math.max(8, left),
    Math.max(8, viewportWidth - labelWidth - 8),
  );
  const labelTop = top > 48
    ? top - 39
    : Math.min(viewportHeight - 38, top + height + 9);
  const targetRadius = Number.parseFloat(getComputedStyle(target).borderRadius) || 0;
  const borderRadius = clamp(targetRadius + padding, 7, 20);
  return { left, top, width, height, labelLeft, labelTop, borderRadius };
}
