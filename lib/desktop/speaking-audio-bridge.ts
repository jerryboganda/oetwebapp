/**
 * Electron-aware audio bridge. When window.desktopBridge.speakingAudio is
 * present (preload script exposes it via contextBridge), use IPC. Otherwise
 * fall back to the standard Capacitor/web recorder.
 */
import { createAudioRecorder, type AudioRecorder } from '@/lib/native/audio-recorder-bridge';

interface DesktopBridge {
  speakingAudio?: {
    start(): Promise<void>;
    stop(): Promise<{ base64: string; mimeType: string }>;
    getPlatform(): 'win32' | 'darwin' | 'linux';
  };
}

function getDesktopBridge(): DesktopBridge | undefined {
  if (typeof window === 'undefined') return undefined;
  return (window as unknown as { desktopBridge?: DesktopBridge }).desktopBridge;
}

class ElectronRecorder implements AudioRecorder {
  private recording = false;
  constructor(private readonly bridge: NonNullable<DesktopBridge['speakingAudio']>) {}

  async start(): Promise<void> { await this.bridge.start(); this.recording = true; }
  async stop(): Promise<Blob> {
    const res = await this.bridge.stop();
    this.recording = false;
    const bytes = Uint8Array.from(atob(res.base64), (c) => c.charCodeAt(0));
    return new Blob([bytes], { type: res.mimeType });
  }
  async pause(): Promise<void> { /* not yet supported via IPC */ }
  async resume(): Promise<void> { /* not yet supported via IPC */ }
  isRecording(): boolean { return this.recording; }
}

export function createSpeakingAudioRecorder(): AudioRecorder {
  const bridge = getDesktopBridge()?.speakingAudio;
  if (bridge) return new ElectronRecorder(bridge);
  return createAudioRecorder();
}
