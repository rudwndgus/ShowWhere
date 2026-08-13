import type { GuideRequest } from '../../../../src/contracts';
import { findWindowsKnowledge } from '../windows-knowledge/WindowsKnowledgeResolver';
import {
  catalogMentionedInGoal,
} from '../web-knowledge/WebKnowledgeResolver';
import type { WebKnowledgeSource } from '../web-knowledge/WebKnowledgeStore';

export type KnowledgeDomain = 'web' | 'windows' | 'unknown';

export interface RoutedRequestIntent {
  domain: KnowledgeDomain;
  reason: 'explicit_site' | 'windows_intent' | 'ambiguous';
  siteId?: string;
}

/**
 * Classifies what the user is trying to do before looking for a control.
 * Candidates and screen geometry are deliberately not consulted here.
 */
export function classifyRequestIntent(
  request: GuideRequest,
  knowledge: WebKnowledgeSource,
): RoutedRequestIntent {
  const explicitSite = knowledge.catalogs.find((catalog) => catalogMentionedInGoal(catalog, request));
  if (explicitSite) return { domain: 'web', reason: 'explicit_site', siteId: explicitSite.siteId };

  // A recognized Windows request remains a Windows request even while Chrome,
  // another website, or any unrelated application is currently foreground.
  if (findWindowsKnowledge(request.session.goal ?? request.session.originalUserMessage)) {
    return { domain: 'windows', reason: 'windows_intent' };
  }

  return { domain: 'unknown', reason: 'ambiguous' };
}
