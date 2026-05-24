'use client';

/**
 * Phase 3 — tutor-side LiveKit room shell (plan C.3).
 *
 * Mirrors `LearnerLiveRoomShell` but inverts the tile sizes: the
 * learner video is rendered large (so the tutor can read body
 * language), and the tutor's own self-view is rendered small. The
 * tutor sidebar (typically `<TutorCuePanel />`) is rendered alongside
 * via the `children` prop.
 *
 * Like the learner shell, `@livekit/components-react` is lazy-loaded.
 *
 * TODO(P3-infra): install @livekit/components-react and remove the
 * `unavailable` placeholder branch below.
 */
import { useEffect, useState, type ReactNode } from 'react';
import { Loader2, Mic, MicOff, PhoneOff, Video, VideoOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { LiveRoomPlaceholder } from '@/components/domain/speaking/LearnerLiveRoomShell';

type LiveKitModule = typeof import('@livekit/components-react');

export interface LiveTutorRoomShellProps {
  /** LiveKit signalling URL (wss://...). */
  livekitWssUrl: string;
  /** Short-lived LiveKit JWT minted by `issueLiveRoomToken` for role=tutor. */
  token: string;
  /** Invoked when the tutor clicks "End session". */
  onEnd: () => void;
  /** Sidebar slot — typically `<TutorCuePanel cardId={...} />`. */
  children?: ReactNode;
  /** Optional className for the outer two-column grid. */
  className?: string;
}

export function LiveTutorRoomShell({
  livekitWssUrl,
  token,
  onEnd,
  children,
  className,
}: LiveTutorRoomShellProps) {
  const [livekit, setLivekit] = useState<LiveKitModule | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let cancelled = false;
    // webpackIgnore: keep the optional dep out of the bundle graph.
    // See LearnerLiveRoomShell for the full rationale; .catch() renders
    // the placeholder when the dep isn't installed at runtime.
    import(/* webpackIgnore: true */ '@livekit/components-react')
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

  const sidebar = (
    <aside className="flex h-full min-h-[480px] w-full flex-col gap-3 overflow-y-auto rounded-2xl border border-border bg-surface p-4 lg:max-w-md">
      {children}
    </aside>
  );

  if (unavailable) {
    return (
      <div className={cn('grid gap-4 lg:grid-cols-[1fr_minmax(280px,420px)]', className)}>
        <LiveRoomPlaceholder
          role="tutor"
          onEnd={onEnd}
          livekitWssUrl={livekitWssUrl}
          tokenPresent={Boolean(token)}
        />
        {sidebar}
      </div>
    );
  }

  if (!livekit) {
    return (
      <div className={cn('grid gap-4 lg:grid-cols-[1fr_minmax(280px,420px)]', className)}>
        <div className="flex h-full min-h-[480px] items-center justify-center rounded-2xl border border-border bg-muted">
          <span className="inline-flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Connecting tutor room…
          </span>
        </div>
        {sidebar}
      </div>
    );
  }

  const { LiveKitRoom, RoomAudioRenderer, useTracks, VideoTrack, useLocalParticipant } = livekit;

  function RoomInterior() {
    const tracks = (useTracks as unknown as (sources: Array<unknown>) => Array<{
      participant: { identity: string; isLocal: boolean };
      source?: string;
    }>)(['camera', 'microphone']);
    const local = (useLocalParticipant as unknown as () => {
      localParticipant: {
        isMicrophoneEnabled: boolean;
        isCameraEnabled: boolean;
        setMicrophoneEnabled: (on: boolean) => Promise<unknown>;
        setCameraEnabled: (on: boolean) => Promise<unknown>;
      };
    })();

    // Tutor view: remote (learner) is the LARGE tile.
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
        <div className="absolute inset-0 flex items-center justify-center">
          {remoteCamera ? (
            <VideoTrack trackRef={remoteCamera} className="h-full w-full object-cover" />
          ) : (
            <div className="text-sm text-slate-400">Waiting for the candidate to join…</div>
          )}
        </div>

        <div className="absolute bottom-4 right-4 h-32 w-44 overflow-hidden rounded-xl border border-white/20 bg-slate-900 shadow-lg sm:h-40 sm:w-56">
          {localCamera ? (
            <VideoTrack trackRef={localCamera} className="h-full w-full object-cover" />
          ) : (
            <div className="flex h-full items-center justify-center text-xs text-slate-500">
              Camera off
            </div>
          )}
        </div>

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
            data-testid="tutor-room-end"
          >
            <PhoneOff className="mr-2 h-4 w-4" /> End session
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={cn('grid gap-4 lg:grid-cols-[1fr_minmax(280px,420px)]', className)}>
      <div className="relative h-full min-h-[480px] w-full">
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
          <RoomAudioRenderer />
        </LiveKitRoom>
      </div>
      {sidebar}
    </div>
  );
}

export default LiveTutorRoomShell;
