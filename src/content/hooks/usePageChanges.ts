import { useEffect } from 'react';

export function usePageChanges(onPageChange: () => void) {
  useEffect(() => {
    let timer: number | undefined;
    let lastUrl = location.href;
    const schedule = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(onPageChange, 160);
    };

    const originalPushState = history.pushState;
    const originalReplaceState = history.replaceState;
    history.pushState = function (...args) {
      originalPushState.apply(this, args);
      schedule();
    };
    history.replaceState = function (...args) {
      originalReplaceState.apply(this, args);
      schedule();
    };

    window.addEventListener('popstate', schedule);
    const observer = new MutationObserver(schedule);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    const urlPoll = window.setInterval(() => {
      if (location.href !== lastUrl) {
        lastUrl = location.href;
        schedule();
      }
    }, 500);

    return () => {
      window.clearTimeout(timer);
      window.clearInterval(urlPoll);
      observer.disconnect();
      window.removeEventListener('popstate', schedule);
      history.pushState = originalPushState;
      history.replaceState = originalReplaceState;
    };
  }, [onPageChange]);
}
