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
  /** Added in Android 1.4.15 / the next iOS build; older installs reject (unimplemented). */
  openAppSettings(): Promise<void>;
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

/** Open this app's OS settings page so a learner can re-allow the microphone. False on web or on app builds that predate the method. */
export async function tryOpenNativeAppSettings(): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return false;
  }

  try {
    await SpeakingRecorder.openAppSettings();
    return true;
  } catch {
    return false;
  }
}

export interface MicrophoneErrorInfo {
  message: string;
  permissionDenied: boolean;
}

/**
 * One learner-facing message for every way microphone capture can fail to start:
 * browser DOMExceptions (web, and the WebView inside the apps) and the native
 * SpeakingRecorder plugin's own "permission was denied" rejection. The recorder
 * wording is the default; `live` words it for the live AI patient, where the
 * learner speaks (the control reads "Start speaking") and nothing is recorded.
 */
export function describeMicrophoneError(
  error: unknown,
  native = Capacitor.isNativePlatform(),
  options: { live?: boolean } = {},
): MicrophoneErrorInfo {
  const live = options.live === true;
  const startControl = live ? 'Start speaking' : 'Start recording';
  const name = error instanceof DOMException ? error.name : '';
  const text = error instanceof Error ? error.message : '';

  if (name === 'NotAllowedError' || name === 'SecurityError' || /permission.*denied/i.test(text)) {
    return {
      permissionDenied: true,
      message: native
        ? `Microphone permission was blocked. Allow Microphone for this app in your device Settings (Open app settings), then press ${startControl} again.`
        : `Microphone permission was blocked. Allow microphone access in your browser settings, then press ${startControl} again.`,
    };
  }

  const message = (() => {
    switch (name) {
      case 'NotFoundError':
      case 'DevicesNotFoundError':
        return 'No microphone was found. Connect a microphone and try again.';
      case 'NotReadableError':
      case 'TrackStartError':
        return 'Your microphone is busy or unavailable. Close other apps using it and try again.';
      case 'OverconstrainedError':
      case 'ConstraintNotSatisfiedError':
        return live
          ? 'This microphone does not support the requested audio settings. Try another device.'
          : 'This microphone does not support the requested recording settings. Try another device.';
      case 'NotSupportedError':
        return live
          ? 'This browser does not support the audio mode needed for Speaking practice.'
          : 'This browser does not support the recording mode needed for Speaking practice.';
      default:
        return text
          ? `${live ? 'The microphone' : 'Recording'} could not start: ${text}`
          : 'Could not start the microphone. Check your audio settings and try again.';
    }
  })();

  return { message, permissionDenied: false };
}
