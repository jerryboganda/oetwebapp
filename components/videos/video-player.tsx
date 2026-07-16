'use client';

import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from 'react';
import {
  Captions,
  Loader2,
  Maximize,
  Minimize,
  Pause,
  Play,
  RotateCcw,
  Volume2,
  VolumeX,
} from 'lucide-react';
import { createHlsEngine, type HlsEngineHandle, type HlsQualityLevel } from '@/lib/video/hls-engine';
import {
  PlaybackGateError,
  requestPlaybackSession,
  type PlaybackGateErrorCode,
} from '@/lib/video/attestation';
import { postVideoEvent, postVideoProgress, renewPlaybackSession } from '@/lib/api/videos';
import { setVideoScreenProtection } from '@/lib/video/screen-protection';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import type { PlaybackSession, VideoChapter, VideoLibraryProgress } from '@/lib/types/videos';
import { UpdateAppNotice } from '@/components/videos/update-app-notice';
import { WatermarkOverlay } from '@/components/videos/watermark-overlay';

const PLAYBACK_RATES = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
const HEARTBEAT_INTERVAL_MS = 60_000;
const RENEW_LEEWAY_MS = 120_000;

// TEMPORARY: on-screen diagnostic HUD for the desktop black-video investigation.
// Flip to false / delete once resolved. Shows the real <video> element state so we
// can tell "stream not loading" (readyState 0-1, dim 0x0) from "loading but not
// rendering / overlay-blackout" (readyState 4, dim 1920x1080, time advancing).
const SHOW_VIDEO_DIAG = true;

export interface VideoPlayerHandle {
  seekTo(seconds: number): void;
}

type PlayerPhase =
  | { kind: 'attesting' }
  | { kind: 'playing'; session: PlaybackSession }
  | { kind: 'update-required'; platform: 'desktop' | 'capacitor-native' }
  | { kind: 'error'; code: PlaybackGateErrorCode; message: string };

function gateMessage(code: PlaybackGateErrorCode): string {
  switch (code) {
    case 'CONTENT_LOCKED':
      return 'This video is part of a premium package. Upgrade your plan to watch it.';
    case 'SUBSCRIPTION_FROZEN':
      return 'Your subscription is currently frozen. Unfreeze it to continue watching.';
    case 'SUBSCRIPTION_EXPIRED':
      return 'Your subscription has expired. Renew it to continue watching.';
    case 'SESSION_LIMIT':
      return 'You have too many active video sessions. Close playback on another device and try again.';
    case 'NOT_CONFIGURED':
      return 'Video streaming is not available right now. Please try again later.';
    case 'ATTESTATION_REJECTED':
    case 'ATTESTATION_UNAVAILABLE':
    case 'WEB_NOT_ALLOWED':
      return 'This device could not be verified for secure playback.';
    default:
      return 'Could not start playback. Check your connection and try again.';
  }
}

function formatClock(totalSeconds: number): string {
  if (!Number.isFinite(totalSeconds) || totalSeconds < 0) return '0:00';
  const seconds = Math.floor(totalSeconds % 60);
  const minutes = Math.floor((totalSeconds / 60) % 60);
  const hours = Math.floor(totalSeconds / 3600);
  const mm = hours > 0 ? String(minutes).padStart(2, '0') : String(minutes);
  return `${hours > 0 ? `${hours}:` : ''}${mm}:${String(seconds).padStart(2, '0')}`;
}

export interface VideoPlayerProps {
  videoId: string;
  userId: string;
  durationSeconds: number;
  initialProgress: VideoLibraryProgress | null;
  chapters: VideoChapter[];
  onProgressPersisted?: (progress: { percentComplete: number; completed: boolean; positionSeconds: number }) => void;
}

/**
 * Native-only video player. Mounted exclusively when the runtime is a native
 * shell — but even if forced to mount on web, the server refuses the playback
 * session, so nothing plays. Renders its own controls so the forensic
 * watermark survives fullscreen (the CONTAINER fullscreens, not the video).
 */
