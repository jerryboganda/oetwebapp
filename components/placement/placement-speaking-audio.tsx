import { useEffect, useRef, useState } from 'react';
import { AlertTriangle, Headphones, Loader2, Play, RotateCcw } from 'lucide-react';
import { fetchAuthorizedObjectUrl } from '@/lib/api/binary';
import { SECONDARY_BUTTON } from './placement-shared';

// ── Speaking ─────────────────────────────────────────────────────────

export type SpeakPhase = 'ready' | 'prep' | 'recording' | 'recorded' | 'uploading' | 'upload-failed';

export function pickRecordingMimeType(): string {
  if (typeof MediaRecorder === 'undefined') return '';
  if (MediaRecorder.isTypeSupported('audio/webm;codecs=opus')) return 'audio/webm;codecs=opus';
  if (MediaRecorder.isTypeSupported('audio/mp4')) return 'audio/mp4';
  return '';
}

export const BIG_PLAY_BUTTON =
  'inline-flex min-h-14 w-full items-center justify-center gap-2 rounded-xl bg-primary px-6 py-3 text-base font-semibold text-white transition hover:opacity-90 disabled:opacity-60';

/** Shown in place of the prompt for a hear-only task (sentence reconstruction).
 *  The engine already sends an instruction here, but a stale engine could
 *  still send the sentence itself, which the candidate must never read. */
export const HEAR_ONLY_INSTRUCTION = 'Listen carefully, then repeat the sentence you heard, exactly as you heard it.';

export function AudioUnavailable({
  onRetry,
  busy = false,
  detail = null,
}: {
  onRetry: () => void;
  busy?: boolean;
  detail?: string | null;
}) {
  return (
    <div role="alert" className="space-y-3 rounded-xl border border-warning/40 bg-warning/10 px-4 py-3">
      <p className="flex items-center gap-2 text-sm font-semibold text-navy">
        <AlertTriangle className="h-4 w-4 shrink-0 text-warning" aria-hidden /> Audio unavailable
      </p>
      <p className="text-sm text-navy">
        The audio for this task could not be loaded, so it cannot be started yet. Check your connection and press Retry.
        If it keeps failing, please contact support.
      </p>
      {detail ? <p className="text-xs text-muted">{detail}</p> : null}
      <button type="button" onClick={onRetry} disabled={busy} className={SECONDARY_BUTTON}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <RotateCcw className="h-4 w-4" aria-hidden />}
        Retry
      </button>
    </div>
  );
}

/**
 * Prompt audio for a Speaking task the candidate hears (listen-and-repeat,
 * retell, interlocutor turn). Same authorised-blob approach as Listening; the
 * Play tap is the user gesture, so no pre-unlocked shared element is needed.
 * A play only counts once it has run to the end, so an interrupted or failed
 * play never uses up the candidate's allowance. `onHeard` fires each time the
 * clip finishes; the parent keeps planning/recording locked until then.
 */
