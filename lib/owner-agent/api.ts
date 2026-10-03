/**
 * Owner Agent Console REST client — every `/v1/owner-agent/*` call (CONTRACT §5).
 *
 * - All traffic goes through the shared `apiClient` (bearer, x-csrf-token,
 *   device id, ApiError mapping stay consistent with the rest of the app) with
 *   `credentials: 'include'`, so the HttpOnly `oet_owner_unlock` cookie set by
 *   POST /unlock rides along through the same-origin `/api/backend` proxy.
 *   No unlock or step-up header is ever attached: the cookie is the only
 *   unlock credential, and there is no step-up any more.
 * - Retries are disabled (`maxRetries: 0`): unlock must never be replayed
 *   against the brute-force counters, and mutations such as "send message" or
 *   "approve" must never be double-submitted. Polling callers retry on their
 *   own schedule.
 * - The unlock lasts a fixed 60 minutes (no refresh). The unlock store
 *   (./unlock-store) keeps only `{ unlocked, expiresAt }` from /unlock and /me
 *   and flips back to the unlock screen at `expiresAt`.
 */

import { apiClient } from '@/lib/api';
import {
  clearUnlock,
  getUnlockGeneration,
  setUnlocked,
} from './unlock-store';
import {
  OWNER_AGENT_API_BASE,
  OWNER_AGENT_SESSIONS_MAX_LIMIT,
  OWNER_AGENT_SESSIONS_QUERY_MAX,
  SYSTEM_QUEUE_SESSION_ID,
  isEngine,
  isSessionStatus,
  type ApplyUpdateResult,
  type ApprovalDecisionBody,
  type ConnectFlow,
  type ConsoleStatus,
  type CreateSession,
  type Engine,
  type EngineAuth,
  type GithubStatus,
  type GithubTokensBody,
  type HandoffBody,
  type KillSwitchResult,
  type LeaseResponse,
  type ListSessionsParams,
  type OwnerAgentAuditEvent,
  type OwnerAgentAuditPage,
  type OwnerAgentMe,
  type SendMessageBody,
  type SessionDetail,
  type SessionDiff,
  type SessionPatch,
  type SessionSummary,
  type ShipBody,
  type ShipState,
  type UnlockBody,
  type UnlockResponse,
} from './types';

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH';

interface OwnerAgentRequestOptions {
  signal?: AbortSignal;
  timeoutMs?: number;
}

/**
 * Error codes the API uses when the unlock is missing, expired or
 * revoked (backend `OwnerAgentFailureCodes.UnlockCodes`, 403 `{ code, message }`
 * on REST and the HubException message on the hub). Any of them clears the
 * unlock state so the unlock screen shows.
 */
export const OWNER_AGENT_LOCK_ERROR_CODES: readonly string[] = [
  'owner_agent_unlock_required',
  'owner_agent_unlock_invalid',
  'owner_agent_unlock_expired',
  'owner_agent_unlock_revoked',
  'owner_agent_unlock_session_mismatch',
  'owner_agent_session_revoked',
];

const LOCK_CODE_IN_TEXT = new RegExp(`\\b(?:${OWNER_AGENT_LOCK_ERROR_CODES.join('|')})\\b`);

/**
 * True when free text (e.g. a SignalR stream error such as
 * "… HubException: owner_agent_unlock_expired") carries one of the lock codes.
 */
export function isOwnerAgentLockMessage(text: string | null | undefined): boolean {
  return typeof text === 'string' && LOCK_CODE_IN_TEXT.test(text);
}

export class OwnerAgentClientError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'OwnerAgentClientError';
  }
}

function errorCode(error: unknown): string | null {
  if (!error || typeof error !== 'object') return null;
  const code = (error as { code?: unknown }).code;
  return typeof code === 'string' ? code : null;
}

function errorStatus(error: unknown): number | null {
  if (!error || typeof error !== 'object') return null;
  const status = (error as { status?: unknown }).status;
  return typeof status === 'number' ? status : null;
}

