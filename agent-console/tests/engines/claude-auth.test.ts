import { describe, expect, it, vi } from 'vitest';
import {
  AuthFlowError,
  ClaudeAuthManager,
  type ClaudeCliConfig,
  detectLoginOutcome,
  extractLoginUrl,
  FlowRegistry,
  isAllowedClaudeLoginUrl,
  isLoginMethodMenu,
  newFlowId,
  normalizeAuthCode,
  parseClaudeAuthStatus,
  parseCliVersion,
  type PtyProcess,
  sanitizeDetail,
  stripAnsi,
} from '../../src/auth/claude.js';
import type { RunResult } from '../../src/exec.js';

// Fixtures are synthetic: fake client ids, challenges, states and codes built for these tests.
const LOGIN_URL =
  'https://claude.ai/oauth/authorize?code=true&client_id=00000000-0000-4000-8000-000000000000&response_type=code' +
  '&redirect_uri=https%3A%2F%2Fplatform.claude.com%2Foauth%2Fcode%2Fcallback&scope=user%3Aprofile+user%3Ainference' +
  '&code_challenge=test-challenge&code_challenge_method=S256&state=test-state';
const PASTE_CODE = 'test-code-123456#test-state';
const ESC = '\u001b';

describe('terminal output parsers', () => {
  it('strips colours, cursor moves and OSC sequences', () => {
    expect(stripAnsi(`${ESC}[1m${ESC}[38;5;174mSign in${ESC}[0m${ESC}[2K${ESC}[1G done`)).toBe('Sign in done');
    expect(stripAnsi(`${ESC}]8;;https://claude.ai/x${ESC}\\link${ESC}]8;;${ESC}\\`)).toBe('link');
  });

  it('finds the OAuth URL in plain, coloured and hyperlinked output', () => {
    expect(extractLoginUrl(`Browser didn't open? Use the url below to sign in:\r\n\r\n${LOGIN_URL}\r\n\r\nPaste code here if prompted > `)).toBe(LOGIN_URL);
    expect(extractLoginUrl(`${ESC}[2m${LOGIN_URL}${ESC}[22m`)).toBe(LOGIN_URL);
    expect(extractLoginUrl(`${ESC}]8;;${LOGIN_URL}${ESC}\\Click here to sign in${ESC}]8;;${ESC}\\`)).toBe(LOGIN_URL);
    expect(extractLoginUrl(`(${LOGIN_URL}).`)).toBe(LOGIN_URL);
  });

  it('prefers the manual paste-code URL and ignores non-Anthropic hosts', () => {
    const automatic = LOGIN_URL.replace('code=true&', '');
    expect(extractLoginUrl(`${automatic}\n${LOGIN_URL}\n`)).toBe(LOGIN_URL);
    expect(extractLoginUrl('https://evil.example/oauth/authorize?code=true')).toBeUndefined();
    expect(extractLoginUrl('https://claude.ai/settings')).toBeUndefined();
    expect(extractLoginUrl('Opening browser…')).toBeUndefined();
  });

  it('only relays https Anthropic URLs', () => {
    expect(isAllowedClaudeLoginUrl('https://claude.ai/oauth/authorize')).toBe(true);
    expect(isAllowedClaudeLoginUrl('https://platform.claude.com/oauth/authorize')).toBe(true);
    expect(isAllowedClaudeLoginUrl('https://console.anthropic.com/oauth/authorize')).toBe(true);
    expect(isAllowedClaudeLoginUrl('http://claude.ai/oauth/authorize')).toBe(false);
    expect(isAllowedClaudeLoginUrl('https://claude.ai.evil.example/oauth/authorize')).toBe(false);
    expect(isAllowedClaudeLoginUrl('https://user:pw@claude.ai/oauth/authorize')).toBe(false);
    expect(isAllowedClaudeLoginUrl('not a url')).toBe(false);
  });

  it('detects the login-method menu', () => {
    expect(isLoginMethodMenu(`Select login method:\r\n${ESC}[36m❯ 1. Claude account with subscription${ESC}[39m`)).toBe(true);
    expect(isLoginMethodMenu('Paste code here if prompted >')).toBe(false);
  });

  it('classifies the CLI verdict after the code without echoing secrets', () => {
    expect(detectLoginOutcome(`\r\n${ESC}[32mLogin successful.${ESC}[39m Press Enter to continue…`)).toEqual({ outcome: 'success' });
    const failure = detectLoginOutcome(`\r\nOAuth error: Request failed with status code 400 (invalid_grant) ${PASTE_CODE}\r\n`);
    expect(failure?.outcome).toBe('failure');
    expect(failure?.detail).toContain('OAuth error');
    expect(failure?.detail).not.toContain(PASTE_CODE);
    expect(detectLoginOutcome('Paste code here if prompted > ****')).toBeUndefined();
  });

  it('sanitizes details', () => {
    expect(sanitizeDetail(`${ESC}[31merror${ESC}[0m   token abcdefghijklmnopqrstuvwxyz0123`)).toBe('error token [redacted]');
    expect(sanitizeDetail('x '.repeat(200)).length).toBeLessThanOrEqual(160);
  });

  it('accepts only plausible paste codes', () => {
    expect(normalizeAuthCode(`  ${PASTE_CODE}\n`)).toBe(PASTE_CODE);
    for (const bad of ['short', 'has spaces in it#state', 'semi;colon-code-value', `${'a'.repeat(1025)}`]) {
      expect(() => normalizeAuthCode(bad)).toThrow(AuthFlowError);
    }
    try {
      normalizeAuthCode('bad code');
    } catch (err) {
      expect(err).toMatchObject({ status: 400, code: 'invalid_code' });
    }
  });

  it('parses claude auth status JSON (signed in, signed out, noise)', () => {
    const json = '{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","email":"owner@example.test","orgId":"org-test","orgName":"OET","subscriptionType":"max"}';
    expect(parseClaudeAuthStatus(`warning: something\n${json}\n`, 0)).toEqual({
      loggedIn: true,
      authMethod: 'claude.ai',
      apiProvider: 'firstParty',
      email: 'owner@example.test',
      orgName: 'OET',
      subscriptionType: 'max',
    });
    expect(parseClaudeAuthStatus('{"loggedIn":false,"authMethod":"none"}', 1)).toEqual({ loggedIn: false, authMethod: 'none' });
    expect(parseClaudeAuthStatus('Not logged in', 1)).toEqual({ loggedIn: false });
    expect(parseClaudeAuthStatus('{"loggedIn":true,"subscriptionType":null}', 0)).toEqual({ loggedIn: true });
    expect(() => parseClaudeAuthStatus('garbage', 0)).toThrow(/unreadable/);
  });

  it('parses the CLI version', () => {
    expect(parseCliVersion('2.1.283 (Claude Code)\n')).toBe('2.1.283');
    expect(parseCliVersion('')).toBeNull();
  });
});

