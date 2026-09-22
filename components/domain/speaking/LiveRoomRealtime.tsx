'use client';

import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import type { HubConnection } from '@microsoft/signalr';

interface LiveRoomCue {
  liveRoomId: string;
  cueIndex: string;
  raisedBy: string;
  timestamp: string;
}

interface LiveRoomRealtimeContextValue {
  liveRoomId: string;
  connected: boolean;
  lastCue: LiveRoomCue | null;
  broadcastCue: (cueIndex: string) => Promise<void>;
}

const LiveRoomRealtimeContext = createContext<LiveRoomRealtimeContextValue | null>(null);

export function LiveRoomRealtimeProvider({ liveRoomId, children }: { liveRoomId: string; children: ReactNode }) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [lastCue, setLastCue] = useState<LiveRoomCue | null>(null);

  useEffect(() => {
    let cancelled = false;
    let connection: HubConnection | null = null;

    const connect = async () => {
      const [{ HubConnectionBuilder, LogLevel }, { ensureFreshAccessToken }] = await Promise.all([
        import('@microsoft/signalr'),
        import('@/lib/auth-client'),
      ]);
      if (cancelled) return;

      const nextConnection = new HubConnectionBuilder()
        .withUrl('/api/backend/v1/speaking/live-rooms/hub', {
          accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.None)
        .build();
      connection = nextConnection;

      nextConnection.on('CueRaised', (payload: LiveRoomCue) => {
        if (!cancelled) setLastCue(payload);
      });
      nextConnection.onreconnecting(() => {
        if (!cancelled) setConnected(false);
      });
      nextConnection.onreconnected(async () => {
        if (cancelled) return;
        try {
          await nextConnection.invoke('JoinRoom', liveRoomId);
          if (!cancelled) setConnected(true);
        } catch {
          if (!cancelled) setConnected(false);
        }
      });

      await nextConnection.start();
      await nextConnection.invoke('JoinRoom', liveRoomId);
      if (cancelled) return;
      connectionRef.current = nextConnection;
      setConnected(true);
    };

    connect().catch(() => {
      if (!cancelled) setConnected(false);
    });

    return () => {
      cancelled = true;
      const active = connectionRef.current ?? connection;
      connectionRef.current = null;
      setConnected(false);
      if (active) {
        void active.invoke('LeaveRoom', liveRoomId).catch(() => undefined).finally(() => active.stop());
      }
    };
  }, [liveRoomId]);

  const broadcastCue = useCallback(async (cueIndex: string) => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== 'Connected') {
      throw new Error('live_room_hub_not_connected');
    }
    await connection.invoke('BroadcastCue', liveRoomId, cueIndex);
  }, [liveRoomId]);

  return (
    <LiveRoomRealtimeContext.Provider value={{ liveRoomId, connected, lastCue, broadcastCue }}>
      {children}
    </LiveRoomRealtimeContext.Provider>
  );
}

export function useLiveRoomRealtime() {
  const context = useContext(LiveRoomRealtimeContext);
  if (!context) {
    throw new Error('useLiveRoomRealtime must be used inside LiveRoomRealtimeProvider');
  }
  return context;
}
