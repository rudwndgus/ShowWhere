import {
  GuidanceTeachingRecordV2Schema,
  type ConceptDictionaryEntry,
  type GuidanceTeachingRecordV2,
  type TaskGraph,
  type TeachingValidationResult,
} from './contracts';

const sensitivePatterns: Array<{ code: string; pattern: RegExp }> = [
  { code: 'possible_api_key', pattern: /(?:api[_-]?key|bearer)\s*[:=]?\s*[a-z0-9_-]{16,}/iu },
  { code: 'possible_password', pattern: /(?:password|비밀번호)\s*[:=]\s*\S+/iu },
  { code: 'possible_email', pattern: /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/iu },
  { code: 'possible_card', pattern: /\b(?:\d[ -]*?){13,19}\b/u },
];
const coordinatePattern = /(?:\bx\s*[=:]\s*-?\d+|\by\s*[=:]\s*-?\d+|\b(?:left|top|width|height)\s*[=:]\s*\d+)/iu;

export interface SemanticValidationContext {
  concepts: ConceptDictionaryEntry[];
  taskGraphs: TaskGraph[];
  existingGold: GuidanceTeachingRecordV2[];
  allowNewConceptIds?: readonly string[];
  allowNewTaskId?: boolean;
}

export function validateTeachingRecord(
  value: unknown,
  validationContext: SemanticValidationContext,
): TeachingValidationResult {
  const parsed = GuidanceTeachingRecordV2Schema.safeParse(value);
  const issues: TeachingValidationResult['issues'] = [];
  if (!parsed.success) {
    for (const issue of parsed.error.issues) {
      issues.push({ code: 'schema_invalid', path: issue.path.join('.'), message: issue.message, severity: 'error' });
    }
    return { valid: false, confidence: 0, issues, shortReason: 'Semantic v2 schema validation failed.' };
  }
  const record = parsed.data;
  const allText = JSON.stringify(record.source);
  if (coordinatePattern.test(JSON.stringify(record))) {
    issues.push({ code: 'coordinates_forbidden', path: '', message: 'Semantic Gold must not contain runtime coordinates.', severity: 'error' });
  }
  for (const sensitive of sensitivePatterns) {
    if (sensitive.pattern.test(allText)) {
      issues.push({ code: sensitive.code, path: 'source', message: 'Possible secret or personal data must be removed before Gold save.', severity: 'error' });
    }
  }

  const conceptIds = new Set(validationContext.concepts.map((concept) => concept.conceptId));
  const allowedNew = new Set(validationContext.allowNewConceptIds ?? []);
  const referencedConcepts = [
    ...record.state.visibleConcepts.map((concept) => concept.conceptId),
    ...(record.decision.target ? [record.decision.target.conceptId, ...record.decision.target.confusableNegativeConceptIds] : []),
    ...record.expectedTransition.evidenceConcepts,
  ];
  for (const conceptId of new Set(referencedConcepts)) {
    if (!conceptIds.has(conceptId) && !allowedNew.has(conceptId)) {
      issues.push({ code: 'unknown_concept', path: 'decision/state', message: `Unknown concept ID: ${conceptId}`, severity: 'error' });
    }
  }

  const graph = validationContext.taskGraphs.find((candidate) => candidate.taskId === record.task.taskId);
  if (!graph && !validationContext.allowNewTaskId) {
    issues.push({ code: 'unknown_task', path: 'task.taskId', message: `Unknown task ID: ${record.task.taskId}`, severity: 'error' });
  }
  if (graph) {
    const currentState = graph.states.find((state) => state.stateId === record.state.stateId);
    const nextState = graph.states.find((state) => state.stateId === record.expectedTransition.nextStateId);
    if (!currentState) issues.push({ code: 'unknown_state', path: 'state.stateId', message: 'Current state is not in the task graph.', severity: 'error' });
    if (!nextState) issues.push({ code: 'unknown_next_state', path: 'expectedTransition.nextStateId', message: 'Next state is not in the task graph.', severity: 'error' });
    if (currentState && nextState && record.decision.action === 'highlight') {
      const transition = currentState.transitions.find((item) =>
        item.nextStateId === nextState.stateId && item.targetConceptId === record.decision.target?.conceptId);
      if (!transition) issues.push({ code: 'impossible_transition', path: 'expectedTransition', message: 'No matching task-graph transition exists.', severity: 'error' });
    }
  }

  const duplicate = validationContext.existingGold.find((existing) =>
    existing.task.taskId === record.task.taskId
    && existing.state.stateId === record.state.stateId
    && existing.intent.action === record.intent.action
    && existing.intent.object === record.intent.object
    && existing.id !== record.id);
  if (duplicate) {
    const sameTarget = duplicate.decision.target?.conceptId === record.decision.target?.conceptId
      && duplicate.expectedTransition.nextStateId === record.expectedTransition.nextStateId;
    issues.push({
      code: sameTarget ? 'duplicate_gold' : 'conflicting_gold',
      path: 'task/state',
      message: sameTarget ? `Equivalent Gold already exists: ${duplicate.id}` : `Conflicting Gold exists: ${duplicate.id}`,
      severity: 'error',
    });
  }

  const hasError = issues.some((issue) => issue.severity === 'error');
  return {
    valid: !hasError,
    confidence: hasError ? 0 : issues.length > 0 ? 0.85 : 1,
    issues,
    shortReason: hasError
      ? 'The record is not safe to save as Gold until validation errors are fixed.'
      : issues.length > 0 ? 'The record is valid with review warnings.' : 'The semantic step is consistent and Gold-ready.',
  };
}
