/**
 * Owner Agent hub connection + resumable event stream.
 *
 * Builder adapted from lib/ai-assistant/signalr.ts with these differences:
 * - hub path `/v1/owner-agent/hub` (allow-listed in lib/backend-proxy.ts);
 * - long polling through the same-origin `/api/backend` proxy (it cannot
 *   upgrade WebSockets);
 * - no unlock header: the HttpOnly `oet_owner_unlock` cookie rides along on
 *   every same-origin negotiate / poll / send / close request
 *   (`withCredentials: true`), and the API re-checks it on every batch;
 * - one server-streaming method `Stream(sessionId, afterSeq)`; when the stream
 *   errors or the connection drops, it resubscribes with the last seq it saw so
 *   nothing is lost and nothing is replayed twice.
 * - hub errors are stable codes (HubException message). An unlock error after
 *   the owner has unlocked again (a different unlock than the one the
 *   connection started under) recycles the connection; any other terminal
 *   error (unlock gone, not the owner, invalid id) stops the stream instead of
 *   retrying forever.
 */

import type { HubConnection, HubConnectionState, ISubscription } from '@microsoft/signalr';
import { env } from '@/lib/env';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { OWNER_AGENT_HUB_PATH, type AgentEvent } from './types';

export type OwnerAgentConnectionState =
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnecting';

export function mapHubState(state: HubConnectionState | string): OwnerAgentConnectionState {
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

const RETRY_DELAYS_MS = [0, 1000, 2000, 5000, 10000, 15000, 30000];

export function getRetryDelay(previousRetryCount: number): number {
  if (previousRetryCount < 0) return RETRY_DELAYS_MS[0];
  if (previousRetryCount >= RETRY_DELAYS_MS.length) return 30_000;
  return RETRY_DELAYS_MS[previousRetryCount];
}

export function resolveOwnerAgentHubUrl(): string {
  if (typeof window !== 'undefined') {
    return `/api/backend${OWNER_AGENT_HUB_PATH}`;
  }
  const base = env.apiBaseUrl ?? 'http://127.0.0.1:5198';
  return `${base}${OWNER_AGENT_HUB_PATH}`;
}

export interface OwnerAgentConnectionOptions {
  onReconnecting?: (error?: Error) => void;
  onReconnected?: (connectionId?: string) => void;
  onClose?: (error?: Error) => void;
}

/**
 * Create (not start) a HubConnection to the owner-agent hub.
 */
export async function createOwnerAgentConnection(options: OwnerAgentConnectionOptions = {}): Promise<HubConnection> {
  const signalR = await import('@microsoft/signalr');
  const { HubConnectionBuilder, HttpTransportType, LogLevel } = signalR;
  const hubUrl = resolveOwnerAgentHubUrl();
  const transport = hubUrl.startsWith('/') ? HttpTransportType.LongPolling : undefined;

  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: async () => (await ensureFreshAccessToken().catch(() => null)) ?? '',
      // The unlock is the HttpOnly cookie; make sure every hub request carries it.
      withCredentials: true,
      ...(transport !== undefined ? { transport } : {}),
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds(retryContext) {
        return getRetryDelay(retryContext.previousRetryCount);
      },
    })
    .configureLogging(process.env.NODE_ENV === 'development' ? LogLevel.Information : LogLevel.Warning)
    .build();

  // Turns can be quiet for minutes (long tool calls, approvals waiting on the
  // owner); the sidecar sends a heartbeat every 15 s but keep the client
  // generous so a slow poll is never mistaken for a dead server.
  connection.serverTimeoutInMilliseconds = 120_000;
  connection.keepAliveIntervalInMilliseconds = 15_000;

  if (options.onReconnecting) connection.onreconnecting(options.onReconnecting);
  if (options.onReconnected) connection.onreconnected(options.onReconnected);
  if (options.onClose) connection.onclose(options.onClose);

  return connection;
}

// ─── Hub error classification ───────────────────────────────────────────────

/** Unlock failures (backend `OwnerAgentFailureCodes.UnlockCodes`). */
const UNLOCK_ERROR_CODES = [
  'owner_agent_unlock_required',
  'owner_agent_unlock_invalid',
  'owner_agent_unlock_expired',
  'owner_agent_unlock_revoked',
  'owner_agent_unlock_session_mismatch',
  'owner_agent_session_revoked',
];

/** Failures retrying cannot fix. */
const OTHER_TERMINAL_ERROR_CODES = ['owner_agent_not_owner', 'invalid_session_id', 'invalid_after_seq'];

