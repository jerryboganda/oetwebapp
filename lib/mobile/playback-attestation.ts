'use client';

import { Capacitor, registerPlugin, type PluginListenerHandle } from '@capacitor/core';

/**
 * Native HMAC attestation for app-only video playback (Capacitor shells >= 1.2.0).
 *
 * The native side signs "{nonce}|{videoId}|{userId}|{platform}|{keyId}" with
 * HMAC-SHA256 using a build-time secret that lives only in native code (Android
 * BuildConfig / iOS Info.plist injected by CI) and returns the signature as
 * lowercase hex. JS can only obtain signatures over server-issued nonces — it
 * can never read the secret.
 */

export interface PlaybackAttestationSignResult {
  /** Lowercase-hex HMAC-SHA256 over the pinned challenge message. */
  signature: string;
  platform: 'capacitor-android' | 'capacitor-ios';
  keyId: string;
  appVersion: string;
}

export interface PlaybackAttestationSecureScreenResult {
  /**
   * True when the platform engaged capture protection: Android FLAG_SECURE
   * (black in screenshots + recordings), iOS recording/mirroring blackout via
   * UIScreen.isCaptured (blanks during active capture; iOS cannot black-out a
   * single still screenshot without DRM).
   */
  ok: boolean;
}

export interface PlaybackAttestationIntegrityResult {
  /** Best-effort root/jailbreak/emulator/debugger signals — never exhaustive,
   * every check is independently defeatable. Empty on desktop/web (native-only). */
  signals: string[];
  isSuspicious: boolean;
}

/**
 * Hardware-attestation material (Play Integrity / App Attest), distinct from the
 * heuristic {@link PlaybackAttestationIntegrityResult}. The `token` is OPAQUE
 * and platform-defined — JS must never try to interpret it; only the server
 * verifies it.
 */
export interface PlaybackAttestationIntegrityTokenResult {
  /**
   * Opaque platform attestation material:
   *  - Android: the Play Integrity token (the JWE string from the Play Integrity
   *    `StandardIntegrityTokenProvider` / classic `IntegrityTokenResponse.token`).
   *  - iOS: base64 of the `DCAppAttestService` attestation object (first run) or
   *    assertion (subsequent runs).
   */
  token: string;
  /** iOS App Attest key id (`DCAppAttestService.generateKey` result). Null on Android. */
  keyId?: string | null;
}

export interface PlaybackAttestationPlugin {
  sign(options: { nonce: string; videoId: string; userId: string }): Promise<PlaybackAttestationSignResult>;
  setSecureScreen(options: { enabled: boolean }): Promise<PlaybackAttestationSecureScreenResult>;
  /** Native-only (Android + iOS). Rejects on desktop/web shells that don't implement it. */
  getIntegrity(): Promise<PlaybackAttestationIntegrityResult>;
  /**
   * Native-only (Android + iOS). OPTIONAL — shells predating hardware attestation
   * do not implement it. Returns a real hardware attestation token (Play
   * Integrity / App Attest) bound to the supplied server-issued `nonce`.
   * Feature-detect via the {@link getIntegrityToken} helper, which returns null
   * (never throws) when the method is missing or the native call rejects.
   */
  getIntegrityToken?(options: { nonce: string }): Promise<PlaybackAttestationIntegrityTokenResult>;
  /** iOS only: fires whenever `UIApplication.userDidTakeScreenshotNotification`
   * is posted. Detection-after-the-fact only — iOS cannot block a still
   * screenshot of an arbitrary WKWebView. Android blocks screenshots outright
   * via FLAG_SECURE (see setSecureScreen) so never fires this event. */
  addListener(
    eventName: 'screenshotTaken',
    listenerFunc: () => void,
  ): Promise<PluginListenerHandle>;
  /** iOS only: fires whenever `UIScreen.isCaptured` changes (recording/mirroring
   * start or stop). Mirrors the native blackout that `setSecureScreen` applies. */
  addListener(
    eventName: 'captureStateChanged',
    listenerFunc: (data: { isCaptured: boolean }) => void,
  ): Promise<PluginListenerHandle>;
}

export const PlaybackAttestation = registerPlugin<PlaybackAttestationPlugin>('PlaybackAttestation');

