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
    const form = new FormData();
    form.set('model', this.options.model);
    form.set('response_format', 'json');
    form.set(
      'prompt',
      'Transcribe verbatim. Preserve Korean, English, mixed-language speech, product names, and UI labels exactly. Do not summarize or answer.',
    );
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
      return { text: body.text.trim(), providerLatencyMs: Math.round(performance.now() - startedAt) };
    } finally {
      clearTimeout(timeout);
    }
  }
}