function codePattern(codes: readonly string[]): RegExp {
  return new RegExp(`\\b(?:${codes.join('|')})\\b`);
}

const UNLOCK_ERROR = codePattern(UNLOCK_ERROR_CODES);
const TERMINAL_ERROR = codePattern([...UNLOCK_ERROR_CODES, ...OTHER_TERMINAL_ERROR_CODES]);

function errorText(error: unknown): string {
  if (error instanceof Error) return error.message;
  return typeof error === 'string' ? error : '';
}

/** The hub rejected the unlock (e.g. "… HubException: owner_agent_unlock_expired"). */
export function isUnlockStreamError(error: unknown): boolean {
  return UNLOCK_ERROR.test(errorText(error));
}

/** Errors that retrying with the same credentials cannot fix. */
export function isTerminalStreamError(error: unknown): boolean {
  return TERMINAL_ERROR.test(errorText(error));
}

// ─── Resumable stream ───────────────────────────────────────────────────────

export interface OwnerAgentEventStreamOptions {
  sessionId: string;
  /** Resume point: every persisted event with seq > afterSeq is replayed, then live. */
  afterSeq?: number;
  /**
   * Opaque identity of the current unlock (null while locked), e.g.
   * `getUnlockKey` from ./unlock-store. It changes when the owner unlocks
   * again. Never the unlock credential — that is an HttpOnly cookie.
   */
  getUnlockKey: () => string | null;
  onEvent: (event: AgentEvent) => void;
  onStateChange?: (state: OwnerAgentConnectionState) => void;
  onError?: (error: Error) => void;
  /**
   * The hub rejected the unlock this connection was started under, and the
   * owner has not unlocked again since. The stream has stopped; `unlockKey`
   * lets the caller clear exactly that unlock (a concurrent re-unlock must
   * survive).
   */
  onUnlockRejected?: (unlockKey: string | null) => void;
  /** Injection point for tests; defaults to createOwnerAgentConnection. */
  connectionFactory?: (options: OwnerAgentConnectionOptions) => Promise<HubConnection>;
  /** Injection point for tests; defaults to setTimeout. */
  schedule?: (callback: () => void, delayMs: number) => unknown;
  cancelSchedule?: (handle: unknown) => void;
}

export interface OwnerAgentEventStream {
  readonly sessionId: string;
  /** Highest seq delivered so far (the resume point after a reconnect). */
  lastSeq(): number;
  close(): Promise<void>;
}

function toError(value: unknown): Error {
  return value instanceof Error ? value : new Error(typeof value === 'string' ? value : 'Stream failed.');
}

/**
 * Open `Stream(sessionId, afterSeq)` and keep it alive until `close()`:
 * - stream error/complete while the connection is up → resubscribe (backoff);
 * - connection reconnecting → wait; reconnected → resubscribe from lastSeq;
 * - connection closed for good → start a new connection (backoff);
 * - unlock rejected but the owner has unlocked again since → new connection;
 * - any other terminal error → stop (no further retries until re-opened).
 */
