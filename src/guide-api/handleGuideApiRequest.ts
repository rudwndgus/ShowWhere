import {
  DEFAULT_GUIDE_CONFIDENCE_THRESHOLD,
  GuideDecisionSchema,
  GuideRequestSchema,
  targetExistsInRequest,
  type GuideDecision,
} from '../contracts';
import type { AiProvider } from './AiProvider';
import { guardContextualDecision } from './contextualDecisionGuard';
import { MockAiProvider } from './MockAiProvider';

export const GUIDE_API_PATH = '/api/guide';

export type GuideApiErrorCode =
  | 'invalid_route'
  | 'invalid_request'
  | 'provider_failure'
  | 'malformed_decision'
  | 'unknown_target_id'
  | 'low_confidence';

export interface GuideApiResponse {
  status: number;
  decision: GuideDecision;
  errorCode?: GuideApiErrorCode;
}

function safeFallback(message: string): GuideDecision {
  return {
    status: 'needs_clarification',
    action: 'ask_user',
    message,
    confidence: 0,
  };
}

export async function handleGuideApiRequest(
  path: string,
  body: unknown,
  provider: AiProvider = new MockAiProvider(),
  confidenceThreshold = DEFAULT_GUIDE_CONFIDENCE_THRESHOLD,
): Promise<GuideApiResponse> {
  if (path !== GUIDE_API_PATH) {
    return {
      status: 404,
      errorCode: 'invalid_route',
      decision: safeFallback('요청한 안내 경로를 사용할 수 없어요. 다시 시도해 주세요.'),
    };
  }

  const parsedRequest = GuideRequestSchema.safeParse(body);
  if (!parsedRequest.success) {
    return {
      status: 400,
      errorCode: 'invalid_request',
      decision: safeFallback('현재 화면 정보를 확인하지 못했어요. 다시 시도해 주세요.'),
    };
  }

  let rawDecision: unknown;
  try {
    rawDecision = await provider.decideNextAction(parsedRequest.data);
  } catch {
    return {
      status: 502,
      errorCode: 'provider_failure',
      decision: safeFallback('안내를 준비하지 못했어요. 잠시 후 다시 시도해 주세요.'),
    };
  }

  const parsedDecision = GuideDecisionSchema.safeParse(rawDecision);
  if (!parsedDecision.success) {
    return {
      status: 502,
      errorCode: 'malformed_decision',
      decision: safeFallback('안내 결과를 확인하지 못했어요. 다른 표현으로 다시 물어봐 주세요.'),
    };
  }

  if (!targetExistsInRequest(parsedDecision.data, parsedRequest.data)) {
    return {
      status: 422,
      errorCode: 'unknown_target_id',
      decision: safeFallback('안전하게 표시할 대상을 확인하지 못했어요. 다시 찾아볼게요.'),
    };
  }

  if (
    parsedDecision.data.action === 'highlight' &&
    parsedDecision.data.confidence < confidenceThreshold
  ) {
    return {
      status: 200,
      errorCode: 'low_confidence',
      decision: safeFallback('어느 항목인지 확실하지 않아요. 화면에 보이는 이름을 조금 더 알려주세요.'),
    };
  }

  return { status: 200, decision: guardContextualDecision(parsedRequest.data, parsedDecision.data) };
}