export function isOwnerAgentLockError(error: unknown): boolean {
  const code = errorCode(error);
  return code !== null && OWNER_AGENT_LOCK_ERROR_CODES.includes(code);
}

/**
 * Sidecar 4xx bodies are passed through as `{ error: { code, message } }`,
 * which the shared client cannot read (it expects a flat `{ code, message }`)
 * and reports as "Request failed: <status>". Explain the statuses the sidecar
 * uses (CONTRACT §3) instead of showing that generic text.
 */
const SIDECAR_STATUS_MESSAGE: Record<number, string> = {
  400: 'The agent console rejected the request.',
  404: 'The agent console could not find that item.',
  409: 'The agent console is busy with this session (conflict). Try again when the current step finishes.',
  423: 'The agent console is stopped (kill switch) or draining for an update.',
  429: 'Too many turns are running. Wait for one to finish.',
};

/** Best human-readable message for an owner-agent failure. */
export function describeOwnerAgentError(error: unknown, fallback = 'Request failed.'): string {
  if (error && typeof error === 'object') {
    const status = errorStatus(error);
    const message = (error as { message?: unknown }).message;
    if (
      status !== null
      && SIDECAR_STATUS_MESSAGE[status]
      && errorCode(error) === 'unknown_error'
      && typeof message === 'string'
      && /^Request failed: \d+$/.test(message)
    ) {
      return SIDECAR_STATUS_MESSAGE[status];
    }
    const userMessage = (error as { userMessage?: unknown }).userMessage;
    if (typeof userMessage === 'string' && userMessage.trim()) return userMessage;
    if (typeof message === 'string' && message.trim()) return message;
  }
  return fallback;
}

// ─── Path validation ────────────────────────────────────────────────────────

const ULID_PATTERN = /^[0-9A-HJKMNP-TV-Z]{26}$/i;
const OPAQUE_ID_PATTERN = /^[A-Za-z0-9_-]{1,128}$/;

/** ULID check for session ids (the system-queue pseudo-session is a valid ULID too). */
export function isOwnerAgentSessionId(value: unknown): value is string {
  return typeof value === 'string' && ULID_PATTERN.test(value);
}

/** A real session id: a ULID that is not the system-queue pseudo-session. */
export function isOwnerAgentUserSessionId(value: unknown): value is string {
  return isOwnerAgentSessionId(value) && value !== SYSTEM_QUEUE_SESSION_ID;
}

function sessionSegment(sessionId: string, options: { allowSystem?: boolean } = {}): string {
  const valid = options.allowSystem ? isOwnerAgentSessionId(sessionId) : isOwnerAgentUserSessionId(sessionId);
  if (!valid) {
    throw new OwnerAgentClientError('Invalid session id.');
  }
  return encodeURIComponent(sessionId);
}

function opaqueSegment(value: string, label: string): string {
  if (typeof value !== 'string' || !OPAQUE_ID_PATTERN.test(value)) {
    throw new OwnerAgentClientError(`Invalid ${label}.`);
  }
  return encodeURIComponent(value);
}

function providerSegment(value: string | undefined): string {
  if (typeof value !== 'string' || value.length > 128 || !/^[A-Za-z0-9_.-]+$/.test(value)) {
    throw new OwnerAgentClientError('Invalid OpenCode provider.');
  }
  return value;
}

function engineSegment(engine: Engine): Engine {
  if (!isEngine(engine)) throw new OwnerAgentClientError('Invalid engine.');
  return engine;
}

// ─── Core request ───────────────────────────────────────────────────────────

