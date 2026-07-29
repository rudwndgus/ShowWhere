import { useLayoutEffect, useRef } from 'react';
import type { ChatMessage } from '../types';

interface MessageListProps {
  messages: ChatMessage[];
  thinking: boolean;
}

export function MessageList({ messages, thinking }: MessageListProps) {
  const listRef = useRef<HTMLDivElement>(null);
  const shouldFollowLatest = useRef(true);

  useLayoutEffect(() => {
    const list = listRef.current;
    if (list && shouldFollowLatest.current) {
      list.scrollTo({ top: list.scrollHeight, behavior: 'smooth' });
    }
  }, [messages, thinking]);

  if (messages.length === 0 && !thinking) return null;

  return (
    <div
      ref={listRef}
      className="sw-messages"
      aria-live="polite"
      aria-relevant="additions"
      onScroll={(event) => {
        const list = event.currentTarget;
        shouldFollowLatest.current = list.scrollHeight - list.scrollTop - list.clientHeight < 72;
      }}
    >
      {messages.map((message) => (
        <p key={message.id} className={`sw-message sw-message--${message.role}`}>
          {message.text}
        </p>
      ))}
      {thinking && (
        <div className="sw-thinking" role="status" aria-label="ShowWhere가 답변을 준비하고 있습니다">
          <span />
          <span />
          <span />
        </div>
      )}
    </div>
  );
}
