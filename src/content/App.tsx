import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AssistantButton } from './components/AssistantButton';
import { HighlightOverlay } from './components/HighlightOverlay';
import { SpeechBubble } from './components/SpeechBubble';
import { useAssistantPosition } from './hooks/useAssistantPosition';
import { useOutsideClick } from './hooks/useOutsideClick';
import { usePageChanges } from './hooks/usePageChanges';
import type { AssistantMode, ChatMessage } from './types';
import type { CandidateChoice, SearchOutcome } from './types/search';
import { createCandidateChoice } from './utils/createCandidateChoice';
import { truncateLabel } from './utils/createElementLabel';
import { rankCandidates } from './utils/rankCandidates';

type LastResultType = 'clear' | 'ambiguous' | 'notFound' | null;

interface LastSearchResult {
  normalizedQuery: string;
  candidateSignature: string;
  resultType: LastResultType;
}

function message(role: ChatMessage['role'], text: string): ChatMessage {
  return { id: `${Date.now()}-${Math.random().toString(36).slice(2)}`, role, text };
}

function createGuidanceMessage(choice: CandidateChoice): string {
  const label = truncateLabel(choice.label, 40);
  if (choice.actionType === 'options' && !choice.optionButtonFound) {
    return '해당 대화 항목을 표시했어요.\n오른쪽의 점 세 개 메뉴를 눌러보세요.';
  }
  if (choice.actionType === 'open' || choice.actionType === 'options') {
    const targetName = truncateLabel(label.replace(/\s*(열기|open)$/iu, '').trim(), 40);
    return `좋아요! ${targetName}을 열려면\n빨간색으로 표시한 곳을 눌러보세요.`;
  }
  if (choice.actionType === 'select') {
    return `좋아요! ${label}을 선택하려면\n빨간색으로 표시한 곳을 눌러보세요.`;
  }
  return `좋아요! ${label}을 이용하려면\n빨간색으로 표시한 곳을 눌러보세요.`;
}

