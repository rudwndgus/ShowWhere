import { describe, expect, it, vi } from 'vitest';
import { HuggingFaceIntentClassifier } from './HuggingFaceIntentClassifier';

describe('Hugging Face intent classifier', () => {
  it('embeds immutable intent prototypes once and only the question afterwards', async () => {
    const embed = vi.fn(async (texts: string[]) => texts.map((text) =>
      text.includes('printer') || text.includes('프린터') ? [1, 0] : [0, 1]));
    const classifier = new HuggingFaceIntentClassifier({ embed }, { catalogs: [], patterns: [] });
    await classifier.classify('프린터가 안 돼');
    await classifier.classify('프린터 문제 해결');

    expect(embed).toHaveBeenCalledTimes(3);
    expect(embed.mock.calls[0][0].length).toBeGreaterThan(50);
    expect(embed.mock.calls[1][0]).toHaveLength(1);
    expect(embed.mock.calls[2][0]).toHaveLength(1);
  });
});
