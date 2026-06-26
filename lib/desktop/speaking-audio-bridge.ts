/**
 * Desktop-aware audio bridge. When window.desktopBridge.speakingAudio is
 * present (the Tauri shell injects it), use IPC. Otherwise fall back to the
 * standard Capacitor/web recorder.
 */
import { createAudioRecorder, type AudioRecorder } from '@/lib/native/audio-recorder-bridge';

interface DesktopBridge {
  speakingAudio?: {
    start(sessionId: string, mimeType?: string): Promise<{ ok: boolean; mimeType: string }>;
    stop(sessionId: string, chunks?: Array<ArrayBuffer | ArrayBufferView>): Promise<{ ok: boolean; error?: string }>;
    getBlob(sessionId: string): Promise<{ ok: boolean; data?: ArrayBuffer; mimeType?: string; error?: string }>;
    discard(sessionId: string): Promise<{ ok: boolean }>;
    getPlatform(): NodeJS.Platform;
  };
}

function getDesktopBridge(): DesktopBridge | undefined {
  if (typeof window === 'undefined') return undefined;
  return (window as unknown as { desktopBridge?: DesktopBridge }).desktopBridge;
}

class ElectronRecorder implements AudioRecorder {
  private recording = false;
  private sessionId: string | null = null;
  private mimeType = 'audio/webm';

  constructor(private readonly bridge: NonNullable<DesktopBridge['speakingAudio']>) {}

  async start(): Promise<void> {
    this.sessionId = `speaking-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    const result = await this.bridge.start(this.sessionId, this.mimeType);
    if (!result.ok) throw new Error('Desktop speaking audio bridge failed to start.');
    this.mimeType = result.mimeType || this.mimeType;
    this.recording = true;
  }

  async stop(): Promise<Blob> {
    if (!this.sessionId) throw new Error('Desktop speaking audio bridge was not started.');
    const sessionId = this.sessionId;
    const res = await this.bridge.stop(sessionId);
    this.recording = false;
    if (!res.ok) throw new Error(res.error || 'Desktop speaking audio bridge failed to stop.');
    const blob = await this.bridge.getBlob(sessionId);
    if (!blob.ok || !blob.data) throw new Error(blob.error || 'Desktop speaking audio blob was unavailable.');
    await this.bridge.discard(sessionId);
    this.sessionId = null;
    return new Blob([blob.data], { type: blob.mimeType || this.mimeType });
  }
  async pause(): Promise<void> { /* not yet supported via IPC */ }
  async resume(): Promise<void> { /* not yet supported via IPC */ }
  isRecording(): boolean { return this.recording; }
}

export function createSpeakingAudioRecorder(): AudioRecorder {
  // The desktop IPC surface is exposed for future native capture, but current
  // production recording still depends on Chromium MediaRecorder chunks.
  // Falling back here avoids producing zero-byte desktop recordings.
  return createAudioRecorder();
}