async function ownerAgentRequest<T>(
  method: HttpMethod,
  path: string,
  body?: unknown,
  options: OwnerAgentRequestOptions = {},
): Promise<T> {
  // The unlock cookie is HttpOnly and travels on its own; nothing to attach.
  const init: RequestInit = { method, credentials: 'include' };
  if (options.signal) init.signal = options.signal;
  if (body !== undefined) init.body = JSON.stringify(body);

  const generation = getUnlockGeneration();
  try {
    return await apiClient.request<T>(`${OWNER_AGENT_API_BASE}${path}`, init, {
      maxRetries: 0,
      ...(options.timeoutMs ? { timeoutMs: options.timeoutMs } : {}),
    });
  } catch (error) {
    // Only drop the unlock this request ran under — a concurrent re-unlock must survive.
    if (isOwnerAgentLockError(error) && getUnlockGeneration() === generation) {
      clearUnlock('server_locked');
    }
    throw error;
  }
}

// ─── Unlock lifecycle ───────────────────────────────────────────────────────

/**
 * Password + TOTP → the API sets the HttpOnly unlock cookie (60 minutes). Only
 * the expiry from the body is kept; callers re-read /me afterwards.
 */
export async function unlock(body: UnlockBody): Promise<UnlockResponse> {
  const response = await ownerAgentRequest<UnlockResponse>('POST', '/unlock', {
    password: body.password,
    code: body.code,
  });
  if (response && typeof response.expiresAt === 'string') {
    setUnlocked({ expiresAt: response.expiresAt, absoluteExpiresAt: response.absoluteExpiresAt ?? null });
  }
  return response;
}

/**
 * "Lock now": POST /lock revokes the unlock server-side and clears the cookie.
 * Always sent (the cookie is invisible to JS, so local state cannot tell), and
 * the local state is cleared even if the call fails.
 */
export async function lockNow(): Promise<void> {
  try {
    await ownerAgentRequest<unknown>('POST', '/lock');
  } finally {
    clearUnlock('locked');
  }
}

// ─── Identity + status ──────────────────────────────────────────────────────

export function getMe(options: OwnerAgentRequestOptions = {}): Promise<OwnerAgentMe> {
  return ownerAgentRequest<OwnerAgentMe>('GET', '/me', undefined, options);
}

export function getStatus(options: OwnerAgentRequestOptions = {}): Promise<ConsoleStatus> {
  return ownerAgentRequest<ConsoleStatus>('GET', '/status', undefined, options);
}

/** Browser heartbeat; the server clamps the expiry to min(unlock, now + 3 min). */
export function postLease(expiresAt: string): Promise<LeaseResponse> {
  return ownerAgentRequest<LeaseResponse>('POST', '/lease', { expiresAt });
}

export function killSwitch(): Promise<KillSwitchResult> {
  return ownerAgentRequest<KillSwitchResult>('POST', '/kill-switch');
}

export function applyUpdate(): Promise<ApplyUpdateResult> {
  return ownerAgentRequest<ApplyUpdateResult>('POST', '/apply-update');
}

/** Undo the kill switch / a drain: the sidecar accepts new turns again. */
export function resumeConsole(): Promise<{ draining: boolean; activeTurns: number }> {
  return ownerAgentRequest<{ draining: boolean; activeTurns: number }>('POST', '/resume');
}

/**
 * GET /audit → `{ items, chainIntact }` (API `OwnerAgentAuditPage`). A bare
 * array is accepted too and treated as an unverified chain.
 */
export async function getAudit(take = 100): Promise<OwnerAgentAuditPage> {
  const safeTake = Math.min(Math.max(Math.trunc(take) || 100, 1), 500);
  const page = await ownerAgentRequest<OwnerAgentAuditPage | OwnerAgentAuditEvent[] | null>('GET', `/audit?take=${safeTake}`);
  if (Array.isArray(page)) return { items: page, chainIntact: false };
  if (page && typeof page === 'object' && Array.isArray(page.items)) {
    return { items: page.items, chainIntact: page.chainIntact === true };
  }
  return { items: [], chainIntact: false };
}

