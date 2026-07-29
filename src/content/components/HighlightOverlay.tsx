import { useEffect, useState } from 'react';
import { getElementViewportRect } from '../utils/elementVisibility';

interface HighlightOverlayProps {
  target: HTMLElement | null;
}

interface OverlayGeometry {
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

function calculateGeometry(target: HTMLElement): OverlayGeometry {
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

export function HighlightOverlay({ target }: HighlightOverlayProps) {
  const [geometry, setGeometry] = useState<OverlayGeometry | null>(null);

  useEffect(() => {
    if (!target || !target.isConnected) {
      setGeometry(null);
      return;
    }

    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        if (target.isConnected) setGeometry(calculateGeometry(target));
      });
    };
    update();
    window.addEventListener('scroll', update, true);
    window.addEventListener('resize', update);
    const targetWindow = target.ownerDocument.defaultView;
    if (targetWindow && targetWindow !== window) {
      targetWindow.addEventListener('scroll', update, true);
      targetWindow.addEventListener('resize', update);
    }
    const observer = new ResizeObserver(update);
    observer.observe(target);
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      window.removeEventListener('scroll', update, true);
      window.removeEventListener('resize', update);
      if (targetWindow && targetWindow !== window) {
        targetWindow.removeEventListener('scroll', update, true);
        targetWindow.removeEventListener('resize', update);
      }
    };
  }, [target]);

  if (!geometry) return null;
  return (
    <div className="sw-guide-layer" aria-hidden="true">
      <div
        className="sw-highlight"
        style={{
          left: geometry.left,
          top: geometry.top,
          width: geometry.width,
          height: geometry.height,
          borderRadius: geometry.borderRadius,
        }}
      />
      <div
        className="sw-guide-label"
        style={{ left: geometry.labelLeft, top: geometry.labelTop }}
      >
        여기를 눌러보세요!
      </div>
    </div>
  );
}
