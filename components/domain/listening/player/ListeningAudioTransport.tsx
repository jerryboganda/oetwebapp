'use client';

// Listening V2 — sticky audio transport bar. Renders play/pause control,
// scrub-or-progress, save-state indicator, and the optional whole-attempt
// 40-minute countdown chip. Visual-only; the parent owns the timer & save
// state. Extracted from the monolithic player so the chrome can be
// Storybook'd in isolation.

import { Clock, Loader2, Pause, Play, Save, WifiOff } from 'lucide-react';
import { formatReviewSeconds } from '@/lib/listening-sections';

export interface ListeningAudioTransportProps {
  isPlaying: boolean;
  progressSeconds: number;
  durationSeconds: number;
  /** When false (exam/home/paper), the scrub slider is hidden. */
  canScrub: boolean;
  /** Disables the play/pause button while the FSM is in preview phase. */
  isPreviewPhase: boolean;
  audioState: 'idle' | 'buffering' | 'ready' | 'error';
  saveState: 'idle' | 'saving' | 'saved' | 'error';
  answeredCount: number;
  totalQuestions: number;
  /** Optional whole-attempt countdown (seconds). `null` hides the chip. */
  attemptSecondsRemaining: number | null;
  onTogglePlayPause: () => void;
  onScrub: (event: React.ChangeEvent<HTMLInputElement>) => void;
}

function formatTime(seconds: number) {
  if (!seconds || Number.isNaN(seconds)) return '00:00';
  const minutes = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

export function ListeningAudioTransport(props: ListeningAudioTransportProps) {
  const {
    isPlaying,
    progressSeconds,
    durationSeconds,
    canScrub,
    isPreviewPhase,
    audioState,
    saveState,
    answeredCount,
    totalQuestions,
    attemptSecondsRemaining,
    onTogglePlayPause,
    onScrub,
  } = props;

  const widthPercent =
    durationSeconds > 0 ? (progressSeconds / durationSeconds) * 100 : 0;

  return (
    <div
      data-testid="listening-audio-transport"
      className="sticky top-20 z-20 flex items-center gap-4 rounded-2xl bg-navy p-4 text-white shadow-xl shadow-navy/10 sm:p-5"
    >
      <button
        onClick={onTogglePlayPause}
        disabled={isPreviewPhase}
        aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
        className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-full transition-colors ${
          isPreviewPhase
            ? 'cursor-not-allowed bg-white/10 text-white/30'
            : 'bg-surface text-navy hover:bg-background-light'
        }`}
      >
        {isPlaying ? <Pause className="h-6 w-6" /> : <Play className="ml-1 h-6 w-6" />}
      </button>

      <div className="flex flex-1 flex-col gap-1.5">
        <div className="flex justify-between font-mono text-xs font-bold text-white/70">
          <span>{formatTime(progressSeconds)}</span>
          <span>{formatTime(durationSeconds)}</span>
        </div>
        <div className="relative h-2.5 w-full overflow-hidden rounded-full bg-white/20">
          <div
            className="absolute left-0 top-0 h-full bg-info transition-all duration-100 ease-linear"
            style={{ width: `${widthPercent}%` }}
          />
          {canScrub ? (
            <input
              type="range"
              min="0"
              max={durationSeconds || 100}
              value={progressSeconds}
              onChange={onScrub}
              className="absolute left-0 top-0 h-full w-full cursor-pointer opacity-0"
              aria-label="Scrub audio"
            />
          ) : null}
        </div>
      </div>

      <div className="hidden items-center gap-2 text-xs font-bold text-white/60 sm:flex">
        {audioState === 'buffering' ? (
          <Loader2 className="h-4 w-4 animate-spin" />
        ) : audioState === 'error' ? (
          <WifiOff className="h-4 w-4" />
        ) : (
          <Save className="h-4 w-4" />
        )}
        {saveState === 'saving'
          ? 'Saving'
          : saveState === 'error'
            ? 'Save issue'
            : `${answeredCount}/${totalQuestions} saved`}
      </div>

      {attemptSecondsRemaining !== null ? (
        <div
          data-testid="listening-attempt-timer"
          className={`flex shrink-0 items-center gap-1.5 rounded-xl px-3 py-2 font-mono text-sm font-black ${
            attemptSecondsRemaining === 0
              ? 'bg-danger/20 text-danger'
              : attemptSecondsRemaining <= 30
                ? 'bg-danger/20 text-danger'
                : attemptSecondsRemaining <= 120
                  ? 'bg-warning/20 text-warning'
                  : 'bg-white/10 text-white'
          }`}
          aria-label={`Attempt time remaining ${formatReviewSeconds(attemptSecondsRemaining)}`}
        >
          <Clock className="h-4 w-4" />
          {attemptSecondsRemaining === 0 ? 'Time up' : formatReviewSeconds(attemptSecondsRemaining)}
        </div>
      ) : null}
    </div>
  );
}