describe('flow ids and registry', () => {
  it('generates time-ordered ULIDs', () => {
    const a = newFlowId(1_790_000_000_000);
    const b = newFlowId(1_790_000_000_001);
    expect(a).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/);
    expect(a < b).toBe(true);
    expect(newFlowId()).not.toBe(newFlowId());
  });

  it('keeps terminal flows immutable and prunes them after the retention window', () => {
    let now = 1_000;
    const registry = new FlowRegistry('claude', () => now, 60_000);
    const flow = registry.create('paste_code');
    expect(flow).toMatchObject({ engine: 'claude', kind: 'paste_code', state: 'pending' });
    expect(registry.active()?.flowId).toBe(flow.flowId);
    registry.update(flow.flowId, { state: 'awaiting_code', verificationUrl: LOGIN_URL });
    registry.update(flow.flowId, { state: 'completed' });
    expect(registry.update(flow.flowId, { state: 'failed' })?.state).toBe('completed');
    expect(registry.active()).toBeUndefined();
    expect(registry.update('missing', { state: 'failed' })).toBeUndefined();
    now += 61_000;
    expect(registry.get(flow.flowId)).toBeUndefined();
  });
});

// ─── PTY-driven login ──────────────────────────────────────────────────────────────────────

class FakePty implements PtyProcess {
  readonly written: string[] = [];
  readonly kills: string[] = [];
  onWrite: ((data: string) => void) | undefined;
  private readonly dataListeners: ((data: string) => void)[] = [];
  private readonly exitListeners: ((event: { exitCode: number }) => void)[] = [];
  private exited = false;

  onData(listener: (data: string) => void): { dispose(): void } {
    this.dataListeners.push(listener);
    return { dispose: () => undefined };
  }

  onExit(listener: (event: { exitCode: number }) => void): { dispose(): void } {
    this.exitListeners.push(listener);
    return { dispose: () => undefined };
  }

  write(data: string): void {
    this.written.push(data);
    this.onWrite?.(data);
  }

  kill(signal?: string): void {
    this.kills.push(signal ?? 'SIGHUP');
    this.exit(0);
  }

  output(text: string): void {
    for (const listener of this.dataListeners) listener(text);
  }

  exit(exitCode: number): void {
    if (this.exited) return;
    this.exited = true;
    for (const listener of this.exitListeners) listener({ exitCode });
  }
}

function run(code: number, stdout: string, stderr = ''): RunResult {
  return { code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr, stdoutTruncated: false, timedOut: false };
}

