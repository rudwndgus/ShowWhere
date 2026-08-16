export interface SpeechTranscription {
  text: string;
  providerLatencyMs: number;
}

export interface SpeechTranscriber {
  transcribe(audio: Buffer, contentType: string): Promise<SpeechTranscription>;
}

export interface OpenAiSpeechTranscriberOptions {
  apiKey: string;
  baseUrl: string;
  model: string;
  requestTimeoutMs: number;
}

function extensionFor(contentType: string): string {
  if (contentType.includes('wav')) return 'wav';
  if (contentType.includes('mp4') || contentType.includes('m4a')) return 'm4a';
  if (contentType.includes('ogg')) return 'ogg';
  if (contentType.includes('mpeg') || contentType.includes('mp3')) return 'mp3';
  return 'webm';
}

export class OpenAiSpeechTranscriber implements SpeechTranscriber {
  constructor(private readonly options: OpenAiSpeechTranscriberOptions) {}

  async transcribe(audio: Buffer, contentType: string): Promise<SpeechTranscription> {
    const startedAt = performance.now();
    let lastError: unknown;
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        const text = await this.transcribeOnce(audio, contentType);
        return { text, providerLatencyMs: Math.round(performance.now() - startedAt) };
      } catch (error) {
        lastError = error;
        const retryable = error instanceof Error
          && (error.message === 'transcription_provider_network'
            || error.message === 'transcription_provider_timeout'
            || /^transcription_provider_(408|409|429|5\d\d)$/u.test(error.message));
        if (!retryable || attempt > 0) throw error;
        await new Promise((resolve) => setTimeout(resolve, 150));
      }
    }
    throw lastError;
  }

  private async transcribeOnce(audio: Buffer, contentType: string): Promise<string> {
    const form = new FormData();
    form.set('model', this.options.model);
    form.set('response_format', 'json');
    form.set(
      'prompt',
      'A short computer guidance request. Preserve product names and visible UI labels exactly.',
    );
    if (this.options.model === 'gpt-transcribe') {
      form.append('languages[]', 'en');
      form.append('languages[]', 'ko');
    }
    form.set('file', new Blob([Uint8Array.from(audio)], { type: contentType }), `showwhere-speech.${extensionFor(contentType)}`);

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.options.requestTimeoutMs);
    try {
      const response = await fetch(`${this.options.baseUrl}/audio/transcriptions`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${this.options.apiKey}` },
        body: form,
        signal: controller.signal,
      });
      if (!response.ok) throw new Error(`transcription_provider_${response.status}`);
      const body = await response.json() as { text?: unknown };
      if (typeof body.text !== 'string') throw new Error('transcription_provider_malformed');
      return body.text.trim();
    } catch (error) {
      if (error instanceof Error && error.name === 'AbortError')
        throw new Error('transcription_provider_timeout', { cause: error });
      if (error instanceof Error && error.message.startsWith('transcription_provider_')) throw error;
      throw new Error('transcription_provider_network', { cause: error });
    } finally {
      clearTimeout(timeout);
    }
  }
}
