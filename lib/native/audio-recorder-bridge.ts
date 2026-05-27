/**
 * Platform-aware audio recorder. On Capacitor native, routes to the shared
 * SpeakingRecorder plugin. On web (and as fallback), uses MediaRecorder.
 * API surface is identical so callers do not branch.
 */

import {
  SpeakingRecorder,
  base64ToBlob,
  tryPauseNativeSpeakingRecorder,
  tryResumeNativeSpeakingRecorder,
} from '@/lib/mobile/speaking-recorder';

export interface AudioRecorder {
  start(): Promise<void>;
  stop(): Promise<Blob>;
  pause(): Promise<void>;
  resume(): Promise<void>;
  isRecording(): boolean;
}

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

  async start(): Promise<void> { await SpeakingRecorder.start({ mimeType: 'audio/mp4' }); this.recording = true; }
  async stop(): Promise<Blob> {
    const res = await SpeakingRecorder.stop();
    this.recording = false;
    return base64ToBlob(res.base64, res.mimeType || 'audio/mp4');
  }
  async pause(): Promise<void> { await tryPauseNativeSpeakingRecorder(); }
  async resume(): Promise<void> { await tryResumeNativeSpeakingRecorder(); }
  isRecording(): boolean { return this.recording; }
}

export function createAudioRecorder(): AudioRecorder {
  return isCapacitorNative() ? new NativeRecorder() : new WebRecorder();
}