const cli: ClaudeCliConfig = {
  asAgentPath: '/usr/local/bin/as-agent',
  claudeBinPath: '/usr/local/lib/oet-agent/claude',
  homeDir: '/home/agent',
  env: () => ({ HOME: '/home/agent', PATH: '/usr/bin', CLAUDE_CONFIG_DIR: '/home/agent/.claude' }),
};

function setup(opts: { banner?: string | null; status?: Record<string, unknown>; logoutCode?: number; flowTtlMs?: number } = {}) {
  const ptys: FakePty[] = [];
  const spawns: { file: string; args: string[]; options: { cols: number; rows: number; cwd: string; env: Record<string, string> } }[] = [];
  const status = { current: opts.status ?? { loggedIn: false, authMethod: 'none' } };
  const runner = vi.fn(async (_command: string, args: readonly string[]) => {
    const joined = args.join(' ');
    if (joined === 'auth status') return run(status.current['loggedIn'] ? 0 : 1, JSON.stringify(status.current));
    if (joined === 'auth logout') return run(opts.logoutCode ?? 0, '', opts.logoutCode ? 'logout exploded' : '');
    if (joined === '--version') return run(0, '2.1.283 (Claude Code)');
    return run(1, '');
  });
  const onAuthChanged = vi.fn();
  const banner = opts.banner === undefined ? `Browser didn't open? Use the url below to sign in:\r\n\r\n${LOGIN_URL}\r\n\r\nPaste code here if prompted > ` : opts.banner;
  const manager = new ClaudeAuthManager(cli, {
    spawnPty: (file, args, options) => {
      const pty = new FakePty();
      ptys.push(pty);
      spawns.push({ file, args, options });
      if (banner !== null) setImmediate(() => pty.output(banner));
      return pty;
    },
    runner: runner as never,
    urlTimeoutMs: 1_000,
    outcomeWaitMs: 2_000,
    ...(opts.flowTtlMs ? { flowTtlMs: opts.flowTtlMs } : {}),
    onAuthChanged,
  });
  return { manager, ptys, spawns, status, runner, onAuthChanged };
}

