import { randomBytes, timingSafeEqual } from 'node:crypto';
import { ulid } from 'ulid';
import type { ApprovalDecision, ApprovalRequest, Mode, ResolvedBy } from './contract.js';
import { SYSTEM_SESSION_ID } from './contract.js';
import { conflict, notFound } from './errors.js';
import type { Logger } from './log.js';

// Pending approval registry (control identity, memory only).
// Every card carries a single-use nonce that is emitted only on the
// API-facing event stream; a card resolves exactly once — by the owner, by
// Autopilot, by the kill switch, or by expiry (30 min) — and is then gone.

export type ApprovalSource = 'tool' | 'egress' | 'docker';

export interface ApprovalResolution {
  decision: ApprovalDecision;
  by: ResolvedBy;
  note?: string;
}

export interface OpenApprovalInput {
  sessionId: string;
  turnId?: string;
  toolCallId?: string;
  source: ApprovalSource;
  summary: string;
  command?: string;
  cwd?: string;
  uid: number;
  target?: string;
  reasons: string[];
  tainted: boolean;
  /** approve_session grant key. */
  grantKey?: string;
}

export interface OpenApproval {
  request: ApprovalRequest;
  result: Promise<ApprovalResolution>;
}

interface Pending {
  request: ApprovalRequest;
  sessionId: string;
  turnId?: string;
  source: ApprovalSource;
  grantKey?: string;
  timer: NodeJS.Timeout;
  settle: (resolution: ApprovalResolution) => void;
}

export interface ApprovalRegistryOptions {
  ttlMs: number;
  now?: () => number;
  /** Persists + fans out approval_request / approval_resolved events. */
  emit: (sessionId: string, type: 'approval_request' | 'approval_resolved', data: Record<string, unknown>, turnId?: string) => void;
  /** Index hooks (SQLite); optional so tests can run without a store. */
  onOpen?: (pending: { request: ApprovalRequest; sessionId: string; turnId?: string; source: ApprovalSource }) => void;
  onResolve?: (approvalId: string, resolution: ApprovalResolution) => void;
  /** Who an expiry is attributed to (lease_expired when the owner lease is lapsed). */
  expiryBy?: () => ResolvedBy;
  logger?: Logger;
}

const MAX_SUMMARY = 500;
const MAX_COMMAND = 16_384;

