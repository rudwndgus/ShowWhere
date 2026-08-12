import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import type { ApiConfig } from '../config';
import { OpenAiGuideProvider } from './OpenAiGuideProvider';
import { HuggingFaceEmbeddingClient } from './HuggingFaceEmbeddingClient';
import { HybridGuideProvider } from './HybridGuideProvider';

export function createAiProvider(config: ApiConfig): AiProvider {
  const openai = new OpenAiGuideProvider(config.openai);
  const embeddings = config.huggingFace
    ? new HuggingFaceEmbeddingClient(config.huggingFace)
    : undefined;
  return new HybridGuideProvider(openai, embeddings, {
    minScore: config.huggingFace?.minScore ?? 0.68,
    minMargin: config.huggingFace?.minMargin ?? 0.08,
    debug: config.debug,
  });
}