/** GET /owners → allow-listed admin accounts (`accountId` → `email`) for History "started by". */
export async function listOwners(): Promise<{ accountId: string; email: string }[]> {
  const page = await ownerAgentRequest<{ items?: { accountId?: unknown; email?: unknown }[] } | null>('GET', '/owners');
  const items = Array.isArray(page?.items) ? page.items : [];
  return items.flatMap((o) =>
    typeof o.accountId === 'string' && typeof o.email === 'string' ? [{ accountId: o.accountId, email: o.email }] : [],
  );
}

// ─── Engine sign-in ─────────────────────────────────────────────────────────

// Helpers below are `async` so validation failures surface as rejected promises.

export async function connectEngine(
  engine: Engine,
  options?: { providerId: string; methodIndex: number },
): Promise<ConnectFlow> {
  let body: { providerId: string; methodIndex: number } | undefined;
  if (engine === 'opencode') {
    if (!options || !Number.isInteger(options.methodIndex) || options.methodIndex < 0) {
      throw new OwnerAgentClientError('Select an OpenCode OAuth method.');
    }
    body = { providerId: providerSegment(options.providerId), methodIndex: options.methodIndex };
  }
  return ownerAgentRequest<ConnectFlow>('POST', `/auth/${engineSegment(engine)}/connect`, body);
}

export async function getConnectFlow(engine: Engine, flowId: string): Promise<ConnectFlow> {
  return ownerAgentRequest<ConnectFlow>(
    'GET',
    `/auth/${engineSegment(engine)}/flows/${opaqueSegment(flowId, 'flow id')}`,
  );
}

/** Claude paste-code flow only: hand the code shown by Anthropic back to the CLI. */
export async function submitConnectCode(engine: Engine, flowId: string, code: string): Promise<ConnectFlow> {
  opaqueSegment(flowId, 'flow id');
  return ownerAgentRequest<ConnectFlow>('POST', `/auth/${engineSegment(engine)}/code`, { flowId, code });
}

export async function cancelConnect(engine: Engine, flowId: string): Promise<ConnectFlow> {
  opaqueSegment(flowId, 'flow id');
  return ownerAgentRequest<ConnectFlow>('POST', `/auth/${engineSegment(engine)}/cancel`, { flowId });
}

export async function logoutEngine(engine: Engine): Promise<EngineAuth> {
  return ownerAgentRequest<EngineAuth>('POST', `/auth/${engineSegment(engine)}/logout`);
}

/** Write-only: tokens are never returned by the API. Blank fields are omitted. */
export async function putGithubTokens(body: GithubTokensBody): Promise<GithubStatus> {
  const payload: GithubTokensBody = {};
  if (body.agentToken && body.agentToken.trim()) payload.agentToken = body.agentToken.trim();
  if (body.shipToken && body.shipToken.trim()) payload.shipToken = body.shipToken.trim();
  if (!payload.agentToken && !payload.shipToken) {
    throw new OwnerAgentClientError('Enter at least one token.');
  }
  return ownerAgentRequest<GithubStatus>('PUT', '/github-tokens', payload);
}

// ─── Sessions ───────────────────────────────────────────────────────────────

/**
 * Build the GET /sessions query string. Invalid values are dropped rather than
 * sent (the API validates too): `q` is trimmed and capped at 100 chars, the
 * engine/status must be known values, `before` must parse as a date and
 * `limit` is clamped to 1..200. `includeArchived` is always explicit.
 */
export function buildSessionsQuery(params: ListSessionsParams = {}): string {
  const query = new URLSearchParams();
  const q = typeof params.q === 'string' ? params.q.trim().slice(0, OWNER_AGENT_SESSIONS_QUERY_MAX) : '';
  if (q) query.set('q', q);
  if (params.engine !== undefined && isEngine(params.engine)) query.set('engine', params.engine);
  if (params.status !== undefined && isSessionStatus(params.status)) query.set('status', params.status);
  query.set('includeArchived', params.includeArchived ? 'true' : 'false');
  if (typeof params.before === 'string' && params.before && Number.isFinite(Date.parse(params.before))) {
    query.set('before', params.before);
  }
  if (typeof params.limit === 'number' && Number.isFinite(params.limit)) {
    const limit = Math.min(Math.max(Math.trunc(params.limit), 1), OWNER_AGENT_SESSIONS_MAX_LIMIT);
    query.set('limit', String(limit));
  }
  return query.toString();
}

