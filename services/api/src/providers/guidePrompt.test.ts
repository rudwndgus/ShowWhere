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
});
