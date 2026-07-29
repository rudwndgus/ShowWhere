export const EXTENSION_MESSAGES = {
  ping: 'SHOWWHERE_PING',
  toggle: 'SHOWWHERE_TOGGLE',
} as const;

export type ExtensionMessage =
  | { type: typeof EXTENSION_MESSAGES.ping }
  | { type: typeof EXTENSION_MESSAGES.toggle };
