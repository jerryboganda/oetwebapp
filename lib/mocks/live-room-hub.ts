'use client';

export interface MockLiveRoomSnapshot {
  bookingId: string;
  liveRoomState: string;
  status: string;
  transitionVersion: number;
  scheduledStartAt: string;
  timezoneIana: string;
}

export interface MockLiveRoomStateChanged extends MockLiveRoomSnapshot {
  fromState: string;
  reason?: string | null;
  occurredAt: string;
}

export interface MockLiveRoomSubscriptionHandlers {
  onSnapshot?: (snapshot: MockLiveRoomSnapshot) => void;
  onStateChanged?: (event: MockLiveRoomStateChanged) => void;
  onError?: (error: Error) => void;
}

export async function subscribeMockLiveRoomBooking(
  bookingId: string,
  handlers: MockLiveRoomSubscriptionHandlers,
): Promise<() => Promise<void>> {
  const [{ HubConnectionBuilder, LogLevel }, { ensureFreshAccessToken }] = await Promise.all([
    import('@microsoft/signalr'),
    import('@/lib/auth-client'),
  ]);

  const connection = new HubConnectionBuilder()
    .withUrl(`${process.env.NEXT_PUBLIC_API_BASE_URL || ''}/v1/mocks/live-room/hub`, {
      accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
    })
    .configureLogging(LogLevel.None)
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
    .build();

  connection.on('LiveRoomSnapshot', (snapshot: MockLiveRoomSnapshot) => {
    handlers.onSnapshot?.(snapshot);
  });
  connection.on('LiveRoomStateChanged', (event: MockLiveRoomStateChanged) => {
    handlers.onStateChanged?.(event);
  });
  connection.onreconnected(() => {
    void connection.invoke('JoinBooking', bookingId).catch((error: unknown) => {
      handlers.onError?.(error instanceof Error ? error : new Error('Failed to rejoin the live room.'));
    });
  });

  try {
    await connection.start();
    await connection.invoke('JoinBooking', bookingId);
  } catch (error) {
    await connection.stop().catch(() => undefined);
    throw error;
  }

  return async () => {
    try {
      await connection.invoke('LeaveBooking', bookingId);
    } catch {
      // The connection may already be closed during route transitions.
    }
    await connection.stop();
  };
}