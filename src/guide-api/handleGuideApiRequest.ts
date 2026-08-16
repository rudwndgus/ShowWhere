import {
  DEFAULT_GUIDE_CONFIDENCE_THRESHOLD,
  GuideDecisionSchema,
  GuideRequestSchema,
  targetExistsInRequest,
  type GuideDecision,
} from '../contracts';
import type { AiProvider } from './AiProvider';
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

function requestText(body: unknown): string | undefined {
  if (!body || typeof body !== 'object' || !('session' in body)) return undefined;
  const session = body.session;
  if (!session || typeof session !== 'object') return undefined;
  if ('originalUserMessage' in session && typeof session.originalUserMessage === 'string') return session.originalUserMessage;
  return 'goal' in session && typeof session.goal === 'string' ? session.goal : undefined;
}

function localized(text: string | undefined, korean: string, english: string): string {
  return /[가-힣]/u.test(text ?? '') ? korean : english;
}

function safeFallback(message: string): GuideDecision {
  return {
    status: 'needs_clarification',
    action: 'ask_user',
    message,
    confidence: 0,
  };
}

function providerFailure(error: unknown, original: string | undefined): GuideDecision {
  const detail = error instanceof Error ? error.message : String(error);
  if (/credit_balance_exhausted|insufficient_quota/iu.test(detail)) {
    return {
      status: 'blocked',
      action: 'explain',
      message: localized(original,
        'OpenAI API 크레딧이 부족해서 화면을 분석할 수 없어요. API 결제를 충전한 뒤 다시 시도해 주세요.',
        'The OpenAI API does not have enough credit to analyze the screen. Add API credit and try again.'),
      confidence: 1,
    };
  }
  if (/invalid_api_key|401/iu.test(detail)) {
    return {
      status: 'blocked',
      action: 'explain',
      message: localized(original,
        'OpenAI API 키가 유효하지 않아요. .env의 OPENAI_API_KEY를 확인해 주세요.',
        'The OpenAI API key is invalid. Check OPENAI_API_KEY in the environment settings.'),
      confidence: 1,
    };
  }
  if (/request too large|tokens per min/iu.test(detail)) {
    return {
      status: 'blocked',
      action: 'explain',
      message: localized(original,
        '현재 화면 정보가 OpenAI API 토큰 한도를 초과했어요. 잠시 후 다시 시도해 주세요.',
        'The current screen exceeds the OpenAI API token limit. Try again shortly.'),
      confidence: 1,
    };
  }
  return safeFallback(localized(original,
    '안내를 준비하지 못했어요. 잠시 후 다시 시도해 주세요.',
    'Guidance could not be prepared. Try again shortly.'));
}

export async function handleGuideApiRequest(
  path: string,
  body: unknown,
  provider: AiProvider = new MockAiProvider(),
  confidenceThreshold = DEFAULT_GUIDE_CONFIDENCE_THRESHOLD,
): Promise<GuideApiResponse> {
  const original = requestText(body);
  if (path !== GUIDE_API_PATH) {
    return {
      status: 404,
      errorCode: 'invalid_route',
      decision: safeFallback(localized(original, '요청한 안내 경로를 사용할 수 없어요. 다시 시도해 주세요.', 'The requested guidance route is unavailable. Try again.')),
    };
  }

  const parsedRequest = GuideRequestSchema.safeParse(body);
  if (!parsedRequest.success) {
    return {
      status: 400,
      errorCode: 'invalid_request',
      decision: safeFallback(localized(original, '현재 화면 정보를 확인하지 못했어요. 다시 시도해 주세요.', 'The current screen information could not be validated. Try again.')),
    };
  }

  let rawDecision: unknown;
  try {
    rawDecision = await provider.decideNextAction(parsedRequest.data);
  } catch (error) {
    console.error(
      `[showwhere:api] provider_failure ${error instanceof Error ? error.message.slice(0, 800) : String(error).slice(0, 800)}`,
    );
    const failureDecision = providerFailure(error, parsedRequest.data.session.originalUserMessage);
    return {
      status: failureDecision.status === 'blocked' ? 200 : 502,
      errorCode: 'provider_failure',
      decision: failureDecision,
    };
  }

  const parsedDecision = GuideDecisionSchema.safeParse(rawDecision);
  if (!parsedDecision.success) {
    return {
      status: 502,
      errorCode: 'malformed_decision',
      decision: safeFallback(localized(original, '안내 결과를 확인하지 못했어요. 다른 표현으로 다시 물어봐 주세요.', 'The guidance result could not be validated. Try asking in a different way.')),
    };
  }

  if (!targetExistsInRequest(parsedDecision.data, parsedRequest.data)) {
    return {
      status: 422,
      errorCode: 'unknown_target_id',
      decision: safeFallback(localized(original, '안전하게 표시할 대상을 확인하지 못했어요. 다시 찾아볼게요.', 'A safe target could not be verified. I will search again.')),
    };
  }

  if (
    parsedDecision.data.action === 'highlight' &&
    parsedDecision.data.confidence < confidenceThreshold
  ) {
    return {
      status: 200,
      errorCode: 'low_confidence',
      decision: safeFallback(localized(original, '어느 항목인지 확실하지 않아요. 화면에 보이는 이름을 조금 더 알려주세요.', 'I am not certain which item you mean. Tell me a little more about the name visible on the screen.')),
    };
  }

  return { status: 200, decision: parsedDecision.data };
}
