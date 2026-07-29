export type AssistantMode =
  | 'idle'
  | 'thinking'
  | 'showingCandidates'
  | 'guiding'
  | 'success'
  | 'notFound';

export type AssistantState = AssistantMode | 'inactive' | 'hover' | 'open';

export type MessageRole = 'user' | 'assistant';

export interface ChatMessage {
  id: string;
  role: MessageRole;
  text: string;
}

export interface Point {
  x: number;
  y: number;
}
