import { Capacitor } from '@capacitor/core';

/**
 * Capacitor-aware pronunciation recorder wrapper. Mobile apps route audio
 * capture through the Capacitor Voice Recorder plugin (when present) to get
 * better mic-quality control, echo cancellation, and background-safe state.
 * On web this resolves to null; callers fall back to the DOM MediaRecorder
 * in <c>hooks/usePronunciationRecorder.ts</c>.
 *
 * Keeping this behind a thin interface means importing this module on web
 * still works — it simply returns `null` and doesn't pull in the Capacitor
 * plugin at all.
 */

export interface NativeRecorderResult {
  base64: string;
  mimeType: string;
  durationMs: number;
}

interface VoiceRecorderPlugin {
  requestAudioRecordingPermission(): Promise<{ value: boolean }>;
  hasAudioRecordingPermission(): Promise<{ value: boolean }>;
  startRecording(): Promise<{ value: boolean }>;
  stopRecording(): Promise<{ value: { recordDataBase64: string; msDuration: number; mimeType: string } }>;
  pauseRecording(): Promise<{ value: boolean }>;
  resumeRecording(): Promise<{ value: boolean }>;
  getCurrentStatus(): Promise<{ status: string }>;
}

let pluginCache: VoiceRecorderPlugin | null | undefined;

async function getPlugin(): Promise<VoiceRecorderPlugin | null> {
  if (pluginCache !== undefined) return pluginCache;
  if (!Capacitor.isNativePlatform()) {
    pluginCache = null;
    return null;
  }
  try {
    // Dynamic import so web builds don't bundle the plugin at all
    const mod = (await import(
      /* webpackIgnore: true */
      /* @vite-ignore */
      // @ts-expect-error — optional peer dep: @capacitor-community/voice-recorder
      '@capacitor-community/voice-recorder'
    )) as { VoiceRecorder: VoiceRecorderPlugin };
    pluginCache = mod?.VoiceRecorder ?? null;
  } catch {
    pluginCache = null;
  }
  return pluginCache;
}

export function isNativeRecorderAvailable(): boolean {
  return Capacitor.isNativePlatform();
}

export async function requestNativePermission(): Promise<boolean> {
  const plugin = await getPlugin();
  if (!plugin) return false;
  try {
    const has = await plugin.hasAudioRecordingPermission();
    if (has.value) return true;
    const req = await plugin.requestAudioRecordingPermission();
    return req.value;
  } catch {
    return false;
  }
}

export async function startNativeRecording(): Promise<boolean> {
  const plugin = await getPlugin();
  if (!plugin) return false;
  try {
    const res = await plugin.startRecording();
    return res.value;
  } catch {
    return false;
  }
}

export async function stopNativeRecording(): Promise<NativeRecorderResult | null> {
  const plugin = await getPlugin();
  if (!plugin) return null;
  try {
    const res = await plugin.stopRecording();
    return {
      base64: res.value.recordDataBase64,
      mimeType: res.value.mimeType || 'audio/aac',
      durationMs: res.value.msDuration,
    };
  } catch {
    return null;
  }
}

/** Utility: convert a Capacitor base64 result to a Blob usable by fetch(). */
export function nativeResultToBlob(result: NativeRecorderResult): Blob {
  const binary = atob(result.base64);
  const len = binary.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i++) bytes[i] = binary.charCodeAt(i);
  return new Blob([bytes], { type: result.mimeType });
}
