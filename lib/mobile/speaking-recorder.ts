'use client';

import { registerPlugin } from '@capacitor/core';

export interface NativeSpeakingRecorderStartResult {
  mimeType: string;
  fileName: string;
  startedAt: number;
}

export interface NativeSpeakingRecorderStopResult {
  base64: string;
  mimeType: string;
  fileName: string;
  durationMs: number;
}

export interface NativeSpeakingRecorderPlugin {
  start(options?: { fileName?: string; mimeType?: string }): Promise<NativeSpeakingRecorderStartResult>;
  pause(): Promise<void>;
  resume(): Promise<void>;
  stop(): Promise<NativeSpeakingRecorderStopResult>;
  cancel(): Promise<void>;
}

export const SpeakingRecorder = registerPlugin<NativeSpeakingRecorderPlugin>('SpeakingRecorder');
export function base64ToBlob(base64: string, mimeType: string): Blob {
  const binary = globalThis.atob(base64);
  const bytes = new Uint8Array(binary.length);

  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return new Blob([bytes], { type: mimeType });
}
