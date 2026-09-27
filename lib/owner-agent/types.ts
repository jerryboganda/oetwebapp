/**
 * Owner Agent Console — wire types.
 *
 * Mirrors agent-console/CONTRACT.md §3 (sidecar shapes, passed through the .NET
 * API verbatim), §4 (AgentEvent envelope + event payloads) and §5 (public API
 * extras: /me, /unlock, /step-up, /audit). All JSON is camelCase; timestamps are
 * ISO-8601 UTC strings; ids are ULIDs unless stated otherwise.
 *
 * Model and effort ids are OPAQUE strings reported by the engines at runtime —
 * never enumerate them here or anywhere in the UI.
 */

// ─── §1 Vocabulary ──────────────────────────────────────────────────────────

export type Engine = 'claude' | 'codex';
export type Mode = 'read_only' | 'guarded' | 'autopilot';
export type SessionStatus =
  | 'idle'
  | 'running'
  | 'awaiting_approval'
  | 'interrupted'
  | 'error'
  | 'archived';
export type ApprovalDecision = 'approve' | 'deny' | 'approve_session';

export const OWNER_AGENT_ENGINES: readonly Engine[] = ['claude', 'codex'];
export const OWNER_AGENT_MODES: readonly Mode[] = ['read_only', 'guarded', 'autopilot'];

export function isEngine(value: unknown): value is Engine {
  return value === 'claude' || value === 'codex';
}

export function isMode(value: unknown): value is Mode {
  return value === 'read_only' || value === 'guarded' || value === 'autopilot';
}

// ─── §3 Shapes ──────────────────────────────────────────────────────────────

export interface ModelInfo {
  value: string;
  displayName: string;
  description?: string;
  supportsEffort: boolean;
  efforts: string[];
  defaultEffort?: string;
}

export interface RateLimit {
  label: string;
  status: 'ok' | 'warning' | 'limited' | 'unknown';
  usedPercent?: number;
  resetsAt?: string;
}

export interface EngineAuth {
  state: 'signed_out' | 'signing_in' | 'signed_in' | 'error';
  account?: { email?: string; plan?: string; workspace?: string };
  detail?: string;
}

export interface EngineStatus {
  engine: Engine;
  version: string | null;
  auth: EngineAuth;
  /** Empty until signed in. */
  models: ModelInfo[];
  /** null = unknown yet. */
  rateLimits: RateLimit[] | null;
}

export interface GithubStatus {
  agentTokenSet: boolean;
  shipTokenSet: boolean;
  login?: string;
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
   * Additive to CONTRACT §3 (sidecar `src/contract.ts`): proxy approvals with no
   * session attribution — the global "system" queue of CONTRACT §6.
   */
  systemApprovals?: ApprovalRequest[];
  /** Pseudo-session id carrying the system queue (see SYSTEM_QUEUE_SESSION_ID). */
  systemSessionId?: string;
}

export type ConnectFlowKind = 'paste_code' | 'device_code';
export type ConnectFlowState = 'pending' | 'awaiting_code' | 'completed' | 'failed' | 'cancelled' | 'expired';

