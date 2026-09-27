import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { ApprovalRegistry, handleProxyApproval } from '../src/approvals.js';
import { loadConfig, type AppConfig } from '../src/config.js';
import type { GithubStatus, SessionDiff, ShipState } from '../src/contract.js';
import type {
  ConnectFlow,
  Engine,
  EngineAdapter,
  EngineAuth,
  EngineHooks,
  EngineSession,
  EngineStatus,
  Mode,
  SessionEngineOptions,
  TurnResult,
} from '../src/engines/types.js';
import { ControlState, LeaseManager, stopAll } from '../src/lease.js';
import { silentLogger } from '../src/log.js';
import { Redactor } from '../src/redact.js';
import type { ServerContext } from '../src/server.js';
import { SessionManager } from '../src/sessions.js';
import type { SnapshotResult } from '../src/snapshot.js';
import { Store } from '../src/store.js';
import type { WorkspaceApi } from '../src/workspace.js';

// Test doubles: no network, no real engines, no credentials. Token values are
// generated at runtime so no secret-looking literal lives in the repository.

export const TEST_TOKEN = `test-internal-${'t'.repeat(40)}`;
export const TEST_PROXY_TOKEN = `test-proxy-${'p'.repeat(40)}`;
export const OWNER_ID = '6f1c2d3e-0000-4000-8000-00000000abcd';

export function tempDir(prefix = 'oet-agent-console-'): string {
  return mkdtempSync(path.join(os.tmpdir(), prefix));
}

export function testConfig(root: string, env: Record<string, string> = {}): AppConfig {
  return loadConfig({
    env: {
      OWNER_AGENT_OWNER_ACCOUNT_IDS: OWNER_ID.toUpperCase(),
      AGENT_CONSOLE_DATA_DIR: path.join(root, 'data'),
      AGENT_CONSOLE_WORKSPACE_ROOT: path.join(root, 'workspace'),
      AGENT_CONSOLE_DOCKER_CONFIG_ROOT: path.join(root, 'docker'),
      AGENT_CONSOLE_VERSION: 'test',
      ...env,
    },
    readFile: (file) => {
      if (file.endsWith('owner_agent_internal_token')) return TEST_TOKEN;
      if (file.endsWith('owner_agent_proxy_token')) return TEST_PROXY_TOKEN;
      return null;
    },
  });
}

export interface FakeTurn {
  text: string;
  opts: { model: string; effort?: string; mode: Mode };
  hooks: EngineHooks;
  signal: AbortSignal;
  release: (result?: TurnResult) => void;
}

export type TurnScript = (turn: FakeTurn) => Promise<TurnResult | void>;

export class FakeEngineSession implements EngineSession {
  readonly turns: FakeTurn[] = [];
  closed = false;
  interrupts = 0;
  script: TurnScript | null = null;

  constructor(readonly engine: Engine, readonly options: SessionEngineOptions) {}

  runTurn(text: string, opts: { model: string; effort?: string; mode: Mode }, hooks: EngineHooks, signal: AbortSignal): Promise<TurnResult> {
    return new Promise<TurnResult>((resolve) => {
      let settled = false;
      const release = (result: TurnResult = { status: 'ok', resumeId: `resume-${this.options.sessionId}` }): void => {
        if (settled) return;
        settled = true;
        resolve(result);
      };
      const turn: FakeTurn = { text, opts, hooks, signal, release };
      this.turns.push(turn);
      signal.addEventListener('abort', () => release({ status: 'interrupted' }), { once: true });
      if (this.script) {
        this.script(turn).then(
          (result) => release(result ?? { status: 'ok' }),
          (error: unknown) => release({ status: 'error', error: { code: 'script', message: String(error) } }),
        );
      }
    });
  }

  async interrupt(): Promise<void> {
    this.interrupts += 1;
  }

  async close(): Promise<void> {
    this.closed = true;
  }
}