export function App() {
  const { position, setPosition } = useAssistantPosition();
  const [mode, setMode] = useState<AssistantMode>('idle');
  const [open, setOpen] = useState(false);
  const [hovered, setHovered] = useState(false);
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [candidateChoices, setCandidateChoices] = useState<CandidateChoice[]>([]);
  const [currentTarget, setCurrentTarget] = useState<HTMLElement | null>(null);
  const [currentTargetLabel, setCurrentTargetLabel] = useState('');
  const [awaitingCandidateSelection, setAwaitingCandidateSelection] = useState(false);
  const [lastNormalizedQuery, setLastNormalizedQuery] = useState('');
  const [lastCandidateElementIds, setLastCandidateElementIds] = useState<string[]>([]);
  const [lastCandidateSignature, setLastCandidateSignature] = useState('');
  const [lastResultType, setLastResultType] = useState<LastResultType>(null);
  const [highlightVisible, setHighlightVisible] = useState(false);
  const [guidanceNote, setGuidanceNote] = useState<string | null>(null);
  const [focusRequest, setFocusRequest] = useState(0);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const bubbleRef = useRef<HTMLDivElement>(null);
  const timers = useRef<number[]>([]);
  const lastResultRef = useRef<LastSearchResult>({
    normalizedQuery: '',
    candidateSignature: '',
    resultType: null,
  });
  const clickPendingRef = useRef<{
    expiresAt: number;
    messageScheduled: boolean;
  } | null>(null);

  const clearTimers = useCallback(() => {
    timers.current.forEach(window.clearTimeout);
    timers.current = [];
  }, []);

  useEffect(() => clearTimers, [clearTimers]);

  const recordResult = useCallback((
    normalizedQuery: string,
    choices: CandidateChoice[],
    resultType: LastResultType,
  ) => {
    const ids = choices.map((choice) => choice.id);
    const signature = ids.join('|');
    setLastNormalizedQuery(normalizedQuery);
    setLastCandidateElementIds(ids);
    setLastCandidateSignature(signature);
    setLastResultType(resultType);
    lastResultRef.current = { normalizedQuery, candidateSignature: signature, resultType };
  }, []);

  const resetGuidance = useCallback(() => {
    setCurrentTarget(null);
    setCurrentTargetLabel('');
    setHighlightVisible(false);
    setGuidanceNote(null);
  }, []);

  const closeDialog = useCallback((clearGuide = false) => {
    setOpen(false);
    setHovered(false);
    setMode('idle');
    if (clearGuide) {
      resetGuidance();
      setCandidateChoices([]);
      setAwaitingCandidateSelection(false);
    }
  }, [resetGuidance]);

  const outsideRefs = useMemo(() => [bubbleRef, buttonRef], []);
  useOutsideClick(outsideRefs, () => closeDialog(false), open);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      resetGuidance();
      setCandidateChoices([]);
      setAwaitingCandidateSelection(false);
      if (open) closeDialog(false);
    };
    document.addEventListener('keydown', handleKeyDown, true);
    return () => document.removeEventListener('keydown', handleKeyDown, true);
  }, [closeDialog, open, resetGuidance]);

  const startGuide = useCallback((choice: CandidateChoice, response: string) => {
    const target = choice.resolvedTarget.isConnected ? choice.resolvedTarget : choice.target;
    setCandidateChoices([]);
    setAwaitingCandidateSelection(false);
    setCurrentTarget(target);
    setCurrentTargetLabel(truncateLabel(choice.label, 40));
    setHighlightVisible(true);
    setGuidanceNote('직접 눌러야 하며, ShowWhere가 자동으로 클릭하지는 않아요.');
    setMode('guiding');
    setOpen(true);
    setMessages((current) => [...current, message('assistant', response)]);
    target.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });
    const ownerFrame = target.ownerDocument.defaultView?.frameElement;
    if (ownerFrame instanceof HTMLElement) {
      ownerFrame.scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });
    }
  }, []);

  useEffect(() => {
    if (!currentTarget) return;
    const clickedTarget = currentTarget;
    const handleTargetClick = () => {
      setCurrentTarget(null);
      setHighlightVisible(false);
      setCandidateChoices([]);
      setAwaitingCandidateSelection(false);
      setGuidanceNote(null);
      setMode('success');
      setOpen(true);
      setMessages((current) => [
        ...current,
        message('assistant', '잘했어요! 선택한 곳을 눌렀어요.'),
      ]);
      clickPendingRef.current = { expiresAt: Date.now() + 1400, messageScheduled: false };
      const timer = window.setTimeout(() => {
        clickPendingRef.current = null;
        setMode('idle');
      }, 1400);
      timers.current.push(timer);
    };
    clickedTarget.addEventListener('click', handleTargetClick, true);
    return () => clickedTarget.removeEventListener('click', handleTargetClick, true);
  }, [currentTarget]);

  const handlePageChange = useCallback(() => {
    const pendingClick = clickPendingRef.current;
    if (pendingClick && Date.now() < pendingClick.expiresAt && !pendingClick.messageScheduled) {
      pendingClick.messageScheduled = true;
      const timer = window.setTimeout(() => {
        setMessages((current) => [
          ...current,
          message('assistant', '화면이 바뀌었어요. 다음으로 필요한 것을 다시 물어보세요.'),
        ]);
        setMode('idle');
      }, 400);
      timers.current.push(timer);
      return;
    }
    if (currentTarget && !currentTarget.isConnected) {
      resetGuidance();
      setMessages((current) => [
        ...current,
        message('assistant', '화면이 바뀌었어요. 필요한 항목을 다시 질문해 주세요.'),
      ]);
      setMode('idle');
    }
  }, [currentTarget, resetGuidance]);
  usePageChanges(handlePageChange);

  const handleSearchOutcome = useCallback((outcome: SearchOutcome) => {
    const normalized = outcome.query.normalized;
    const previous = lastResultRef.current;

    if (!outcome.best) {
      const isDuplicate = previous.normalizedQuery === normalized && previous.resultType === 'notFound';
      setMode('notFound');
      setCandidateChoices([]);
      setAwaitingCandidateSelection(false);
      if (!isDuplicate) {
        setMessages((current) => [
          ...current,
          message(
            'assistant',
            '현재 화면에서 정확히 일치하는 항목을 찾지 못했어요.\n화면에 보이는 버튼이나 메뉴 이름을 조금 더 구체적으로 말해 주세요.',
          ),
        ]);
      }
      recordResult(normalized, [], 'notFound');
      return;
    }

    const alternatives = outcome.alternatives.map(createCandidateChoice);
    if (outcome.ambiguous) {
      const signature = alternatives.map((choice) => choice.id).join('|');
      const exactDuplicate =
        previous.normalizedQuery === normalized &&
        previous.candidateSignature === signature &&
        previous.resultType === 'ambiguous';
      const sameCandidates =
        previous.candidateSignature === signature && previous.resultType === 'ambiguous';

      setCandidateChoices(alternatives);
      setAwaitingCandidateSelection(true);
      setMode('showingCandidates');
      if (!exactDuplicate) {
        setMessages((current) => [
          ...current,
          message(
            'assistant',
            sameCandidates
              ? '아직 정확한 항목을 고르기 어려워요.\n아래에서 가장 가까운 항목을 선택해 주세요.'
              : '비슷한 항목이 몇 개 있어요. 하나를 골라 주세요.',
          ),
        ]);
      }
      recordResult(normalized, alternatives, 'ambiguous');
      return;
    }

    const choice = createCandidateChoice(outcome.best);
    recordResult(normalized, [choice], 'clear');
    const subject = outcome.intent?.label ?? truncateLabel(outcome.query.normalized || choice.label, 30);
    startGuide(choice, `찾았어요. ${subject}과 가장 가까운 항목을 표시했어요.`);
  }, [recordResult, startGuide]);

  const handleSubmit = useCallback(() => {
    const query = input.trim();
    if (!query || mode === 'thinking') return;
    clearTimers();
    clickPendingRef.current = null;
    resetGuidance();
    setCandidateChoices([]);
    setAwaitingCandidateSelection(false);
    setMessages((current) => [...current, message('user', query)]);
    setInput('');
    setMode('thinking');

    const timer = window.setTimeout(() => {
      handleSearchOutcome(rankCandidates(query));
    }, 550);
    timers.current.push(timer);
  }, [clearTimers, handleSearchOutcome, input, mode, resetGuidance]);

  const selectCandidate = useCallback((choice: CandidateChoice) => {
    startGuide(choice, createGuidanceMessage(choice));
  }, [startGuide]);

  const handleInputChange = (value: string) => {
    setInput(value);
    if (value.trim() && awaitingCandidateSelection) {
      setCandidateChoices([]);
      setAwaitingCandidateSelection(false);
      setLastNormalizedQuery('');
      setLastCandidateElementIds([]);
      setLastCandidateSignature('');
      setLastResultType(null);
      setMode('idle');
    }
  };

  const retrySearch = () => {
    resetGuidance();
    setCandidateChoices([]);
    setAwaitingCandidateSelection(false);
    setMode('idle');
    setFocusRequest((current) => current + 1);
  };

  const clearHighlight = () => {
    setHighlightVisible(false);
    setGuidanceNote('표시를 지웠어요. 다시 표시하려면 ‘다시 찾기’를 눌러 주세요.');
  };

  const toggleDialog = () => {
    if (open) {
      closeDialog(false);
    } else {
      setOpen(true);
      setHovered(false);
      setMode(candidateChoices.length > 0 ? 'showingCandidates' : currentTarget ? 'guiding' : 'idle');
    }
  };

  const tooltipOnLeft = position.x > 215;
  const tooltipStyle = {
    left: tooltipOnLeft ? Math.max(12, position.x - 188) : position.x + 55,
    top: Math.min(Math.max(12, position.y + 5), window.innerHeight - 50),
  };

  return (
    <div
      className="sw-root"
      data-state={mode}
      data-last-query={lastNormalizedQuery}
      data-last-result={lastResultType ?? ''}
      data-candidate-signature={lastCandidateSignature}
      data-candidate-count={lastCandidateElementIds.length}
      data-target-label={currentTargetLabel}
    >
      <AssistantButton
        buttonRef={buttonRef}
        position={position}
        state={mode}
        open={open}
        onActivate={toggleDialog}
        onHoverChange={(next) => setHovered(next)}
        onDrag={(next, finished) => setPosition(next, finished)}
      />

      {hovered && !open && (
        <div
          className={`sw-tooltip sw-tooltip--${tooltipOnLeft ? 'left' : 'right'}`}
          role="tooltip"
          style={tooltipStyle}
        >
          무엇을 도와드릴까요?
        </div>
      )}

      {open && (
        <SpeechBubble
          bubbleRef={bubbleRef}
          anchorRef={buttonRef}
          anchorPosition={position}
          mode={mode}
          input={input}
          messages={messages}
          candidateChoices={candidateChoices}
          guidanceNote={guidanceNote}
          showGuidanceActions={mode === 'guiding'}
          focusRequest={focusRequest}
          onInputChange={handleInputChange}
          onSubmit={handleSubmit}
          onCandidateSelect={selectCandidate}
          onRetry={retrySearch}
          onClearHighlight={clearHighlight}
          onClose={() => closeDialog(true)}
        />
      )}

      <HighlightOverlay target={highlightVisible ? currentTarget : null} />
    </div>
  );
}
