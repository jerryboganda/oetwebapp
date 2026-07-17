/**
 * AI Assistant SignalR hub connection manager.
 * Connects to /v1/ai-assistant/hub with auto-reconnect and exponential backoff.
 */

import type { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { env } from '@/lib/env';

// ─── Connection State ───────────────────────────────────────────────────────

export type AssistantConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnecting';

export function mapHubState(state: HubConnectionState): AssistantConnectionState {
  switch (String(state)) {
    case 'Connected':
      return 'connected';
    case 'Connecting':
      return 'connecting';
    case 'Reconnecting':
      return 'reconnecting';
    case 'Disconnecting':
      return 'disconnecting';
    case 'Disconnected':
    default:
      return 'disconnected';
  }
}

// ─── Exponential Backoff Policy ─────────────────────────────────────────────

const RETRY_DELAYS_MS = [0, 1000, 2000, 5000, 10000, 15000, 30000];

function getRetryDelay(previousRetryCount: number): number | null {
  if (previousRetryCount >= RETRY_DELAYS_MS.length) {
    // After exhausting the table, cap at 30s
    return 30_000;
  }
  return RETRY_DELAYS_MS[previousRetryCount];
}

// ─── Hub URL Resolution ─────────────────────────────────────────────────────

function resolveHubUrl(): string {
  // In browser, use same-origin proxy path
  if (typeof window !== 'undefined') {
    return '/api/backend/v1/ai-assistant/hub';
  }
  // Server-side (shouldn't be needed for SignalR, but defensive)
  const base = env.apiBaseUrl ?? 'http://127.0.0.1:5198';
  return `${base}/v1/ai-assistant/hub`;
}

// ─── Connection Factory ─────────────────────────────────────────────────────

export interface AssistantConnectionOptions {
  onReconnecting?: (error?: Error) => void;
  onReconnected?: (connectionId?: string) => void;
  onClose?: (error?: Error) => void;
}

/**
 * Creates a SignalR HubConnection to the AI Assistant hub.
 * The connection is NOT started — call `.start()` after attaching event handlers.
 */
export async function createAssistantConnection(
  token: string,
  options?: AssistantConnectionOptions,
): Promise<HubConnection> {
  const {
    HubConnectionBuilder,
    HttpTransportType,
    LogLevel,
  } = await import('@microsoft/signalr');
  const hubUrl = resolveHubUrl();

  // `/api/backend` is a Next route handler that proxies HTTP but cannot perform a
  // SignalR WebSocket upgrade — pinning long polling here (mirroring the
  // notification hub) skips one guaranteed-to-fail WebSocket handshake attempt on
  // every connect and reconnect.
  const transport = hubUrl.startsWith('/') ? HttpTransportType.LongPolling : undefined;

  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => token,
      ...(transport !== undefined ? { transport } : {}),
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds(retryContext) {
        return getRetryDelay(retryContext.previousRetryCount);
      },
    })
    .configureLogging(
      process.env.NODE_ENV === 'development' ? LogLevel.Information : LogLevel.Warning,
    )
    .build();

  // Wire up lifecycle callbacks
  if (options?.onReconnecting) {
    connection.onreconnecting(options.onReconnecting);
  }
  if (options?.onReconnected) {
    connection.onreconnected(options.onReconnected);
  }
  if (options?.onClose) {
    connection.onclose(options.onClose);
  }

  return connection;
}

// ─── Hub Method Invokers ────────────────────────────────────────────────────

export async function invokeCreateThread(
  connection: HubConnection,
  role: string,
): Promise<string> {
  return connection.invoke<string>('CreateThread', role);
}

export async function invokeStartTurn(
  connection: HubConnection,
  threadId: string,
  message: string,
): Promise<void> {
  return connection.invoke('StartTurn', threadId, message);
}

export async function invokeCancelTurn(
  connection: HubConnection,
  threadId: string,
): Promise<void> {
  return connection.invoke('CancelTurn', threadId);
}

// ─── Event Registration Helpers ─────────────────────────────────────────────

export interface AssistantHubCallbacks {
  onTextDelta?: (text: string) => void;
  onToolCallStart?: (toolCallId: string, toolName: string, args: string) => void;
  onToolCallResult?: (toolCallId: string, result: string, isError: boolean) => void;
  onTurnComplete?: (messageId: string, fullText: string) => void;
  onTurnError?: (code: string, message: string) => void;
}

export function registerHubCallbacks(
  connection: HubConnection,
  callbacks: AssistantHubCallbacks,
): () => void {
  const handlers: Array<[string, (...args: unknown[]) => void]> = [];

  function on(method: string, handler: (...args: unknown[]) => void) {
    connection.on(method, handler);
    handlers.push([method, handler]);
  }

  if (callbacks.onTextDelta) {
    const cb = callbacks.onTextDelta;
    on('TextDelta', (text: unknown) => cb(text as string));
  }

  if (callbacks.onToolCallStart) {
    const cb = callbacks.onToolCallStart;
    on('ToolCallStart', (toolCallId: unknown, toolName: unknown, args: unknown) =>
      cb(toolCallId as string, toolName as string, args as string),
    );
  }

  if (callbacks.onToolCallResult) {
    const cb = callbacks.onToolCallResult;
    on('ToolCallResult', (toolCallId: unknown, result: unknown, isError: unknown) =>
      cb(toolCallId as string, result as string, isError as boolean),
    );
  }

  if (callbacks.onTurnComplete) {
    const cb = callbacks.onTurnComplete;
    on('TurnComplete', (messageId: unknown, fullText: unknown) =>
      cb(messageId as string, fullText as string),
    );
  }

  if (callbacks.onTurnError) {
    const cb = callbacks.onTurnError;
    on('TurnError', (code: unknown, message: unknown) =>
      cb(code as string, message as string),
    );
  }

  // Return unsubscribe function
  return () => {
    for (const [method, handler] of handlers) {
      connection.off(method, handler);
    }
  };
}
