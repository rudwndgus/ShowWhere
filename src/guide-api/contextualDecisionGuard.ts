import type { GuideDecision, GuideRequest, UiCandidate } from '../contracts';

const destinationAliases = [
  ['youtube music', '유튜브 뮤직', '유튜브뮤직'],
  ['youtube', '유튜브'],
  ['spotify', '스포티파이'],
  ['netflix', '넷플릭스'],
  ['naver', '네이버'],
  ['google maps', '구글 지도', '구글지도'],
];

const searchTerms = ['search', 'find', '검색', '찾', '노래', '음악', '곡', 'song', 'music', 'play', '재생', '영상', 'video'];
const explicitBrowserNavigationTerms = [
  '주소창', 'url', '웹 주소', '사이트로 이동', '사이트 열', '구글에서 검색', 'google search', '웹 검색', '인터넷 검색',
];

function attribute(candidate: UiCandidate, key: string): string | undefined {
  const value = candidate.attributes?.[key];
  return typeof value === 'string' ? value.toLowerCase() : undefined;
}

function isSearchControl(candidate: UiCandidate): boolean {
  const searchable = `${candidate.label ?? ''} ${candidate.description ?? ''} ${candidate.role}`.toLowerCase();
  return ['edit', 'textbox', 'searchbox', 'combobox'].includes(candidate.role.toLowerCase())
    || searchTerms.some((term) => searchable.includes(term));
}

function hasNamedDestination(intent: string): boolean {
  return destinationAliases.some((aliases) => aliases.some((alias) => intent.includes(alias)));
}

export function guardContextualDecision(
  request: GuideRequest,
  decision: GuideDecision,
): GuideDecision {
  const intent = [
    request.session.originalUserMessage,
    request.session.goal,
    ...request.session.completedSteps,
    ...request.session.knownFacts,
  ].filter(Boolean).join(' ').toLowerCase();
  const isNamedDestinationSearch = hasNamedDestination(intent)
    && searchTerms.some((term) => intent.includes(term))
    && !explicitBrowserNavigationTerms.some((term) => intent.includes(term));

  if (request.screenshot) return decision;
  if (isNamedDestinationSearch
      && decision.action === 'ask_user'
      && !decision.alternativeTargetIds) {
    return {
      status: 'in_progress',
      action: 'request_vision',
      message: '요청한 앱 안의 검색창을 화면에서 직접 확인할게요.',
      confidence: 0,
    };
  }
  if (decision.action !== 'highlight' || !decision.targetId) return decision;
  const selected = request.candidates.find((candidate) => candidate.id === decision.targetId);
  if (!selected || attribute(selected, 'sourceScope') !== 'browser_chrome' || !isSearchControl(selected)) {
    return decision;
  }
  if (!searchTerms.some((term) => intent.includes(term))) return decision;
  if (explicitBrowserNavigationTerms.some((term) => intent.includes(term))) return decision;

  if (isNamedDestinationSearch) {
    return {
      status: 'in_progress',
      action: 'request_vision',
      message: '요청한 앱 안의 검색창을 화면에서 다시 정확히 찾을게요.',
      confidence: 0,
    };
  }

  const contentSearchCandidates = request.candidates.filter((candidate) =>
    attribute(candidate, 'sourceScope') === 'browser_content' && isSearchControl(candidate));
  if (contentSearchCandidates.length === 0) return decision;

  const alternativeTargetIds = [selected, ...contentSearchCandidates]
    .map((candidate) => candidate.id)
    .filter((id, index, values) => values.indexOf(id) === index)
    .slice(0, 4);
  if (alternativeTargetIds.length < 2) return decision;
  return {
    status: 'needs_clarification',
    action: 'ask_user',
    alternativeTargetIds,
    message: '현재 사이트 안에서 검색할까요, 아니면 인터넷 전체에서 검색할까요?',
    confidence: 0,
  };
}
