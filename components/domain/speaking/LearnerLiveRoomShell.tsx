'use client';

import type { ReactNode } from 'react';
import { Mic, MicOff, PhoneOff, Video, VideoOff } from 'lucide-react';
import {
  LiveKitRoom,
  RoomAudioRenderer,
  VideoTrack,
  useLocalParticipant,
  useTracks,
} from '@livekit/components-react';
import { Track } from 'livekit-client';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { LiveRoomRealtimeProvider } from './LiveRoomRealtime';

export interface LearnerLiveRoomShellProps {
  liveRoomId: string;
  livekitWssUrl: string;
  token: string;
  onEnd: () => void;
  className?: string;
  children?: ReactNode;
}

export function LearnerLiveRoomShell({
  liveRoomId,
  livekitWssUrl,
  token,
  onEnd,
  className,
  children,
}: LearnerLiveRoomShellProps) {
  return (
    <LiveRoomRealtimeProvider liveRoomId={liveRoomId}>
      <div className={cn('relative h-full min-h-[480px] w-full', className)}>
        <LiveKitRoom
          token={token}
          serverUrl={livekitWssUrl}
          connect
          video
          audio
          data-lk-theme="default"
          className="h-full w-full"
        >
          <LearnerRoomInterior onEnd={onEnd}>{children}</LearnerRoomInterior>
          <RoomAudioRenderer />
        </LiveKitRoom>
      </div>
    </LiveRoomRealtimeProvider>
  );
}

function LearnerRoomInterior({ onEnd, children }: { onEnd: () => void; children?: ReactNode }) {
  const tracks = useTracks([Track.Source.Camera, Track.Source.Microphone]);
  const { localParticipant } = useLocalParticipant();
  const remoteCamera = tracks.find((track) => !track.participant.isLocal && track.source === Track.Source.Camera);
  const localCamera = tracks.find((track) => track.participant.isLocal && track.source === Track.Source.Camera);
  const micOn = localParticipant.isMicrophoneEnabled;
  const camOn = localParticipant.isCameraEnabled;

  return (
    <div className="relative h-full w-full overflow-hidden rounded-2xl bg-background-dark">
      <div className="absolute inset-0 flex items-center justify-center">
        {remoteCamera ? <VideoTrack trackRef={remoteCamera} className="h-full w-full object-cover" /> : <div className="text-sm text-white/70">Waiting for your tutor to join…</div>}
      </div>
      <div className="absolute bottom-4 right-4 h-32 w-44 overflow-hidden rounded-xl border border-white/20 bg-navy shadow-lg sm:h-40 sm:w-56">
        {localCamera ? <VideoTrack trackRef={localCamera} className="h-full w-full object-cover" /> : <div className="flex h-full items-center justify-center text-xs text-white/50">Camera off</div>}
      </div>
      {children ? <div className="absolute bottom-44 left-1/2 w-[min(90%,640px)] -translate-x-1/2 rounded-xl bg-navy/60 px-4 py-2 text-sm text-white backdrop-blur">{children}</div> : null}
      <div className="absolute bottom-4 left-1/2 flex -translate-x-1/2 items-center gap-2 rounded-full bg-navy/60 px-3 py-2 backdrop-blur">
        <Button
          type="button"
          variant={micOn ? 'ghost' : 'destructive'}
          size="sm"
          onClick={() => void localParticipant.setMicrophoneEnabled(!micOn)}
          aria-label={micOn ? 'Mute microphone' : 'Unmute microphone'}
          className={cn('rounded-full', micOn && 'text-white hover:bg-white/10')}
        >
          {micOn ? <Mic className="h-4 w-4" /> : <MicOff className="h-4 w-4" />}
        </Button>
        <Button
          type="button"
          variant={camOn ? 'ghost' : 'destructive'}
          size="sm"
          onClick={() => void localParticipant.setCameraEnabled(!camOn)}
          aria-label={camOn ? 'Turn camera off' : 'Turn camera on'}
          className={cn('rounded-full', camOn && 'text-white hover:bg-white/10')}
        >
          {camOn ? <Video className="h-4 w-4" /> : <VideoOff className="h-4 w-4" />}
        </Button>
        <Button type="button" variant="destructive" size="sm" onClick={onEnd} className="rounded-full" data-testid="live-room-end">
          <PhoneOff className="mr-2 h-4 w-4" /> End session
        </Button>
      </div>
    </div>
  );
}

export default LearnerLiveRoomShell;
