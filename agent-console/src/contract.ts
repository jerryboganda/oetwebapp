// Wire shapes of the sidecar HTTP API and event stream (CONTRACT.md §3–§4).
// Engine-facing shapes live in ./engines/types.ts and are re-used here.

import type { ApprovalDecision, EngineStatus, Engine, Mode } from './engines/types.js';

export type { ApprovalDecision, ConnectFlow, Engine, EngineAuth, EngineStatus, Mode, ModelInfo, RateLimit } from './engines/types.js';

/**
 * Pseudo-session that carries the global "system" approval queue (CONTRACT.md §6:
 * proxy requests with an unknown/absent session). A valid ULID so the API's id
 * validation accepts it; no session row ever has this id.
 */
export const SYSTEM_SESSION_ID = '00000000000000000000000000';

export const ULID_PATTERN = /^[0-9A-HJKMNP-TV-Z]{26}$/;

export const ENGINES: readonly Engine[] = ['claude', 'codex'];
export const MODES: readonly Mode[] = ['read_only', 'guarded', 'autopilot'];
export const APPROVAL_DECISIONS: readonly ApprovalDecision[] = ['approve', 'deny', 'approve_session'];

export type SessionStatus = 'idle' | 'running' | 'awaiting_approval' | 'interrupted' | 'error' | 'archived';
export const SESSION_STATUSES: readonly SessionStatus[] = ['idle', 'running', 'awaiting_approval', 'interrupted', 'error', 'archived'];
export type ResolvedBy = 'owner' | 'autopilot' | 'lease_expired' | 'kill' | 'timeout';
export type TurnStatus = 'ok' | 'interrupted' | 'error' | 'max_turns';

export interface GithubStatus {
  agentTokenSet: boolean;
  shipTokenSet: boolean;
  login?: string;
}

export interface ApprovalRequest {
  approvalId: string;
  nonce: string;
  toolCallId: string;
  summary: string;
  command?: string;
  cwd?: string;
  uid: number;
  target?: string;
  reasons: string[];
  tainted: boolean;
  expiresAt: string;
}

export interface ConsoleStatus {
  version: string;
  draining: boolean;
  killed: boolean;
  updatePending: boolean;
  activeTurns: number;
  maxConcurrentTurns: number;
  lease: { expiresAt: string | null };
  engines: { claude: EngineStatus; codex: EngineStatus };
  github: GithubStatus;
  /**
   * Additive to CONTRACT.md §3: pending proxy approvals with no session attribution
   * (the global "system" queue of §6). Resolve them through
   * POST /v1/sessions/{SYSTEM_SESSION_ID}/approvals/:approvalId and stream their events
   * from GET /v1/sessions/{SYSTEM_SESSION_ID}/events.
   */
  systemApprovals: ApprovalRequest[];
  systemSessionId: string;
}

export interface CreateSession {
  engine: Engine;
  model: string;
  effort?: string;
  mode: Mode;
  title?: string;
  initialMessage?: string;
}

export interface SessionUsage {
  inputTokens: number;
  outputTokens: number;
  costUsd?: number;
}

export interface SessionSummary {
  id: string;
  title: string;
  engine: Engine;
  model: string;
  effort?: string;
  mode: Mode;
  status: SessionStatus;
  branch: string;
  tainted: boolean;
  createdAt: string;
  updatedAt: string;
  lastSeq: number;
  usage: SessionUsage;
  /** Owner account id that created the session (absent for sessions created before it was recorded). */
  createdBy?: string;
  /** First 200 chars of the first user message, redacted (absent until one is sent). */
  firstMessage?: string;
}

export interface SessionDetail extends SessionSummary {
  pendingApprovals: ApprovalRequest[];
  pr?: { number: number; url: string; state: string };
  handoffFrom?: string;
}

export type DiffFileStatus = 'added' | 'modified' | 'deleted' | 'renamed' | 'untracked';

export interface SessionDiffFile {
  path: string;
  status: DiffFileStatus;
  additions: number;
  deletions: number;
}

export interface SessionDiff {
  baseRef: string;
  head: string;
  branch: string;
  files: SessionDiffFile[];
  patch: string;
  truncated: boolean;
}

export type ShipPhase =
  | 'queued'
  | 'scanning'
  | 'pushing'
  | 'pr_open'
  | 'visibility'
  | 'merging'
  | 'deploying'
  | 'health'
  | 'restoring_visibility'
  | 'done'
  | 'failed';

export interface ShipState {
  shipId: string;
  sessionId: string;
  phase: ShipPhase;
  prNumber?: number;
  prUrl?: string;
  mergeSha?: string;
  runUrl?: string;
  error?: string;
  startedAt: string;
  updatedAt: string;
}

export interface AgentEvent {
  seq: number;
  sessionId: string;
  turnId?: string;
  ts: string;
  type: string;
  data: Record<string, unknown>;
}
