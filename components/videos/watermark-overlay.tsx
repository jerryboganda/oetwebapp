'use client';

import { useEffect, useRef, useState } from 'react';
import { useWatermarkGuard } from '@/lib/video/watermark-guard';
import type { PlaybackWatermark } from '@/lib/types/videos';

/**
 * Dynamic forensic watermark (Course Platform Security Requirements §2.3).
 * The watermark content is SERVER-provided on the playback session, so
 * blanking it client-side would require tampering that the integrity
 * watchdog below (and the session's own audit trail) already records.
 *
 * Two layers:
 *  1. A readable badge — full name, masked email, a live clock, and the
 *     session reference — that jumps to a new RANDOM position on a
 *     randomized interval (20–45s) so it cannot be cropped or masked with a
 *     static overlay.
 *  2. A near-invisible full-frame tiled layer repeating name + session
 *     reference at low opacity, rotated. This is the closest a DOM-rendered
 *     overlay gets to an "invisible forensic fingerprint": it survives
 *     cropping (the whole frame is tiled) and is recoverable from a leaked
 *     recording with a contrast/levels boost, at the cost of being close to
 *     invisible during normal playback. True per-frame server-side
 *     watermarking is a documented upgrade path — see
 *     docs/SECURITY-CONTENT-PROTECTION-MATRIX.md.
 *
 * Renders ABOVE the player controls (z-40 vs the controls' z-30) — the
 * previous z-20 placement sat below the controls gradient, which is a spec
 * violation ("must remain visible in full-screen mode").
 */

const MIN_HOP_MS = 20_000;
const MAX_HOP_MS = 45_000;
const CLOCK_TICK_MS = 30_000;
const TILE_COUNT = 12;

function randomHopDelay(): number {
  return MIN_HOP_MS + Math.random() * (MAX_HOP_MS - MIN_HOP_MS);
}

function randomPosition(): { top: string; left: string } {
  return {
    top: `${5 + Math.random() * 75}%`,
    left: `${5 + Math.random() * 70}%`,
  };
}

function formatClock(date: Date): string {
  return new Intl.DateTimeFormat(undefined, {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}

export interface WatermarkOverlayProps {
  watermark: PlaybackWatermark | null;
  /** @deprecated legacy single-string fallback, used only while `watermark` is null. */
  fallbackText?: string;
  /** Called when the integrity watchdog detects the watermark was hidden,
   * detached, or CSS-tampered. The caller (video-player) decides the
   * response — typically remounting this overlay and, after repeated
   * tampers, pausing playback. */
  onTamper?: () => void;
}

export function WatermarkOverlay({ watermark, fallbackText, onTamper }: WatermarkOverlayProps) {
  const [position, setPosition] = useState(randomPosition);
  const [now, setNow] = useState<Date | null>(null);
  const rootRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    // Avoid a hydration mismatch: the first paint uses no clock text, then
    // fills in once mounted client-side.
    setNow(new Date());
    let timeoutId: number;
    const schedule = () => {
      timeoutId = window.setTimeout(() => {
        setPosition(randomPosition());
        schedule();
      }, randomHopDelay());
    };
    schedule();
    const clock = window.setInterval(() => setNow(new Date()), CLOCK_TICK_MS);
    return () => {
      window.clearTimeout(timeoutId);
      window.clearInterval(clock);
    };
  }, []);

  useWatermarkGuard(rootRef, onTamper ?? (() => {}));

  const badgeText = watermark
    ? [watermark.fullName, watermark.maskedEmail, now ? formatClock(now) : null, watermark.sessionRef]
        .filter(Boolean)
        .join(' · ')
    : fallbackText;

  const tileText = watermark ? `${watermark.fullName} · ${watermark.sessionRef}` : fallbackText;

  if (!badgeText) return null;

  return (
    <div
      ref={rootRef}
      aria-hidden="true"
      data-watermark-root=""
      className="pointer-events-none absolute inset-0 z-40 select-none overflow-hidden"
    >
      {/* Layer 2: near-invisible tiled forensic fingerprint — covers the
          whole frame so cropping a leaked recording cannot remove it. */}
      <div className="absolute inset-0 grid grid-cols-4 grid-rows-3 opacity-[0.05]">
        {Array.from({ length: TILE_COUNT }, (_, index) => (
          <span
            key={index}
            className="flex -rotate-[18deg] items-center justify-center overflow-visible whitespace-nowrap font-mono text-[10px] text-white"
          >
            {tileText}
          </span>
        ))}
      </div>

      {/* Layer 1: readable, moving badge */}
      <span
        className="absolute whitespace-nowrap rounded bg-black/20 px-1.5 py-0.5 font-mono text-xs font-semibold text-white/40 transition-all duration-[1500ms] ease-in-out"
        style={position}
      >
        {badgeText}
      </span>
    </div>
  );
}
