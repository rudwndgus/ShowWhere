import { describe, expect, it } from 'vitest';
import { loadApiConfig } from './config';

describe('API environment configuration', () => {
  it('uses GPT-5.6 as the default model', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'test-key' });
    expect(config.openai.model).toBe('gpt-5.6');
    expect(config.openai.baseUrl).toBe('https://api.openai.com/v1');
    expect(config.huggingFace).toBeUndefined();
  });

  it('requires a server-only OpenAI API key', () => {
    expect(() => loadApiConfig({})).toThrow();
  });

  it('enables optional Hugging Face semantic routing without exposing it to the client', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'openai', HF_TOKEN: 'hf-secret' });
    expect(config.huggingFace?.model).toBe('Qwen/Qwen3-Embedding-0.6B');
    expect(config.huggingFace?.token).toBe('hf-secret');
  });
});
