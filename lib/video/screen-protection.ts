'use client';

import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { setSecureScreen } from '@/lib/mobile/playback-attestation';
import { reportProtectionEvent } from '@/lib/api/video-protection';

/**
 * Best-effort OS-level screen-capture protection while a video plays. On the
 * native shells the app window is EXCLUDED from screenshots, screen recorders,
 * screen shares and mirroring — capture tools see solid black:
 *
 *   - Desktop (Tauri, shell >= 0.6.0): Windows SetWindowDisplayAffinity
 *     (WDA_EXCLUDEFROMCAPTURE) / macOS NSWindow.sharingType = None.
 *   - Mobile (Capacitor): Android FLAG_SECURE (blocks stills AND recordings —
 *     no detection needed, the OS just makes them black). iOS cannot block a
 *     still screenshot of an arbitrary WKWebView (only DRM/AVPlayer-protected
 *     content can), but DOES blank the app during active screen recording /
 *     mirroring via `UIScreen.isCaptured` (see PlaybackAttestationPlugin.swift
 *     `enableCaptureBlackout` — NOT a no-op). Still-screenshot detection on
 *     iOS is handled separately (see Phase 8 — `screenshotTaken` listener).
 *   - Web: no such capability — resolves false.
 *
 * Returns false when the control cannot be engaged. Protected-course callers
 * treat that as a hard gate in native shells; web browsers continue with the
 * watermark, short token, session and audit compensating controls. The caller
 * reports `protection_engaged` / `protection_unavailable` telemetry.
 */
export async function setVideoScreenProtection(enabled: boolean): Promise<boolean> {
  const runtime = getAppRuntimeKind();
  const engaged = await engage(runtime, enabled);

  if (enabled) {
    void reportProtectionEvent(
      { kind: engaged ? 'protection_engaged' : 'protection_unavailable' },
      { immediate: !engaged },
    );
  }

  return engaged;
}

async function engage(runtime: ReturnType<typeof getAppRuntimeKind>, enabled: boolean): Promise<boolean> {
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