describe('ClaudeAuthManager (claude auth login in a PTY)', () => {
  it('runs the bundled CLI as the agent in a wide PTY and relays only the sign-in URL', async () => {
    const { manager, spawns } = setup();
    const flow = await manager.connect();
    expect(flow).toMatchObject({ engine: 'claude', kind: 'paste_code', state: 'awaiting_code', verificationUrl: LOGIN_URL });
    expect(flow.flowId).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/);
    expect(Date.parse(flow.expiresAt ?? '')).toBeGreaterThan(Date.now());
    expect(flow.userCode).toBeUndefined();
    expect(spawns[0]?.file).toBe('/usr/local/bin/as-agent');
    expect(spawns[0]?.args).toEqual(['/usr/local/lib/oet-agent/claude', 'auth', 'login']);
    expect(spawns[0]?.args).not.toContain('--console');
    expect(spawns[0]?.options.cols).toBeGreaterThanOrEqual(400);
    expect(spawns[0]?.options.cwd).toBe('/home/agent');
    expect(spawns[0]?.options.env).toMatchObject({ TERM: 'xterm-256color', CLAUDE_CONFIG_DIR: '/home/agent/.claude' });
    expect(manager.hasActiveFlow()).toBe(true);
    manager.shutdown();
  });

  it('types the paste code + Enter and completes once claude auth status confirms the subscription', async () => {
    const { manager, ptys, status, onAuthChanged } = setup();
    const flow = await manager.connect();
    const pty = ptys[0]!;
    pty.onWrite = (data) => {
      if (data === '\r' && pty.written.length === 2) {
        status.current = { loggedIn: true, authMethod: 'claude.ai', apiProvider: 'firstParty', subscriptionType: 'max' };
        setImmediate(() => pty.output(`\r\n${ESC}[32mLogin successful.${ESC}[39m Press Enter to continue…\r\n`));
      }
    };
    const done = await manager.submitCode(flow.flowId, `  ${PASTE_CODE}  `);
    expect(done).toMatchObject({ state: 'completed', detail: 'Signed in (max).' });
    expect(pty.written.slice(0, 2)).toEqual([PASTE_CODE, '\r']);
    expect(pty.kills.length).toBeGreaterThan(0);
    expect(onAuthChanged).toHaveBeenCalled();
    expect(manager.getFlow(flow.flowId)?.state).toBe('completed');
    expect(manager.hasActiveFlow()).toBe(false);
  });

  it('fails without echoing the code when Anthropic rejects it', async () => {
    const { manager, ptys } = setup();
    const flow = await manager.connect();
    const pty = ptys[0]!;
    pty.onWrite = (data) => {
      if (data === '\r') setImmediate(() => pty.output(`\r\nOAuth error: Request failed with status code 400 (invalid_grant) ${PASTE_CODE}\r\n`));
    };
    const done = await manager.submitCode(flow.flowId, PASTE_CODE);
    expect(done.state).toBe('failed');
    expect(done.detail).toContain('OAuth error');
    expect(done.detail).not.toContain(PASTE_CODE);
    expect(pty.kills.length).toBeGreaterThan(0);
  });

  it('fails when the CLI signs in with something other than a Claude subscription', async () => {
    const { manager, ptys, status } = setup();
    const flow = await manager.connect();
    const pty = ptys[0]!;
    pty.onWrite = (data) => {
      if (data === '\r') {
        status.current = { loggedIn: true, authMethod: 'console' };
        setImmediate(() => pty.output('\r\nLogin successful.\r\n'));
      }
    };
    const done = await manager.submitCode(flow.flowId, PASTE_CODE);
    expect(done).toMatchObject({ state: 'failed', detail: expect.stringMatching(/not with a Claude subscription/) });
  });

  it('rejects malformed codes and flows that are not waiting for one', async () => {
    const { manager, ptys } = setup();
    const flow = await manager.connect();
    await expect(manager.submitCode(flow.flowId, 'not a code')).rejects.toMatchObject({ code: 'invalid_code', status: 400 });
    expect(ptys[0]!.written).toEqual([]);
    await expect(manager.submitCode('01J8Z000000000000000000000', PASTE_CODE)).rejects.toMatchObject({ code: 'flow_not_found', status: 404 });
    await manager.cancel(flow.flowId);
    await expect(manager.submitCode(flow.flowId, PASTE_CODE)).rejects.toMatchObject({ code: 'flow_not_awaiting_code', status: 409 });
  });

  it('answers the login-method menu with the subscription option', async () => {
    const { manager, ptys } = setup({ banner: `Select login method:\r\n${ESC}[36m❯ 1. Claude account with subscription${ESC}[39m\r\n  2. Anthropic Console account\r\n` });
    const connecting = manager.connect();
    await vi.waitFor(() => expect(ptys[0]?.written).toEqual(['\r']));
    ptys[0]!.output(`\r\n${LOGIN_URL}\r\nPaste code here if prompted > `);
    await expect(connecting).resolves.toMatchObject({ state: 'awaiting_code', verificationUrl: LOGIN_URL });
    manager.shutdown();
  });

  it('fails when no URL appears in time or the CLI exits early', async () => {
    const silent = setup({ banner: null });
    const flow = await silent.manager.connect();
    expect(flow).toMatchObject({ state: 'failed', detail: expect.stringMatching(/did not print a sign-in URL/) });
    expect(silent.ptys[0]!.kills.length).toBeGreaterThan(0);

    const early = setup({ banner: null });
    const connecting = early.manager.connect();
    await vi.waitFor(() => expect(early.ptys).toHaveLength(1));
    early.ptys[0]!.exit(1);
    await expect(connecting).resolves.toMatchObject({ state: 'failed', detail: expect.stringMatching(/exited \(code 1\)/) });
  });

  it('cancels, expires and supersedes flows', async () => {
    const { manager, ptys } = setup({ flowTtlMs: 150 });
    const first = await manager.connect();
    const second = await manager.connect();
    expect(manager.getFlow(first.flowId)).toMatchObject({ state: 'cancelled', detail: 'Superseded by a new sign-in.' });
    expect(ptys[0]!.kills.length).toBeGreaterThan(0);
    await vi.waitFor(() => expect(manager.getFlow(second.flowId)?.state).toBe('expired'), { timeout: 2_000 });

    const third = await manager.connect();
    await expect(manager.cancel(third.flowId)).resolves.toMatchObject({ state: 'cancelled' });
    await expect(manager.cancel('01J8Z000000000000000000000')).rejects.toMatchObject({ code: 'flow_not_found' });
  });

  it('reads status/version and logs out through the CLI', async () => {
    const ok = setup({ status: { loggedIn: true, authMethod: 'claude.ai', subscriptionType: 'max' } });
    await expect(ok.manager.readStatus()).resolves.toMatchObject({ loggedIn: true, subscriptionType: 'max' });
    await expect(ok.manager.version()).resolves.toBe('2.1.283');
    await expect(ok.manager.logout()).resolves.toBeUndefined();
    expect(ok.onAuthChanged).toHaveBeenCalled();
    expect(ok.runner).toHaveBeenCalledWith('/usr/local/lib/oet-agent/claude', ['auth', 'logout'], expect.objectContaining({ asAgent: true }));

    const broken = setup({ logoutCode: 1 });
    await expect(broken.manager.logout()).rejects.toMatchObject({ code: 'logout_failed', status: 500 });
  });
});
