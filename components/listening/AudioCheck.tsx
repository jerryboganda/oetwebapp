'use client';

/**
 * AudioCheck — self-contained playback self-check widget (§5.4).
 *
 * Consumed by:
 *   - app/listening/audio-check — gates entry to the diagnostic (§6.x) until
 *     the learner confirms they can hear the test tone.
 *
 * The widget plays a short test asset (5 seconds), then asks the learner to
 * self-report the outcome via three radio options. The selected outcome is
 * lifted to the parent via `onResult` once the learner clicks Continue.
 *
 * Asset path: `/test-audio.mp3` is expected under `public/`. If the asset is
 * missing, the browser will fire an `error` event on the <audio> element and
 * the widget surfaces an inline note — the radio options remain available so
 * the learner can still self-report.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { Pause, Play, Volume2 } from 'lucide-react';
import { HeadphoneDetector } from './HeadphoneDetector';

export type AudioCheckOutcome = 'clear' | 'quiet' | 'failed';

export interface AudioCheckProps {
  onResult: (outcome: AudioCheckOutcome) => void;
  /** Override the default `/test-audio.mp3` path for testing. */
  audioSrc?: string;
  /** Lock the controls (e.g. while the parent is persisting the result). */
  disabled?: boolean;
}

const TEST_AUDIO_DURATION_MS = 5000;

interface OptionDef {
  value: AudioCheckOutcome;
  label: string;
  description: string;
}

const OPTIONS: OptionDef[] = [
  {
    value: 'clear',
    label: 'Yes, clearly',
    description: 'The sound was crisp at a comfortable volume.',
  },
  {
    value: 'quiet',
    label: 'Yes, but quiet',
    description: 'I could hear it but had to strain or turn the volume up.',
  },
  {
    value: 'failed',
    label: "No, I didn't hear anything",
    description: 'Silent — check your output device and try again.',
  },
];

export function AudioCheck({ onResult, audioSrc = '/test-audio.mp3', disabled = false }: AudioCheckProps) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const stopTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [hasPlayedOnce, setHasPlayedOnce] = useState(false);
  const [selected, setSelected] = useState<AudioCheckOutcome | null>(null);
  const [audioError, setAudioError] = useState<string | null>(null);

  useEffect(() => {
    return () => {
      if (stopTimerRef.current) clearTimeout(stopTimerRef.current);
      const audio = audioRef.current;
      if (audio) {
        audio.pause();
        audio.currentTime = 0;
      }
    };
  }, []);

  const handlePlay = useCallback(async () => {
    const audio = audioRef.current;
    if (!audio) return;
    setAudioError(null);
    try {
      audio.currentTime = 0;
      await audio.play();
      setIsPlaying(true);
      setHasPlayedOnce(true);
      if (stopTimerRef.current) clearTimeout(stopTimerRef.current);
      stopTimerRef.current = setTimeout(() => {
        audio.pause();
        audio.currentTime = 0;
        setIsPlaying(false);
      }, TEST_AUDIO_DURATION_MS);
    } catch {
      setIsPlaying(false);
      setAudioError(
        'Playback was blocked. You can still pick an option below to self-report the outcome.',
      );
    }
  }, []);

  const handleEnded = useCallback(() => {
    setIsPlaying(false);
    if (stopTimerRef.current) {
      clearTimeout(stopTimerRef.current);
      stopTimerRef.current = null;
    }
  }, []);

  const handleAudioError = useCallback(() => {
    setIsPlaying(false);
    setAudioError(
      'We could not load the test sound. You can still pick an option below to self-report.',
    );
  }, []);

  const handleContinue = useCallback(() => {
    if (selected) onResult(selected);
  }, [selected, onResult]);

  return (
    <div className="rounded-2xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 p-5 shadow-sm">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-slate-100">Audio check</h2>
        <HeadphoneDetector />
      </div>

      <p className="mb-4 text-sm text-gray-600 dark:text-gray-400">
        Before the diagnostic begins, please confirm that audio plays clearly. For best
        results, use headphones in a quiet room — earbuds or over-ear headphones both work.
      </p>

      <audio
        ref={audioRef}
        src={audioSrc}
        preload="auto"
        onEnded={handleEnded}
        onError={handleAudioError}
        aria-hidden="true"
      />

      <button
        type="button"
        onClick={handlePlay}
        disabled={isPlaying || disabled}
        className={[
          'inline-flex items-center gap-2 rounded-xl px-5 py-3 text-base font-semibold',
          'bg-primary text-white shadow-md transition-colors',
          'hover:bg-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
          'disabled:cursor-not-allowed disabled:opacity-60',
        ].join(' ')}
      >
        {isPlaying ? (
          <Pause aria-hidden="true" className="h-5 w-5" />
        ) : (
          <Play aria-hidden="true" className="h-5 w-5" />
        )}
        <Volume2 aria-hidden="true" className="h-5 w-5" />
        <span aria-hidden="true">🔊</span>
        {isPlaying ? 'Playing…' : hasPlayedOnce ? 'Play again' : 'Play test sound'}
      </button>

      {audioError ? (
        <p className="mt-3 text-xs text-warning" role="alert">
          {audioError}
        </p>
      ) : null}

      <fieldset className="mt-6">
        <legend className="mb-2 text-sm font-medium text-gray-900 dark:text-slate-100">
          Did you hear the test sound?
        </legend>
        <div className="grid gap-2">
          {OPTIONS.map((opt) => {
            const checked = selected === opt.value;
            return (
              <label
                key={opt.value}
                className={[
                  'flex cursor-pointer items-start gap-3 rounded-xl border px-4 py-3',
                  'transition-colors hover:border-border-hover',
                  checked
                    ? 'border-primary bg-lavender/40 dark:bg-primary/10'
                    : 'border-border bg-surface',
                ].join(' ')}
              >
                <input
                  type="radio"
                  name="audio-check-outcome"
                  value={opt.value}
                  checked={checked}
                  onChange={() => setSelected(opt.value)}
                  className="mt-1 h-4 w-4 accent-primary"
                />
                <span className="flex-1">
                  <span className="block text-sm font-medium text-gray-900 dark:text-slate-100">
                    {opt.label}
                  </span>
                  <span className="block text-xs text-gray-600 dark:text-gray-400">{opt.description}</span>
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>

      <div className="mt-6 flex justify-end">
        <button
          type="button"
          onClick={handleContinue}
          disabled={!selected || disabled}
          className={[
            'inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-sm font-semibold',
            'bg-primary text-white shadow-sm transition-colors',
            'hover:bg-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
            'disabled:cursor-not-allowed disabled:opacity-50',
          ].join(' ')}
        >
          Continue
        </button>
      </div>
    </div>
  );
}

export default AudioCheck;
