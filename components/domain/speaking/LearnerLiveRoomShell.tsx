'use client';

/**
 * Phase 3 — learner-side LiveKit room shell (plan C.3).
 *
 * Wraps a single `LiveKitRoom` instance and renders a learner-friendly
 * 2-tile video layout:
 *   • Large tile  → tutor video + audio
 *   • Small tile  → own self-view (muted to avoid feedback)
 * The learner is never shown the interlocutor script — that lives in
 * `TutorCuePanel`, gated by tutor role.
 *
 * NOTE: `@livekit/components-react` is not installed yet. This file
 * lazy-imports the package at runtime so a missing dependency yields
 * a clean placeholder instead of a build failure. Once the package is
 * added (`npm i @livekit/components-react @livekit/components-styles
 * livekit-client`), the placeholder branch is never taken.
 *
 * TODO(P3-infra): install @livekit/components-react and remove the
 * `unknownLiveKit` fallback below.
 */
import { useEffect, useState, type ReactNode } from 'react';
import { Loader2, Mic, MicOff, PhoneOff, Video, VideoOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

export interface LearnerLiveRoomShellProps {
  /** LiveKit signalling URL (wss://...). */
  livekitWssUrl: string;
  /** Short-lived LiveKit JWT minted by `issueLiveRoomToken`. */
  token: string;
  /** Invoked when the learner clicks "End session". */
  onEnd: () => void;
  /** Optional className for the outer container. */
  className?: string;
  /** Optional slot rendered above the controls (e.g. captions). */
  children?: ReactNode;
}

type LiveKitModule = typeof import('@livekit/components-react');

export function LearnerLiveRoomShell({
  livekitWssUrl,
  token,
  onEnd,
  className,
  children,
}: LearnerLiveRoomShellProps) {
  const [livekit, setLivekit] = useState<LiveKitModule | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let cancelled = false;
    // Dynamic import — keeps the dep optional during the migration.
    // TODO(P3-infra): replace with a top-level static import once the
    // package is installed.
    import('@livekit/components-react')
      .then((mod) => {
        if (!cancelled) setLivekit(mod as LiveKitModule);
      })
      .catch(() => {
        if (!cancelled) setUnavailable(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (unavailable) {
    return (
      <LiveRoomPlaceholder
        className={className}
        role="learner"
        onEnd={onEnd}
        livekitWssUrl={livekitWssUrl}
        tokenPresent={Boolean(token)}
      />
    );
  }

  if (!livekit) {
    return (
      <div
        className={cn(
          'flex h-full min-h-[420px] items-center justify-center rounded-2xl border border-slate-200 bg-slate-50',
          className,
        )}
      >
        <span className="inline-flex items-center gap-2 text-sm text-slate-500">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Connecting to live room…
        </span>
      </div>
    );
  }

  const {
    LiveKitRoom,
    RoomAudioRenderer,
    useTracks,
    VideoTrack,
    useLocalParticipant,
  } = livekit;

  // Inner consumer that has access to LiveKit context. We can't reach
  // hooks until we're inside <LiveKitRoom>, so split the rendering.
  function RoomInterior() {
    // Both `useTracks` and `useLocalParticipant` are LiveKit hooks; the
    // module type narrows them at runtime above.
    const tracks = (useTracks as unknown as (sources: Array<unknown>) => Array<{
      participant: { identity: string; isLocal: boolean };
      publication?: { kind: string };
      source?: string;
    }>)([
      // Subscribe to camera + microphone published by all participants.
      // We rely on string literals because the type imports are dynamic.
      'camera',
      'microphone',
    ]);
    const local = (useLocalParticipant as unknown as () => {
      localParticipant: {
        isMicrophoneEnabled: boolean;
        isCameraEnabled: boolean;
        setMicrophoneEnabled: (on: boolean) => Promise<unknown>;
        setCameraEnabled: (on: boolean) => Promise<unknown>;
      };
    })();

    const remoteCamera = tracks.find(
      (t) => !t.participant.isLocal && t.source === 'camera',
    );
    const localCamera = tracks.find(
      (t) => t.participant.isLocal && t.source === 'camera',
    );

    const micOn = local.localParticipant.isMicrophoneEnabled;
    const camOn = local.localParticipant.isCameraEnabled;

    return (
      <div className="relative h-full w-full overflow-hidden rounded-2xl bg-slate-950">
        {/* Large tile — tutor */}
        <div className="absolute inset-0 flex items-center justify-center">
          {remoteCamera ? (
            // @ts-expect-error — dynamic import type narrowing
            <VideoTrack trackRef={remoteCamera} className="h-full w-full object-cover" />
          ) : (
            <div className="text-sm text-slate-400">Waiting for your tutor to join…</div>
          )}
        </div>

        {/* Small tile — self-view (muted) */}
        <div className="absolute bottom-4 right-4 h-32 w-44 overflow-hidden rounded-xl border border-white/20 bg-slate-900 shadow-lg sm:h-40 sm:w-56">
          {localCamera ? (
            // @ts-expect-error — dynamic import type narrowing
            <VideoTrack trackRef={localCamera} className="h-full w-full object-cover" />
          ) : (
            <div className="flex h-full items-center justify-center text-xs text-slate-500">
              Camera off
            </div>
          )}
        </div>

        {/* Captions / extra slot */}
        {children ? (
          <div className="absolute bottom-44 left-1/2 w-[min(90%,640px)] -translate-x-1/2 rounded-xl bg-black/60 px-4 py-2 text-sm text-white backdrop-blur">
            {children}
          </div>
        ) : null}

        {/* Controls */}
        <div className="absolute bottom-4 left-1/2 flex -translate-x-1/2 items-center gap-2 rounded-full bg-black/60 px-3 py-2 backdrop-blur">
          <Button
            type="button"
            variant={micOn ? 'ghost' : 'destructive'}
            size="sm"
            onClick={() => void local.localParticipant.setMicrophoneEnabled(!micOn)}
            aria-label={micOn ? 'Mute microphone' : 'Unmute microphone'}
            className={cn('rounded-full', micOn && 'text-white hover:bg-white/10')}
          >
            {micOn ? <Mic className="h-4 w-4" /> : <MicOff className="h-4 w-4" />}
          </Button>
          <Button
            type="button"
            variant={camOn ? 'ghost' : 'destructive'}
            size="sm"
            onClick={() => void local.localParticipant.setCameraEnabled(!camOn)}
            aria-label={camOn ? 'Turn camera off' : 'Turn camera on'}
            className={cn('rounded-full', camOn && 'text-white hover:bg-white/10')}
          >
            {camOn ? <Video className="h-4 w-4" /> : <VideoOff className="h-4 w-4" />}
          </Button>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            onClick={onEnd}
            className="rounded-full"
            data-testid="live-room-end"
          >
            <PhoneOff className="mr-2 h-4 w-4" /> End session
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={cn('relative h-full min-h-[480px] w-full', className)}>
      {/* @ts-expect-error — dynamic import type narrowing */}
      <LiveKitRoom
        token={token}
        serverUrl={livekitWssUrl}
        connect
        video
        audio
        data-lk-theme="default"
        onDisconnected={onEnd}
        className="h-full w-full"
      >
        <RoomInterior />
        {/* @ts-expect-error — dynamic import type narrowing */}
        <RoomAudioRenderer />
      </LiveKitRoom>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Placeholder shown when @livekit/components-react isn't installed
// ─────────────────────────────────────────────────────────────────────────────

interface LiveRoomPlaceholderProps {
  className?: string;
  role: 'learner' | 'tutor';
  onEnd: () => void;
  livekitWssUrl: string;
  tokenPresent: boolean;
}

export function LiveRoomPlaceholder({
  className,
  role,
  onEnd,
  livekitWssUrl,
  tokenPresent,
}: LiveRoomPlaceholderProps) {
  return (
    <div
      className={cn(
        'flex h-full min-h-[420px] flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-amber-300 bg-amber-50 p-6 text-center',
        className,
      )}
      data-testid="livekit-placeholder"
    >
      <h3 className="text-lg font-semibold text-amber-900">
        Live video temporarily unavailable
      </h3>
      <p className="max-w-md text-sm text-amber-800">
        The LiveKit client package isn&apos;t installed in this build. Booking
        details have been preserved — please refresh once the room is ready,
        or end this session.
      </p>
      <code className="rounded bg-white/60 px-2 py-1 text-xs text-amber-700">
        role={role} · server={livekitWssUrl} · token={tokenPresent ? 'present' : 'missing'}
      </code>
      <Button type="button" variant="destructive" onClick={onEnd}>
        <PhoneOff className="mr-2 h-4 w-4" /> End session
      </Button>
    </div>
  );
}

export default LearnerLiveRoomShell;
