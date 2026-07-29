import { createRoot, type Root } from 'react-dom/client';
import { App } from './App';
import styleText from './styles.css?inline';
import { EXTENSION_MESSAGES, type ExtensionMessage } from '../shared/messages';

declare global {
  interface Window {
    __SHOWWHERE_CONTROLLER__?: true;
  }
}

if (!window.__SHOWWHERE_CONTROLLER__) {
  window.__SHOWWHERE_CONTROLLER__ = true;

  let active = false;
  let host: HTMLDivElement | null = null;
  let reactRoot: Root | null = null;

  const mount = () => {
    if (active) return;
    host = document.createElement('div');
    host.id = 'showwhere-extension-root';
    const shadow = host.attachShadow({ mode: 'open' });
    const style = document.createElement('style');
    style.textContent = styleText;
    const mountPoint = document.createElement('div');
    shadow.append(style, mountPoint);
    (document.documentElement || document.body).append(host);
    reactRoot = createRoot(mountPoint);
    reactRoot.render(<App />);
    active = true;
  };

  const unmount = () => {
    if (!active) return;
    reactRoot?.unmount();
    host?.remove();
    reactRoot = null;
    host = null;
    active = false;
  };

  chrome.runtime.onMessage.addListener(
    (message: ExtensionMessage, _sender, sendResponse: (response: { active: boolean }) => void) => {
      if (message.type === EXTENSION_MESSAGES.ping) {
        sendResponse({ active });
        return;
      }
      if (message.type === EXTENSION_MESSAGES.toggle) {
        if (active) unmount();
        else mount();
        sendResponse({ active });
      }
    },
  );
}
