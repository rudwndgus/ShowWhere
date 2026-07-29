import { useEffect, type RefObject } from 'react';

export function useOutsideClick(
  refs: Array<RefObject<HTMLElement | null>>,
  onOutsideClick: () => void,
  enabled: boolean,
) {
  useEffect(() => {
    if (!enabled) return;
    const handlePointerDown = (event: PointerEvent) => {
      const path = event.composedPath();
      if (refs.some((ref) => ref.current && path.includes(ref.current))) return;
      onOutsideClick();
    };
    document.addEventListener('pointerdown', handlePointerDown, true);
    return () => document.removeEventListener('pointerdown', handlePointerDown, true);
  }, [enabled, onOutsideClick, refs]);
}
