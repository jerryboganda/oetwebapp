/**
 * Platform-aware audio recorder. On Capacitor native, routes to the
 * @capacitor-community/voice-recorder plugin. On web (and as fallback),
 * uses MediaRecorder. API surface is identical so callers do not branch.
 */

export interface AudioRecorder {
  start(): Promise<void>;
  stop(): Promise<Blob>;
  pause(): Promise<void>;
  resume(): Promise<void>;
  isRecording(): boolean;
}

type NativeVoiceRecorderPlugin = {
  startRecording: () => Promise<unknown>;
  stopRecording: () => Promise<{ value: { recordDataBase64: string; mimeType: string } }>;
};

function isCapacitorNative(): boolean {
  if (typeof window === 'undefined') return false;
  const w = window as unknown as { Capacitor?: { isNativePlatform?: () => boolean } };
  return Boolean(w.Capacitor?.isNativePlatform?.());
}

class WebRecorder implements AudioRecorder {
  private rec: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private recording = false;

  async start(): Promise<void> {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    this.chunks = [];
    this.rec = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
    this.rec.ondataavailable = (e) => { if (e.data.size) this.chunks.push(e.data); };
    this.rec.start(1000);
    this.recording = true;
  }

  stop(): Promise<Blob> {
    return new Promise((resolve) => {
      if (!this.rec) { resolve(new Blob([])); return; }
      this.rec.onstop = () => {
        this.recording = false;
        const blob = new Blob(this.chunks, { type: 'audio/webm' });
        resolve(blob);
      };
      this.rec.stop();
      this.rec.stream.getTracks().forEach((t) => t.stop());
    });
  }

  async pause(): Promise<void> { this.rec?.pause(); }
  async resume(): Promise<void> { this.rec?.resume(); }
  isRecording(): boolean { return this.recording; }
}

class NativeRecorder implements AudioRecorder {
  private recording = false;
  private plugin: NativeVoiceRecorderPlugin | null = null;

  private async ensurePlugin() {
    if (this.plugin) return;
    const mod = await import(/* @vite-ignore */ 'capacitor-voice-recorder')
      .catch(() => null);
    this.plugin = (mod as unknown as { VoiceRecorder?: NativeVoiceRecorderPlugin } | null)?.VoiceRecorder ?? null;
    if (!this.plugin) throw new Error('Voice recorder plugin missing.');
  }

  async start(): Promise<void> { await this.ensurePlugin(); await this.plugin!.startRecording(); this.recording = true; }
  async stop(): Promise<Blob> {
    if (!this.plugin) return new Blob([]);
    const res = await this.plugin.stopRecording();
    this.recording = false;
    const bytes = Uint8Array.from(atob(res.value.recordDataBase64), (c) => c.charCodeAt(0));
    return new Blob([bytes], { type: res.value.mimeType || 'audio/m4a' });
  }
  async pause(): Promise<void> { /* plugin currently does not support pause */ }
  async resume(): Promise<void> { /* plugin currently does not support resume */ }
  isRecording(): boolean { return this.recording; }
}

export function createAudioRecorder(): AudioRecorder {
  return isCapacitorNative() ? new NativeRecorder() : new WebRecorder();
}
