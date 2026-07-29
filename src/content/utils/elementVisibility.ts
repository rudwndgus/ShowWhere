export function getElementViewportRect(element: HTMLElement): DOMRect {
  let rect = element.getBoundingClientRect();
  let ownerWindow = element.ownerDocument.defaultView;
  try {
    while (ownerWindow && ownerWindow !== window) {
      const frame = ownerWindow.frameElement as HTMLElement | null;
      if (!frame) break;
      const frameRect = frame.getBoundingClientRect();
      rect = new DOMRect(
        rect.x + frameRect.x,
        rect.y + frameRect.y,
        rect.width,
        rect.height,
      );
      ownerWindow = frame.ownerDocument.defaultView;
    }
  } catch {
    // Cross-origin frames are never collected, but fail closed if access changes.
  }
  return rect;
}

export function isElementVisible(element: HTMLElement): boolean {
  const ownerWindow = element.ownerDocument.defaultView ?? window;
  const style = ownerWindow.getComputedStyle(element);
  const rect = getElementViewportRect(element);
  return (
    style.display !== 'none' &&
    style.visibility !== 'hidden' &&
    Number(style.opacity) !== 0 &&
    rect.width > 0 &&
    rect.height > 0
  );
}

export function isElementInViewport(element: HTMLElement): boolean {
  const rect = getElementViewportRect(element);
  return (
    rect.bottom > 0 &&
    rect.right > 0 &&
    rect.top < window.innerHeight &&
    rect.left < window.innerWidth
  );
}

export function isDisabled(element: HTMLElement): boolean {
  if ('disabled' in element && Boolean((element as HTMLButtonElement).disabled)) return true;
  return element.getAttribute('aria-disabled') === 'true';
}

export function isElementClickable(element: HTMLElement): boolean {
  const role = element.getAttribute('role');
  return (
    element.matches('button, a[href], input[type="button"], input[type="submit"], input[type="reset"], select, summary') ||
    ['button', 'link', 'menuitem', 'tab', 'checkbox', 'radio', 'switch'].includes(role ?? '') ||
    element.tabIndex >= 0 ||
    element.hasAttribute('onclick')
  );
}
