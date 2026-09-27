// ChatGPT sign-in for Codex via the app-server's device-code flow.
//
// `account/login/start {type:'chatgptDeviceCode'}` returns a loginId, a verification URL and
// a user code; the owner opens the URL on any device, enters the code and Codex completes the
// login in the background (`account/login/completed` + `account/updated`). API-key login is
// never offered (`type:'apiKey'` is not reachable from here), and credentials stay in Codex's
// own store under $CODEX_HOME (one store per machine; never copied in or out).
// Device-code login must be enabled for Codex in the ChatGPT Business workspace settings.

import type { ConnectFlow, EngineAuth } from '../engines/types.js';
import { mapAccountRead, parseDeviceCodeStart, parseLoginCompleted, type DeviceCodeStart } from '../engines/codex-protocol.js';
import { AuthFlowError, FlowRegistry, isTerminalFlow, sanitizeDetail } from './claude.js';

/** The slice of the shared app-server connection this module needs. */
export interface CodexRpc {
  request(method: string, params?: unknown, opts?: { timeoutMs?: number }): Promise<unknown>;
}

export interface CodexAuthOptions {
  now?: () => number;
  /** Used when the server does not report an expiry (OpenAI device codes last ~15 min). VERIFY-ON-PIN. */
  deviceCodeTtlMs?: number;
  /** Called after a sign-in completes, the account changes or a logout succeeds. */
  onAuthChanged?(): void;
}

/** Turns a login/start failure into an owner-facing hint without echoing raw payloads. */
export function describeLoginStartFailure(error: unknown): string {
  const message = error instanceof Error ? error.message : '';
  if (/device.?code/i.test(message) && /(disabled|not enabled|not allowed|unsupported)/i.test(message)) {
    return 'Device-code login is not enabled for Codex in the ChatGPT workspace settings (Security → device code login).';
  }
  if (/workspace/i.test(message)) {
    return `ChatGPT workspace check failed: ${sanitizeDetail(message)}. Check OWNER_AGENT__CODEXWORKSPACEID.`;
  }
  return message ? `Could not start ChatGPT sign-in: ${sanitizeDetail(message)}` : 'Could not start ChatGPT sign-in.';
}

export class CodexAuthManager {
  private readonly flows: FlowRegistry;
  private readonly now: () => number;
  private readonly loginIds = new Map<string, string>();
  private readonly expiryTimers = new Map<string, ReturnType<typeof setTimeout>>();

  constructor(
    private readonly rpc: CodexRpc,
    private readonly options: CodexAuthOptions = {},
  ) {
    this.now = options.now ?? Date.now;
    this.flows = new FlowRegistry('codex', this.now);
  }

  hasActiveFlow(): boolean {
    return this.flows.active() !== undefined;
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return this.flows.get(flowId);
  }

  async connect(): Promise<ConnectFlow> {
    const previous = this.flows.active();
    if (previous) await this.finish(previous.flowId, { state: 'cancelled', detail: 'Superseded by a new sign-in.' }, true);
    const flow = this.flows.create('device_code');
    let start: DeviceCodeStart;
    try {
      start = parseDeviceCodeStart(await this.rpc.request('account/login/start', { type: 'chatgptDeviceCode' }));
    } catch (err) {
      return this.flows.update(flow.flowId, { state: 'failed', detail: describeLoginStartFailure(err) }) ?? flow;
    }
    const ttl = this.options.deviceCodeTtlMs ?? 15 * 60_000;
    const expiresAt = start.expiresAt ?? new Date(this.now() + ttl).toISOString();
    this.loginIds.set(flow.flowId, start.loginId);
    const updated = this.flows.update(flow.flowId, {
      state: 'pending',
      verificationUrl: start.verificationUrl,
      userCode: start.userCode,
      expiresAt,
      detail: 'Open the link, sign in with the ChatGPT Business account and enter the code.',
    });
    const delay = Math.max(1_000, Date.parse(expiresAt) - this.now());
    const timer = setTimeout(() => {
      void this.finish(flow.flowId, { state: 'expired', detail: 'The code expired before it was entered. Start again.' }, true);
    }, delay);
    timer.unref?.();
    this.expiryTimers.set(flow.flowId, timer);
    return updated ?? flow;
  }