export const VideoPlayer = forwardRef<VideoPlayerHandle, VideoPlayerProps>(function VideoPlayer(
  { videoId, userId, durationSeconds, initialProgress, chapters, onProgressPersisted },
  ref,
) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const engineRef = useRef<HlsEngineHandle | null>(null);
  const sessionRef = useRef<PlaybackSession | null>(null);
  const renewTimerRef = useRef<number | null>(null);
  const heartbeatTimerRef = useRef<number | null>(null);
  const maxWatchedRef = useRef(initialProgress?.positionSeconds ?? 0);
  const lastReportedRef = useRef(initialProgress?.positionSeconds ?? 0);
  const resumedRef = useRef(false);
  const recoveringRef = useRef(false);

  const [phase, setPhase] = useState<PlayerPhase>({ kind: 'attesting' });
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(durationSeconds);
  const [muted, setMuted] = useState(false);
  const [playbackRate, setPlaybackRate] = useState(1);
  const [qualityLevels, setQualityLevels] = useState<HlsQualityLevel[]>([]);
  const [currentQuality, setCurrentQuality] = useState(-1);
  const [captionsOn, setCaptionsOn] = useState(false);
  const [hasCaptionTracks, setHasCaptionTracks] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [diagWdaOk, setDiagWdaOk] = useState<boolean | null>(null);
  const [diagVersion, setDiagVersion] = useState<string>('?');
  const [diag, setDiag] = useState<string>('');

  useImperativeHandle(ref, () => ({
    seekTo(seconds: number) {
      const video = videoRef.current;
      if (!video) return;
      video.currentTime = Math.max(0, Math.min(seconds, video.duration || seconds));
      void postVideoEvent({ videoId, sessionId: sessionRef.current?.sessionId, eventType: 'seek', positionSeconds: seconds });
    },
  }));

  const reportProgress = useCallback(
    async (seconds: number) => {
      try {
        const response = await postVideoProgress(videoId, seconds);
        onProgressPersisted?.({
          percentComplete: response.percentComplete,
          completed: response.completed,
          positionSeconds: response.positionSeconds,
        });
      } catch {
        // Best-effort — never interrupt playback for a progress sync failure.
      }
    },
    [onProgressPersisted, videoId],
  );

  const teardownEngine = useCallback(() => {
    if (renewTimerRef.current !== null) {
      window.clearTimeout(renewTimerRef.current);
      renewTimerRef.current = null;
    }
    engineRef.current?.destroy();
    engineRef.current = null;
  }, []);

  const scheduleRenewal = useCallback(
    (session: PlaybackSession, onRenewFailed: () => void) => {
      if (renewTimerRef.current !== null) {
        window.clearTimeout(renewTimerRef.current);
        renewTimerRef.current = null;
      }
      const expiresIn = new Date(session.expiresAt).getTime() - Date.now();
      const delay = Math.max(expiresIn - RENEW_LEEWAY_MS, 15_000);
      if (!Number.isFinite(expiresIn) || expiresIn <= 0) return;
      renewTimerRef.current = window.setTimeout(() => {
        void (async () => {
          try {
            const renewed = await renewPlaybackSession(session.sessionId);
            sessionRef.current = renewed;
            await engineRef.current?.recoverWithUrl(renewed.playbackUrl);
            void postVideoEvent({
              videoId,
              sessionId: renewed.sessionId,
              eventType: 'session_renewed',
              positionSeconds: videoRef.current?.currentTime ?? 0,
            });
            scheduleRenewal(renewed, onRenewFailed);
          } catch {
            onRenewFailed();
          }
        })();
      }, delay);
    },
    [videoId],
  );

  const startPlayback = useCallback(async () => {
    const video = videoRef.current;
    if (!video) return;

    setPhase({ kind: 'attesting' });
    teardownEngine();

    let session: PlaybackSession;
    try {
      session = await requestPlaybackSession(videoId, userId);
    } catch (error) {
      if (error instanceof PlaybackGateError) {
        if (error.code === 'DESKTOP_UPDATE_REQUIRED') {
          setPhase({ kind: 'update-required', platform: 'desktop' });
        } else if (error.code === 'MOBILE_UPDATE_REQUIRED') {
          setPhase({ kind: 'update-required', platform: 'capacitor-native' });
        } else {
          setPhase({ kind: 'error', code: error.code, message: gateMessage(error.code) });
        }
      } else {
        setPhase({ kind: 'error', code: 'NETWORK', message: gateMessage('NETWORK') });
      }
      return;
    }

    sessionRef.current = session;

    // Full re-attestation path once the session itself has expired.
    const reattest = () => {
      if (recoveringRef.current) return;
      recoveringRef.current = true;
      void (async () => {
        try {
          const fresh = await requestPlaybackSession(videoId, userId);
          sessionRef.current = fresh;
          await engineRef.current?.recoverWithUrl(fresh.playbackUrl);
          setPhase({ kind: 'playing', session: fresh });
          scheduleRenewal(fresh, reattest);
        } catch {
          setPhase({ kind: 'error', code: 'NETWORK', message: gateMessage('NETWORK') });
        } finally {
          recoveringRef.current = false;
        }
      })();
    };

    try {
      const engine = await createHlsEngine(video, session.playbackUrl);
      engineRef.current = engine;
      engine.onLevelsUpdated(setQualityLevels);
      engine.onFatalNetworkError(() => {
        // Signed URL most likely expired mid-play: renew, then re-attest.
        void (async () => {
          const active = sessionRef.current;
          if (!active) return reattest();
          try {
            const renewed = await renewPlaybackSession(active.sessionId);
            sessionRef.current = renewed;
            await engineRef.current?.recoverWithUrl(renewed.playbackUrl);
            scheduleRenewal(renewed, reattest);
          } catch {
            reattest();
          }
        })();
      });
      setQualityLevels(engine.levels);
      setPhase({ kind: 'playing', session });
      scheduleRenewal(session, reattest);
      void postVideoEvent({ videoId, sessionId: session.sessionId, eventType: 'play', positionSeconds: 0 });
    } catch {
      setPhase({ kind: 'error', code: 'NETWORK', message: gateMessage('NETWORK') });
    }
  }, [scheduleRenewal, teardownEngine, userId, videoId]);

  // Boot: attest + attach. Also engage OS screen-capture protection for the
  // lifetime of the player on every native shell — desktop (Tauri window
  // capture-exclusion) AND mobile (Android FLAG_SECURE) — so screenshots and
  // screen recorders capture only black. No-op on web. Best-effort, never a gate.
  useEffect(() => {
    void setVideoScreenProtection(true).then((ok) => setDiagWdaOk(ok));
    void startPlayback();
    return () => {
      teardownEngine();
      if (heartbeatTimerRef.current !== null) window.clearInterval(heartbeatTimerRef.current);
      void setVideoScreenProtection(false);
      if (maxWatchedRef.current > lastReportedRef.current) {
        lastReportedRef.current = maxWatchedRef.current;
        void reportProgress(maxWatchedRef.current);
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [videoId]);

  // Heartbeat analytics while playing.
  useEffect(() => {
    if (phase.kind !== 'playing' || !isPlaying) return;
    heartbeatTimerRef.current = window.setInterval(() => {
      void postVideoEvent({
        videoId,
        sessionId: sessionRef.current?.sessionId,
        eventType: 'heartbeat',
        positionSeconds: videoRef.current?.currentTime ?? 0,
      });
    }, HEARTBEAT_INTERVAL_MS);
    return () => {
      if (heartbeatTimerRef.current !== null) {
        window.clearInterval(heartbeatTimerRef.current);
        heartbeatTimerRef.current = null;
      }
    };
  }, [isPlaying, phase.kind, videoId]);

  useEffect(() => {
    const onFullscreenChange = () => setIsFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener('fullscreenchange', onFullscreenChange);
    return () => document.removeEventListener('fullscreenchange', onFullscreenChange);
  }, []);

  // TEMPORARY diagnostic: capture the desktop shell version + sample the live
  // <video> element state so we can see (from the user's machine) whether the
  // stream is decoding or the frame is being suppressed. Remove with SHOW_VIDEO_DIAG.
  useEffect(() => {
    if (!SHOW_VIDEO_DIAG) return;
    void (async () => {
      try {
        const info = await window.desktopBridge?.runtime?.info?.();
        if (info?.appVersion) setDiagVersion(info.appVersion);
        else setDiagVersion(getAppRuntimeKind());
      } catch {
        setDiagVersion(getAppRuntimeKind());
      }
    })();
    const id = window.setInterval(() => {
      const v = videoRef.current;
      if (!v) return;
      const buffered = v.buffered;
      const bufEnd = buffered.length ? buffered.end(buffered.length - 1) : 0;
      setDiag(
        [
          `v${diagVersion} ${getAppRuntimeKind()} wda=${diagWdaOk === null ? '…' : diagWdaOk ? 'ON' : 'off'} phase=${phase.kind}`,
          `hls=${engineRef.current?.mode ?? '-'} ready=${v.readyState} net=${v.networkState} err=${v.error?.code ?? '-'}`,
          `dim=${v.videoWidth}x${v.videoHeight} t=${v.currentTime.toFixed(1)} buf=${bufEnd.toFixed(1)} paused=${v.paused}`,
        ].join('\n'),
      );
    }, 500);
    return () => window.clearInterval(id);
  }, [diagWdaOk, diagVersion, phase.kind]);

  const refreshCaptionTracks = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    setHasCaptionTracks(video.textTracks.length > 0);
  }, []);

  const toggleCaptions = useCallback(() => {
    const video = videoRef.current;
    if (!video || video.textTracks.length === 0) return;
    const next = !captionsOn;
    for (let i = 0; i < video.textTracks.length; i += 1) {
      video.textTracks[i].mode = next && i === 0 ? 'showing' : 'hidden';
    }
    setCaptionsOn(next);
  }, [captionsOn]);

  const togglePlay = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) {
      void video.play().catch(() => undefined);
    } else {
      video.pause();
    }
  }, []);

  const toggleFullscreen = useCallback(() => {
    const container = containerRef.current;
    if (!container) return;
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => undefined);
    } else {
      void container.requestFullscreen().catch(() => undefined);
    }
  }, []);

  const seekBy = useCallback((delta: number) => {
    const video = videoRef.current;
    if (!video) return;
    video.currentTime = Math.max(0, Math.min(video.currentTime + delta, video.duration || Infinity));
  }, []);

  const handleKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLDivElement>) => {
      const video = videoRef.current;
      if (!video) return;
      const key = event.key;
      if (key === ' ' || key.toLowerCase() === 'k') {
        event.preventDefault();
        togglePlay();
      } else if (key === 'ArrowLeft') {
        event.preventDefault();
        seekBy(-5);
      } else if (key === 'ArrowRight') {
        event.preventDefault();
        seekBy(5);
      } else if (key.toLowerCase() === 'j') {
        seekBy(-10);
      } else if (key.toLowerCase() === 'l') {
        seekBy(10);
      } else if (key.toLowerCase() === 'm') {
        video.muted = !video.muted;
        setMuted(video.muted);
      } else if (key.toLowerCase() === 'f') {
        toggleFullscreen();
      } else if (key.toLowerCase() === 'c') {
        toggleCaptions();
      } else if (/^[0-9]$/.test(key) && Number.isFinite(video.duration)) {
        video.currentTime = (Number(key) / 10) * video.duration;
      } else if (key === '>' || key === '.') {
        const next = PLAYBACK_RATES.find((rate) => rate > video.playbackRate);
        if (next) {
          video.playbackRate = next;
          setPlaybackRate(next);
        }
      } else if (key === '<' || key === ',') {
        const slower = [...PLAYBACK_RATES].reverse().find((rate) => rate < video.playbackRate);
        if (slower) {
          video.playbackRate = slower;
          setPlaybackRate(slower);
        }
      }
    },
    [seekBy, toggleCaptions, toggleFullscreen, togglePlay],
  );

  if (phase.kind === 'update-required') {
    return <UpdateAppNotice platform={phase.platform} />;
  }

  if (phase.kind === 'error') {
    return (
      <div className="flex h-full min-h-[280px] w-full flex-col items-center justify-center gap-4 bg-navy px-6 py-10 text-center">
        <p className="max-w-md text-sm leading-6 text-white/75">{phase.message}</p>
        <button
          type="button"
          onClick={() => void startPlayback()}
          className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-dark"
        >
          <RotateCcw className="h-4 w-4" aria-hidden="true" />
          Try again
        </button>
      </div>
    );
  }

  const progressPercent = duration > 0 ? Math.min(100, (currentTime / duration) * 100) : 0;

  return (
    <div
      ref={containerRef}
      className="group relative h-full w-full bg-black outline-none"
      tabIndex={0}
      role="application"
      aria-label="Video player"
      onKeyDown={handleKeyDown}
    >
      {/* eslint-disable-next-line jsx-a11y/media-has-caption -- caption tracks arrive via the HLS manifest */}
      <video
        ref={videoRef}
        className="h-full w-full"
        playsInline
        controls={false}
        controlsList="nodownload noremoteplayback"
        disablePictureInPicture
        onContextMenu={(event) => event.preventDefault()}
        onClick={togglePlay}
        onLoadedMetadata={(event) => {
          setDuration(event.currentTarget.duration || durationSeconds);
          refreshCaptionTracks();
          if (resumedRef.current) return;
          const resumeAt = initialProgress?.completed ? 0 : initialProgress?.positionSeconds ?? 0;
          const total = event.currentTarget.duration || durationSeconds;
          if (resumeAt > 5 && resumeAt < total - 10) {
            event.currentTarget.currentTime = resumeAt;
          }
          resumedRef.current = true;
        }}
        onTimeUpdate={(event) => {
          const watched = Math.floor(event.currentTarget.currentTime);
          setCurrentTime(event.currentTarget.currentTime);
          maxWatchedRef.current = Math.max(maxWatchedRef.current, watched);
          if (maxWatchedRef.current - lastReportedRef.current >= 15) {
            lastReportedRef.current = maxWatchedRef.current;
            void reportProgress(maxWatchedRef.current);
          }
        }}
        onPlay={() => {
          setIsPlaying(true);
          void postVideoEvent({
            videoId,
            sessionId: sessionRef.current?.sessionId,
            eventType: 'play',
            positionSeconds: videoRef.current?.currentTime ?? 0,
          });
        }}
        onPause={() => {
          setIsPlaying(false);
          if (maxWatchedRef.current > lastReportedRef.current) {
            lastReportedRef.current = maxWatchedRef.current;
            void reportProgress(maxWatchedRef.current);
          }
          void postVideoEvent({
            videoId,
            sessionId: sessionRef.current?.sessionId,
            eventType: 'pause',
            positionSeconds: videoRef.current?.currentTime ?? 0,
          });
        }}
        onEnded={() => {
          setIsPlaying(false);
          maxWatchedRef.current = Math.max(maxWatchedRef.current, Math.floor(duration));
          lastReportedRef.current = maxWatchedRef.current;
          void reportProgress(maxWatchedRef.current);
          void postVideoEvent({
            videoId,
            sessionId: sessionRef.current?.sessionId,
            eventType: 'complete',
            positionSeconds: Math.floor(duration),
          });
        }}
      />

      {phase.kind === 'attesting' && (
        <div className="absolute inset-0 z-30 flex flex-col items-center justify-center gap-3 bg-navy/90 text-white">
          <Loader2 className="h-8 w-8 animate-spin" aria-hidden="true" />
          <p className="text-sm text-white/75">Verifying this device for secure playback…</p>
        </div>
      )}

      {phase.kind === 'playing' && <WatermarkOverlay text={phase.session.watermarkText} />}

      {/* TEMPORARY diagnostic HUD — remove with SHOW_VIDEO_DIAG once the black-video issue is resolved. */}
      {SHOW_VIDEO_DIAG && diag && (
        <div className="pointer-events-none absolute left-2 top-2 z-40 whitespace-pre rounded bg-black/75 px-2 py-1 font-mono text-[11px] leading-tight text-lime-300">
          {diag}
        </div>
      )}

      {/* Controls */}
      <div className="absolute inset-x-0 bottom-0 z-30 bg-gradient-to-t from-black/85 via-black/40 to-transparent px-3 pb-2 pt-8 opacity-100 transition-opacity duration-200 md:opacity-0 md:group-focus-within:opacity-100 md:group-hover:opacity-100">
        <div className="relative mb-2 h-1.5 w-full cursor-pointer rounded-full bg-white/25"
          role="slider"
          aria-label="Seek"
          aria-valuemin={0}
          aria-valuemax={Math.floor(duration)}
          aria-valuenow={Math.floor(currentTime)}
          onClick={(event) => {
            const rect = event.currentTarget.getBoundingClientRect();
            const ratio = (event.clientX - rect.left) / rect.width;
            const video = videoRef.current;
            if (video && Number.isFinite(video.duration)) {
              video.currentTime = ratio * video.duration;
            }
          }}
        >
          <div className="h-full rounded-full bg-primary" style={{ width: `${progressPercent}%` }} />
          {chapters.map((chapter) =>
            duration > 0 && chapter.timeSeconds < duration ? (
              <span
                key={`${chapter.timeSeconds}-${chapter.title}`}
                title={chapter.title}
                className="absolute top-1/2 h-2.5 w-0.5 -translate-y-1/2 bg-white/80"
                style={{ left: `${(chapter.timeSeconds / duration) * 100}%` }}
              />
            ) : null,
          )}
        </div>
        <div className="flex items-center gap-2 text-white">
          <button type="button" onClick={togglePlay} aria-label={isPlaying ? 'Pause' : 'Play'} className="rounded p-1.5 hover:bg-white/15">
            {isPlaying ? <Pause className="h-5 w-5" /> : <Play className="h-5 w-5" />}
          </button>
          <button
            type="button"
            onClick={() => {
              const video = videoRef.current;
              if (!video) return;
              video.muted = !video.muted;
              setMuted(video.muted);
            }}
            aria-label={muted ? 'Unmute' : 'Mute'}
            className="rounded p-1.5 hover:bg-white/15"
          >
            {muted ? <VolumeX className="h-5 w-5" /> : <Volume2 className="h-5 w-5" />}
          </button>
          <span className="font-mono text-xs text-white/85">
            {formatClock(currentTime)} / {formatClock(duration)}
          </span>
          <div className="ml-auto flex items-center gap-2">
            <select
              value={playbackRate}
              onChange={(event) => {
                const rate = Number(event.target.value);
                const video = videoRef.current;
                if (video) video.playbackRate = rate;
                setPlaybackRate(rate);
              }}
              aria-label="Playback speed"
              className="rounded bg-white/15 px-1.5 py-1 text-xs font-semibold text-white [&>option]:text-navy"
            >
              {PLAYBACK_RATES.map((rate) => (
                <option key={rate} value={rate}>{rate}×</option>
              ))}
            </select>
            {qualityLevels.length > 0 && (
              <select
                value={currentQuality}
                onChange={(event) => {
                  const level = Number(event.target.value);
                  engineRef.current?.setQuality(level);
                  setCurrentQuality(level);
                  void postVideoEvent({
                    videoId,
                    sessionId: sessionRef.current?.sessionId,
                    eventType: 'quality_changed',
                    positionSeconds: videoRef.current?.currentTime ?? 0,
                  });
                }}
                aria-label="Video quality"
                className="rounded bg-white/15 px-1.5 py-1 text-xs font-semibold text-white [&>option]:text-navy"
              >
                <option value={-1}>Auto</option>
                {qualityLevels.map((level) => (
                  <option key={level.index} value={level.index}>{level.label}</option>
                ))}
              </select>
            )}
            {hasCaptionTracks && (
              <button
                type="button"
                onClick={toggleCaptions}
                aria-label={captionsOn ? 'Hide captions' : 'Show captions'}
                aria-pressed={captionsOn}
                className={`rounded p-1.5 hover:bg-white/15 ${captionsOn ? 'text-primary' : ''}`}
              >
                <Captions className="h-5 w-5" />
              </button>
            )}
            <button type="button" onClick={toggleFullscreen} aria-label={isFullscreen ? 'Exit fullscreen' : 'Fullscreen'} className="rounded p-1.5 hover:bg-white/15">
              {isFullscreen ? <Minimize className="h-5 w-5" /> : <Maximize className="h-5 w-5" />}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
});
