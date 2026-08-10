import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { MockAiProvider } from '../../../../src/guide-api/MockAiProvider';
import type { ApiConfig } from '../config';
import { FeatherlessProvider } from './FeatherlessProvider';
import { KnowledgeGroundedProvider } from './KnowledgeGroundedProvider';

export function createAiProvider(config: ApiConfig): AiProvider {
  if (config.aiMode === 'mock') return new MockAiProvider();
  if (!config.featherless) throw new Error('Featherless configuration is unavailable.');
  return new KnowledgeGroundedProvider(new FeatherlessProvider(config.featherless));
}
