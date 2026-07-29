export type IntentId =
  | 'LOGIN'
  | 'SIGN_UP'
  | 'PASSWORD_RESET'
  | 'SETTINGS'
  | 'SEARCH'
  | 'SAVE'
  | 'PRINT'
  | 'PDF'
  | 'CLOSE'
  | 'NEXT'
  | 'BACK'
  | 'SUBMIT'
  | 'MENU'
  | 'PROFILE'
  | 'LOGOUT'
  | 'HELP'
  | 'REFUND'
  | 'DELETE'
  | 'EDIT'
  | 'ORDER_HISTORY';

export interface SearchIntent {
  id: IntentId;
  label: string;
  userPhrases: string[];
  targetTerms: string[];
  preferredRoles?: string[];
}

export interface NormalizedQuery {
  original: string;
  normalized: string;
  tokens: string[];
  comparableOriginal: string;
  compactOriginal: string;
}

export interface CandidateElement {
  element: HTMLElement;
  searchableText: string;
  compactSearchableText: string;
  visibleText: string;
  ariaLabel: string;
  labelledByText: string;
  title: string;
  placeholder: string;
  safeValue: string;
  alt: string;
  name: string;
  id: string;
  className: string;
  role: string | null;
  href: string;
  inputType: string;
  tagName: string;
  isVisible: boolean;
  isInViewport: boolean;
  isClickable: boolean;
  isDisabled: boolean;
  rect: DOMRect;
}

export interface ScoredCandidate extends CandidateElement {
  score: number;
  reasons: string[];
}

export interface SearchOutcome {
  query: NormalizedQuery;
  intent: SearchIntent | null;
  ranked: ScoredCandidate[];
  best: ScoredCandidate | null;
  alternatives: ScoredCandidate[];
  ambiguous: boolean;
}

export type CandidateActionType = 'open' | 'select' | 'click' | 'options';

export interface CandidateChoice {
  id: string;
  label: string;
  candidate: ScoredCandidate;
  target: HTMLElement;
  resolvedTarget: HTMLElement;
  actionType: CandidateActionType;
  score: number;
  optionButtonFound: boolean;
}
