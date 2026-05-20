/**
 * AI Assistant SignalR hub connection manager.
 * Connects to /hubs/ai-assistant with auto-reconnect and exponential backoff.
 */

import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import { env } from '@/lib/env';

// ─── Connection State ───────────────────────────────────────────────────────

export type AssistantConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnecting';

export function mapHubState(state: HubConnectionState): AssistantConnectionState {
  switch (state) {
    case HubConnectionState.Connected:
      return 'connected';
    case HubConnectionState.Connecting:
      return 'connecting';
    case HubConnectionState.Reconnecting:
      return 'reconnecting';
    case HubConnectionState.Disconnecting:
      return 'disconnecting';
    case HubConnectionState.Disconnected:
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
    return '/api/backend/hubs/ai-assistant';
  }
  // Server-side (shouldn't be needed for SignalR, but defensive)
  const base = env.apiBaseUrl ?? 'http://127.0.0.1:5198';
  return `${base}/hubs/ai-assistant`;
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
export function createAssistantConnection(
  token: string,
  options?: AssistantConnectionOptions,
): HubConnection {
  const hubUrl = resolveHubUrl();

  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => token,
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
