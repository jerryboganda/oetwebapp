// Engine-neutral contract between the session layer (src/sessions.ts) and the
// engine adapters (src/engines/claude.ts, src/engines/codex.ts).
// Wire shapes mirror agent-console/CONTRACT.md §3–§4; keep them in sync.

export type Engine = 'claude' | 'codex';
export type Mode = 'read_only' | 'guarded' | 'autopilot';
export type ApprovalDecision = 'approve' | 'deny' | 'approve_session';

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
  models: ModelInfo[];
  rateLimits: RateLimit[] | null;
}

export interface ConnectFlow {
  flowId: string;
  engine: Engine;
  kind: 'paste_code' | 'device_code';
  state: 'pending' | 'awaiting_code' | 'completed' | 'failed' | 'cancelled' | 'expired';
  verificationUrl?: string;
  userCode?: string;
  expiresAt?: string;
  detail?: string;
}

/** Result of the Guard for one tool call (src/guard.ts). */
export interface GuardVerdict {
  destructive: boolean;
  unparseable: boolean;
  reasons: string[];
  /** Reads that make the turn tainted (learner content, logs, web, GH comments). */
  taintSource?: string;
}

/** What the session layer decides for a tool call the engine wants to run. */
export type ToolDecision =
  | { behavior: 'allow' }
  | { behavior: 'deny'; message: string };

/** A tool call surfaced by an engine, before it runs. */
export interface ToolCallRequest {
  toolCallId: string;
  name: string;
  input: Record<string, unknown>;
  /** Shell command line when the tool is a shell/exec tool. */
  command?: string;
  cwd?: string;
  /** Paths the call writes, when known (Edit/Write/apply_patch). */
  writePaths?: string[];
}

/**
 * Events an engine emits for one turn. The session layer assigns seq/ts/sessionId/turnId,
 * persists them and fans them out (CONTRACT.md §4). Engines never emit approval_* events:
 * approvals flow through EngineHooks.onToolCall.
 */
export type EngineEvent =
  | { type: 'text_delta'; data: { messageId: string; text: string } }
  | { type: 'text'; data: { messageId: string; text: string } }
  | { type: 'thinking_delta'; data: { text: string } }
  | { type: 'tool_call'; data: { toolCallId: string; name: string; input: unknown; command?: string; cwd?: string } }
  | { type: 'tool_output_delta'; data: { toolCallId: string; text: string } }
  | { type: 'tool_result'; data: { toolCallId: string; ok: boolean; output: string; exitCode?: number } }
  | { type: 'file_change'; data: { path: string; changeKind: 'add' | 'modify' | 'delete'; diff?: string } }
  | { type: 'usage'; data: { model: string; inputTokens: number; outputTokens: number; cacheReadTokens?: number; costUsd?: number } }
  | { type: 'rate_limit'; data: { engine: Engine; limits: RateLimit[] } }
  | { type: 'error'; data: { code: string; message: string } };

export interface EngineHooks {
  emit(event: EngineEvent): void;
  /**
   * Called for EVERY tool call before it executes (Claude: PreToolUse hook; Codex: approval
   * requests under approvalPolicy 'untrusted'). Resolves after Guard + (optional) owner
   * approval + (optional) pre-snapshot. May take minutes.
   */
  onToolCall(req: ToolCallRequest, signal: AbortSignal): Promise<ToolDecision>;
}

export interface SessionEngineOptions {
  sessionId: string;
  /** Worktree directory; must be identical on resume. */
  cwd: string;
  model: string;
  effort?: string;
  mode: Mode;
  /** Engine-native id persisted by the session layer (Claude session_id / Codex threadId). */
  resumeId?: string;
  /** Extra system instructions (etc/MANUAL.md + worktree AGENTS.md). */
  appendSystemPrompt: string;
  /** Allow-listed child env built by src/env.ts for this session. */
  env: Record<string, string>;
}

export interface TurnResult {
  status: 'ok' | 'interrupted' | 'error' | 'max_turns';
  /** Engine-native id to persist for resume. */
  resumeId?: string;
  error?: { code: string; message: string };
}

export interface EngineSession {
  readonly engine: Engine;
  /** Run one user turn to completion, emitting events through hooks. */
  runTurn(text: string, opts: { model: string; effort?: string; mode: Mode }, hooks: EngineHooks, signal: AbortSignal): Promise<TurnResult>;
  /** Best-effort interrupt of the in-flight turn. */
  interrupt(): Promise<void>;
  /** Release processes/queries (idle close); a later runTurn must transparently resume. */
  close(): Promise<void>;
}

export interface EngineAdapter {
  readonly engine: Engine;
  status(): Promise<EngineStatus>;
  openSession(opts: SessionEngineOptions): Promise<EngineSession>;
  connect(): Promise<ConnectFlow>;
  getFlow(flowId: string): ConnectFlow | undefined;
  submitCode(flowId: string, code: string): Promise<ConnectFlow>;
  cancel(flowId: string): Promise<ConnectFlow>;
  logout(): Promise<EngineAuth>;
  shutdown(): Promise<void>;
}
