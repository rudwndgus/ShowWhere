import type { GuideRequest } from '../contracts';
import type { LearningEventV2 } from './contracts';
import type { OutcomeVerifier } from './interfaces';

function normalize(value: string): string {
  return value.toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

export class EvidenceOutcomeVerifier implements OutcomeVerifier {
  async verify(event: LearningEventV2, nextRequest: GuideRequest): Promise<Partial<LearningEventV2>> {
    const observed = [
      nextRequest.context.applicationName,
      nextRequest.context.windowTitle,
      ...nextRequest.candidates.flatMap((candidate) => [candidate.label, candidate.description]),
    ].filter((value): value is string => Boolean(value)).join(' | ');
    const normalizedObserved = normalize(observed);
    const evidence = event.expectedEvidence.map(normalize).filter(Boolean);
    const evidenceMatched = evidence.length > 0 && evidence.some((item) => normalizedObserved.includes(item));
    const stateChanged = normalize(event.stateBefore ?? '') !== normalize(`${nextRequest.context.applicationName}.${nextRequest.context.windowTitle ?? ''}`);
    const transitionMatched = evidence.length > 0 ? evidenceMatched : stateChanged;
    return {
      observedNextState: `${nextRequest.context.applicationName}.${nextRequest.context.windowTitle ?? ''}`.slice(0, 240),
      transitionMatched,
      taskContinued: transitionMatched,
      finalOutcome: transitionMatched ? 'success' : 'failure',
      authority: transitionMatched ? 'verified_real' : 'raw',
      dataQualityStatus: transitionMatched ? 'verified' : 'scrubbed',
    };
  }
}

