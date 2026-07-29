import {
  useEffect,
  useLayoutEffect,
  useRef,
  type FormEvent,
  type RefObject,
} from 'react';
import type { AssistantMode, ChatMessage } from '../types';
import type { CandidateChoice } from '../types/search';
import { calculateFloatingPosition } from '../utils/positioning';
import { CandidateChoices } from './CandidateChoices';
import { MessageList } from './MessageList';

interface SpeechBubbleProps {
  bubbleRef: RefObject<HTMLDivElement | null>;
  anchorRef: RefObject<HTMLButtonElement | null>;
  mode: AssistantMode;
  input: string;
  messages: ChatMessage[];
  candidateChoices: CandidateChoice[];
  guidanceNote: string | null;
  showGuidanceActions: boolean;
  focusRequest: number;
  anchorPosition: { x: number; y: number };
  onInputChange: (value: string) => void;
  onSubmit: () => void;
  onCandidateSelect: (choice: CandidateChoice) => void;
  onRetry: () => void;
  onClearHighlight: () => void;
  onClose: () => void;
}

export function SpeechBubble({
  bubbleRef,
  anchorRef,
  mode,
  input,
  messages,
  candidateChoices,
  guidanceNote,
  showGuidanceActions,
  focusRequest,
  anchorPosition,
  onInputChange,
  onSubmit,
  onCandidateSelect,
  onRetry,
  onClearHighlight,
  onClose,
}: SpeechBubbleProps) {
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const updatePosition = () => {
    const bubble = bubbleRef.current;
    const anchor = anchorRef.current;
    if (!bubble || !anchor) return;
    const anchorRect = anchor.getBoundingClientRect();
    const next = calculateFloatingPosition(anchorRect, bubble.offsetWidth, bubble.offsetHeight);
    bubble.style.left = `${next.x}px`;
    bubble.style.top = `${next.y}px`;
    bubble.dataset.side = next.side;
    bubble.dataset.vertical = next.vertical;
  };

  useLayoutEffect(updatePosition);
  useEffect(() => {
    inputRef.current?.focus();
    const update = () => updatePosition();
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, []);

  useEffect(() => {
    if (focusRequest > 0) inputRef.current?.focus();
  }, [focusRequest]);

  useLayoutEffect(updatePosition, [
    messages,
    mode,
    candidateChoices,
    guidanceNote,
    anchorPosition,
  ]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit();
  };

  return (
    <div
      ref={bubbleRef}
      className="sw-dialog"
      role="dialog"
      aria-modal="false"
      aria-labelledby="sw-title"
      data-state={mode}
    >
      <header className="sw-dialog-header">
        <div>
          <span className="sw-eyebrow">ShowWhere</span>
          <h2 id="sw-title">무엇을 하고 싶으세요?</h2>
        </div>
        <button type="button" className="sw-close" onClick={onClose} aria-label="ShowWhere 대화창 닫기">
          <span aria-hidden="true">×</span>
        </button>
      </header>

      <div className="sw-dialog-body">
        <MessageList messages={messages} thinking={mode === 'thinking'} />
        <CandidateChoices choices={candidateChoices} onSelect={onCandidateSelect} />
        {guidanceNote && <p className="sw-guidance-note">{guidanceNote}</p>}
        {showGuidanceActions && (
          <div className="sw-guidance-actions">
            <button type="button" onClick={onRetry}>다시 찾기</button>
            <button type="button" onClick={onClearHighlight}>강조 지우기</button>
          </div>
        )}
      </div>

      <form className="sw-form" onSubmit={submit}>
        <label className="sw-sr-only" htmlFor="sw-query">도움받고 싶은 내용을 입력하세요</label>
        <textarea
          ref={inputRef}
          id="sw-query"
          rows={2}
          value={input}
          placeholder="예: 로그인하고 싶어요"
          onChange={(event) => onInputChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault();
              if (input.trim() && mode !== 'thinking') onSubmit();
            }
          }}
        />
        <button
          className="sw-send"
          type="submit"
          disabled={!input.trim() || mode === 'thinking'}
          aria-label="질문 보내기"
        >
          보내기
        </button>
      </form>
      <p className="sw-disclaimer">현재는 UI 테스트 버전입니다.</p>
    </div>
  );
}