export function openOwnerAgentEventStream(options: OwnerAgentEventStreamOptions): OwnerAgentEventStream {
  const factory = options.connectionFactory ?? createOwnerAgentConnection;
  const schedule = options.schedule ?? ((callback: () => void, delayMs: number) => setTimeout(callback, delayMs));
  const cancelSchedule = options.cancelSchedule ?? ((handle: unknown) => clearTimeout(handle as ReturnType<typeof setTimeout>));

  let closed = false;
  /** Set after a terminal hub error: no more (re)subscribes until close(). */
  let stopped = false;
  let lastSeq = Math.max(0, Math.trunc(options.afterSeq ?? 0));
  let connection: HubConnection | null = null;
  /** Unlock the current connection was started under (what the server saw at connect). */
  let connectionUnlockKey: string | null = null;
  let subscription: ISubscription<AgentEvent> | null = null;
  let retryHandle: unknown = null;
  let attempt = 0;

  const setState = (state: OwnerAgentConnectionState) => {
    if (!closed) options.onStateChange?.(state);
  };

  const clearRetry = () => {
    if (retryHandle !== null) {
      cancelSchedule(retryHandle);
      retryHandle = null;
    }
  };

  const disposeSubscription = () => {
    const current = subscription;
    subscription = null;
    if (current) {
      try {
        current.dispose();
      } catch {
        // Disposing a stream on a dead connection can throw; it is gone either way.
      }
    }
  };

  /** Drop the current connection (its late callbacks are ignored) without stopping the stream. */
  const discardConnection = () => {
    disposeSubscription();
    const current = connection;
    connection = null;
    if (current) void current.stop().catch(() => undefined);
  };

  /**
   * Returns true when the error ended the stream (terminal) or triggered a
   * connection recycle, so the caller must not schedule a normal retry.
   */
  const handleTerminal = (error: unknown): boolean => {
    if (!isTerminalStreamError(error)) return false;
    if (isUnlockStreamError(error)) {
      const current = options.getUnlockKey();
      if (current && current !== connectionUnlockKey) {
        // The server judged an older unlock; reconnect so it sees the current cookie.
        discardConnection();
        setState('disconnected');
        scheduleRecovery();
        return true;
      }
      options.onUnlockRejected?.(connectionUnlockKey);
    }
    stopped = true;
    clearRetry();
    setState('disconnected');
    discardConnection();
    return true;
  };

  const subscribe = () => {
    if (closed || stopped || !connection) return;
    clearRetry();
    disposeSubscription();
    const stream = connection.stream<AgentEvent>('Stream', options.sessionId, lastSeq);
    let self: ISubscription<AgentEvent> | null = null;
    self = stream.subscribe({
      next: (event) => {
        if (closed || stopped) return;
        attempt = 0;
        if (event && typeof event.seq === 'number' && Number.isFinite(event.seq)) {
          if (event.seq <= lastSeq) return; // already delivered before a resubscribe
          lastSeq = event.seq;
        }
        options.onEvent(event);
      },
      error: (err) => {
        if (subscription === self) subscription = null;
        if (closed || stopped) return;
        options.onError?.(toError(err));
        if (handleTerminal(err)) return;
        scheduleRecovery();
      },
      complete: () => {
        if (subscription === self) subscription = null;
        if (closed || stopped) return;
        scheduleRecovery();
      },
    });
    subscription = self;
  };

  const start = async () => {
    if (closed || stopped) return;
    try {
      if (!connection) {
        let created: HubConnection | null = null;
        const isCurrent = () => created !== null && connection === created;
        created = await factory({
          onReconnecting: () => {
            if (isCurrent()) setState('reconnecting');
          },
          onReconnected: () => {
            if (closed || stopped || !isCurrent()) return;
            // Automatic reconnect renegotiated with the unlock cookie held right now.
            connectionUnlockKey = options.getUnlockKey();
            setState('connected');
            attempt = 0;
            subscribe();
          },
          onClose: (error) => {
            if (!isCurrent()) return; // a discarded connection closing late
            subscription = null;
            if (closed || stopped) return;
            if (error) {
              options.onError?.(toError(error));
              if (handleTerminal(error)) return;
            }
            setState('disconnected');
            scheduleRecovery();
          },
        });
        if (closed || stopped) {
          await created.stop().catch(() => undefined);
          return;
        }
        connection = created;
      }
      const current = connection;
      if (mapHubState(current.state) === 'disconnected') {
        setState('connecting');
        connectionUnlockKey = options.getUnlockKey();
        await current.start();
      }
      if (closed || stopped) {
        await current.stop().catch(() => undefined);
        return;
      }
      if (connection !== current) return; // replaced while starting
      setState('connected');
      attempt = 0;
      subscribe();
    } catch (error) {
      if (closed || stopped) return;
      options.onError?.(toError(error));
      if (handleTerminal(error)) return;
      setState('disconnected');
      scheduleRecovery();
    }
  };

  function scheduleRecovery() {
    if (closed || stopped) return;
    clearRetry();
    const delay = Math.max(getRetryDelay(attempt), 500);
    attempt += 1;
    retryHandle = schedule(() => {
      retryHandle = null;
      if (closed || stopped) return;
      const state = connection ? mapHubState(connection.state) : 'disconnected';
      if (state === 'connected') {
        subscribe();
      } else if (state === 'disconnected') {
        void start();
      }
      // connecting/reconnecting: onReconnected (or start) will resubscribe.
    }, delay);
  }

  void start();

  return {
    sessionId: options.sessionId,
    lastSeq: () => lastSeq,
    close: async () => {
      if (closed) return;
      closed = true;
      clearRetry();
      disposeSubscription();
      const current = connection;
      connection = null;
      if (current) await current.stop().catch(() => undefined);
    },
  };
}
