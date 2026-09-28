import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Headphones, Loader2, Play, RotateCcw } from 'lucide-react';
import { fetchAuthorizedObjectUrl } from '@/lib/api/binary';
import { resolvePlacementAudioUrl, type PlacementTechnicalReason } from '@/lib/api/placement';
import { PRIMARY_BUTTON, SECONDARY_BUTTON } from './placement-shared';
import { getListeningAudio } from './placement-preflight';

export function UnitAudio({
  audioUrl,
  maxPlays,
  onPlaybackStart,
  onPlay,
  onFailure,
}: {
  audioUrl: string;
  maxPlays: number;
  onPlaybackStart: () => void;
  onPlay: (plays: number) => void;
  onFailure: (reason: PlacementTechnicalReason) => void;
}) {
  const apiPath = useMemo(() => resolvePlacementAudioUrl(audioUrl), [audioUrl]);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [phase, setPhase] = useState<'loading' | 'blocked' | 'playing' | 'ended'>('loading');
  const [plays, setPlays] = useState(0);
  const [progress, setProgress] = useState(0);
  const playsRef = useRef(0);
  const startedRef = useRef(false);
  const failedRef = useRef(false);
  const readyTimeoutRef = useRef<number | null>(null);
  // Parent callbacks change identity every render; keep the latest in refs so
  // the media listeners below are attached once per clip.
  const callbacksRef = useRef({ onPlaybackStart, onPlay, onFailure });
  useEffect(() => {
    callbacksRef.current = { onPlaybackStart, onPlay, onFailure };
  });

  const fail = useCallback((reason: PlacementTechnicalReason) => {
    if (failedRef.current) return;
    failedRef.current = true;
    if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
    callbacksRef.current.onFailure(reason);
  }, []);

  const tryPlay = useCallback(() => {
    const element = getListeningAudio();
    element.play().catch((err: unknown) => {
      if (err instanceof DOMException && err.name === 'NotAllowedError') setPhase('blocked');
      else if (!(err instanceof DOMException && err.name === 'AbortError')) fail('audio_decode_error');
    });
  }, [fail]);

  // Fetch the clip as an authorised blob: the endpoint is Bearer-gated and
  // lives behind the API base, which a bare <audio src> cannot satisfy.
  useEffect(() => {
    if (!apiPath) {
      const handle = window.setTimeout(() => fail('audio_unavailable'), 0);
      return () => window.clearTimeout(handle);
    }
    let cancelled = false;
    let created: string | null = null;
    readyTimeoutRef.current = window.setTimeout(() => fail('media_timeout'), 30_000);
    fetchAuthorizedObjectUrl(apiPath)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        created = url;
        setObjectUrl(url);
      })
      .catch(() => {
        if (!cancelled) fail('audio_unavailable');
      });
    return () => {
      cancelled = true;
      if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
      if (created) URL.revokeObjectURL(created);
    };
  }, [apiPath, fail]);

  // Drive the shared (pre-unlocked) element for this clip.
  useEffect(() => {
    if (!objectUrl) return;
    const element = getListeningAudio();
    const onLoaded = () => {
      if (readyTimeoutRef.current !== null) window.clearTimeout(readyTimeoutRef.current);
      if (!Number.isFinite(element.duration) || element.duration <= 0) {
        fail('audio_zero_duration');
        return;
      }
      tryPlay();
    };
    const onPlaying = () => {
      setPhase('playing');
      if (!startedRef.current) {
        startedRef.current = true;
        callbacksRef.current.onPlaybackStart();
      }
    };
    const onPlayEvent = () => {
      playsRef.current += 1;
      setPlays(playsRef.current);
      callbacksRef.current.onPlay(playsRef.current);
    };
    const onTime = () => {
      if (element.duration > 0) setProgress(Math.min(1, element.currentTime / element.duration));
    };
    const onEnded = () => {
      setProgress(1);
      setPhase('ended');
    };
    const onError = () => fail('audio_decode_error');

    element.addEventListener('loadedmetadata', onLoaded);
    element.addEventListener('playing', onPlaying);
    element.addEventListener('play', onPlayEvent);
    element.addEventListener('timeupdate', onTime);
    element.addEventListener('ended', onEnded);
    element.addEventListener('error', onError);
    element.preload = 'auto';
    element.src = objectUrl;
    element.load();

    return () => {
      element.removeEventListener('loadedmetadata', onLoaded);
      element.removeEventListener('playing', onPlaying);
      element.removeEventListener('play', onPlayEvent);
      element.removeEventListener('timeupdate', onTime);
      element.removeEventListener('ended', onEnded);
      element.removeEventListener('error', onError);
      element.pause();
    };
  }, [fail, objectUrl, tryPlay]);

  const replaysLeft = Math.max(0, maxPlays - plays);

  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface p-5 sm:p-6" aria-label="Listening audio">
      <div className="flex items-center gap-2 text-sm font-semibold text-navy">
        <Headphones className="h-4 w-4 text-primary" aria-hidden />
        <span role="status">
          {phase === 'loading' ? 'Loading audio…' : null}
          {phase === 'blocked' ? 'Tap to start the audio' : null}
          {phase === 'playing' ? 'Playing — listen carefully' : null}
          {phase === 'ended' ? 'Audio finished' : null}
        </span>
      </div>

      <div
        className="h-2 w-full overflow-hidden rounded-full bg-border"
        role="progressbar"
        aria-label="Audio progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(progress * 100)}
      >
        <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${Math.round(progress * 100)}%` }} />
      </div>

      {phase === 'loading' ? <Loader2 className="h-5 w-5 animate-spin text-muted" aria-hidden /> : null}

      {phase === 'blocked' ? (
        <button type="button" onClick={tryPlay} className={`${PRIMARY_BUTTON} min-h-12 sm:w-full`}>
          <Play className="h-5 w-5" aria-hidden /> Tap to start audio
        </button>
      ) : null}

      {phase === 'ended' && replaysLeft > 0 ? (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <button
            type="button"
            onClick={() => {
              const element = getListeningAudio();
              element.currentTime = 0;
              setProgress(0);
              tryPlay();
            }}
            className={SECONDARY_BUTTON}
          >
            <RotateCcw className="h-4 w-4" aria-hidden /> Play again ({replaysLeft} left)
          </button>
          <p className="text-xs text-muted">A replay never lowers your result.</p>
        </div>
      ) : null}
    </section>
  );
}
