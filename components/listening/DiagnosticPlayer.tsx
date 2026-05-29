'use client';

/**
 * DiagnosticPlayer — no-replay audio wrapper for the listening diagnostic
 * (§6.2).
 *
 * Consumed by:
 *   - app/listening/diagnostic — one-shot Part A / B / C question audio.
 *   - app/listening/practice/{sessionId} — when `allowReplay` is enabled.
 *
 * Diagnostic-mode invariants (allowReplay=false, allowSpeedControl=false):
 *   - No scrubber. The progress bar is presentational only and reflects the
 *     underlying <audio> element's `currentTime` / `duration` ratio.
 *   - Playback is fixed at 1.0× regardless of any browser default.
 *   - The "Start listening" button is gated to honour browser autoplay
 *     policies — playback never begins on mount.
 *   - Once `ended` fires, `onEnded` is called and the player locks. There is
 *     no manual rewind / replay control.
 *
 * Practice / review mode (`allowReplay=true`, `allowSpeedControl=true`):
 *   - Surfaces a replay button and a 0.75× / 1.0× / 1.25× selector.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { Play, Pause, RotateCcw } from 'lucide-react';

export interface DiagnosticPlayerProps {
  audioUrl: string | null;
  onEnded: () => void;
  /** When true, exposes replay + scrubber. Default `false` (diagnostic). */
  allowReplay?: boolean;
  /** When true, exposes a 0.75× / 1× / 1.25× speed selector. Default `false`. */
  allowSpeedControl?: boolean;
  /** Lock all controls (e.g. between submission and next question). */
  disabled?: boolean;
  className?: string;
}

const SPEED_OPTIONS = [0.75, 1.0, 1.25] as const;

function formatSeconds(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00';
  const total = Math.floor(seconds);
  const mm = Math.floor(total / 60);
  const ss = total % 60;
  return `${mm}:${ss.toString().padStart(2, '0')}`;
}

