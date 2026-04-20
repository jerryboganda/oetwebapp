'use client';

import { useMemo } from 'react';

type WaveformMeterProps = {
  level: number; // 0..1
  isRecording: boolean;
  elapsedMs: number;
  maxDurationMs: number;
  className?: string;
};

/**
 * Lightweight visual meter for active recording. Avoids heavy
 * WaveSurfer-style libraries — we only need a strong "am I being
 * recorded?" cue plus a progress bar to the hard-stop.
 */
export function WaveformMeter({
  level,
  isRecording,
  elapsedMs,
  maxDurationMs,
  className = '',
}: WaveformMeterProps) {
  const bars = useMemo(() => Array.from({ length: 24 }, (_, i) => i), []);
  const progress = maxDurationMs > 0 ? Math.min(1, elapsedMs / maxDurationMs) : 0;
  const elapsedSec = Math.floor(elapsedMs / 1000);
  const maxSec = Math.floor(maxDurationMs / 1000);

  return (
    <div className={`flex items-center gap-4 rounded-2xl border border-border bg-surface p-4 ${className}`}>
      <div
        aria-hidden
        className={`flex h-10 items-end gap-[2px] flex-1`}
      >
        {bars.map((i) => {
          // Compute each bar's height deterministically around the current level
          const phase = (i / bars.length) * Math.PI * 2;
          const sinWave = 0.3 + 0.7 * Math.abs(Math.sin(phase + elapsedMs / 80));
          const h = isRecording
            ? Math.max(4, level * 100 * sinWave)
            : 4;
          return (
            <span
              key={i}
              className={`inline-block w-[3px] rounded-full transition-[height] duration-75 ${
                isRecording ? 'bg-rose-500' : 'bg-muted/40'
              }`}
              style={{ height: `${Math.round(h)}%` }}
            />
          );
        })}
      </div>
      <div className="flex w-24 shrink-0 flex-col items-end text-right">
        <div className="text-xs uppercase tracking-[0.15em] text-muted">
          {isRecording ? 'Recording' : 'Ready'}
        </div>
        <div className="font-mono text-lg font-semibold text-navy dark:text-white" aria-live="polite">
          {formatTime(elapsedSec)} / {formatTime(maxSec)}
        </div>
        <div className="mt-1 h-1 w-full overflow-hidden rounded-full bg-background-light">
          <div
            className={`h-full transition-[width] duration-100 ${progress > 0.9 ? 'bg-rose-500' : 'bg-primary'}`}
            style={{ width: `${Math.round(progress * 100)}%` }}
          />
        </div>
      </div>
    </div>
  );
}

function formatTime(s: number): string {
  const mm = Math.floor(s / 60).toString().padStart(1, '0');
  const ss = (s % 60).toString().padStart(2, '0');
  return `${mm}:${ss}`;
}
