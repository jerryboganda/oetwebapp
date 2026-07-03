'use client';

import { useEffect, useState } from 'react';

/**
 * Forensic watermark rendered above the video surface.
 *
 * The text (learner email + short session id) is SERVER-provided on the
 * playback session, so blanking it client-side would require tampering that
 * the session audit trail already records. It drifts to a new anchor every
 * ~30s so it cannot be cropped or masked statically. `pointer-events-none`
 * keeps player controls fully usable.
 */
const ANCHORS: Array<{ top?: string; bottom?: string; left?: string; right?: string }> = [
  { top: '8%', left: '6%' },
  { top: '10%', right: '8%' },
  { bottom: '18%', left: '10%' },
  { bottom: '12%', right: '6%' },
  { top: '45%', left: '38%' },
];

export function WatermarkOverlay({ text }: { text: string }) {
  const [anchorIndex, setAnchorIndex] = useState(0);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setAnchorIndex((current) => (current + 1 + Math.floor(Math.random() * (ANCHORS.length - 1))) % ANCHORS.length);
    }, 30000);
    return () => window.clearInterval(timer);
  }, []);

  if (!text) return null;

  const anchor = ANCHORS[anchorIndex] ?? ANCHORS[0];

  return (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0 z-20 select-none overflow-hidden">
      <span
        className="absolute whitespace-nowrap font-mono text-xs font-semibold text-white/25 transition-all duration-[3000ms] ease-in-out"
        style={anchor}
      >
        {text}
      </span>
    </div>
  );
}
