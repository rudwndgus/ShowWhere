import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { MockAiProvider } from '../../../../src/guide-api/MockAiProvider';
import type { ApiConfig } from '../config';
import { FeatherlessProvider } from './FeatherlessProvider';
import { BrainV2Provider } from './BrainV2Provider';

export function createAiProvider(config: ApiConfig): AiProvider {
  const legacy = config.aiMode === 'mock'
    ? new MockAiProvider()
    : new FeatherlessProvider(config.featherless!);
  if (config.brainMode === 'legacy') return legacy;
  return new BrainV2Provider({
    mode: config.brainMode,
    baseUrl: config.brainV2.baseUrl,
    apiToken: config.brainV2.apiToken,
    timeoutMs: config.brainV2.timeoutMs,
    maxRetries: config.brainV2.maxRetries,
    dataRoot: config.brainV2.dataRoot,
    memoryReuseThreshold: config.brainV2.memoryReuseThreshold,
    rerankThreshold: config.brainV2.rerankThreshold,
    legacy,
  });
}