export class FakeAdapter implements EngineAdapter {
  readonly sessions: FakeEngineSession[] = [];
  script: TurnScript | null = null;
  authState: EngineAuth['state'] = 'signed_in';

  constructor(readonly engine: Engine) {}

  async status(): Promise<EngineStatus> {
    return {
      engine: this.engine,
      version: '0.0.0-test',
      auth: { state: this.authState },
      models: [
        { value: 'model-a', displayName: 'Model A', supportsEffort: true, efforts: ['low', 'high'], defaultEffort: 'low' },
        { value: 'model-b', displayName: 'Model B', supportsEffort: false, efforts: [] },
      ],
      rateLimits: null,
    };
  }

  async openSession(options: SessionEngineOptions): Promise<EngineSession> {
    const session = new FakeEngineSession(this.engine, options);
    session.script = this.script;
    this.sessions.push(session);
    return session;
  }

  async connect(): Promise<ConnectFlow> {
    return { flowId: 'flow-1', engine: this.engine, kind: this.engine === 'claude' ? 'paste_code' : 'device_code', state: 'pending' };
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return flowId === 'flow-1' ? { flowId, engine: this.engine, kind: 'paste_code', state: 'awaiting_code' } : undefined;
  }

  async submitCode(flowId: string): Promise<ConnectFlow> {
    return { flowId, engine: this.engine, kind: 'paste_code', state: 'completed' };
  }

  async cancel(flowId: string): Promise<ConnectFlow> {
    return { flowId, engine: this.engine, kind: 'paste_code', state: 'cancelled' };
  }

  async logout(): Promise<EngineAuth> {
    return { state: 'signed_out' };
  }

  async shutdown(): Promise<void> {}

  /** All turns across sessions, oldest first. */
  get turns(): FakeTurn[] {
    return this.sessions.flatMap((s) => s.turns);
  }
}

export class FakeWorkspace implements WorkspaceApi {
  removed: string[] = [];
  constructor(private readonly root: string) {}
  async ensureRepo(): Promise<void> {}
  async createWorktree(sessionId: string): Promise<{ path: string; branch: string }> {
    return { path: path.posix.join('/workspace/sessions', sessionId), branch: `agent/20260927-test-${sessionId.slice(-6).toLowerCase()}` };
  }
  async restoreWorktree(): Promise<void> {}
  async removeWorktree(worktreePath: string): Promise<void> {
    this.removed.push(worktreePath);
  }
  async diff(worktree: { path: string; branch: string }): Promise<SessionDiff> {
    return { baseRef: 'origin/main', head: 'abc123', branch: worktree.branch, files: [], patch: '', truncated: false };
  }
  async checkDisk(): Promise<{ freeBytes: number; ok: boolean }> {
    return { freeBytes: 10 * 1024 ** 3, ok: true };
  }
  async readAgentsMd(): Promise<string> {
    return `# AGENTS (test root ${path.basename(this.root)})`;
  }
}

export class FakeSnapshots {
  readonly requests: { label: string; tables?: string[] }[] = [];
  result: Partial<SnapshotResult> = { ok: true, file: '/backups/agent-snap-test.dump' };
  async take(request: { label: string; tables?: string[] }): Promise<SnapshotResult> {
    this.requests.push(request);
    return { ok: this.result.ok ?? true, label: request.label, ...(this.result.file ? { file: this.result.file } : {}), ...(this.result.error ? { error: this.result.error } : {}) };
  }
}

export interface TestHarness {
  root: string;
  config: AppConfig;
  store: Store;
  redactor: Redactor;
  lease: LeaseManager;
  control: ControlState;
  approvals: ApprovalRegistry;
  sessions: SessionManager;
  adapters: Record<Engine, FakeAdapter>;
  workspace: FakeWorkspace;
  snapshots: FakeSnapshots;
  context: ServerContext;
  killed: { processes: number; containers: number };
  cleanup(): void;
}

