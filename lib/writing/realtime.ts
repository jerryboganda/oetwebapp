/**
 * Writing Module V2 — realtime channel helpers.
 *
 * We pick **WebSocket** (not SSE) for the coach channel per spec §11.6
 * (Haiku 4.5 hints fired every 30s, bidirectional updates so the client
 * can push current draft snapshots without re-establishing requests).
 *
 * The other two channels (submission grade-ready, today plan updates)
 * piggyback on the existing SignalR hubs (per spec §27 and Wave B WS6):
 *   /hubs/writing-submissions   — push `WritingGradeReady`
 *   /hubs/writing-today         — push daily plan updates
 *
 * For the coach channel we use a native WebSocket against a backend
 * route `/ws/writing/coach/{sessionId}` (WS6 will mount this; ASP.NET
 * Core's `MapWebSocket` or a hub-bridge approach is acceptable on the
 * server side). HTTP polling fallback at 30s is provided by
 * `coachPollingFallback()` for environments where WS is blocked.
 *
 * All helpers return a `Disposable { close() }` so callers can wire
 * them into `useEffect` cleanups deterministically.
 */

import { ensureFreshAccessToken } from '../auth-client';
import type { HubConnection } from '@microsoft/signalr';
import type { WritingCoachHintDto, WritingTodayPlanDto, WritingGradeDto } from './types';
import { requestWritingCoachHints, type WritingCoachHintRequestPayload } from './api';

// ─────────────────────────────────────────────────────────────────────────────
// Common types
// ─────────────────────────────────────────────────────────────────────────────

export interface Disposable {
  close(): void;
}

interface BackoffOptions {
  initialMs?: number;
  maxMs?: number;
  maxAttempts?: number;
}

const DEFAULT_BACKOFF: Required<BackoffOptions> = {
  initialMs: 1000,
  maxMs: 30_000,
  maxAttempts: 5,
};

function nextDelay(attempt: number, opts: Required<BackoffOptions>): number {
  return Math.min(opts.initialMs * 2 ** attempt, opts.maxMs);
}

// ─────────────────────────────────────────────────────────────────────────────
// URL resolvers
// ─────────────────────────────────────────────────────────────────────────────

function resolveWsBase(): string {
  return resolveRealtimeHttpBase().replace(/^http/, 'ws');
}

function resolveRealtimeHttpBase(): string {
  return (process.env.NEXT_PUBLIC_API_BASE_URL?.trim() || 'http://127.0.0.1:5198').replace(/\/$/, '');
}