export interface ConnectFlow {
  flowId: string;
  engine: Engine;
  kind: ConnectFlowKind;
  state: ConnectFlowState;
  verificationUrl?: string;
  userCode?: string;
  expiresAt?: string;
  detail?: string;
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

export interface SessionDetail extends SessionSummary {
  pendingApprovals: ApprovalRequest[];
  pr?: { number: number; url: string; state: string };
  handoffFrom?: string;
}

export interface SessionPatch {
  title?: string;
  mode?: Mode;
  model?: string;
  effort?: string;
  archived?: boolean;
}

export interface SendMessageBody {
  text: string;
  model?: string;
  effort?: string;
}

export interface HandoffBody {
  engine: Engine;
  model: string;
  effort?: string;
}

export interface ApprovalDecisionBody {
  decision: ApprovalDecision;
  nonce: string;
  note?: string;
}

export type SessionDiffFileStatus = 'added' | 'modified' | 'deleted' | 'renamed' | 'untracked';

export interface SessionDiffFile {
  path: string;
  status: SessionDiffFileStatus;
  additions: number;
  deletions: number;
}

export interface SessionDiff {
  baseRef: string;
  head: string;
  branch: string;
  files: SessionDiffFile[];
  /** Capped at 2 MB. */
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

export const SHIP_PHASES: readonly ShipPhase[] = [
  'queued',
  'scanning',
  'pushing',
  'pr_open',
  'visibility',
  'merging',
  'deploying',
  'health',
  'restoring_visibility',
  'done',
];

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

export interface ShipBody {
  prTitle?: string;
  prBody?: string;
}

export interface LeaseResponse {
  expiresAt: string;
}

export interface KillSwitchResult {
  stoppedTurns: number;
  killedProcesses: number;
}

export interface GithubTokensBody {
  agentToken?: string;
  shipToken?: string;
}

// ─── §4 Events ──────────────────────────────────────────────────────────────

export type TurnCompleteStatus = 'ok' | 'interrupted' | 'error' | 'max_turns';
export type ApprovalResolvedBy = 'owner' | 'autopilot' | 'lease_expired' | 'kill' | 'timeout';
export type FileChangeKind = 'add' | 'modify' | 'delete';

export interface ToolClassification {
  destructive: boolean;
  unparseable: boolean;
  reasons: string[];
}

export interface AgentEventDataMap {
  turn_started: { model: string; effort?: string; mode: Mode };
  user_message: { text: string };
  text_delta: { messageId: string; text: string };
  text: { messageId: string; text: string };
  thinking_delta: { text: string };
  tool_call: {
    toolCallId: string;
    name: string;
    input: unknown;
    command?: string;
    cwd?: string;
    classification?: ToolClassification;
  };
  tool_output_delta: { toolCallId: string; text: string };
  tool_result: { toolCallId: string; ok: boolean; output: string; exitCode?: number };
  file_change: { path: string; changeKind: FileChangeKind; diff?: string };
  approval_request: ApprovalRequest;
  approval_resolved: { approvalId: string; decision: ApprovalDecision; by: ApprovalResolvedBy };
  snapshot: { approvalId?: string; label: string; file?: string; ok: boolean; error?: string };
  taint: { reason: string; source: string };
  mode_changed: { mode: Mode; reason: string };
  usage: { model: string; inputTokens: number; outputTokens: number; cacheReadTokens?: number; costUsd?: number };
  rate_limit: { engine: Engine; limits: RateLimit[] };
  turn_complete: { status: TurnCompleteStatus; durationMs: number };
  error: { code: string; message: string };
  ship: { phase: string; message: string; level: 'info' | 'warn' | 'error' };
  heartbeat: Record<string, never>;
}

export type AgentEventType = keyof AgentEventDataMap;

/**
 * Envelope persisted as one JSON line per event. `seq` is monotonic per session
 * starting at 1; `heartbeat` is never persisted and arrives without a `seq`.
 */
export interface AgentEvent {
  seq: number;
  sessionId: string;
  turnId?: string;
  ts: string;
  type: string;
  data: object;
}

/** Discriminated view of an AgentEvent whose `type` is one the UI understands. */
export type KnownAgentEvent = {
  [K in AgentEventType]: Omit<AgentEvent, 'type' | 'data'> & { type: K; data: AgentEventDataMap[K] };
}[AgentEventType];

// ─── §5 Public API extras ───────────────────────────────────────────────────

/** GET /v1/owner-agent/me — non-owners get `{ isOwner: false }` only. */
export interface OwnerAgentMe {
  isOwner: boolean;
  unlocked?: boolean;
  unlockExpiresAt?: string | null;
  absoluteExpiresAt?: string | null;
  featureEnabled?: boolean;
  /**
   * Additive (API): set while console unlock is blocked, e.g. for 72 h after an
   * authenticator re-enrolment (plan Phase 3 owner-account hardening).
   */
  unlockBlockedUntil?: string | null;
}

export interface UnlockBody {
  password: string;
  code: string;
}

/** POST /unlock and POST /unlock/refresh. */
export interface UnlockResponse {
  ticket: string;
  expiresAt: string;
  absoluteExpiresAt: string;
}

/** POST /step-up — 5 minutes, single use. */
export interface StepUpResponse {
  stepUpToken: string;
  expiresAt: string;
}

/** POST /apply-update — drain + dispatch of agent-console.yml (shape owned by the API). */
export interface ApplyUpdateResult {
  draining?: boolean;
  activeTurns?: number | null;
  /** True when the sidecar dispatched agent-console.yml with apply=true (Ship PAT). */
  dispatched?: boolean;
  /** API-authored next step (dispatched, or how to finish by hand). */
  instructions?: string;
  [key: string]: unknown;
}

/**
 * One row of GET /audit — latest AuditEvent rows with ResourceType = "OwnerAgent"
 * (API `OwnerAgentAuditEntry`). `details` is the sanitized JSON object the API
 * stored (secrets redacted, strings capped at 200 chars).
 */
export interface OwnerAgentAuditEvent {
  id: string;
  occurredAt: string;
  actorId?: string | null;
  actorName?: string | null;
  action: string;
  resourceId?: string | null;
  details?: unknown;
  hash?: string | null;
  previousHash?: string | null;
  /** False when this row's hash does not verify against the chain. */
  hashValid?: boolean;
}

/** GET /audit response (API `OwnerAgentAuditPage`). */
export interface OwnerAgentAuditPage {
  items: OwnerAgentAuditEvent[];
  /** False when any row in the page breaks the hash chain. */
  chainIntact: boolean;
}

// ─── Constants ──────────────────────────────────────────────────────────────

export const OWNER_AGENT_API_BASE = '/v1/owner-agent';
export const OWNER_AGENT_HUB_PATH = '/v1/owner-agent/hub';
export const OWNER_AGENT_UNLOCK_HEADER = 'X-Owner-Agent-Unlock';
export const OWNER_AGENT_STEP_UP_HEADER = 'X-Owner-Agent-StepUp';

/**
 * Pseudo-session id carrying the global "system" approval queue (proxy
 * approvals whose session is unknown/absent, CONTRACT §6). It mirrors the
 * sidecar's `SYSTEM_SESSION_ID` (agent-console/src/contract.ts): an all-zero
 * ULID, so the API's ULID validation accepts it on the hub
 * (`Stream(SYSTEM_QUEUE_SESSION_ID, afterSeq)`) and on
 * `POST /sessions/{SYSTEM_QUEUE_SESSION_ID}/approvals/{approvalId}` without a
 * special route. No real session ever has this id.
 */
export const SYSTEM_QUEUE_SESSION_ID = '00000000000000000000000000';

/** Uid of the unprivileged engine user inside the sidecar (CONTRACT §2). */
export const AGENT_UID = 10002;
