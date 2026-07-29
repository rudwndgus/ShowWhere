import { useCallback, useEffect, useState } from 'react';
import type { Point } from '../types';
import { clampAssistantPosition, defaultAssistantPosition } from '../utils/positioning';

const STORAGE_KEY = 'showwhereAssistantPosition';

export function useAssistantPosition() {
  const [position, setPositionState] = useState<Point>(() => defaultAssistantPosition());

  useEffect(() => {
    let active = true;
    void chrome.storage.local.get(STORAGE_KEY).then((result) => {
      const stored = result[STORAGE_KEY] as Point | undefined;
      if (active && stored && Number.isFinite(stored.x) && Number.isFinite(stored.y)) {
        setPositionState(clampAssistantPosition(stored));
      }
    });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    const keepOnScreen = () => {
      setPositionState((current) => {
        const next = clampAssistantPosition(current);
        void chrome.storage.local.set({ [STORAGE_KEY]: next });
        return next;
      });
    };
    window.addEventListener('resize', keepOnScreen);
    return () => window.removeEventListener('resize', keepOnScreen);
  }, []);

  const setPosition = useCallback((next: Point, persist = false) => {
    const clamped = clampAssistantPosition(next);
    setPositionState(clamped);
    if (persist) void chrome.storage.local.set({ [STORAGE_KEY]: clamped });
  }, []);

  return { position, setPosition };
}
