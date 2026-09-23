'use client';

import type { ReactNode } from 'react';
import { Mic, MicOff, PhoneOff } from 'lucide-react';
import {
  LiveKitRoom,
  RoomAudioRenderer,
  useLocalParticipant,
  useRemoteParticipants,
} from '@livekit/components-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { LiveRoomRealtimeProvider } from './LiveRoomRealtime';

export interface LiveTutorRoomShellProps {
  liveRoomId: string;
  livekitWssUrl: string;
  token: string;
  onEnd: () => void;
  children?: ReactNode;
  className?: string;
}

export function LiveTutorRoomShell({
  liveRoomId,
  livekitWssUrl,
  token,
  onEnd,
  children,
  className,
}: LiveTutorRoomShellProps) {
  return (
    <LiveRoomRealtimeProvider liveRoomId={liveRoomId}>
      <div className={cn('grid gap-4 lg:grid-cols-[1fr_minmax(280px,420px)]', className)}>
        <div className="relative h-full min-h-[480px] w-full">
          <LiveKitRoom
            token={token}
            serverUrl={livekitWssUrl}
            connect
            video={false}
            audio
            data-lk-theme="default"
            className="h-full w-full"
          >
            <TutorRoomInterior onEnd={onEnd} />
            <RoomAudioRenderer />
          </LiveKitRoom>
        </div>
        <aside className="flex h-full min-h-[480px] w-full flex-col gap-3 overflow-y-auto rounded-2xl border border-border bg-surface p-4 lg:max-w-md">
          {children}
        </aside>
      </div>
    </LiveRoomRealtimeProvider>
  );
}

function TutorRoomInterior({ onEnd }: { onEnd: () => void }) {
  // Audio-only room: the token grants microphone publish only.
  const { localParticipant } = useLocalParticipant();
  const remoteConnected = useRemoteParticipants().length > 0;
  const micOn = localParticipant.isMicrophoneEnabled;

  return (
    <div className="relative h-full w-full overflow-hidden rounded-2xl bg-background-dark">
      <div className="absolute inset-0 flex items-center justify-center">
        <div className="text-sm text-white/70" role="status">{remoteConnected ? 'The candidate is connected (audio only).' : 'Waiting for the candidate to join…'}</div>
      </div>
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
        <Button type="button" variant="destructive" size="sm" onClick={onEnd} className="rounded-full" data-testid="tutor-room-end">
          <PhoneOff className="mr-2 h-4 w-4" /> End session
        </Button>
      </div>
    </div>
  );
}

export default LiveTutorRoomShell;
