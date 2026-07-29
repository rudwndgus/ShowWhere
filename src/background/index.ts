import { EXTENSION_MESSAGES, type ExtensionMessage } from '../shared/messages';

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
