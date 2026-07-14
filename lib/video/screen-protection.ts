'use client';

import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { setSecureScreen } from '@/lib/mobile/playback-attestation';

/**
 * Best-effort OS-level screen-capture protection while a video plays. On the
 * native shells the app window is EXCLUDED from screenshots, screen recorders,
 * screen shares and mirroring — capture tools see solid black:
 *
 *   - Desktop (Tauri, shell >= 0.6.0): Windows SetWindowDisplayAffinity
 *     (WDA_EXCLUDEFROMCAPTURE) / macOS NSWindow.sharingType = None.
 *   - Mobile (Capacitor): Android FLAG_SECURE. iOS is a native no-op today
 *     (see PlaybackAttestationPlugin.swift) — resolves false there.
 *   - Web: no such capability — resolves false.
 *
 * Hardening ONLY, never a gate: callers treat a false result as "protection
 * unavailable on this platform" and continue playing. No-throw.
 */
export async function setVideoScreenProtection(enabled: boolean): Promise<boolean> {
  const runtime = getAppRuntimeKind();

  if (runtime === 'capacitor-native') {
    return setSecureScreen(enabled);
  }

  if (runtime === 'desktop' && typeof window !== 'undefined') {
    try {
      const result = await window.desktopBridge?.captureProtection?.set(enabled);
      return result?.ok === true;
    } catch {
      return false;
    }
  }

  return false;
}
