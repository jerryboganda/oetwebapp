'use client';

import { Capacitor, registerPlugin } from '@capacitor/core';

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

export type SpeakingRecordingCaptureMethod = 'browser-recording' | 'native-speaking-recorder' | 'desktop-recorder';

export interface CapturedSpeakingRecording {
  blob: Blob;
  mimeType: string;
  fileName: string;
  durationMs: number;
  captureMethod: SpeakingRecordingCaptureMethod;
  pauseSupported: boolean;
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

export function nativeSpeakingPauseSupported(platform = Capacitor.getPlatform()): boolean {
  return platform === 'ios';
}

export function capturedSpeakingRecordingFromNativeStop(
  result: NativeSpeakingRecorderStopResult,
  fallbackFileName: string,
): CapturedSpeakingRecording {
  const mimeType = result.mimeType || 'audio/mp4';
  const fileName = result.fileName || fallbackFileName;
  return {
    blob: base64ToBlob(result.base64, mimeType),
    mimeType,
    fileName,
    durationMs: Math.max(0, result.durationMs || 0),
    captureMethod: 'native-speaking-recorder',
    pauseSupported: nativeSpeakingPauseSupported(),
  };
}

export function capturedSpeakingRecordingFromWebBlob(
  blob: Blob,
  fallbackFileName: string,
  durationMs: number,
): CapturedSpeakingRecording {
  const mimeType = blob.type || 'audio/webm';
  return {
    blob,
    mimeType,
    fileName: fallbackFileName,
    durationMs: Math.max(0, durationMs),
    captureMethod: 'browser-recording',
    pauseSupported: true,
  };
}

export async function tryPauseNativeSpeakingRecorder(): Promise<boolean> {
  if (!nativeSpeakingPauseSupported()) {
    return false;
  }

  try {
    await SpeakingRecorder.pause();
    return true;
  } catch {
    return false;
  }
}

export async function tryResumeNativeSpeakingRecorder(): Promise<boolean> {
  if (!nativeSpeakingPauseSupported()) {
    return false;
  }

  try {
    await SpeakingRecorder.resume();
    return true;
  } catch {
    return false;
  }
}
