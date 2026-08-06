import type { GuideRequest } from '../contracts';

export const EXTENSION_MESSAGES = {
  ping: 'SHOWWHERE_PING',
  toggle: 'SHOWWHERE_TOGGLE',
  guideRequest: 'SHOWWHERE_GUIDE_REQUEST',
} as const;

export type ExtensionMessage =
  | { type: typeof EXTENSION_MESSAGES.ping }
  | { type: typeof EXTENSION_MESSAGES.toggle }
  | { type: typeof EXTENSION_MESSAGES.guideRequest; request: GuideRequest };

export type GuideRuntimeResponse =
  | { ok: true; decision: unknown }
  | { ok: false };
