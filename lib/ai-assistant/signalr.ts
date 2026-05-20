// SignalR client wrapper for the AI Assistant streaming hub.
//
// Backend hub: /v1/ai-assistant/hub (mapped in Program.cs).
// Auth: bearer access token. The hub also accepts `?access_token=` on the
//       SignalR negotiate/polling requests (Program.cs JWT OnMessageReceived
//       whitelist), which is what `accessTokenFactory` produces. Same-origin
//       `/api/backend` proxy connections use long polling because the Next
//       route proxy is HTTP-only.

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import type { StreamFrame } from './types';

export interface AiAssistantConnection {
  start: () => Promise<void>;
  subscribeThread: (threadId: string) => Promise<void>;
  unsubscribeThread: (threadId: string) => Promise<void>;
  startTurn: (threadId: string, content: string) => Promise<string>;
  cancel: (messageId: string) => Promise<void>;
  onFrame: (handler: (frame: StreamFrame) => void) => () => void;
  onStateChange: (handler: (connected: boolean) => void) => () => void;
  stop: () => Promise<void>;
  readonly connection: HubConnection;
}

const HUB_PATH = `${env.apiBaseUrl}/v1/ai-assistant/hub`;
export const AI_ASSISTANT_SIGNALR_TRANSPORT = env.apiBaseUrl.startsWith('/')
  ? HttpTransportType.LongPolling
  : HttpTransportType.WebSockets;

/**
 * Build a SignalR connection to the AI Assistant hub.
 *
 * Frames pushed via "ReceiveFrame" arrive with backend property casing and
 * backend frame discriminators. We normalise top-level keys and frame type
 * values so the discriminated-union types in `./types.ts` line up.
 */
export function createAiAssistantConnection(hubUrl: string = HUB_PATH): AiAssistantConnection {
  const builder = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
      transport: AI_ASSISTANT_SIGNALR_TRANSPORT,
    })
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.Warning);
  const connection = builder.build();

  const frameHandlers = new Set<(frame: StreamFrame) => void>();
  const stateHandlers = new Set<(connected: boolean) => void>();

  connection.on('ReceiveFrame', (raw: unknown) => {
    if (raw == null || typeof raw !== 'object') return;
    const frame = normaliseStreamFrame(raw as Record<string, unknown>) as StreamFrame;
    for (const h of frameHandlers) {
      try { h(frame); } catch (err) { console.error('[ai-assistant] frame handler threw', err); }
    }
  });

  connection.on('KillSwitch', () => {
    for (const h of frameHandlers) {
      try {
        h({ type: 'Error', threadId: '', messageId: '00000000-0000-0000-0000-000000000000', message: 'AI Assistant disabled by administrator.', code: 'kill_switch' });
      } catch { /* swallow */ }
    }
  });

  const notifyState = (connected: boolean) => {
    for (const h of stateHandlers) {
      try { h(connected); } catch { /* swallow */ }
    }
  };

  connection.onreconnecting(() => notifyState(false));
  connection.onreconnected(() => notifyState(true));
  connection.onclose(() => notifyState(false));

  return {
    get connection() {
      return connection;
    },
    async start() {
      if (connection.state !== HubConnectionState.Disconnected) return;
      await connection.start();
      notifyState(true);
    },
    async subscribeThread(threadId: string) {
      await connection.invoke('Subscribe', threadId);
    },
    async unsubscribeThread(threadId: string) {
      await connection.invoke('Unsubscribe', threadId);
    },
    async startTurn(threadId: string, content: string) {
      const id = await connection.invoke<string>('StartTurn', threadId, content);
      return id;
    },
    async cancel(messageId: string) {
      await connection.invoke('Cancel', messageId);
    },
    onFrame(handler) {
      frameHandlers.add(handler);
      return () => { frameHandlers.delete(handler); };
    },
    onStateChange(handler) {
      stateHandlers.add(handler);
      return () => { stateHandlers.delete(handler); };
    },
    async stop() {
      try { await connection.stop(); } catch { /* swallow */ }
      notifyState(false);
    },
  };
}

const STREAM_FRAME_TYPE_ALIASES: Record<string, StreamFrame['type']> = {
  message_start: 'MessageStart',
  token_delta: 'TokenDelta',
  tool_call_start: 'ToolCallStart',
  tool_call_delta: 'ToolCallDelta',
  tool_call_result: 'ToolCallResult',
  approval_request: 'ApprovalRequest',
  message_end: 'MessageEnd',
  error: 'Error',
};

export function normaliseStreamFrame(raw: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(raw)) {
    const key = k.length > 0 ? k[0].toLowerCase() + k.slice(1) : k;
    out[key] = key === 'type' ? normaliseFrameType(v) : v;
  }
  return out;
}

function normaliseFrameType(value: unknown): unknown {
  if (typeof value !== 'string') return value;
  return STREAM_FRAME_TYPE_ALIASES[value] ?? value;
}
