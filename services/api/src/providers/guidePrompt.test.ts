import { describe, expect, it } from 'vitest';
import type { GuideRequest } from '../../../../src/contracts';
import { guideRequestFixture } from '../testFixtures';
import { createGuideMessages } from './guidePrompt';

describe('guide prompt', () => {
  it('preserves multilingual intent and requires semantic matching across languages', () => {
    const request: GuideRequest = {
      ...guideRequestFixture,
      session: {
        ...guideRequestFixture.session,
        originalUserMessage: '내 티켓은 어디서 확인해?',
        goal: '내 티켓 확인',
      },
      candidates: [
        { ...guideRequestFixture.candidates[0], id: 'new-ticket', label: 'New Ticket' },
        { ...guideRequestFixture.candidates[0], id: 'my-tickets', label: 'My Tickets' },
      ],
    };

    const messages = createGuideMessages(request);

    expect(messages[0].content).toContain('different languages');
    expect(messages[0].content).toContain('"내 티켓"');
    expect(messages[0].content).toContain('global Windows taskbar');
    expect(messages.at(-1)?.content).toContain('내 티켓은 어디서 확인해?');
    expect(messages.at(-1)?.content).toContain('My Tickets');
    expect(messages.at(-1)?.content).not.toContain('"bounds"');
  });

  it('explains browser chrome versus destination-site content', () => {
    const messages = createGuideMessages({
      ...guideRequestFixture,
      session: {
        ...guideRequestFixture.session,
        originalUserMessage: '유튜브 뮤직에서 노래를 찾아줘',
      },
      context: { platform: 'windows', applicationName: 'chrome', windowTitle: 'YouTube Music - Google Chrome' },
      candidates: [{
        ...guideRequestFixture.candidates[0],
        id: 'youtube-search',
        label: 'Search',
        role: 'edit',
        attributes: { sourceScope: 'browser_content', containerLabel: 'YouTube Music' },
      }],
    });

    expect(messages[0].content).toContain('sourceScope=browser_chrome');
    expect(messages[0].content).toContain('Never choose the Chrome/Edge address bar');
    expect(messages.at(-1)?.content).toContain('"scope":"browser_content"');
    expect(messages.at(-1)?.content).toContain('"container":"YouTube Music"');
  });

  it('omits semantic candidates during screenshot fallback and requires visual targeting', () => {
    const messages = createGuideMessages({
      ...guideRequestFixture,
      screenshot: 'data:image/jpeg;base64,abc',
      screenshotBounds: { x: 0, y: 0, width: 1920, height: 1080 },
    });
    const content = messages.at(-1)?.content;

    expect(messages[0].content).toContain('Do not use highlight');
    expect(Array.isArray(content)).toBe(true);
    expect((content as Array<{ type: string; text?: string }>)[0].text).toContain('"candidates":[]');
    expect(JSON.stringify(content)).not.toContain('candidate-settings');
  });
});
