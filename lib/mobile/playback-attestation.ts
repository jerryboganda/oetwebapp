'use client';

import { Capacitor, registerPlugin } from '@capacitor/core';

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
  /** True when the platform actually applied the secure flag (Android FLAG_SECURE); iOS resolves false (no-op). */
  ok: boolean;
}

export interface PlaybackAttestationPlugin {
  sign(options: { nonce: string; videoId: string; userId: string }): Promise<PlaybackAttestationSignResult>;
  setSecureScreen(options: { enabled: boolean }): Promise<PlaybackAttestationSecureScreenResult>;
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
