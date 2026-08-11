import type { GuideRequest, UiCandidate } from '../contracts';
import type {
  ConceptDictionaryEntry,
  GuidanceTeachingRecordV2,
  SemanticKnowledgeContext,
  TaskGraph,
} from './contracts';

export interface SemanticEmbeddingProvider {
  embed(texts: readonly string[]): Promise<readonly number[][]>;
}

function normalize(text: string): string {
  return text.toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

function tokens(text: string): Set<string> {
  return new Set(normalize(text).split(/\s+/u).filter(Boolean));
}

function overlap(left: string, right: string): number {
  const a = tokens(left);
  const b = tokens(right);
  let score = 0;
  for (const token of a) if (b.has(token)) score += token.length >= 4 ? 3 : 1;
  return score;
}

function candidateText(candidate: UiCandidate): string {
  return [
    candidate.label,
    candidate.description,
    candidate.role,
    candidate.attributes?.automationId,
    candidate.attributes?.containerLabel,
  ].filter(Boolean).join(' ');
}

export function retrieveSemanticContext(
  request: GuideRequest,
  concepts: ConceptDictionaryEntry[],
  taskGraphs: TaskGraph[],
  gold: GuidanceTeachingRecordV2[],
): SemanticKnowledgeContext {
  const query = [request.session.originalUserMessage, request.session.goal, ...request.session.completedSteps].filter(Boolean).join(' ');
  const screenText = request.candidates.map(candidateText).join(' ');
  const rankedConcepts = concepts.map((concept) => ({
    concept,
    score: Math.max(...concept.aliases.map((alias) => overlap(query, alias) * 3 + overlap(screenText, alias))),
  })).filter((item) => item.score > 0).sort((a, b) => b.score - a.score).slice(0, 12).map((item) => item.concept);

  const graphScores = taskGraphs.map((graph) => ({
    graph,
    score: overlap(query, `${graph.taskId} ${graph.intent.action} ${graph.intent.object} ${graph.desiredOutcome}`)
      + graph.states.flatMap((state) => state.evidenceConceptIds).filter((id) => rankedConcepts.some((concept) => concept.conceptId === id)).length * 4,
  })).sort((a, b) => b.score - a.score);
  const likelyTask = graphScores[0]?.score > 0 ? graphScores[0].graph : null;

  const relatedGoldSteps = gold.map((record) => ({
    record,
    score: overlap(query, `${record.source.userQuestion} ${record.intent.keyPhrases.join(' ')} ${record.task.desiredOutcome}`)
      + (likelyTask?.taskId === record.task.taskId ? 10 : 0),
  })).filter((item) => item.score > 0).sort((a, b) => b.score - a.score).slice(0, 5).map((item) => item.record);
  return { likelyTask, relevantConcepts: rankedConcepts, relatedGoldSteps };
}

export function resolveSemanticTarget(
  conceptId: string,
  candidates: UiCandidate[],
  concepts: ConceptDictionaryEntry[],
): UiCandidate | undefined {
  const concept = concepts.find((item) => item.conceptId === conceptId);
  if (!concept) return undefined;
  return candidates.filter((candidate) => candidate.enabled && candidate.visible && candidate.clickable)
    .map((candidate) => {
      const text = candidateText(candidate);
      const aliasScore = Math.max(...concept.aliases.map((alias) => overlap(text, alias) * 10));
      const roleScore = concept.preferredRoles.includes(candidate.role) ? 8 : 0;
      const processName = String(candidate.attributes?.processName ?? '').toLocaleLowerCase();
      const processScore = concept.platformHints?.processNames.some((name) => processName === name.toLocaleLowerCase()) ? 6 : 0;
      return { candidate, score: aliasScore + roleScore + processScore };
    })
    .filter((item) => item.score > 0)
    .sort((a, b) => b.score - a.score)[0]?.candidate;
}

export function expectedTransitionMatches(
  expectedEvidenceConceptIds: readonly string[],
  candidates: UiCandidate[],
  concepts: ConceptDictionaryEntry[],
): boolean {
  if (expectedEvidenceConceptIds.length === 0) return true;
  return expectedEvidenceConceptIds.some((conceptId) =>
    resolveSemanticTarget(conceptId, candidates, concepts) !== undefined);
}