/**
 * True when running inside a native shell that ships the PlaybackAttestation
 * plugin (mobile app >= 1.2.0). Older shells return false — feature-detect
 * before relying on native attestation.
 */
export function isPlaybackAttestationAvailable(): boolean {
  return Capacitor.isNativePlatform() && Capacitor.isPluginAvailable('PlaybackAttestation');
}

/**
 * Signs a backend-issued playback challenge. No-throw: returns null when the
 * plugin is unavailable (old shell / web) or the native call rejects (missing
 * params, secret not embedded in a release build), so callers can fall back to
 * their non-attested error path without try/catch.
 */
export async function signVideoChallenge(
  nonce: string,
  videoId: string,
  userId: string,
): Promise<PlaybackAttestationSignResult | null> {
  if (!isPlaybackAttestationAvailable()) {
    return null;
  }

  try {
    return await PlaybackAttestation.sign({ nonce, videoId, userId });
  } catch {
    return null;
  }
}

/**
 * Toggles screenshot/screen-record protection while video plays. No-throw:
 * resolves false when unsupported (iOS is currently a native no-op, web/old
 * shells lack the plugin) or when the native call rejects — callers must treat
 * this as best-effort hardening, never a gate.
 */
export async function setSecureScreen(enabled: boolean): Promise<boolean> {
  if (!isPlaybackAttestationAvailable()) {
    return false;
  }

  try {
    const result = await PlaybackAttestation.setSecureScreen({ enabled });
    return result?.ok === true;
  } catch {
    return false;
  }
}

/**
 * Security spec §3 (mobile hardening) — best-effort root/jailbreak/emulator
 * signals for the playback session's `integrity` field. No-throw: returns
 * null on web/old shells or if the native call fails, so the caller sends no
 * integrity string rather than a fabricated one (the backend treats a null
 * integrity from a client that never sends one as fail-open + logged).
 */
export async function getDeviceIntegrity(): Promise<PlaybackAttestationIntegrityResult | null> {
  if (!isPlaybackAttestationAvailable()) {
    return null;
  }

  try {
    return await PlaybackAttestation.getIntegrity();
  } catch {
    return null;
  }
}

/**
 * Hardware attestation (security standard MOB-09 / MOB-10) — best-effort, no
 * throw. Requests a real Play Integrity / App Attest token bound to the
 * server-issued `nonce`. Returns null on web/desktop, on shells that do not
 * implement the optional native `getIntegrityToken` method, or when the native
 * call rejects — so callers fall back to the heuristic {@link getDeviceIntegrity}
 * path without try/catch. This is a RISK SIGNAL, never an access control.
 */
export async function getIntegrityToken(
  nonce: string,
): Promise<PlaybackAttestationIntegrityTokenResult | null> {
  if (!isPlaybackAttestationAvailable()) {
    return null;
  }

  const request = PlaybackAttestation.getIntegrityToken?.bind(PlaybackAttestation);
  if (!request) {
    return null;
  }

  try {
    const result = await request({ nonce });
    if (!result || typeof result.token !== 'string' || result.token.length === 0) {
      return null;
    }
    return result;
  } catch {
    return null;
  }
}

/**
 * iOS only: registers for `screenshotTaken`. Returns a no-op unsubscribe
 * function on web/Android/old shells so callers don't need to feature-detect
 * before calling it.
 */
export async function addScreenshotListener(onScreenshot: () => void): Promise<() => void> {
  if (!isPlaybackAttestationAvailable()) {
    return () => {};
  }

  try {
    const handle = await PlaybackAttestation.addListener('screenshotTaken', onScreenshot);
    return () => void handle.remove();
  } catch {
    return () => {};
  }
}

/**
 * iOS only: registers for `captureStateChanged` (screen recording/mirroring
 * start or stop). Returns a no-op unsubscribe function on web/Android/old
 * shells so callers don't need to feature-detect before calling it.
 */
export async function addCaptureStateListener(
  onCaptureStateChanged: (isCaptured: boolean) => void,
): Promise<() => void> {
  if (!isPlaybackAttestationAvailable()) {
    return () => {};
  }

  try {
    const handle = await PlaybackAttestation.addListener('captureStateChanged', (data) => {
      onCaptureStateChanged(data.isCaptured);
    });
    return () => void handle.remove();
  } catch {
    return () => {};
  }
}
