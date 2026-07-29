import { useRef, type PointerEvent as ReactPointerEvent, type RefObject } from 'react';
import type { AssistantState, Point } from '../types';

interface AssistantButtonProps {
  buttonRef: RefObject<HTMLButtonElement | null>;
  position: Point;
  state: AssistantState;
  open: boolean;
  onActivate: () => void;
  onHoverChange: (hovered: boolean) => void;
  onDrag: (position: Point, finished: boolean) => void;
}

const DRAG_THRESHOLD = 7;

export function AssistantButton({
  buttonRef,
  position,
  state,
  open,
  onActivate,
  onHoverChange,
  onDrag,
}: AssistantButtonProps) {
  const pointer = useRef<{
    id: number;
    startX: number;
    startY: number;
    originX: number;
    originY: number;
    dragged: boolean;
  } | null>(null);

  const handlePointerDown = (event: ReactPointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    pointer.current = {
      id: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      originX: position.x,
      originY: position.y,
      dragged: false,
    };
  };

  const handlePointerMove = (event: ReactPointerEvent<HTMLButtonElement>) => {
    const drag = pointer.current;
    if (!drag || drag.id !== event.pointerId) return;
    const deltaX = event.clientX - drag.startX;
    const deltaY = event.clientY - drag.startY;
    if (!drag.dragged && Math.hypot(deltaX, deltaY) >= DRAG_THRESHOLD) drag.dragged = true;
    if (drag.dragged) onDrag({ x: drag.originX + deltaX, y: drag.originY + deltaY }, false);
  };

  const handlePointerUp = (event: ReactPointerEvent<HTMLButtonElement>) => {
    const drag = pointer.current;
    if (!drag || drag.id !== event.pointerId) return;
    pointer.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    if (drag.dragged) {
      const deltaX = event.clientX - drag.startX;
      const deltaY = event.clientY - drag.startY;
      onDrag({ x: drag.originX + deltaX, y: drag.originY + deltaY }, true);
    } else {
      onActivate();
    }
  };

  return (
    <button
      ref={buttonRef}
      type="button"
      className="sw-assistant"
      style={{ left: position.x, top: position.y }}
      data-state={state}
      aria-label={open ? 'ShowWhere 도우미 닫기' : 'ShowWhere 도우미 열기'}
      aria-expanded={open}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={() => {
        pointer.current = null;
      }}
      onClick={(event) => {
        if (event.detail === 0) onActivate();
      }}
      onMouseEnter={() => onHoverChange(true)}
      onMouseLeave={() => onHoverChange(false)}
    >
      <span className="sw-question" aria-hidden="true">?</span>
    </button>
  );
}
