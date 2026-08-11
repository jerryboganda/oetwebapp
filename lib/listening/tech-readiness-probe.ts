/**
 * Client-side tech-readiness probe for the Listening exam.
 *
 * Surfaces:
 *   - The active audio output / input device labels for real-exam guidance.
 *   - The screen resolution for real-exam guidance (1920×1080 is a
 *     recommendation, not a platform launch constraint).
 *   - A coarse display-scale estimate via devicePixelRatio for guidance
 *     (125% is a recommendation, not a platform launch constraint).
 *
 * The helper degrades gracefully: when `navigator.mediaDevices` is
 * unavailable (older browsers, SSR), the device-label fields are returned
 * as `null` and the backend treats their absence as "unknown" rather than
 * a guidance signal. The only strict pre-start gate is the separate audio
 * sound check (`audioOk`).
 */

import type { TechReadinessProbe } from './v2-api';

export interface BuildProbeOptions {
  audioOk: boolean;
  durationMs: number;
}

/** Run the probe; safe to call inside an effect during exam start-up. */
export async function buildTechReadinessProbe(options: BuildProbeOptions): Promise<TechReadinessProbe> {
  const base: TechReadinessProbe = {
    audioOk: options.audioOk,
    durationMs: options.durationMs,
  };

  // Devices — only if the browser supports the modern enumerate API.
  if (typeof navigator !== 'undefined' && navigator.mediaDevices?.enumerateDevices) {
    try {
      const devices = await navigator.mediaDevices.enumerateDevices();
      base.audioOutputDeviceLabel = pickLabel(devices, 'audiooutput');
      base.audioInputDeviceLabel = pickLabel(devices, 'audioinput');
    } catch {
      // Browser may refuse without prior permission — leave as undefined.
    }
  }

  // Screen metrics — safe in browser only.
  if (typeof window !== 'undefined' && typeof window.screen !== 'undefined') {
    base.screenWidth = window.screen.width;
    base.screenHeight = window.screen.height;
  }

  // Display scale proxy. Windows scaling 125% renders devicePixelRatio ≈ 1.25.
  if (typeof window !== 'undefined' && typeof window.devicePixelRatio === 'number') {
    base.displayScalePercent = Math.round(window.devicePixelRatio * 100);
  }

  return base;
}

function pickLabel(devices: MediaDeviceInfo[], kind: MediaDeviceKind): string | null {
  // Prefer the default device when the browser tags one.
  const def = devices.find((d) => d.kind === kind && d.deviceId === 'default');
  if (def?.label) return def.label;
  const first = devices.find((d) => d.kind === kind && d.label);
  return first?.label ?? null;
}

/**
 * Pure heuristic — exposed for guidance copy, diagnostics, and tests. It must
 * not be used to block a practice or strict attempt launch.
 */
export function looksLikeBluetoothAudio(label: string | null | undefined): boolean {
  if (!label) return false;
  return /\b(bluetooth|airpods|beats|wireless|sony wf|sony wh|jabra|bose qc)\b/i.test(label);
}
