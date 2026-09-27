'use client';

/**
 * React state for the Owner Agent Console (/admin/agent-console).
 *
 * `useOwnerAgent()` — console level, mounted once by the console shell:
 *   owner/unlock state (GET /me is the source of truth; the unlock itself is
 *   the HttpOnly `oet_owner_unlock` cookie, valid a fixed 60 minutes), status
 *   polling, sessions list, a 60 s lease heartbeat while mounted and unlocked
 *   (lapse ⇒ the sidecar drops Autopilot to Guarded and starts no new turns),
 *   and console-wide actions.
 *
 * `useOwnerAgentSession(sessionId)` — one session (or the "system" approval
 *   queue): detail, the resumable hub stream folded through the event reducer
 *   into a render model, and the per-session actions (send, interrupt, approve,
 *   patch, handoff, ship, diff).
 *
 * `useOwnerAgentHistory(filters)` — the searchable, paged session history
 *   (GET /sessions with q/engine/status/includeArchived/before/limit).
 *
 * Modeled on hooks/use-ai-assistant.ts; REST via lib/owner-agent/api.ts
 * (apiClient), streaming via lib/owner-agent/signalr.ts.
 */

import { useCallback, useEffect, useMemo, useReducer, useRef, useState, useSyncExternalStore } from 'react';
import * as ownerAgentApi from '@/lib/owner-agent/api';
import {
  createInitialRenderModel,
  sessionRenderReducer,
  type SessionRenderModel,
} from '@/lib/owner-agent/event-reducer';
import {
  openOwnerAgentEventStream,
  type OwnerAgentConnectionState,
} from '@/lib/owner-agent/signalr';
import {
  applyMeUnlock,
  clearUnlock,
  getServerUnlockSnapshot,
  getUnlockGeneration,
  getUnlockKey,
  getUnlockSnapshot,
  isSnapshotUnlocked,
  isUnlocked,
  subscribeUnlock,
  type UnlockSnapshot,
} from '@/lib/owner-agent/unlock-store';
import {
  SYSTEM_QUEUE_SESSION_ID,
  type AgentEvent,
  type ApplyUpdateResult,
  type ApprovalDecision,
  type ApprovalRequest,
  type ConsoleStatus,
  type CreateSession,
  type Engine,
  type KillSwitchResult,
  type ListSessionsParams,
  type OwnerAgentMe,
  type SessionDetail,
  type SessionDiff,
  type SessionPatch,
  type SessionStatus,
  type SessionSummary,
  type ShipState,
} from '@/lib/owner-agent/types';

export const OWNER_AGENT_LEASE_INTERVAL_MS = 60_000;
/** Requested lease length; the server clamps it to min(unlock expiry, now + 3 min). */
export const OWNER_AGENT_LEASE_LENGTH_MS = 3 * 60_000;
const ME_POLL_MS = 60_000;
const STATUS_POLL_MS = 15_000;
const SESSIONS_POLL_MS = 20_000;
const EVENT_FLUSH_MS = 16;

function describeOwnerAgentError(error: unknown, fallback: string): string {
  return ownerAgentApi.describeOwnerAgentError(error, fallback);
}

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

function isDocumentHidden(): boolean {
  return typeof document !== 'undefined' && document.visibilityState === 'hidden';
}

/** Read the in-memory unlock-state store from React (never a credential). */
export function useOwnerAgentUnlock(): UnlockSnapshot {
  return useSyncExternalStore(subscribeUnlock, getUnlockSnapshot, getServerUnlockSnapshot);
}

// ─── Console-level hook ─────────────────────────────────────────────────────

export interface UseOwnerAgentOptions {
  /** Disable all network activity (tests / non-owner shells). */
  enabled?: boolean;
  statusPollMs?: number;
  sessionsPollMs?: number;
  leaseIntervalMs?: number;
}

export interface UseOwnerAgentReturn {
  me: OwnerAgentMe | null;
  meState: LoadState;
  meError: string | null;
  isOwner: boolean;
  featureEnabled: boolean;
  /** True while /me (or the unlock response) says unlocked and the 60-minute expiry has not passed. */
  unlocked: boolean;
  /** Unlock state: `{ unlocked, expiresAt, clearedReason }` — never a credential. */
  unlock: UnlockSnapshot;