  /** Device-code flows have no paste-back step (CONTRACT: `code` is Claude-only). */
  submitCode(flowId: string): Promise<ConnectFlow> {
    if (!this.flows.get(flowId)) return Promise.reject(new AuthFlowError('flow_not_found', 'Unknown sign-in flow.'));
    return Promise.reject(new AuthFlowError('flow_not_awaiting_code', 'ChatGPT sign-in uses a device code; enter it on the ChatGPT page.'));
  }

  async cancel(flowId: string): Promise<ConnectFlow> {
    const flow = this.flows.get(flowId);
    if (!flow) throw new AuthFlowError('flow_not_found', 'Unknown sign-in flow.');
    if (isTerminalFlow(flow)) return flow;
    return this.finish(flowId, { state: 'cancelled' }, true);
  }

  async logout(): Promise<void> {
    const active = this.flows.active();
    if (active) await this.finish(active.flowId, { state: 'cancelled', detail: 'Signed out.' }, true);
    try {
      await this.rpc.request('account/logout', {});
    } catch (err) {
      throw new AuthFlowError('logout_failed', `Codex logout failed: ${sanitizeDetail(err instanceof Error ? err.message : String(err))}`);
    }
    this.options.onAuthChanged?.();
  }

  /** account/read → EngineAuth (`signing_in` while a device-code flow is open). */
  async readAccount(): Promise<EngineAuth> {
    const auth = mapAccountRead(await this.rpc.request('account/read', { refreshToken: false }));
    if (auth.state === 'signed_out' && this.hasActiveFlow()) return { state: 'signing_in' };
    return auth;
  }

  /** Routed from the shared app-server for account/* notifications. */
  handleNotification(method: string, params: unknown): void {
    if (method === 'account/login/completed') {
      const done = parseLoginCompleted(params);
      const flowId = done.loginId ? this.flowIdForLogin(done.loginId) : this.flows.active()?.flowId;
      if (!flowId) return;
      if (done.success) {
        void this.finish(flowId, { state: 'completed', detail: 'Signed in to ChatGPT.' }, false);
        this.options.onAuthChanged?.();
      } else {
        void this.finish(flowId, { state: 'failed', detail: done.error ? sanitizeDetail(done.error) : 'ChatGPT sign-in failed.' }, false);
      }
    } else if (method === 'account/updated') {
      this.options.onAuthChanged?.();
    }
  }

  /** The app-server died: an in-flight device-code login cannot complete any more. */
  onServerRestart(): void {
    const active = this.flows.active();
    if (active) void this.finish(active.flowId, { state: 'failed', detail: 'Codex restarted during sign-in. Start again.' }, false);
  }

  shutdown(): void {
    for (const timer of this.expiryTimers.values()) clearTimeout(timer);
    this.expiryTimers.clear();
  }

  private flowIdForLogin(loginId: string): string | undefined {
    for (const [flowId, id] of this.loginIds) if (id === loginId) return flowId;
    return undefined;
  }

  private async finish(
    flowId: string,
    patch: { state: ConnectFlow['state']; detail?: string },
    cancelOnServer: boolean,
  ): Promise<ConnectFlow> {
    const timer = this.expiryTimers.get(flowId);
    if (timer) clearTimeout(timer);
    this.expiryTimers.delete(flowId);
    const loginId = this.loginIds.get(flowId);
    const before = this.flows.get(flowId);
    const result = this.flows.update(flowId, patch);
    if (cancelOnServer && loginId && before && !isTerminalFlow(before)) {
      try {
        await this.rpc.request('account/login/cancel', { loginId });
      } catch {
        // Already finished or the server restarted; the local flow is terminal either way.
      }
    }
    return result ?? { flowId, engine: 'codex', kind: 'device_code', ...patch };
  }
}
