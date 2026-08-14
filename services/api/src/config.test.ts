import { describe, expect, it } from 'vitest';
import { loadApiConfig } from './config';

describe('API environment configuration', () => {
  it('uses the efficient GPT-5.6 model tiers by default', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'test-key' });
    expect(config.openai.fastModel).toBe('gpt-5.6-luna');
    expect(config.openai.model).toBe('gpt-5.6-terra');
    expect(config.openai.strongModel).toBe('gpt-5.6-sol');
    expect(config.openai.sttModel).toBe('gpt-4o-transcribe');
    expect(config.host).toBe('127.0.0.1');
    expect(config.port).toBe(8787);
    expect(config.pairingTtlSeconds).toBe(30);
    expect(config.openai.baseUrl).toBe('https://api.openai.com/v1');
    expect(config.huggingFace).toBeUndefined();
  });

  it('uses a hosting platform PORT and enables public binding', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'test-key', PORT: '10000' });
    expect(config.host).toBe('0.0.0.0');
    expect(config.port).toBe(10_000);
  });

  it('treats blank optional tokens as disabled', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'test-key', HF_TOKEN: '', SHOWWHERE_CLIENT_TOKEN: '' });
    expect(config.huggingFace).toBeUndefined();
    expect(config.security.clientToken).toBeUndefined();
  });

  it('requires a server-only OpenAI API key', () => {
    expect(() => loadApiConfig({})).toThrow();
  });

  it('enables optional Hugging Face semantic routing without exposing it to the client', () => {
    const config = loadApiConfig({ OPENAI_API_KEY: 'openai', HF_TOKEN: 'hf-secret' });
    expect(config.huggingFace?.model).toBe('intfloat/multilingual-e5-large');
    expect(config.huggingFace?.token).toBe('hf-secret');
  });
});
