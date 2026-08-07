import { useEffect, useState } from 'react';
import {
  calculateHighlightGeometry,
  getViewportScrollDirection,
  type HighlightGeometry,
} from '../overlay/calculateHighlightGeometry';
import { getElementViewportRect } from '../utils/elementVisibility';

interface HighlightOverlayProps {
  target: HTMLElement | null;
}

export function HighlightOverlay({ target }: HighlightOverlayProps) {
  const [geometry, setGeometry] = useState<HighlightGeometry | null>(null);
  const [scrollDirection, setScrollDirection] = useState<'up' | 'down' | null>(null);

  useEffect(() => {
    if (!target || !target.isConnected) {
      setGeometry(null);
      setScrollDirection(null);
      return;
    }

    let frame = 0;
    const update = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        if (!target.isConnected) return;
        const rect = getElementViewportRect(target);
        const direction = getViewportScrollDirection(rect);
        if (direction === 'down') {
          setGeometry(null);
          setScrollDirection('down');
        } else if (direction === 'up') {
          setGeometry(null);
          setScrollDirection('up');
        } else {
          setScrollDirection(null);
          setGeometry(calculateHighlightGeometry(target));
        }
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

  if (scrollDirection) {
    return (
      <div className="sw-guide-layer" aria-hidden="true">
        <div className={`sw-scroll-indicator sw-scroll-indicator--${scrollDirection}`}>
          <span>{scrollDirection === 'down' ? '↓' : '↑'}</span>
          {scrollDirection === 'down' ? '아래로 스크롤하세요' : '위로 스크롤하세요'}
        </div>
      </div>
    );
  }
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