function resolveCoachWsUrl(sessionId: string, token: string | null): string {
  const base = resolveWsBase();
  const tokenSegment = token ? `?access_token=${encodeURIComponent(token)}` : '';
  return `${base}/ws/writing/coach/${encodeURIComponent(sessionId)}${tokenSegment}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Coach channel — WebSocket primary, HTTP polling fallback.
// ─────────────────────────────────────────────────────────────────────────────

export interface CoachStreamHandlers {
  onHint: (hint: WritingCoachHintDto) => void;
  onOpen?: () => void;
  onError?: (error: Error) => void;
  onClose?: (intentional: boolean) => void;
  onStatusChange?: (status: 'connecting' | 'open' | 'reconnecting' | 'closed' | 'fallback') => void;
}

export function connectWritingCoachStream(
  sessionId: string,
  handlers: CoachStreamHandlers,
  backoff: BackoffOptions = {},
  buildPayload?: () => WritingCoachHintRequestPayload | null,
): Disposable {
  const opts = { ...DEFAULT_BACKOFF, ...backoff };
  let socket: WebSocket | null = null;
  let attempt = 0;
  let intentionallyClosed = false;
  let retryTimer: ReturnType<typeof setTimeout> | null = null;
  let sendTimer: ReturnType<typeof setTimeout> | null = null;

  const setStatus = (s: 'connecting' | 'open' | 'reconnecting' | 'closed' | 'fallback') => {
    if (handlers.onStatusChange) {
      try {
        handlers.onStatusChange(s);
      } catch {
        /* swallow */
      }
    }
  };

  const tryConnect = async () => {
    if (intentionallyClosed) return;
    setStatus(attempt === 0 ? 'connecting' : 'reconnecting');

    let token: string | null = null;
    try {
      token = await ensureFreshAccessToken();
    } catch {
      token = null;
    }

    let ws: WebSocket;
    try {
      ws = new WebSocket(resolveCoachWsUrl(sessionId, token));
    } catch (err) {
      handlers.onError?.(err as Error);
      scheduleRetry();
      return;
    }

    socket = ws;

    ws.addEventListener('open', () => {
      attempt = 0;
      setStatus('open');
      handlers.onOpen?.();
      sendSnapshot();
    });

    ws.addEventListener('message', (event) => {
      try {
        const data = JSON.parse(event.data) as
          | { type: 'hint'; hint?: WritingCoachHintDto; payload?: WritingCoachHintDto }
          | { type: 'ping' }
          | { type: 'error'; message: string };
        if (data.type === 'hint') {
          const hint = data.hint ?? data.payload;
          if (hint) handlers.onHint(hint);
        } else if (data.type === 'error') {
          handlers.onError?.(new Error(data.message));
        }
      } catch (err) {
        handlers.onError?.(err as Error);
      }
    });

    ws.addEventListener('error', () => {
      handlers.onError?.(new Error('Coach WebSocket error'));
    });

    ws.addEventListener('close', () => {
      if (intentionallyClosed) {
        setStatus('closed');
        handlers.onClose?.(true);
        return;
      }
      handlers.onClose?.(false);
      scheduleRetry();
    });
  };

  const sendSnapshot = () => {
    if (intentionallyClosed || !buildPayload || !socket || socket.readyState !== WebSocket.OPEN) return;
    const payload = buildPayload();
    if (payload) {
      try {
        socket.send(JSON.stringify({ ...payload, sessionId }));
      } catch (err) {
        handlers.onError?.(err as Error);
      }
    }
    sendTimer = setTimeout(sendSnapshot, 30_000);
  };

  const scheduleRetry = () => {
    if (intentionallyClosed) return;
    if (attempt >= opts.maxAttempts) {
      setStatus('fallback');
      handlers.onError?.(new Error('Coach WS exhausted; switch to HTTP polling fallback'));
      return;
    }
    const delay = nextDelay(attempt, opts);
    attempt += 1;
    retryTimer = setTimeout(() => {
      void tryConnect();
    }, delay);
  };

  void tryConnect();

  return {
    close() {
      intentionallyClosed = true;
      if (retryTimer) clearTimeout(retryTimer);
      if (sendTimer) clearTimeout(sendTimer);
      if (socket) {
        try {
          socket.close(1000, 'client-disposal');
        } catch {
          /* swallow */
        }
      }
    },
  };
}

/**
 * 30-second polling fallback for the coach channel when WebSocket is
 * unavailable. Returns a Disposable so callers can cancel cleanly.
 */
export function coachPollingFallback(
  buildPayload: () => WritingCoachHintRequestPayload | null,
  onHint: (hint: WritingCoachHintDto) => void,
  intervalMs = 30_000,
): Disposable {
  let cancelled = false;
  let timer: ReturnType<typeof setTimeout> | null = null;

  const tick = async () => {
    if (cancelled) return;
    const payload = buildPayload();
    if (payload) {
      try {
        const result = await requestWritingCoachHints(payload);
        if (!cancelled) {
          for (const hint of result.hints) onHint(hint);
        }
      } catch {
        // swallow polling errors; next tick may succeed
      }
    }
    if (!cancelled) {
      timer = setTimeout(tick, intervalMs);
    }
  };

  timer = setTimeout(tick, intervalMs);

  return {
    close() {
      cancelled = true;
      if (timer) clearTimeout(timer);
    },
  };
}

/**
 * Convenience overload — matches the slim signature shape from the WS4
 * spec (`connectWritingCoachStream(sessionId, onHint, onError)`).
 */
export function connectWritingCoachStreamSimple(
  sessionId: string,
  onHint: (hint: WritingCoachHintDto) => void,
  onError?: (error: Error) => void,
): Disposable {
  return connectWritingCoachStream(sessionId, { onHint, onError });
}

// ─────────────────────────────────────────────────────────────────────────────
// Submission grade-ready channel (SignalR — backend hub: writing-submissions)
// ─────────────────────────────────────────────────────────────────────────────

export interface SubmissionStreamHandlers {
  onGradeReady: (grade: WritingGradeDto) => void;
  onStatusChange?: (status: 'connecting' | 'connected' | 'reconnecting' | 'disconnected') => void;
  onError?: (error: Error) => void;
}

/**
 * Subscribes to grade-ready events for a single submission. Wraps the
 * existing SignalR @microsoft/signalr library; dynamic import to keep
 * the bundle slim on pages that don't use realtime.
 */
export function connectWritingSubmissionStream(
  submissionId: string,
  handlers: SubmissionStreamHandlers,
): Disposable {
  let connection: HubConnection | null = null;
  let disposed = false;

  const setStatus = (s: 'connecting' | 'connected' | 'reconnecting' | 'disconnected') => {
    handlers.onStatusChange?.(s);
  };

  const start = async () => {
    if (disposed) return;
    setStatus('connecting');
    try {
      const [{ HubConnectionBuilder, LogLevel }, token] = await Promise.all([
        import('@microsoft/signalr'),
        ensureFreshAccessToken(),
      ]);
      if (disposed) return;
      const hubUrl = `${resolveRealtimeHttpBase()}/hubs/writing-submissions`;
      connection = new HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: () => token ?? '',
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds(ctx) {
            if (ctx.previousRetryCount >= DEFAULT_BACKOFF.maxAttempts) return null;
            return nextDelay(ctx.previousRetryCount, DEFAULT_BACKOFF);
          },
        })
        .configureLogging(
          process.env.NODE_ENV === 'development' ? LogLevel.Information : LogLevel.Warning,
        )
        .build();

      connection.onreconnecting(() => setStatus('reconnecting'));
      connection.onreconnected(() => setStatus('connected'));
      connection.onclose(() => setStatus('disconnected'));
      connection.on('GradeReady', (grade: unknown) => {
        handlers.onGradeReady(grade as WritingGradeDto);
      });

      await connection.start();
      if (disposed) {
        await connection.stop();
        return;
      }
      await connection.invoke('SubscribeToSubmission', submissionId);
      setStatus('connected');
    } catch (err) {
      handlers.onError?.(err as Error);
      setStatus('disconnected');
    }
  };

  void start();

  return {
    close() {
      disposed = true;
      if (connection) {
        void connection
          .stop()
          .catch(() => {
            /* swallow */
          });
        connection = null;
      }
    },
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Today plan stream — pushes updated WritingTodayPlanDto after pathway recompute.
// ─────────────────────────────────────────────────────────────────────────────

export interface TodayStreamHandlers {
  onUpdate: (plan: WritingTodayPlanDto) => void;
  onError?: (error: Error) => void;
  onStatusChange?: (status: 'connecting' | 'connected' | 'reconnecting' | 'disconnected') => void;
}

/**
 * Convenience overload — matches the slim signature shape from the WS4
 * spec (`connectWritingSubmissionStream(submissionId, onEvent)`).
 */
export function connectWritingSubmissionStreamSimple(
  submissionId: string,
  onEvent: (grade: WritingGradeDto) => void,
): Disposable {
  return connectWritingSubmissionStream(submissionId, { onGradeReady: onEvent });
}

/**
 * Convenience overload — matches the slim signature shape from the WS4
 * spec (`connectWritingTodayStream(onUpdate)`). Wave C may use either
 * this or the full `TodayStreamHandlers` form above.
 */
export function connectWritingTodayStreamSimple(
  onUpdate: (plan: WritingTodayPlanDto) => void,
): Disposable {
  return connectWritingTodayStream({ onUpdate });
}

export function connectWritingTodayStream(handlers: TodayStreamHandlers): Disposable {
  let connection: HubConnection | null = null;
  let disposed = false;

  const setStatus = (s: 'connecting' | 'connected' | 'reconnecting' | 'disconnected') => {
    handlers.onStatusChange?.(s);
  };

  const start = async () => {
    if (disposed) return;
    setStatus('connecting');
    try {
      const [{ HubConnectionBuilder, LogLevel }, token] = await Promise.all([
        import('@microsoft/signalr'),
        ensureFreshAccessToken(),
      ]);
      if (disposed) return;
      const hubUrl = `${resolveRealtimeHttpBase()}/hubs/writing-today`;
      connection = new HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: () => token ?? '',
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds(ctx) {
            if (ctx.previousRetryCount >= DEFAULT_BACKOFF.maxAttempts) return null;
            return nextDelay(ctx.previousRetryCount, DEFAULT_BACKOFF);
          },
        })
        .configureLogging(
          process.env.NODE_ENV === 'development' ? LogLevel.Information : LogLevel.Warning,
        )
        .build();

      connection.onreconnecting(() => setStatus('reconnecting'));
      connection.onreconnected(() => setStatus('connected'));
      connection.onclose(() => setStatus('disconnected'));
      connection.on('TodayPlanUpdated', (plan: unknown) => {
        handlers.onUpdate(plan as WritingTodayPlanDto);
      });

      await connection.start();
      setStatus('connected');
    } catch (err) {
      handlers.onError?.(err as Error);
      setStatus('disconnected');
    }
  };

  void start();

  return {
    close() {
      disposed = true;
      if (connection) {
        void connection
          .stop()
          .catch(() => {
            /* swallow */
          });
        connection = null;
      }
    },
  };
}
