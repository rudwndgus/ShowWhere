import { EXTENSION_MESSAGES, type ExtensionMessage } from '../shared/messages';
import { GuideRequestSchema } from '../contracts';

const UNAVAILABLE_MESSAGE = '이 페이지에서는 ShowWhere를 사용할 수 없습니다.';

async function showUnavailable(tabId: number) {
  await chrome.action.setBadgeBackgroundColor({ tabId, color: '#ca2c34' });
  await chrome.action.setBadgeText({ tabId, text: '!', });
  await chrome.action.setTitle({ tabId, title: UNAVAILABLE_MESSAGE });
  setTimeout(() => {
    void chrome.action.setBadgeText({ tabId, text: '' });
    void chrome.action.setTitle({ tabId, title: 'ShowWhere 켜기/끄기' });
  }, 5000);
}

async function sendToggle(tabId: number) {
  const message: ExtensionMessage = { type: EXTENSION_MESSAGES.toggle };
  return chrome.tabs.sendMessage(tabId, message) as Promise<{ active: boolean }>;
}

chrome.action.onClicked.addListener((tab) => {
  void (async () => {
    if (!tab.id) return;

    try {
      const response = await sendToggle(tab.id);
      await chrome.action.setTitle({
        tabId: tab.id,
        title: response.active ? 'ShowWhere 끄기' : 'ShowWhere 켜기',
      });
      return;
    } catch {
      // The content controller has not been injected on this tab yet.
    }

    try {
      await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        files: ['content.js'],
      });
      const response = await sendToggle(tab.id);
      await chrome.action.setBadgeText({ tabId: tab.id, text: '' });
      await chrome.action.setTitle({
        tabId: tab.id,
        title: response.active ? 'ShowWhere 끄기' : 'ShowWhere 켜기',
      });
    } catch {
      await showUnavailable(tab.id);
    }
  })();
});

chrome.runtime.onMessage.addListener((message: unknown, sender, sendResponse) => {
  if (
    !message ||
    typeof message !== 'object' ||
    (message as { type?: unknown }).type !== EXTENSION_MESSAGES.guideRequest
  ) return false;

  void (async () => {
    try {
      if (sender.id !== chrome.runtime.id || !sender.tab?.id) {
        sendResponse({ ok: false });
        return;
      }
      const endpoint = import.meta.env.VITE_SHOWWHERE_GUIDE_API_URL?.trim();
      const request = GuideRequestSchema.safeParse((message as { request?: unknown }).request);
      if (!endpoint || !request.success) {
        sendResponse({ ok: false });
        return;
      }

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 95_000);
      try {
        const response = await fetch(endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(request.data),
          credentials: 'omit',
          signal: controller.signal,
        });
        const decision: unknown = await response.json();
        sendResponse({ ok: true, decision });
      } finally {
        clearTimeout(timeout);
      }
    } catch {
      sendResponse({ ok: false });
    }
  })();
  return true;
});
