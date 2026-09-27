import { afterEach, describe, expect, it, vi } from 'vitest';
import { CodexAuthManager, type CodexRpc, describeLoginStartFailure } from '../../src/auth/codex.js';
import { RpcError } from '../../src/engines/codex-protocol.js';

const START = { type: 'chatgptDeviceCode', loginId: 'login-test-1', verificationUrl: 'https://auth.openai.com/codex/device', userCode: 'TEST-CODE' };

function fakeRpc(handlers: Record<string, (params: unknown) => unknown>) {
  const calls: { method: string; params: unknown }[] = [];
  const rpc: CodexRpc = {
    request: async (method, params) => {
      calls.push({ method, params });
      const handler = handlers[method];
      if (!handler) throw new RpcError(-32601, `no ${method}`);
      return handler(params);
    },
  };
  return { rpc, calls };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('CodexAuthManager (ChatGPT device code)', () => {
  it('starts a device-code login (never an API key) and exposes URL, code and expiry', async () => {
    const { rpc, calls } = fakeRpc({ 'account/login/start': () => START });
    const manager = new CodexAuthManager(rpc, { now: () => 1_790_000_000_000 });
    const flow = await manager.connect();
    expect(calls).toEqual([{ method: 'account/login/start', params: { type: 'chatgptDeviceCode' } }]);
    expect(flow).toMatchObject({
      engine: 'codex',
      kind: 'device_code',
      state: 'pending',
      verificationUrl: 'https://auth.openai.com/codex/device',
      userCode: 'TEST-CODE',
      expiresAt: new Date(1_790_000_000_000 + 15 * 60_000).toISOString(),
    });
    expect(flow.flowId).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/);
    expect(manager.hasActiveFlow()).toBe(true);
    manager.shutdown();
  });

  it('completes on account/login/completed and notifies listeners', async () => {
    const onAuthChanged = vi.fn();
    const { rpc } = fakeRpc({ 'account/login/start': () => START });
    const manager = new CodexAuthManager(rpc, { onAuthChanged });
    const flow = await manager.connect();
    manager.handleNotification('account/login/completed', { loginId: 'someone-else', success: true });
    expect(manager.getFlow(flow.flowId)?.state).toBe('pending');
    manager.handleNotification('account/login/completed', { loginId: 'login-test-1', success: true, error: null });
    await vi.waitFor(() => expect(manager.getFlow(flow.flowId)?.state).toBe('completed'));
    expect(onAuthChanged).toHaveBeenCalled();
    manager.handleNotification('account/updated', { authMode: 'chatgpt' });
    expect(onAuthChanged).toHaveBeenCalledTimes(2);
    manager.shutdown();
  });

  it('fails on an unsuccessful completion with a sanitized reason', async () => {
    const { rpc } = fakeRpc({ 'account/login/start': () => START });
    const manager = new CodexAuthManager(rpc);
    const flow = await manager.connect();
    manager.handleNotification('account/login/completed', { loginId: 'login-test-1', success: false, error: 'Workspace is not allowed for this login' });
    await vi.waitFor(() => expect(manager.getFlow(flow.flowId)).toMatchObject({ state: 'failed', detail: 'Workspace is not allowed for this login' }));
    manager.shutdown();
  });

  it('expires the code and cancels it on the server', async () => {
    vi.useFakeTimers();
    const { rpc, calls } = fakeRpc({ 'account/login/start': () => START, 'account/login/cancel': () => ({ status: 'canceled' }) });
    const manager = new CodexAuthManager(rpc, { deviceCodeTtlMs: 60_000 });
    const flow = await manager.connect();
    await vi.advanceTimersByTimeAsync(61_000);
    expect(manager.getFlow(flow.flowId)?.state).toBe('expired');
    expect(calls.at(-1)).toEqual({ method: 'account/login/cancel', params: { loginId: 'login-test-1' } });
  });

  it('cancels explicitly and when superseded', async () => {
    const { rpc, calls } = fakeRpc({ 'account/login/start': () => START, 'account/login/cancel': () => ({ status: 'canceled' }) });
    const manager = new CodexAuthManager(rpc);
    const first = await manager.connect();
    const second = await manager.connect();
    expect(manager.getFlow(first.flowId)).toMatchObject({ state: 'cancelled', detail: 'Superseded by a new sign-in.' });
    await expect(manager.cancel(second.flowId)).resolves.toMatchObject({ state: 'cancelled' });
    expect(calls.filter((c) => c.method === 'account/login/cancel')).toHaveLength(2);
    await expect(manager.cancel(second.flowId)).resolves.toMatchObject({ state: 'cancelled' });
    await expect(manager.cancel('01J8Z000000000000000000000')).rejects.toMatchObject({ code: 'flow_not_found', status: 404 });
    manager.shutdown();
  });

  it('reports start failures as a failed flow with an actionable hint', async () => {
    const { rpc } = fakeRpc({
      'account/login/start': () => {
        throw new RpcError(-32600, 'device code login is not enabled for this workspace');
      },
    });
    const manager = new CodexAuthManager(rpc);
    const flow = await manager.connect();
    expect(flow.state).toBe('failed');
    expect(flow.detail).toMatch(/Device-code login is not enabled/);
    expect(manager.hasActiveFlow()).toBe(false);

    const bad = new CodexAuthManager(fakeRpc({ 'account/login/start': () => ({ ...START, verificationUrl: 'https://phish.example/device' }) }).rpc);
    await expect(bad.connect()).resolves.toMatchObject({ state: 'failed', detail: expect.stringMatching(/not an OpenAI host/) });
  });

  it('has no paste-code step', async () => {
    const { rpc } = fakeRpc({ 'account/login/start': () => START });
    const manager = new CodexAuthManager(rpc);
    const flow = await manager.connect();
    await expect(manager.submitCode(flow.flowId)).rejects.toMatchObject({ code: 'flow_not_awaiting_code', status: 409 });
    await expect(manager.submitCode('01J8Z000000000000000000000')).rejects.toMatchObject({ code: 'flow_not_found' });
    manager.shutdown();
  });

  it('reads the account, showing signing_in while a code is open', async () => {
    let account: unknown = null;
    const { rpc, calls } = fakeRpc({ 'account/login/start': () => START, 'account/read': () => ({ account, requiresOpenaiAuth: true }) });
    const manager = new CodexAuthManager(rpc);
    await expect(manager.readAccount()).resolves.toEqual({ state: 'signed_out' });
    expect(calls[0]).toEqual({ method: 'account/read', params: { refreshToken: false } });
    await manager.connect();
    await expect(manager.readAccount()).resolves.toEqual({ state: 'signing_in' });
    account = { type: 'chatgpt', email: 'owner@example.test', planType: 'business' };
    await expect(manager.readAccount()).resolves.toEqual({ state: 'signed_in', account: { email: 'owner@example.test', plan: 'business' } });
    manager.shutdown();
  });

  it('fails an open flow when the app-server restarts, and logs out', async () => {
    const onAuthChanged = vi.fn();
    const { rpc, calls } = fakeRpc({ 'account/login/start': () => START, 'account/logout': () => ({}), 'account/login/cancel': () => ({}) });
    const manager = new CodexAuthManager(rpc, { onAuthChanged });
    const flow = await manager.connect();
    manager.onServerRestart();
    await vi.waitFor(() => expect(manager.getFlow(flow.flowId)?.state).toBe('failed'));
    await manager.logout();
    expect(calls.map((c) => c.method)).toContain('account/logout');
    expect(onAuthChanged).toHaveBeenCalled();

    const broken = new CodexAuthManager(fakeRpc({}).rpc);
    await expect(broken.logout()).rejects.toMatchObject({ code: 'logout_failed' });
  });

  it('turns login/start errors into owner-facing hints', () => {
    expect(describeLoginStartFailure(new Error('Device code auth is disabled for workspace'))).toMatch(/Device-code login is not enabled/);
    expect(describeLoginStartFailure(new Error('workspace mismatch: expected another workspace'))).toMatch(/OWNER_AGENT__CODEXWORKSPACEID/);
    expect(describeLoginStartFailure(new Error('boom'))).toBe('Could not start ChatGPT sign-in: boom');
    expect(describeLoginStartFailure(undefined)).toBe('Could not start ChatGPT sign-in.');
  });
});