function clip(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max)}…` : text;
}

function sameNonce(expected: string, provided: string): boolean {
  const a = Buffer.from(expected, 'utf8');
  const b = Buffer.from(provided, 'utf8');
  if (a.length !== b.length) return false;
  return timingSafeEqual(a, b);
}

export class ApprovalRegistry {
  private readonly pending = new Map<string, Pending>();
  private readonly options: ApprovalRegistryOptions;
  private readonly now: () => number;

  constructor(options: ApprovalRegistryOptions) {
    this.options = options;
    this.now = options.now ?? Date.now;
  }

  open(input: OpenApprovalInput): OpenApproval {
    const approvalId = ulid();
    const request: ApprovalRequest = {
      approvalId,
      // Lower-case hex: can never match a redaction pattern (sk-…, gh?_…, JWT) and be
      // mangled on its way to the event stream, which would make the card unresolvable.
      nonce: randomBytes(24).toString('hex'),
      toolCallId: input.toolCallId ?? `${input.source}-${approvalId}`,
      summary: clip(input.summary, MAX_SUMMARY),
      uid: input.uid,
      reasons: input.reasons.slice(0, 20).map((r) => clip(r, 300)),
      tainted: input.tainted,
      expiresAt: new Date(this.now() + this.options.ttlMs).toISOString(),
    };
    if (input.command !== undefined) request.command = clip(input.command, MAX_COMMAND);
    if (input.cwd !== undefined) request.cwd = input.cwd;
    if (input.target !== undefined) request.target = input.target;

    let settle!: (resolution: ApprovalResolution) => void;
    const result = new Promise<ApprovalResolution>((resolve) => {
      settle = resolve;
    });
    const timer = setTimeout(() => {
      this.finish(approvalId, { decision: 'deny', by: this.options.expiryBy?.() ?? 'timeout' });
    }, this.options.ttlMs);
    timer.unref();

    const entry: Pending = { request, sessionId: input.sessionId, source: input.source, timer, settle };
    if (input.turnId !== undefined) entry.turnId = input.turnId;
    if (input.grantKey !== undefined) entry.grantKey = input.grantKey;
    this.pending.set(approvalId, entry);
    this.options.onOpen?.({ request, sessionId: input.sessionId, source: input.source, ...(input.turnId ? { turnId: input.turnId } : {}) });
    this.options.emit(input.sessionId, 'approval_request', { ...request }, input.turnId);
    return { request, result };
  }

  /**
   * Owner decision from the API. The nonce is compared in constant time and
   * burns with the card: a second use finds nothing (404).
   */
  resolveByOwner(sessionId: string, approvalId: string, decision: ApprovalDecision, nonce: string, note?: string): ApprovalResolution {
    const entry = this.pending.get(approvalId);
    if (!entry || entry.sessionId !== sessionId) {
      throw notFound('approval_not_found', 'No pending approval with this id (already resolved, expired or unknown).');
    }
    if (typeof nonce !== 'string' || !sameNonce(entry.request.nonce, nonce)) {
      // 409, not 403: the API reserves sidecar 401/403 for its own credentials being refused.
      throw conflict('approval_nonce_mismatch', 'Approval nonce does not match.');
    }
    if (Date.parse(entry.request.expiresAt) <= this.now()) {
      this.finish(approvalId, { decision: 'deny', by: 'timeout' });
      throw conflict('approval_expired', 'This approval has expired.');
    }
    const resolution: ApprovalResolution = { decision, by: 'owner' };
    if (note) resolution.note = clip(note, 1000);
    this.finish(approvalId, resolution);
    return resolution;
  }

  /** Non-owner resolution (autopilot / kill / lease / timeout). Returns false when already gone. */
  resolveInternal(approvalId: string, decision: ApprovalDecision, by: ResolvedBy): boolean {
    return this.finish(approvalId, { decision, by });
  }

  cancelSession(sessionId: string, by: ResolvedBy): number {
    let count = 0;
    for (const [id, entry] of [...this.pending]) {
      if (entry.sessionId === sessionId && this.finish(id, { decision: 'deny', by })) count += 1;
    }
    return count;
  }

  cancelTurn(turnId: string, by: ResolvedBy): number {
    let count = 0;
    for (const [id, entry] of [...this.pending]) {
      if (entry.turnId === turnId && this.finish(id, { decision: 'deny', by })) count += 1;
    }
    return count;
  }

  cancelAll(by: ResolvedBy): number {
    let count = 0;
    for (const id of [...this.pending.keys()]) if (this.finish(id, { decision: 'deny', by })) count += 1;
    return count;
  }

  listPending(sessionId: string): ApprovalRequest[] {
    return [...this.pending.values()].filter((p) => p.sessionId === sessionId).map((p) => ({ ...p.request }));
  }

  has(approvalId: string): boolean {
    return this.pending.has(approvalId);
  }

  get size(): number {
    return this.pending.size;
  }

  private finish(approvalId: string, resolution: ApprovalResolution): boolean {
    const entry = this.pending.get(approvalId);
    if (!entry) return false;
    this.pending.delete(approvalId);
    clearTimeout(entry.timer);
    try {
      this.options.onResolve?.(approvalId, resolution);
    } catch (error) {
      this.options.logger?.warn({ err: error, approvalId }, 'approval index update failed');
    }
    this.options.emit(
      entry.sessionId,
      'approval_resolved',
      { approvalId, decision: resolution.decision, by: resolution.by },
      entry.turnId,
    );
    entry.settle(resolution);
    return true;
  }
}

// ------------------------------------------------------------ proxy bridge

export interface ProxyApprovalBody {
  source: 'egress' | 'docker';
  sessionId: string | null;
  summary: string;
  target: string;
  reasons: string[];
  details?: Record<string, unknown>;
}

export interface ProxyApprovalResponse {
  decision: 'approve' | 'deny';
  scope: 'once' | 'session';
}

/** What the bridge needs to know about a session (implemented by SessionManager). */
export interface ProxySessionView {
  mode: Mode;
  tainted: boolean;
  hasGrant(key: string): boolean;
  addGrant(key: string): void;
  turnId?: string;
}

export interface ProxyBridgeDeps {
  approvals: ApprovalRegistry;
  lookup: (sessionId: string) => ProxySessionView | null;
  isKilled: () => boolean;
  waitMs: number;
  uid: number;
  /** Pre-snapshot for gated DB access in Autopilot (docker exec into oet-postgres). Returns ok. */
  snapshot?: (sessionId: string, approvalId: string, target: string) => Promise<boolean>;
  logger?: Logger;
}

export function validateProxyBody(body: unknown): ProxyApprovalBody {
  if (typeof body !== 'object' || body === null) throw new TypeError('body must be an object');
  const b = body as Record<string, unknown>;
  if (b.source !== 'egress' && b.source !== 'docker') throw new TypeError('source must be "egress" or "docker"');
  if (b.sessionId !== null && b.sessionId !== undefined && typeof b.sessionId !== 'string') throw new TypeError('sessionId must be a string or null');
  if (typeof b.summary !== 'string' || b.summary.length === 0) throw new TypeError('summary is required');
  if (typeof b.target !== 'string') throw new TypeError('target is required');
  if (!Array.isArray(b.reasons) || !b.reasons.every((r) => typeof r === 'string')) throw new TypeError('reasons must be a string array');
  const out: ProxyApprovalBody = {
    source: b.source,
    sessionId: typeof b.sessionId === 'string' && b.sessionId.length > 0 ? b.sessionId : null,
    summary: clip(b.summary, MAX_SUMMARY),
    target: clip(b.target, 500),
    reasons: (b.reasons as string[]).slice(0, 20),
  };
  if (typeof b.details === 'object' && b.details !== null && !Array.isArray(b.details)) {
    out.details = b.details as Record<string, unknown>;
  }
  return out;
}

export function proxyGrantKey(body: Pick<ProxyApprovalBody, 'source' | 'target'>): string {
  return `${body.source}:${body.target.toLowerCase()}`;
}

/**
 * CONTRACT.md §6 approval callback. Blocks until decided (≤ waitMs):
 *   read_only → deny; guarded → owner card (or an approve_session grant);
 *   autopilot → auto-approve unless tainted (then owner card);
 *   unknown/absent session → owner card on the system queue.
 */
export async function handleProxyApproval(
  body: ProxyApprovalBody,
  deps: ProxyBridgeDeps,
  signal?: AbortSignal,
): Promise<ProxyApprovalResponse> {
  const deny: ProxyApprovalResponse = { decision: 'deny', scope: 'once' };
  if (deps.isKilled()) return deny;

  const session = body.sessionId ? deps.lookup(body.sessionId) : null;
  const queueId = session && body.sessionId ? body.sessionId : SYSTEM_SESSION_ID;
  const key = proxyGrantKey(body);
  const reasons = [...body.reasons];
  if (body.sessionId && !session) reasons.push(`unknown session ${body.sessionId}`);

  if (session) {
    if (session.mode === 'read_only') return deny;
    if (!session.tainted && session.hasGrant(key)) return { decision: 'approve', scope: 'session' };
    if (session.mode === 'autopilot' && !session.tainted) {
      const card = deps.approvals.open({
        sessionId: queueId,
        ...(session.turnId ? { turnId: session.turnId } : {}),
        source: body.source,
        summary: body.summary,
        uid: deps.uid,
        target: body.target,
        reasons,
        tainted: false,
        grantKey: key,
      });
      if (deps.snapshot && body.source === 'docker' && /oet-postgres/.test(body.target)) {
        const ok = await deps.snapshot(queueId, card.request.approvalId, body.target).catch(() => false);
        if (!ok) {
          deps.approvals.resolveInternal(card.request.approvalId, 'deny', 'autopilot');
          return deny;
        }
      }
      deps.approvals.resolveInternal(card.request.approvalId, 'approve', 'autopilot');
      return { decision: 'approve', scope: 'once' };
    }
    if (session.tainted) reasons.push('session is tainted: untrusted content was read earlier');
  }

  const card = deps.approvals.open({
    sessionId: queueId,
    ...(session?.turnId ? { turnId: session.turnId } : {}),
    source: body.source,
    summary: body.summary,
    uid: deps.uid,
    target: body.target,
    reasons,
    tainted: session?.tainted ?? false,
    grantKey: key,
  });
  const approvalId = card.request.approvalId;
  const timer = setTimeout(() => deps.approvals.resolveInternal(approvalId, 'deny', 'timeout'), deps.waitMs);
  timer.unref();
  const onAbort = (): void => {
    deps.approvals.resolveInternal(approvalId, 'deny', 'timeout');
  };
  signal?.addEventListener('abort', onAbort, { once: true });
  try {
    const resolution = await card.result;
    if (resolution.decision === 'deny') return deny;
    if (resolution.decision === 'approve_session' && session && !session.tainted) {
      session.addGrant(key);
      return { decision: 'approve', scope: 'session' };
    }
    // System queue (no/unknown session): there is no session to hold a grant, and a
    // proxy-side cache keyed by an absent or unverified session id would widen the
    // approval to unrelated traffic, so it is always a one-off approval.
    return { decision: 'approve', scope: 'once' };
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onAbort);
  }
}