export function DiagnosticPlayer({
  audioUrl,
  onEnded,
  allowReplay = false,
  allowSpeedControl = false,
  disabled = false,
  className,
}: DiagnosticPlayerProps) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [hasStarted, setHasStarted] = useState(false);
  const [isPlaying, setIsPlaying] = useState(false);
  const [endedAt, setEndedAt] = useState<number | null>(null);
  const [duration, setDuration] = useState(0);
  const [currentTime, setCurrentTime] = useState(0);
  const [speed, setSpeed] = useState<number>(1.0);
  const [playbackError, setPlaybackError] = useState<string | null>(null);

  // Reset internal state whenever the audio source changes (next question).
  useEffect(() => {
    setHasStarted(false);
    setIsPlaying(false);
    setEndedAt(null);
    setDuration(0);
    setCurrentTime(0);
    setPlaybackError(null);
  }, [audioUrl]);

  // Diagnostic mode locks the rate to 1.0×.
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;
    audio.playbackRate = allowSpeedControl ? speed : 1.0;
  }, [speed, allowSpeedControl]);

  const handleStart = useCallback(async () => {
    if (disabled || !audioUrl) return;
    const audio = audioRef.current;
    if (!audio) return;
    setPlaybackError(null);
    try {
      audio.currentTime = 0;
      audio.playbackRate = allowSpeedControl ? speed : 1.0;
      await audio.play();
      setHasStarted(true);
      setIsPlaying(true);
    } catch {
      setIsPlaying(false);
      setPlaybackError('Playback was blocked. Tap the button again to start.');
    }
  }, [disabled, audioUrl, allowSpeedControl, speed]);

  const handleReplay = useCallback(async () => {
    if (!allowReplay || disabled) return;
    const audio = audioRef.current;
    if (!audio) return;
    audio.currentTime = 0;
    setEndedAt(null);
    try {
      await audio.play();
      setIsPlaying(true);
    } catch {
      setIsPlaying(false);
    }
  }, [allowReplay, disabled]);

  const handleEnded = useCallback(() => {
    setIsPlaying(false);
    setEndedAt(Date.now());
    onEnded();
  }, [onEnded]);

  const handleSkip = useCallback(() => {
    if (disabled) return;
    setEndedAt(Date.now());
    onEnded();
  }, [disabled, onEnded]);

  const handleTimeUpdate = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    setCurrentTime(audio.currentTime);
  }, []);

  const handleLoadedMetadata = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    setDuration(Number.isFinite(audio.duration) ? audio.duration : 0);
  }, []);

  const handlePause = useCallback(() => {
    setIsPlaying(false);
  }, []);

  const progressPct =
    duration > 0 ? Math.min(100, Math.max(0, (currentTime / duration) * 100)) : 0;

  const remaining = Math.max(0, duration - currentTime);
  const isLocked = disabled || (!allowReplay && endedAt !== null);

  // ──────────────────────────────────────────────────────────────────────
  // No audio yet — skippable placeholder.
  // ──────────────────────────────────────────────────────────────────────
  if (!audioUrl) {
    return (
      <div
        className={[
          'rounded-2xl border border-dashed border-border bg-surface p-5',
          className ?? '',
        ]
          .filter(Boolean)
          .join(' ')}
      >
        <p className="text-sm font-medium text-navy">Audio coming soon</p>
        <p className="mt-1 text-xs text-muted">
          This question does not have audio attached yet. You can skip ahead to the question
          prompt.
        </p>
        <button
          type="button"
          onClick={handleSkip}
          disabled={disabled}
          className={[
            'mt-3 inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-xs font-semibold',
            'bg-primary text-white dark:bg-violet-700 shadow-sm transition-colors',
            'hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
            'disabled:cursor-not-allowed disabled:opacity-60',
          ].join(' ')}
        >
          Skip to question
        </button>
      </div>
    );
  }

  return (
    <div
      className={[
        'rounded-2xl border border-border bg-surface p-5 shadow-clinical',
        isLocked ? 'opacity-90' : '',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <audio
        ref={audioRef}
        src={audioUrl}
        preload="auto"
        onEnded={handleEnded}
        onPause={handlePause}
        onTimeUpdate={handleTimeUpdate}
        onLoadedMetadata={handleLoadedMetadata}
        aria-hidden="true"
      />

      <div className="flex items-center gap-3">
        {!hasStarted ? (
          <button
            type="button"
            onClick={handleStart}
            disabled={disabled}
            className={[
              'inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold',
              'bg-primary text-white dark:bg-violet-700 shadow-md transition-colors',
              'hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
              'disabled:cursor-not-allowed disabled:opacity-60',
            ].join(' ')}
          >
            <Play aria-hidden="true" className="h-4 w-4" />
            Start listening
          </button>
        ) : allowReplay && endedAt !== null ? (
          <button
            type="button"
            onClick={handleReplay}
            disabled={disabled}
            className={[
              'inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold',
              'bg-background-light text-navy transition-colors hover:bg-border',
              'disabled:cursor-not-allowed disabled:opacity-60',
            ].join(' ')}
          >
            <RotateCcw aria-hidden="true" className="h-4 w-4" />
            Replay
          </button>
        ) : (
          <div
            className="inline-flex items-center gap-2 rounded-xl bg-background-light px-4 py-2.5 text-sm font-medium text-muted"
            aria-live="polite"
          >
            {isPlaying ? (
              <Pause aria-hidden="true" className="h-4 w-4" />
            ) : (
              <Play aria-hidden="true" className="h-4 w-4" />
            )}
            {endedAt !== null && !allowReplay
              ? 'Audio complete'
              : isPlaying
                ? 'Listening…'
                : 'Paused'}
          </div>
        )}

        <div className="flex-1">
          <div
            className="h-2 w-full overflow-hidden rounded-full bg-border"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(progressPct)}
            aria-label="Audio progress"
          >
            <div
              className="h-full bg-primary transition-[width] duration-150"
              style={{ width: `${progressPct}%` }}
            />
          </div>
          <div className="mt-1 flex items-center justify-between text-[11px] tabular-nums text-muted">
            <span aria-hidden="true">{formatSeconds(currentTime)}</span>
            <span aria-label="Time remaining">-{formatSeconds(remaining)}</span>
          </div>
        </div>

        {allowSpeedControl ? (
          <div
            role="radiogroup"
            aria-label="Playback speed"
            className="flex items-center gap-1 rounded-full bg-background-light p-0.5"
          >
            {SPEED_OPTIONS.map((option) => {
              const active = option === speed;
              return (
                <button
                  key={option}
                  type="button"
                  role="radio"
                  aria-checked={active}
                  onClick={() => setSpeed(option)}
                  disabled={disabled}
                  className={[
                    'rounded-full px-2 py-1 text-[11px] font-semibold transition-colors',
                    active
                      ? 'bg-primary text-white dark:bg-violet-700 shadow-sm'
                      : 'text-muted hover:text-navy',
                    'disabled:cursor-not-allowed disabled:opacity-60',
                  ].join(' ')}
                >
                  {option}×
                </button>
              );
            })}
          </div>
        ) : null}
      </div>

      {!allowReplay ? (
        <p className="mt-3 text-[11px] text-muted">
          You will hear this audio only once. Take notes; replays are not available in the
          diagnostic.
        </p>
      ) : null}

      {playbackError ? (
        <p className="mt-2 text-xs text-warning" role="alert">
          {playbackError}
        </p>
      ) : null}
    </div>
  );
}

export default DiagnosticPlayer;
