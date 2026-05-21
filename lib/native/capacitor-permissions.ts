/**
 * Capacitor-aware microphone permission helper.
 * Falls through to the web Permissions API when not running on native.
 */

export type MicPermissionState = 'granted' | 'denied' | 'prompt' | 'unsupported';

function isCapacitorNative(): boolean {
  if (typeof window === 'undefined') return false;
  const w = window as unknown as { Capacitor?: { isNativePlatform?: () => boolean } };
  return Boolean(w.Capacitor?.isNativePlatform?.());
}

export async function requestMicrophonePermission(): Promise<MicPermissionState> {
  if (isCapacitorNative()) {
    try {
      // Dynamic import so the web bundle does not require the plugin.
      const mod = await import(/* @vite-ignore */ 'capacitor-voice-recorder')
        .catch(() => null);
      const VoiceRecorder = (mod as unknown as { VoiceRecorder?: { requestAudioRecordingPermission: () => Promise<{ value: boolean }> } } | null)?.VoiceRecorder;
      if (!VoiceRecorder) return 'unsupported';
      const res = await VoiceRecorder.requestAudioRecordingPermission();
      return res.value ? 'granted' : 'denied';
    } catch {
      return 'unsupported';
    }
  }

  if (typeof navigator === 'undefined' || !navigator.permissions) return 'unsupported';
  try {
    const status = await navigator.permissions.query({ name: 'microphone' as PermissionName });
    return status.state as MicPermissionState;
  } catch {
    return 'unsupported';
  }
}