export async function listSessions(params: ListSessionsParams = {}): Promise<SessionSummary[]> {
  const rows = await ownerAgentRequest<SessionSummary[]>('GET', `/sessions?${buildSessionsQuery(params)}`);
  return Array.isArray(rows) ? rows : [];
}

export async function createSession(body: CreateSession): Promise<SessionDetail> {
  return ownerAgentRequest<SessionDetail>('POST', '/sessions', body);
}

export async function getSession(sessionId: string): Promise<SessionDetail> {
  return ownerAgentRequest<SessionDetail>('GET', `/sessions/${sessionSegment(sessionId)}`);
}

export async function patchSession(sessionId: string, patch: SessionPatch): Promise<SessionDetail> {
  return ownerAgentRequest<SessionDetail>('PATCH', `/sessions/${sessionSegment(sessionId)}`, patch);
}

export async function sendMessage(sessionId: string, body: SendMessageBody): Promise<{ turnId: string }> {
  const text = typeof body.text === 'string' ? body.text : '';
  if (!text.trim()) throw new OwnerAgentClientError('Message is empty.');
  const payload: SendMessageBody = { text };
  if (body.model) payload.model = body.model;
  if (body.effort) payload.effort = body.effort;
  return ownerAgentRequest<{ turnId: string }>('POST', `/sessions/${sessionSegment(sessionId)}/messages`, payload);
}

export async function interruptSession(sessionId: string): Promise<{ ok: true }> {
  return ownerAgentRequest<{ ok: true }>('POST', `/sessions/${sessionSegment(sessionId)}/interrupt`);
}

export async function handoffSession(sessionId: string, body: HandoffBody): Promise<SessionDetail> {
  const payload: HandoffBody = { engine: engineSegment(body.engine), model: body.model };
  if (body.effort) payload.effort = body.effort;
  return ownerAgentRequest<SessionDetail>('POST', `/sessions/${sessionSegment(sessionId)}/handoff`, payload);
}

export async function decideApproval(
  sessionId: string,
  approvalId: string,
  body: ApprovalDecisionBody,
): Promise<{ ok: true }> {
  const payload: ApprovalDecisionBody = { decision: body.decision, nonce: body.nonce };
  if (body.note && body.note.trim()) payload.note = body.note.trim().slice(0, 500);
  return ownerAgentRequest<{ ok: true }>(
    'POST',
    `/sessions/${sessionSegment(sessionId, { allowSystem: true })}/approvals/${opaqueSegment(approvalId, 'approval id')}`,
    payload,
  );
}

export async function getSessionDiff(sessionId: string): Promise<SessionDiff> {
  return ownerAgentRequest<SessionDiff>('GET', `/sessions/${sessionSegment(sessionId)}/diff`, undefined, {
    timeoutMs: 60_000,
  });
}

export async function shipSession(sessionId: string, body: ShipBody): Promise<ShipState> {
  const payload: ShipBody = {};
  if (body.prTitle && body.prTitle.trim()) payload.prTitle = body.prTitle.trim();
  if (body.prBody && body.prBody.trim()) payload.prBody = body.prBody;
  return ownerAgentRequest<ShipState>('POST', `/sessions/${sessionSegment(sessionId)}/ship`, payload);
}

export async function getShipState(sessionId: string): Promise<ShipState | null> {
  const state = await ownerAgentRequest<ShipState | null>('GET', `/sessions/${sessionSegment(sessionId)}/ship`);
  return state ?? null;
}
