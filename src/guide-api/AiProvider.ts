import type { GuideRequest } from '../contracts';

export interface AiProvider {
  decideNextAction(request: GuideRequest): Promise<unknown>;
}