export function createHarness(options: { maxConcurrentTurns?: number } = {}): TestHarness {
  const root = tempDir();
  const config = testConfig(root, options.maxConcurrentTurns ? { AGENT_CONSOLE_MAX_CONCURRENT_TURNS: String(options.maxConcurrentTurns) } : {});
  const logger = silentLogger();
  const redactor = new Redactor([config.internalToken, config.proxyToken]);
  const store = new Store({ dbPath: path.join(root, 'index.sqlite'), sessionsDir: path.join(root, 'sessions'), redactor });
  const lease = new LeaseManager({ maxMs: config.leaseMaxMs });
  const control = new ControlState();
  const approvals = new ApprovalRegistry({
    ttlMs: config.approvalTtlMs,
    emit: (sessionId, type, data, turnId) => {
      store.appendEvent(sessionId, type, data, turnId);
    },
    logger,
  });
  const adapters: Record<Engine, FakeAdapter> = { claude: new FakeAdapter('claude'), codex: new FakeAdapter('codex') };
  const engines = {
    get: async (engine: Engine) => adapters[engine],
    status: async (engine: Engine) => adapters[engine].status(),
    invalidate: () => undefined,
  };
  const workspace = new FakeWorkspace(root);
  const snapshots = new FakeSnapshots();
  const sessions = new SessionManager({
    config,
    store,
    approvals,
    engines,
    workspace,
    snapshots,
    lease,
    control,
    logger,
    readManual: async () => '# Operating manual (test)',
    prepareDockerConfig: async () => undefined,
  });
  lease.onExpired(() => sessions.onLeaseExpired());
  const killed = { processes: 0, containers: 0 };
  const githubStatus: GithubStatus = { agentTokenSet: false, shipTokenSet: false };
  const ships = new Map<string, ShipState>();

  const context: ServerContext = {
    config,
    logger,
    store,
    sessions,
    approvals,
    engines,
    lease,
    control,
    github: {
      setTokens: async (input) => {
        if (input.agentToken !== undefined) githubStatus.agentTokenSet = input.agentToken !== '';
        if (input.shipToken !== undefined) githubStatus.shipTokenSet = input.shipToken !== '';
        return { ...githubStatus };
      },
      status: async () => ({ ...githubStatus }),
    },
    ship: {
      start: async (sessionId) => {
        const now = new Date().toISOString();
        const state: ShipState = { shipId: 'ship-test', sessionId, phase: 'queued', startedAt: now, updatedAt: now };
        ships.set(sessionId, state);
        return state;
      },
      get: (sessionId) => ships.get(sessionId) ?? null,
      dispatchConsoleUpdate: async () => ({ dispatched: true }),
    },
    stopAll: () =>
      stopAll(control, {
        abortAllTurns: () => sessions.abortAllTurns(),
        cancelApprovals: () => approvals.cancelAll('kill'),
        killAgentProcesses: async () => {
          killed.processes += 1;
          return 3;
        },
        stopSessionContainers: async () => {
          killed.containers += 1;
          return 0;
        },
      }),
    proxyApproval: (body, signal) =>
      handleProxyApproval(
        body,
        {
          approvals,
          lookup: (sessionId) => sessions.proxyView(sessionId),
          isKilled: () => control.killed,
          waitMs: config.proxyApprovalWaitMs,
          uid: config.agentUid,
          snapshot: (sessionId, approvalId) => sessions.proxySnapshot(sessionId, approvalId),
        },
        signal,
      ),
  };

  return {
    root,
    config,
    store,
    redactor,
    lease,
    control,
    approvals,
    sessions,
    adapters,
    workspace,
    snapshots,
    context,
    killed,
    cleanup() {
      approvals.cancelAll('kill');
      lease.stop();
      store.close();
      rmSync(root, { recursive: true, force: true });
    },
  };
}

export const authHeaders = {
  'x-oet-internal-token': TEST_TOKEN,
  'x-oet-owner-account': OWNER_ID,
};

/** Activates the owner lease for the next few minutes. */
export function activateLease(harness: TestHarness): void {
  harness.lease.set(Date.now() + 60_000);
}