export function SpeakingPromptAudio({
  apiPath,
  maxPlays,
  replayLocked,
  onHeard,
}: {
  apiPath: string;
  maxPlays: number;
  /** True while planning, recording or uploading: a replay would bleed into the microphone. */
  replayLocked: boolean;
  onHeard: () => void;
}) {
  const [attempt, setAttempt] = useState(0);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [phase, setPhase] = useState<'loading' | 'ready' | 'playing' | 'failed'>('loading');
  // Plays that ran to the end. An interrupted play never counts.
  const [plays, setPlays] = useState(0);
  const [progress, setProgress] = useState(0);
  const elementRef = useRef<HTMLAudioElement | null>(null);
  // The parent passes a fresh closure every render; keep the latest in a ref so
  // the media listeners below are attached once per clip.
  const onHeardRef = useRef(onHeard);
  useEffect(() => {
    onHeardRef.current = onHeard;
  });

  // Fetch the clip as an authorised blob: the endpoint is Bearer-gated and
  // lives behind the API base, which a bare <audio src> cannot satisfy.
  useEffect(() => {
    let cancelled = false;
    let created: string | null = null;
    fetchAuthorizedObjectUrl(apiPath)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        created = url;
        setObjectUrl(url);
        setPhase('ready');
      })
      .catch(() => {
        if (!cancelled) setPhase('failed');
      });
    return () => {
      cancelled = true;
      if (created) URL.revokeObjectURL(created);
    };
  }, [apiPath, attempt]);

  // Drive one element for this clip. Metadata is enough to judge it: a clip
  // that reports no duration is treated as broken, never as a silent success.
  useEffect(() => {
    if (!objectUrl) return;
    const element = new Audio();
    const onLoaded = () => {
      if (!Number.isFinite(element.duration) || element.duration <= 0) setPhase('failed');
    };
    const onTime = () => {
      if (element.duration > 0) setProgress(Math.min(1, element.currentTime / element.duration));
    };
    const onEnded = () => {
      setProgress(1);
      setPlays((count) => count + 1);
      setPhase('ready');
      onHeardRef.current();
    };
    // A phone call or unplugged headphones pause the clip mid-way: hand the
    // Play button back (it resumes) instead of leaving the screen stuck on
    // "Playing". The natural end also fires `pause`, but with `ended` set.
    const onPause = () => {
      if (!element.ended) setPhase((current) => (current === 'playing' ? 'ready' : current));
    };
    const onError = () => setPhase('failed');
    element.addEventListener('loadedmetadata', onLoaded);
    element.addEventListener('timeupdate', onTime);
    element.addEventListener('ended', onEnded);
    element.addEventListener('pause', onPause);
    element.addEventListener('error', onError);
    element.preload = 'metadata';
    element.src = objectUrl;
    elementRef.current = element;
    return () => {
      element.removeEventListener('loadedmetadata', onLoaded);
      element.removeEventListener('timeupdate', onTime);
      element.removeEventListener('ended', onEnded);
      element.removeEventListener('pause', onPause);
      element.removeEventListener('error', onError);
      element.pause();
      element.removeAttribute('src');
      elementRef.current = null;
    };
  }, [objectUrl]);

  const playsLeft = Math.max(0, maxPlays - plays - (phase === 'playing' ? 1 : 0));

  const play = () => {
    const element = elementRef.current;
    if (!element || playsLeft === 0) return;
    // A fresh play starts the bar over; resuming after an interruption keeps it.
    if (element.currentTime === 0 || element.ended) setProgress(0);
    setPhase('playing');
    element.play().catch((err: unknown) => {
      // An interrupted load is not a fault; anything else means the clip cannot play.
      setPhase(err instanceof DOMException && err.name === 'AbortError' ? 'ready' : 'failed');
    });
  };

  const retry = () => {
    setObjectUrl(null);
    setProgress(0);
    setPhase('loading');
    setAttempt((count) => count + 1);
  };

  if (phase === 'failed') {
    return (
      <section aria-label="Task audio">
        <AudioUnavailable onRetry={retry} />
      </section>
    );
  }

  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Task audio">
      <div className="flex items-center gap-2 text-sm font-semibold text-navy">
        <Headphones className="h-4 w-4 text-primary" aria-hidden />
        <span role="status">
          {phase === 'loading' ? 'Loading audio…' : null}
          {phase === 'ready' ? (plays === 0 ? 'Press play to hear the task' : 'Audio finished') : null}
          {phase === 'playing' ? 'Playing — listen carefully' : null}
        </span>
      </div>

      <div
        className="h-2 w-full overflow-hidden rounded-full bg-border"
        role="progressbar"
        aria-label="Task audio progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(progress * 100)}
      >
        <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${Math.round(progress * 100)}%` }} />
      </div>

      {phase === 'loading' ? <Loader2 className="h-5 w-5 animate-spin text-muted" aria-hidden /> : null}

      {phase === 'ready' && playsLeft > 0 ? (
        <button type="button" onClick={play} disabled={replayLocked} className={BIG_PLAY_BUTTON}>
          {plays === 0 ? <Play className="h-5 w-5" aria-hidden /> : <RotateCcw className="h-5 w-5" aria-hidden />}
          {plays === 0 ? 'Play audio' : 'Play again'}
        </button>
      ) : null}

      <p className="text-xs text-muted">
        Plays left: <span className="font-mono tabular-nums">{playsLeft}</span> of {maxPlays}
      </p>
    </section>
  );
}
