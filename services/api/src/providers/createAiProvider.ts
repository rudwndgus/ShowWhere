import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import type { ApiConfig } from '../config';
import { OpenAiGuideProvider } from './OpenAiGuideProvider';

export function createAiProvider(config: ApiConfig): AiProvider {
  return new OpenAiGuideProvider(config.openai);
}
