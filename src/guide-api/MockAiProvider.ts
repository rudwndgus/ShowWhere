import type { GuideDecision, GuideRequest, UiCandidate } from '../contracts';
import type { AiProvider } from './AiProvider';

const AMBIGUITY_SCORE_GAP = 12;

function numericAttribute(candidate: UiCandidate, name: string): number | null {
  const value = candidate.attributes?.[name];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function stringAttribute(candidate: UiCandidate, name: string): string | null {
  const value = candidate.attributes?.[name];
  return typeof value === 'string' ? value : null;
}

function truncate(value: string, maximum: number): string {
  const cleaned = value.replace(/\s+/g, ' ').trim();
  return cleaned.length > maximum ? `${cleaned.slice(0, maximum - 1).trim()}…` : cleaned;
}

function guidanceMessage(candidate: UiCandidate): string {
  const label = truncate(candidate.label ?? '표시된 항목', 40);
  const actionType = stringAttribute(candidate, 'actionType');
  const optionButtonFound = candidate.attributes?.optionButtonFound === true;
  if (actionType === 'options' && !optionButtonFound) {
    return '해당 대화 항목을 표시했어요. 오른쪽의 점 세 개 메뉴를 눌러보세요.';
  }
  if (actionType === 'open' || actionType === 'options') {
    const targetName = truncate(label.replace(/\s*(열기|open)$/iu, '').trim(), 40);
    return `좋아요! ${targetName}을 열려면 빨간색으로 표시한 곳을 눌러보세요.`;
  }
  if (actionType === 'select') {
    return `좋아요! ${label}을 선택하려면 빨간색으로 표시한 곳을 눌러보세요.`;
  }
  return `찾았어요. ${label}을 이용하려면 빨간색으로 표시한 곳을 눌러보세요.`;
}

export class MockAiProvider implements AiProvider {
  async decideNextAction(request: GuideRequest): Promise<GuideDecision> {
    const eligibleCandidates = request.candidates.filter((candidate) =>
      candidate.visible && candidate.enabled && candidate.clickable);
    const [best, second] = eligibleCandidates;
    if (!best) {
      return {
        status: 'needs_clarification',
        action: 'ask_user',
        message: '현재 화면에서 정확히 일치하는 항목을 찾지 못했어요. 화면에 보이는 버튼이나 메뉴 이름을 조금 더 구체적으로 말해 주세요.',
        confidence: 0,
      };
    }

    if (request.context.platform === 'windows') {
      return {
        status: 'in_progress',
        action: 'highlight',
        targetId: best.id,
        message: guidanceMessage(best),
        expectedChange: '선택한 Windows UI가 열리거나 현재 화면이 변경됩니다.',
        confidence: 0.85,
      };
    }

    const bestScore = numericAttribute(best, 'localScore') ?? 0;
    const secondScore = second ? numericAttribute(second, 'localScore') ?? 0 : null;
    const scoreGap = secondScore === null ? Number.POSITIVE_INFINITY : bestScore - secondScore;
    if (second && scoreGap <= AMBIGUITY_SCORE_GAP) {
      return {
        status: 'needs_clarification',
        action: 'ask_user',
        message: '비슷한 항목이 몇 개 있어요. 하나를 골라 주세요.',
        confidence: 0.55,
      };
    }

    const confidence = secondScore === null
      ? 0.9
      : Math.min(0.98, 0.72 + Math.max(0, scoreGap) / 100);
    return {
      status: 'in_progress',
      action: 'highlight',
      targetId: best.id,
      message: guidanceMessage(best),
      expectedChange: '선택한 UI가 열리거나 현재 화면이 변경됩니다.',
      confidence,
    };
  }
}