  status: ConsoleStatus | null;
  statusState: LoadState;
  statusError: string | null;

  sessions: SessionSummary[];
  sessionsState: LoadState;
  sessionsError: string | null;
  includeArchived: boolean;
  setIncludeArchived: (value: boolean) => void;

  leaseExpiresAt: string | null;
  leaseError: string | null;

  refreshMe: () => Promise<void>;
  refreshStatus: () => Promise<void>;
  refreshSessions: () => Promise<void>;
  /** Password + TOTP → unlock cookie (1 hour), then re-read /me. */
  unlockConsole: (password: string, code: string) => Promise<void>;
  /** "Lock now": POST /lock (clears the cookie server-side) and forget the state. */
  lockConsole: () => Promise<void>;
  createSession: (body: CreateSession) => Promise<SessionDetail>;
  killSwitch: () => Promise<KillSwitchResult>;
  applyUpdate: () => Promise<ApplyUpdateResult>;
  /** Undo the kill switch / a drain (API POST /resume). */
  resume: () => Promise<void>;
}

export function useOwnerAgent(options: UseOwnerAgentOptions = {}): UseOwnerAgentReturn {
  const enabled = options.enabled ?? true;
  const statusPollMs = options.statusPollMs ?? STATUS_POLL_MS;
  const sessionsPollMs = options.sessionsPollMs ?? SESSIONS_POLL_MS;
  const leaseIntervalMs = options.leaseIntervalMs ?? OWNER_AGENT_LEASE_INTERVAL_MS;

  const unlock = useOwnerAgentUnlock();
  const [me, setMe] = useState<OwnerAgentMe | null>(null);
  const [meState, setMeState] = useState<LoadState>('idle');
  const [meError, setMeError] = useState<string | null>(null);
  const [status, setStatus] = useState<ConsoleStatus | null>(null);
  const [statusState, setStatusState] = useState<LoadState>('idle');
  const [statusError, setStatusError] = useState<string | null>(null);
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [sessionsState, setSessionsState] = useState<LoadState>('idle');
  const [sessionsError, setSessionsError] = useState<string | null>(null);
  const [includeArchived, setIncludeArchived] = useState(false);
  const [leaseExpiresAt, setLeaseExpiresAt] = useState<string | null>(null);
  const [leaseError, setLeaseError] = useState<string | null>(null);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const isOwner = me?.isOwner === true;
  const featureEnabled = me?.featureEnabled !== false;
  const unlocked = isOwner && isSnapshotUnlocked(unlock);

  /**
   * GET /me is the source of truth for the unlock state: the HttpOnly cookie
   * survives reloads, new tabs and the console's CSP reload, so /me comes back
   * `unlocked: true` with its expiry until the 60 minutes are up.
   */
  const refreshMe = useCallback(async () => {
    if (!enabled) return;
    const generation = getUnlockGeneration();
    setMeState((prev) => (prev === 'ready' ? prev : 'loading'));
    try {
      const next = await ownerAgentApi.getMe();
      if (!mountedRef.current) return;
      // Skip a late answer that raced an unlock / lock done meanwhile.
      if (getUnlockGeneration() === generation) applyMeUnlock(next);
      setMe(next ?? null);
      setMeError(null);
      setMeState('ready');
    } catch (error) {
      if (!mountedRef.current) return;
      setMeError(describeOwnerAgentError(error, 'Could not reach the owner console API.'));
      setMeState('error');
    }
  }, [enabled]);

  const refreshStatus = useCallback(async () => {
    if (!enabled || !isUnlocked()) return;
    setStatusState((prev) => (prev === 'ready' ? prev : 'loading'));
    try {
      const next = await ownerAgentApi.getStatus();
      if (!mountedRef.current) return;
      setStatus(next);
      setStatusError(null);
      setStatusState('ready');
    } catch (error) {
      if (!mountedRef.current) return;
      setStatusError(describeOwnerAgentError(error, 'Status unavailable.'));
      setStatusState('error');
    }
  }, [enabled]);

  const refreshSessions = useCallback(async () => {
    if (!enabled || !isUnlocked()) return;
    setSessionsState((prev) => (prev === 'ready' ? prev : 'loading'));
    try {
      const rows = await ownerAgentApi.listSessions({ includeArchived });
      if (!mountedRef.current) return;
      // The system approval queue is a pseudo-session, never a row.
      setSessions(rows.filter((row) => row && row.id !== SYSTEM_QUEUE_SESSION_ID));
      setSessionsError(null);
      setSessionsState('ready');
    } catch (error) {
      if (!mountedRef.current) return;
      setSessionsError(describeOwnerAgentError(error, 'Sessions unavailable.'));
      setSessionsState('error');
    }
  }, [enabled, includeArchived]);

  // /me: on mount and every minute (detects server-side lock / revocation / expiry).
  useEffect(() => {
    if (!enabled) return;
    void refreshMe();
    const id = setInterval(() => {
      void refreshMe();
    }, ME_POLL_MS);
    return () => clearInterval(id);
  }, [enabled, refreshMe]);

  // Status polling while unlocked and visible.
  useEffect(() => {
    if (!enabled || !unlocked) return;
    void refreshStatus();
    const id = setInterval(() => {
      if (!isDocumentHidden()) void refreshStatus();
    }, statusPollMs);
    return () => clearInterval(id);
  }, [enabled, unlocked, refreshStatus, statusPollMs]);

  // Sessions polling while unlocked and visible; refetch on archived toggle.
  useEffect(() => {
    if (!enabled || !unlocked) return;
    void refreshSessions();
    const id = setInterval(() => {
      if (!isDocumentHidden()) void refreshSessions();
    }, sessionsPollMs);
    return () => clearInterval(id);
  }, [enabled, unlocked, refreshSessions, sessionsPollMs]);

  // Lease heartbeat: every 60 s while the console is mounted and unlocked.
  useEffect(() => {
    if (!enabled || !unlocked) {
      setLeaseExpiresAt(null);
      return;
    }
    let cancelled = false;
    const beat = async () => {
      try {
        const lease = await ownerAgentApi.postLease(new Date(Date.now() + OWNER_AGENT_LEASE_LENGTH_MS).toISOString());
        if (cancelled) return;
        setLeaseExpiresAt(lease?.expiresAt ?? null);
        setLeaseError(null);
      } catch (error) {
        if (cancelled) return;
        setLeaseError(describeOwnerAgentError(error, 'Lease heartbeat failed.'));
      }
    };
    void beat();
    const id = setInterval(() => {
      void beat();
    }, leaseIntervalMs);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [enabled, unlocked, leaseIntervalMs]);

  // The API sets the unlock cookie; /me is then re-read so the state (and its
  // expiry) comes from the source of truth, exactly as after a reload.
  const unlockConsole = useCallback(async (password: string, code: string) => {
    await ownerAgentApi.unlock({ password, code });
    await refreshMe();
  }, [refreshMe]);

  const lockConsole = useCallback(async () => {
    try {
      await ownerAgentApi.lockNow();
    } finally {
      if (mountedRef.current) {
        setStatus(null);
        setSessions([]);
        setStatusState('idle');
        setSessionsState('idle');
      }
    }
  }, []);

  const createSession = useCallback(async (body: CreateSession) => {
    const detail = await ownerAgentApi.createSession(body);
    void refreshSessions();
    return detail;
  }, [refreshSessions]);

  const killSwitch = useCallback(async () => {
    const result = await ownerAgentApi.killSwitch();
    void refreshStatus();
    void refreshSessions();
    return result;
  }, [refreshStatus, refreshSessions]);

  const applyUpdate = useCallback(async () => {
    const result = await ownerAgentApi.applyUpdate();
    void refreshStatus();
    return result;
  }, [refreshStatus]);

  const resume = useCallback(async () => {
    await ownerAgentApi.resumeConsole();
    await refreshStatus();
  }, [refreshStatus]);

  return {
    me,
    meState,
    meError,
    isOwner,
    featureEnabled,
    unlocked,
    unlock,
    status,
    statusState,
    statusError,
    sessions,
    sessionsState,
    sessionsError,
    includeArchived,
    setIncludeArchived,
    leaseExpiresAt,
    leaseError,
    refreshMe,
    refreshStatus,
    refreshSessions,
    unlockConsole,
    lockConsole,
    createSession,
    killSwitch,
    applyUpdate,
    resume,
  };
}

// ─── Session-level hook ─────────────────────────────────────────────────────

export interface UseOwnerAgentSessionOptions {
  /** Stream + load only while true (e.g. console unlocked). */
  enabled: boolean;
  /** Fetch GET /sessions/{id}; false for the system approval queue. */
  loadDetail?: boolean;
}

export interface UseOwnerAgentSessionReturn {
  model: SessionRenderModel;
  detail: SessionDetail | null;
  detailState: LoadState;
  detailError: string | null;
  connectionState: OwnerAgentConnectionState;
  streamError: string | null;
  /** Approvals still waiting on the owner: live stream first, detail as fallback. */
  pendingApprovals: ApprovalRequest[];
  tainted: boolean;
  running: boolean;
  refreshDetail: () => Promise<void>;
  send: (text: string, turn?: { model?: string; effort?: string }) => Promise<string | null>;
  interrupt: () => Promise<void>;
  decide: (approval: ApprovalRequest, decision: ApprovalDecision, note?: string) => Promise<void>;
  patch: (patch: SessionPatch) => Promise<SessionDetail>;
  handoff: (engine: Engine, model: string, effort?: string) => Promise<SessionDetail>;
  ship: (body: { prTitle?: string; prBody?: string }) => Promise<ShipState>;
  loadShip: () => Promise<ShipState | null>;
  loadDiff: () => Promise<SessionDiff>;
}

export function useOwnerAgentSession(
  sessionId: string | null,
  options: UseOwnerAgentSessionOptions,
): UseOwnerAgentSessionReturn {
  const { enabled } = options;
  const loadDetail = options.loadDetail ?? sessionId !== SYSTEM_QUEUE_SESSION_ID;

  const [model, dispatch] = useReducer(sessionRenderReducer, sessionId, createInitialRenderModel);
  const [detail, setDetail] = useState<SessionDetail | null>(null);
  const [detailState, setDetailState] = useState<LoadState>('idle');
  const [detailError, setDetailError] = useState<string | null>(null);
  const [connectionState, setConnectionState] = useState<OwnerAgentConnectionState>('disconnected');
  const [streamError, setStreamError] = useState<string | null>(null);

  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // Resume point per session, so a relock/unlock does not replay everything
  // (the reducer drops duplicates anyway). Declared before the stream effect so
  // it is current when that effect re-opens the stream.
  const resumeRef = useRef<{ sessionId: string | null; seq: number }>({ sessionId, seq: 0 });
  useEffect(() => {
    if (model.sessionId === sessionId) resumeRef.current = { sessionId, seq: model.lastSeq };
  }, [model.sessionId, model.lastSeq, sessionId]);

  // New session id → fresh model + detail.
  useEffect(() => {
    dispatch({ type: 'reset', sessionId });
    setDetail(null);
    setDetailState('idle');
    setDetailError(null);
    setStreamError(null);
  }, [sessionId]);

  const refreshDetail = useCallback(async () => {
    if (!enabled || !sessionId || !loadDetail) return;
    setDetailState((prev) => (prev === 'ready' ? prev : 'loading'));
    try {
      const next = await ownerAgentApi.getSession(sessionId);
      if (!mountedRef.current) return;
      setDetail(next);
      setDetailError(null);
      setDetailState('ready');
    } catch (error) {
      if (!mountedRef.current) return;
      setDetailError(describeOwnerAgentError(error, 'Session unavailable.'));
      setDetailState('error');
    }
  }, [enabled, sessionId, loadDetail]);

  useEffect(() => {
    void refreshDetail();
  }, [refreshDetail]);

  // Hub stream with batched dispatch (one reducer pass per animation frame-ish).
  useEffect(() => {
    if (!enabled || !sessionId) return;
    const buffer: AgentEvent[] = [];
    let flushTimer: ReturnType<typeof setTimeout> | null = null;
    const flush = () => {
      flushTimer = null;
      if (buffer.length === 0) return;
      dispatch({ type: 'events', events: buffer.splice(0, buffer.length) });
    };
    const afterSeq = resumeRef.current.sessionId === sessionId ? resumeRef.current.seq : 0;
    const stream = openOwnerAgentEventStream({
      sessionId,
      afterSeq,
      getUnlockKey,
      onEvent: (event) => {
        buffer.push(event);
        if (flushTimer === null) flushTimer = setTimeout(flush, EVENT_FLUSH_MS);
      },
      onStateChange: (state) => {
        setConnectionState(state);
        if (state === 'connected') setStreamError(null);
      },
      onError: (error) => setStreamError(error.message || 'Stream interrupted; reconnecting.'),
      // The hub refused the unlock and the owner has not unlocked again: show the unlock screen.
      onUnlockRejected: (rejected) => {
        if (rejected && getUnlockKey() === rejected) clearUnlock('server_locked');
      },
    });
    return () => {
      if (flushTimer !== null) clearTimeout(flushTimer);
      flush();
      void stream.close();
      setConnectionState('disconnected');
    };
  }, [enabled, sessionId]);

  // Keep the header (status, usage, PR, taint) fresh when a turn ends — including
  // turns that start and finish inside one replayed batch — or the mode flips.
  const { completedTurns, mode } = model;
  useEffect(() => {
    if (completedTurns > 0) void refreshDetail();
  }, [completedTurns, refreshDetail]);
  useEffect(() => {
    if (mode) void refreshDetail();
  }, [mode, refreshDetail]);

  const pendingApprovals = model.lastSeq > 0 || !detail
    ? model.pendingApprovals
    : detail.pendingApprovals.filter((a) => !model.resolvedApprovals[a.approvalId]);

  const requireId = useCallback((): string => {
    if (!sessionId) throw new Error('No session selected.');
    return sessionId;
  }, [sessionId]);

  const send = useCallback(async (text: string, turn?: { model?: string; effort?: string }) => {
    const res = await ownerAgentApi.sendMessage(requireId(), { text, model: turn?.model, effort: turn?.effort });
    return res?.turnId ?? null;
  }, [requireId]);

  const interrupt = useCallback(async () => {
    await ownerAgentApi.interruptSession(requireId());
  }, [requireId]);

  const decide = useCallback(async (approval: ApprovalRequest, decision: ApprovalDecision, note?: string) => {
    await ownerAgentApi.decideApproval(requireId(), approval.approvalId, { decision, nonce: approval.nonce, note });
  }, [requireId]);

  const patch = useCallback(async (body: SessionPatch) => {
    const next = await ownerAgentApi.patchSession(requireId(), body);
    if (mountedRef.current) {
      setDetail(next);
      setDetailState('ready');
    }
    return next;
  }, [requireId]);

  const handoff = useCallback(async (engine: Engine, targetModel: string, effort?: string) => {
    return ownerAgentApi.handoffSession(requireId(), { engine, model: targetModel, effort });
  }, [requireId]);

  const ship = useCallback(async (body: { prTitle?: string; prBody?: string }) => {
    return ownerAgentApi.shipSession(requireId(), body);
  }, [requireId]);

  const loadShip = useCallback(async () => ownerAgentApi.getShipState(requireId()), [requireId]);
  const loadDiff = useCallback(async () => ownerAgentApi.getSessionDiff(requireId()), [requireId]);

  return {
    model,
    detail,
    detailState,
    detailError,
    connectionState,
    streamError,
    pendingApprovals,
    tainted: model.tainted || detail?.tainted === true,
    // Once events have been replayed the stream is authoritative; before that, the detail.
    running: model.lastSeq > 0 ? model.running : detail?.status === 'running',
    refreshDetail,
    send,
    interrupt,
    decide,
    patch,
    handoff,
    ship,
    loadShip,
    loadDiff,
  };
}

// ─── History hook ───────────────────────────────────────────────────────────

export const OWNER_AGENT_HISTORY_PAGE_SIZE = 50;

export interface OwnerAgentHistoryFilters {
  /** Search text (title / first message); already debounced by the caller. */
  q: string;
  engine: Engine | '';
  status: SessionStatus | '';
  includeArchived: boolean;
}

export interface UseOwnerAgentHistoryOptions {
  /** Load only while true (e.g. console unlocked). */
  enabled: boolean;
  pageSize?: number;
}

export interface UseOwnerAgentHistoryReturn {
  rows: SessionSummary[];
  state: LoadState;
  error: string | null;
  /** The last page was full, so older sessions may exist. */
  hasMore: boolean;
  loadingMore: boolean;
  /** Next page: `before` = the last row's `updatedAt`. */
  loadMore: () => Promise<void>;
  /** Reload the first page with the current filters. */
  refresh: () => Promise<void>;
}

/** Build the GET /sessions params for a history filter set (empty filters are omitted). */
export function historyParams(filters: OwnerAgentHistoryFilters, pageSize: number = OWNER_AGENT_HISTORY_PAGE_SIZE): ListSessionsParams {
  const params: ListSessionsParams = { includeArchived: filters.includeArchived, limit: pageSize };
  const q = filters.q.trim();
  if (q) params.q = q;
  if (filters.engine) params.engine = filters.engine;
  if (filters.status) params.status = filters.status;
  return params;
}

function withoutSystemQueue(rows: readonly SessionSummary[]): SessionSummary[] {
  return rows.filter((row) => row && row.id !== SYSTEM_QUEUE_SESSION_ID);
}

/**
 * Searchable, paged session history. A filter change reloads the first page
 * (late answers for older filters are dropped); `loadMore()` appends the next
 * page using the last row's `updatedAt` as the `before` cursor.
 */
export function useOwnerAgentHistory(
  filters: OwnerAgentHistoryFilters,
  options: UseOwnerAgentHistoryOptions,
): UseOwnerAgentHistoryReturn {
  const { enabled } = options;
  const pageSize = options.pageSize ?? OWNER_AGENT_HISTORY_PAGE_SIZE;
  const { q, engine, status, includeArchived } = filters;
  const params = useMemo(
    () => historyParams({ q, engine, status, includeArchived }, pageSize),
    [q, engine, status, includeArchived, pageSize],
  );

  const [rows, setRows] = useState<SessionSummary[]>([]);
  const [state, setState] = useState<LoadState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);

  const rowsRef = useRef<SessionSummary[]>([]);
  const hasMoreRef = useRef(false);
  const loadingMoreRef = useRef(false);
  /** Bumped per first-page load; stale answers (older filters) are ignored. */
  const requestRef = useRef(0);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const refresh = useCallback(async () => {
    if (!enabled) return;
    const requestId = ++requestRef.current;
    loadingMoreRef.current = false;
    setLoadingMore(false);
    setState('loading');
    setError(null);
    try {
      const page = await ownerAgentApi.listSessions(params);
      if (!mountedRef.current || requestId !== requestRef.current) return;
      const next = withoutSystemQueue(Array.isArray(page) ? page : []);
      rowsRef.current = next;
      hasMoreRef.current = (page?.length ?? 0) >= pageSize;
      setRows(next);
      setHasMore(hasMoreRef.current);
      setState('ready');
    } catch (err) {
      if (!mountedRef.current || requestId !== requestRef.current) return;
      setError(describeOwnerAgentError(err, 'Session history unavailable.'));
      setState('error');
    }
  }, [enabled, params, pageSize]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const loadMore = useCallback(async () => {
    if (!enabled || loadingMoreRef.current || !hasMoreRef.current) return;
    const last = rowsRef.current[rowsRef.current.length - 1];
    if (!last || !last.updatedAt) return;
    const requestId = requestRef.current;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    try {
      const page = await ownerAgentApi.listSessions({ ...params, before: last.updatedAt });
      if (!mountedRef.current || requestId !== requestRef.current) return;
      const seen = new Set(rowsRef.current.map((row) => row.id));
      const merged = [
        ...rowsRef.current,
        ...withoutSystemQueue(Array.isArray(page) ? page : []).filter((row) => !seen.has(row.id)),
      ];
      rowsRef.current = merged;
      hasMoreRef.current = (page?.length ?? 0) >= pageSize;
      setRows(merged);
      setHasMore(hasMoreRef.current);
      setError(null);
    } catch (err) {
      if (!mountedRef.current || requestId !== requestRef.current) return;
      setError(describeOwnerAgentError(err, 'Older sessions could not be loaded.'));
    } finally {
      if (mountedRef.current && requestId === requestRef.current) {
        loadingMoreRef.current = false;
        setLoadingMore(false);
      }
    }
  }, [enabled, params, pageSize]);

  return { rows, state, error, hasMore, loadingMore, loadMore, refresh };
}
