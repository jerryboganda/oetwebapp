'use client';

/**
 * HeadphoneDetector — Phase 1 stub indicator.
 *
 * Consumed by:
 *   - app/listening/audio-check (§5.4) — surfaces a headphones-recommended
 *     reminder above the audio-playback self-check.
 *
 * Phase 1 ships only a static label + the {@link useHeadphoneDetect} hook
 * stub so call sites can lock in their integration today. Phase 2 will swap
 * the hook for a real implementation that walks
 * `navigator.mediaDevices.enumerateDevices()` and looks for `audiooutput`
 * devices whose label contains "headphone" / "headset" / "bluetooth".
 */

import { Headphones } from 'lucide-react';

export type HeadphoneDetectionStatus = 'unknown' | 'detected' | 'not_detected';

export interface HeadphoneDetectionResult {
  status: HeadphoneDetectionStatus;
  deviceName: string | null;
}

/**
 * Phase-1 stub. Always returns `{ status: 'unknown', deviceName: null }` so
 * that consumers can wire the contract today and pick up real detection in
 * Phase 2 without touching their components.
 */
export function useHeadphoneDetect(): HeadphoneDetectionResult {
  return { status: 'unknown', deviceName: null };
}

export interface HeadphoneDetectorProps {
  className?: string;
}

export function HeadphoneDetector({ className }: HeadphoneDetectorProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={[
        'inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium',
        'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-200',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <Headphones aria-hidden="true" className="h-3.5 w-3.5" />
      <span aria-hidden="true">🎧</span>
      <span>Headphones recommended</span>
    </div>
  );
}

export default HeadphoneDetector;
