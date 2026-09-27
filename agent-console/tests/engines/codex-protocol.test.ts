import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  approvalResponse,
  buildThreadResumeParams,
  buildThreadStartParams,
  buildTurnStartParams,
  capUtf8,
  classifyMessage,
  codexErrorCode,
  CodexTurnMapper,
  encodeMessage,
  EngineError,
  epochToIso,
  LineDecoder,
  mapAccountRead,
  mapChangeKind,
  mapModelList,
  mapRateLimitSnapshot,
  mapRateLimitsPayload,
  normalizeCommand,
  parseDeviceCodeStart,
  parseLoginCompleted,
  parseTurnCompleted,
  parseUserAgentVersion,
  RPC_METHOD_NOT_FOUND,
  RpcConnection,
  RpcError,
  safeErrorMessage,
  threadIdOf,
  tokenizeShell,
  TOOL_OUTPUT_CAP_BYTES,
  turnIdOf,
} from '../../src/engines/codex-protocol.js';
import type { EngineEvent } from '../../src/engines/types.js';
import { classifyToolCall } from '../../src/guard.js';

const flush = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

afterEach(() => {
  vi.useRealTimers();
});

describe('JSONL framing', () => {
  it('encodes one line per message without a jsonrpc member', () => {
    const line = encodeMessage({ id: 1, method: 'initialize', params: { a: 1 } });
    expect(line.endsWith('\n')).toBe(true);
    expect(line.slice(0, -1)).not.toContain('\n');
    expect(line).not.toContain('jsonrpc');
    expect(JSON.parse(line)).toEqual({ id: 1, method: 'initialize', params: { a: 1 } });
  });

  it('splits chunks at newlines, tolerating CRLF, blank lines and partial lines', () => {
    const decoder = new LineDecoder();
    expect(decoder.push('{"a":1}\n{"b"')).toEqual(['{"a":1}']);
    expect(decoder.push(':2}\r\n\n{"c":3}')).toEqual(['{"b":2}']);
    expect(decoder.flush()).toEqual(['{"c":3}']);
    expect(decoder.push(Buffer.from('{"d":4}\n'))).toEqual(['{"d":4}']);
  });

  it('drops an oversize line and resynchronises on the next newline', () => {
    const onOversize = vi.fn();
    const decoder = new LineDecoder(10, onOversize);
    expect(decoder.push('x'.repeat(20))).toEqual([]);
    expect(onOversize).toHaveBeenCalledWith(20);
    expect(decoder.push('tail-of-the-long-line\n{"ok":1}\n')).toEqual(['{"ok":1}']);
  });

  it('classifies requests, notifications, responses and junk', () => {
    expect(classifyMessage({ id: 7, method: 'item/commandExecution/requestApproval', params: { a: 1 } })).toEqual({
      kind: 'request',
      id: 7,
      method: 'item/commandExecution/requestApproval',
      params: { a: 1 },
    });
    expect(classifyMessage({ method: 'turn/started', params: {} })).toEqual({ kind: 'notification', method: 'turn/started', params: {} });
    expect(classifyMessage({ id: 1, result: { ok: true } })).toEqual({ kind: 'response', id: 1, result: { ok: true } });
    expect(classifyMessage({ id: 'a', error: { code: -32000, message: 'boom' } })).toEqual({
      kind: 'response',
      id: 'a',
      error: { code: -32000, message: 'boom' },
    });
    expect(classifyMessage('text').kind).toBe('invalid');
    expect(classifyMessage({}).kind).toBe('invalid');
    expect(classifyMessage([1, 2]).kind).toBe('invalid');
  });
});

describe('RpcConnection', () => {
  function connect(opts: Partial<ConstructorParameters<typeof RpcConnection>[0]> = {}) {
    const written: Record<string, unknown>[] = [];
    const conn = new RpcConnection({
      write: (line) => written.push(JSON.parse(line) as Record<string, unknown>),
      requestTimeoutMs: 1_000,
      ...opts,
    });
    return { conn, written };
  }

  it('correlates responses with requests by id', async () => {
    const { conn, written } = connect();
    const first = conn.request('model/list', { limit: 1 });
    const second = conn.request('account/read');
    expect(written).toEqual([
      { id: 1, method: 'model/list', params: { limit: 1 } },
      { id: 2, method: 'account/read' },
    ]);
    conn.handleLine(JSON.stringify({ id: 2, result: { account: null } }));
    conn.handleLine(JSON.stringify({ id: 1, result: { data: [] } }));
    await expect(first).resolves.toEqual({ data: [] });
    await expect(second).resolves.toEqual({ account: null });
    expect(conn.pendingCount).toBe(0);
  });

  it('rejects with RpcError on an error response', async () => {
    const { conn } = connect();
    const pending = conn.request('thread/resume', { threadId: 'thr_x' });
    conn.handleLine(JSON.stringify({ id: 1, error: { code: -32602, message: 'unknown thread' } }));
    await expect(pending).rejects.toMatchObject({ name: 'RpcError', code: -32602, message: 'unknown thread' });
  });

  it('times out requests that get no answer', async () => {
    vi.useFakeTimers();
    const { conn } = connect();
    const assertion = expect(conn.request('model/list', undefined, { timeoutMs: 50 })).rejects.toThrow(/timed out after 50 ms/);
    await vi.advanceTimersByTimeAsync(60);
    await assertion;
    expect(conn.pendingCount).toBe(0);
  });

  it('supports aborting a request', async () => {
    const { conn } = connect();
    const controller = new AbortController();
    const pending = conn.request('turn/start', {}, { signal: controller.signal });
    controller.abort();
    await expect(pending).rejects.toThrow(/aborted/);
    await expect(conn.request('x', {}, { signal: controller.signal })).rejects.toThrow(/aborted/);
  });

  it('answers server requests with the handler result or a JSON-RPC error', async () => {
    const onRequest = vi.fn(async (method: string) => {
      if (method === 'item/commandExecution/requestApproval') return { decision: 'accept' };
      throw new RpcError(RPC_METHOD_NOT_FOUND, `unsupported ${method}`);
    });
    const { conn, written } = connect({ onRequest });
    conn.handleLine(JSON.stringify({ id: 'srv-1', method: 'item/commandExecution/requestApproval', params: { itemId: 'i' } }));
    conn.handleLine(JSON.stringify({ id: 'srv-2', method: 'mcpServer/elicitation/request', params: {} }));
    await flush();
    expect(written).toContainEqual({ id: 'srv-1', result: { decision: 'accept' } });
    expect(written).toContainEqual({ id: 'srv-2', error: { code: RPC_METHOD_NOT_FOUND, message: 'unsupported mcpServer/elicitation/request' } });
  });

  it('replies method-not-found when no request handler is installed', async () => {
    const { conn, written } = connect();
    conn.handleLine(JSON.stringify({ id: 9, method: 'item/tool/requestUserInput', params: {} }));
    await flush();
    expect(written[0]).toMatchObject({ id: 9, error: { code: RPC_METHOD_NOT_FOUND } });
  });

  it('routes notifications and reports invalid lines', () => {
    const onNotification = vi.fn();
    const onInvalid = vi.fn();
    const { conn } = connect({ onNotification, onInvalid });
    conn.handleLine(JSON.stringify({ method: 'turn/started', params: { threadId: 't' } }));
    conn.handleLine('not json');
    conn.handleLine(JSON.stringify({ foo: 1 }));
    expect(onNotification).toHaveBeenCalledWith('turn/started', { threadId: 't' });
    expect(onInvalid).toHaveBeenCalledTimes(2);
  });

  it('close() rejects in-flight requests and refuses new ones', async () => {
    const { conn } = connect();
    const pending = conn.request('turn/start', {});
    conn.close(new Error('app-server exited'));
    await expect(pending).rejects.toThrow('app-server exited');
    await expect(conn.request('account/read')).rejects.toThrow('app-server exited');
  });
});

describe('request builders', () => {
  it('never asks for approval policy "never" or a read-only sandbox', () => {
    const start = buildThreadStartParams({ cwd: '/workspace/sessions/S1', model: 'gpt-x' });
    expect(start).toEqual({ model: 'gpt-x', cwd: '/workspace/sessions/S1', approvalPolicy: 'untrusted', sandbox: 'danger-full-access' });
    expect(start).not.toHaveProperty('config');
    expect(buildThreadStartParams({ cwd: '/w', model: 'm', developerInstructions: '# manual' })).toMatchObject({ developerInstructions: '# manual' });
    const resume = buildThreadResumeParams({ threadId: 'thr_1', cwd: '/w', model: 'gpt-x' });
    expect(resume).toMatchObject({ threadId: 'thr_1', approvalPolicy: 'untrusted', sandbox: 'danger-full-access' });
    const turn = buildTurnStartParams({ threadId: 'thr_1', text: 'hi', cwd: '/w', model: 'gpt-x', effort: 'high' });
    expect(turn).toEqual({
      threadId: 'thr_1',
      input: [{ type: 'text', text: 'hi' }],
      cwd: '/w',
      model: 'gpt-x',
      effort: 'high',
      summary: 'auto',
      approvalPolicy: 'untrusted',
      sandboxPolicy: { type: 'dangerFullAccess' },
    });
    expect(buildTurnStartParams({ threadId: 't', text: 'x', cwd: '/w', model: 'm' })).not.toHaveProperty('effort');
  });
});

describe('model/list mapping', () => {
  it('maps efforts and defaults, drops hidden models and keeps ids opaque', () => {
    const result = mapModelList({
      data: [
        {
          id: 'model-one',
          model: 'model-one',
          displayName: 'Model One',
          description: 'Frontier',
          supportedReasoningEfforts: [
            { reasoningEffort: 'low', description: 'fast' },
            { reasoningEffort: 'medium', description: '' },
            { reasoningEffort: 'high', description: '' },
          ],
          defaultReasoningEffort: 'medium',
          isDefault: true,
        },
        { id: 'model-two', displayName: 'Two', supportedReasoningEfforts: [], hidden: false },
        { id: 'hidden-model', model: 'hidden-model', hidden: true },
        { id: 'plain-efforts', supportedReasoningEfforts: ['minimal', 'high'], defaultReasoningEffort: 'unknown' },
        { displayName: 'no id' },
      ],
      nextCursor: 'page-2',
    });
    expect(result.nextCursor).toBe('page-2');
    expect(result.models).toEqual([
      {
        value: 'model-one',
        displayName: 'Model One',
        description: 'Frontier',
        supportsEffort: true,
        efforts: ['low', 'medium', 'high'],
        defaultEffort: 'medium',
      },
      { value: 'model-two', displayName: 'Two', supportsEffort: false, efforts: [] },
      { value: 'plain-efforts', displayName: 'plain-efforts', supportsEffort: true, efforts: ['minimal', 'high'] },
    ]);
    expect(mapModelList({ data: [], nextCursor: null }).nextCursor).toBeNull();
    expect(mapModelList(undefined).models).toEqual([]);
  });
});

describe('rate limits', () => {
  it('maps primary/secondary windows with labels, status thresholds and ISO resets', () => {
    const limits = mapRateLimitSnapshot({
      primary: { usedPercent: 45.4, windowDurationMins: 300, resetsAt: 1_790_000_000 },
      secondary: { usedPercent: 85, windowDurationMins: 10_080 },
    });
    expect(limits).toEqual([
      { label: '5-hour window', status: 'ok', usedPercent: 45, resetsAt: new Date(1_790_000_000_000).toISOString() },
      { label: '7-day window', status: 'warning', usedPercent: 85 },
    ]);
    expect(mapRateLimitSnapshot({ primary: { usedPercent: 100 } })).toEqual([{ label: 'Primary window', status: 'limited', usedPercent: 100 }]);
    expect(mapRateLimitSnapshot({ primary: {} })).toEqual([{ label: 'Primary window', status: 'unknown' }]);
    expect(mapRateLimitsPayload({ rateLimits: { secondary: { usedPercent: 10, windowDurationMins: 90 } } })).toEqual([
      { label: '90-minute window', status: 'ok', usedPercent: 10 },
    ]);
    expect(mapRateLimitsPayload({})).toEqual([]);
  });
});

describe('account and device-code login', () => {
  it('maps account/read, treating API-key auth as an error', () => {
    expect(mapAccountRead({ account: { type: 'chatgpt', email: 'owner@example.test', planType: 'business' }, requiresOpenaiAuth: true })).toEqual({
      state: 'signed_in',
      account: { email: 'owner@example.test', plan: 'business' },
    });
    expect(mapAccountRead({ account: null, requiresOpenaiAuth: true })).toEqual({ state: 'signed_out' });
    expect(mapAccountRead({ account: { type: 'apiKey' } }).state).toBe('error');
    expect(mapAccountRead({ account: { type: 'somethingNew' } })).toMatchObject({ state: 'error' });
  });

  it('parses the device-code start response and rejects non-OpenAI verification URLs', () => {
    expect(
      parseDeviceCodeStart({ type: 'chatgptDeviceCode', loginId: 'login-1', verificationUrl: 'https://auth.openai.com/codex/device', userCode: 'ABCD-1234' }),
    ).toEqual({ loginId: 'login-1', verificationUrl: 'https://auth.openai.com/codex/device', userCode: 'ABCD-1234' });
    expect(
      parseDeviceCodeStart({ loginId: 'l', verificationUrl: 'https://auth.openai.com/d', userCode: 'X', expiresAt: 1_790_000_000 }).expiresAt,
    ).toBe(new Date(1_790_000_000_000).toISOString());
    expect(() => parseDeviceCodeStart({ loginId: 'l', verificationUrl: 'https://evil.example/device', userCode: 'X' })).toThrow(/not an OpenAI host/);
    expect(() => parseDeviceCodeStart({ loginId: 'l', verificationUrl: 'http://auth.openai.com/device', userCode: 'X' })).toThrow();
    expect(() => parseDeviceCodeStart({ type: 'chatgptDeviceCode', loginId: 'l' })).toThrow(/missing fields/);
    expect(() => parseDeviceCodeStart({ type: 'apiKey' })).toThrow(/unexpected login response type/);
  });

  it('parses login completion', () => {
    expect(parseLoginCompleted({ loginId: 'l', success: true, error: null })).toEqual({ loginId: 'l', success: true });
    expect(parseLoginCompleted({ success: false, error: 'expired' })).toEqual({ success: false, error: 'expired' });
  });

  it('reads the CLI version from the initialize userAgent', () => {
    expect(parseUserAgentVersion('codex_cli_rs/0.157.1 (Debian 12; x86_64) oet_agent_console/0.1.0')).toBe('0.157.1');
    expect(parseUserAgentVersion(undefined)).toBeNull();
    expect(parseUserAgentVersion('no version here')).toBeNull();
  });
});

describe('commands', () => {
  it('tokenizes quotes and escapes like a POSIX shell', () => {
    expect(tokenizeShell(`git commit -m "fix: it's done" --author='A B'`)).toEqual(['git', 'commit', '-m', "fix: it's done", '--author=A B']);
    expect(tokenizeShell('echo a\\ b')).toEqual(['echo', 'a b']);
    expect(tokenizeShell('echo "unterminated')).toBeUndefined();
  });

  it('unwraps exactly one Codex shell wrapper for the Guard', () => {
    expect(normalizeCommand(['bash', '-lc', 'git status'])).toBe('git status');
    expect(normalizeCommand(`/bin/bash -lc 'git status && ls -la'`)).toBe('git status && ls -la');
    expect(normalizeCommand(['bash', '-lc', "bash -c 'rm -rf /tmp/x'"])).toBe("bash -c 'rm -rf /tmp/x'");
    expect(normalizeCommand(['ls', '-la', 'my dir'])).toBe("ls -la 'my dir'");
    expect(normalizeCommand('git log --oneline')).toBe('git log --oneline');
    expect(normalizeCommand('echo "unbalanced')).toBe('echo "unbalanced');
    expect(normalizeCommand(['bash', '-lc', 'a', 'extra'])).toBe('bash -lc a extra');
    expect(normalizeCommand(42)).toBeUndefined();
    expect(normalizeCommand('   ')).toBeUndefined();
  });
});

describe('approvals', () => {
  it('maps Guard decisions to v2 and legacy replies', () => {
    const v2 = 'item/commandExecution/requestApproval';
    expect(approvalResponse(v2, { behavior: 'allow' })).toEqual({ decision: 'accept' });
    expect(approvalResponse(v2, { behavior: 'deny', message: 'no' })).toEqual({ decision: 'decline' });
    expect(approvalResponse(v2, 'cancel')).toEqual({ decision: 'cancel' });
    expect(approvalResponse('item/fileChange/requestApproval', { behavior: 'allow', scope: 'session' } as never)).toEqual({ decision: 'acceptForSession' });
    expect(approvalResponse('execCommandApproval', { behavior: 'allow' })).toEqual({ decision: 'approved' });
    expect(approvalResponse('applyPatchApproval', { behavior: 'deny', message: 'no' })).toEqual({ decision: 'denied' });
    expect(approvalResponse('execCommandApproval', 'cancel')).toEqual({ decision: 'abort' });
  });

  it('maps change kinds in either wire form', () => {
    expect(mapChangeKind('add')).toBe('add');
    expect(mapChangeKind({ type: 'delete' })).toBe('delete');
    expect(mapChangeKind({ type: 'update', move_path: null })).toBe('modify');
    expect(mapChangeKind(undefined)).toBe('modify');
  });
});

describe('CodexTurnMapper', () => {
  const types = (events: EngineEvent[]): string[] => events.map((e) => e.type);

  it('maps agent messages and reasoning summaries without duplicates', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    expect(mapper.handle('item/agentMessage/delta', { threadId: 't', turnId: 'u', itemId: 'msg_1', delta: 'Hel' })).toEqual([
      { type: 'text_delta', data: { messageId: 'msg_1', text: 'Hel' } },
    ]);
    expect(mapper.handle('item/completed', { item: { type: 'agentMessage', id: 'msg_1', text: 'Hello' } })).toEqual([
      { type: 'text', data: { messageId: 'msg_1', text: 'Hello' } },
    ]);
    expect(mapper.handle('item/reasoning/summaryTextDelta', { itemId: 'rs_1', delta: 'Thinking' })).toEqual([
      { type: 'thinking_delta', data: { text: 'Thinking' } },
    ]);
    expect(mapper.handle('item/completed', { item: { type: 'reasoning', id: 'rs_1', summary: ['Thinking'] } })).toEqual([]);
    expect(mapper.handle('item/completed', { item: { type: 'reasoning', id: 'rs_2', summary: ['A', 'B'], content: [] } })).toEqual([
      { type: 'thinking_delta', data: { text: 'A\n\nB' } },
    ]);
    expect(mapper.handle('item/reasoning/textDelta', { itemId: 'rs_3', delta: 'raw chain of thought' })).toEqual([]);
  });

  it('maps a command execution lifecycle', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    const started = mapper.handle('item/started', {
      item: { type: 'commandExecution', id: 'cmd_1', command: "bash -lc 'git status'", cwd: '/workspace/sessions/S1', status: 'inProgress' },
    });
    expect(started).toEqual([
      {
        type: 'tool_call',
        data: { toolCallId: 'cmd_1', name: 'shell', input: { command: 'git status', cwd: '/workspace/sessions/S1' }, command: 'git status', cwd: '/workspace/sessions/S1' },
      },
    ]);
    expect(mapper.handle('item/commandExecution/outputDelta', { itemId: 'cmd_1', delta: 'On branch agent/x\n' })).toEqual([
      { type: 'tool_output_delta', data: { toolCallId: 'cmd_1', text: 'On branch agent/x\n' } },
    ]);
    expect(
      mapper.handle('item/completed', { item: { type: 'commandExecution', id: 'cmd_1', status: 'completed', exitCode: 0, aggregatedOutput: 'On branch agent/x\n' } }),
    ).toEqual([{ type: 'tool_result', data: { toolCallId: 'cmd_1', ok: true, output: 'On branch agent/x\n', exitCode: 0 } }]);
    const failed = mapper.handle('item/completed', { item: { type: 'commandExecution', id: 'cmd_2', command: 'false', status: 'failed', exitCode: 1 } });
    expect(types(failed)).toEqual(['tool_call', 'tool_result']);
    expect(failed[1]).toEqual({ type: 'tool_result', data: { toolCallId: 'cmd_2', ok: false, output: '', exitCode: 1 } });
  });

  it('maps file changes to tool_call, file_change and tool_result', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    const changes = [
      { path: 'app/page.tsx', kind: { type: 'update', move_path: null }, diff: '@@ -1 +1 @@\n-a\n+b\n' },
      { path: 'docs/new.md', kind: 'add', diff: '+hello\n' },
    ];
    expect(types(mapper.handle('item/started', { item: { type: 'fileChange', id: 'fc_1', changes, status: 'inProgress' } }))).toEqual(['tool_call']);
    const done = mapper.handle('item/completed', { item: { type: 'fileChange', id: 'fc_1', changes, status: 'completed' } });
    expect(done).toEqual([
      { type: 'file_change', data: { path: 'app/page.tsx', changeKind: 'modify', diff: '@@ -1 +1 @@\n-a\n+b\n' } },
      { type: 'file_change', data: { path: 'docs/new.md', changeKind: 'add', diff: '+hello\n' } },
      { type: 'tool_result', data: { toolCallId: 'fc_1', ok: true, output: 'modify app/page.tsx\nadd docs/new.md' } },
    ]);
    const declined = mapper.handle('item/completed', { item: { type: 'fileChange', id: 'fc_2', changes, status: 'declined' } });
    expect(types(declined)).toEqual(['tool_call', 'tool_result']);
    expect(declined[1]).toMatchObject({ data: { ok: false, output: 'Patch was declined.' } });
  });

  it('builds Guard requests from approvals, announcing the tool call once', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    const first = mapper.approvalToolCall('item/commandExecution/requestApproval', {
      threadId: 't',
      turnId: 'u',
      itemId: 'cmd_9',
      command: ['bash', '-lc', 'psql "$OET_AGENT_DATABASE_URL" -c "select 1"'],
      cwd: '/workspace/sessions/S1',
      reason: 'needs network',
    });
    expect(first.request).toEqual({
      toolCallId: 'cmd_9',
      name: 'shell',
      input: { command: 'psql "$OET_AGENT_DATABASE_URL" -c "select 1"', cwd: '/workspace/sessions/S1', reason: 'needs network' },
      command: 'psql "$OET_AGENT_DATABASE_URL" -c "select 1"',
      cwd: '/workspace/sessions/S1',
    });
    expect(types(first.events)).toEqual(['tool_call']);
    // item/started arriving after the approval does not announce the call again.
    expect(mapper.handle('item/started', { item: { type: 'commandExecution', id: 'cmd_9', command: 'x' } })).toEqual([]);

    mapper.handle('item/started', { item: { type: 'fileChange', id: 'fc_9', changes: [{ path: '.github/workflows/deploy.yml', kind: 'update', diff: '' }] } });
    const patch = mapper.approvalToolCall('item/fileChange/requestApproval', { threadId: 't', itemId: 'fc_9', reason: 'write' });
    expect(patch.events).toEqual([]);
    expect(patch.request).toMatchObject({ toolCallId: 'fc_9', name: 'apply_patch', writePaths: ['.github/workflows/deploy.yml'] });

    const legacy = mapper.approvalToolCall('applyPatchApproval', {
      conversationId: 't',
      callId: 'call_1',
      fileChanges: { 'src/a.ts': { update: { unified_diff: '@@', move_path: null } }, 'src/b.ts': { delete: {} } },
    });
    expect(legacy.request?.writePaths).toEqual(['src/a.ts', 'src/b.ts']);
    expect(mapper.approvalToolCall('item/commandExecution/requestApproval', { threadId: 't' }).request).toBeUndefined();
  });

  it('passes the approval kind, id and network context to the Guard', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    const network = mapper.approvalToolCall('item/commandExecution/requestApproval', {
      kind: 'command',
      threadId: 't',
      turnId: 'u',
      itemId: 'cmd_n',
      startedAtMs: 1,
      approvalId: 'ap_1',
      environmentId: null,
      command: 'curl -fsS https://example.org',
      cwd: '/workspace/sessions/S1',
      networkApprovalContext: { host: 'example.org', protocol: 'https' },
      commandActions: null,
    });
    expect(network.request?.input).toEqual({
      command: 'curl -fsS https://example.org',
      cwd: '/workspace/sessions/S1',
      approvalId: 'ap_1',
      network: { host: 'example.org', protocol: 'https' },
    });
    // stdin for a running process is invisible to the Guard: no command, so it fails closed.
    const stdin = mapper.approvalToolCall('item/commandExecution/requestApproval', { kind: 'writeStdin', threadId: 't', itemId: 'cmd_s', command: 'psql "$OET_AGENT_DATABASE_URL"' });
    expect(stdin.request?.input).toEqual({ kind: 'writeStdin', stdinTarget: 'psql "$OET_AGENT_DATABASE_URL"' });
    expect(stdin.request?.command).toBeUndefined();
    expect(classifyToolCall(stdin.request!).unparseable).toBe(true);
  });

  it('caps streamed tool output at 64 KB', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    const big = 'a'.repeat(TOOL_OUTPUT_CAP_BYTES - 10);
    expect(mapper.handle('item/commandExecution/outputDelta', { itemId: 'c', delta: big })).toHaveLength(1);
    const overflow = mapper.handle('item/commandExecution/outputDelta', { itemId: 'c', delta: 'b'.repeat(100) });
    expect(overflow).toHaveLength(1);
    expect((overflow[0] as { data: { text: string } }).data.text).toBe(`${'b'.repeat(10)}\n…[output truncated]`);
    expect(mapper.handle('item/commandExecution/outputDelta', { itemId: 'c', delta: 'more' })).toEqual([]);
  });

  it('computes per-turn usage from thread token totals', () => {
    const fresh = new CodexTurnMapper('gpt-x');
    expect(fresh.usageEvent()).toBeUndefined();
    fresh.handle('thread/tokenUsage/updated', {
      tokenUsage: {
        total: { inputTokens: 5_000, cachedInputTokens: 1_000, outputTokens: 400 },
        last: { inputTokens: 1_500, cachedInputTokens: 500, outputTokens: 100 },
      },
    });
    fresh.handle('thread/tokenUsage/updated', {
      tokenUsage: {
        total: { inputTokens: 7_000, cachedInputTokens: 1_800, outputTokens: 700 },
        last: { inputTokens: 2_000, cachedInputTokens: 800, outputTokens: 300 },
      },
    });
    expect(fresh.usageEvent()).toEqual({ type: 'usage', data: { model: 'gpt-x', inputTokens: 3_500, outputTokens: 400, cacheReadTokens: 1_300 } });
    expect(fresh.totals).toEqual({ inputTokens: 7_000, cachedInputTokens: 1_800, outputTokens: 700 });

    const continued = new CodexTurnMapper('gpt-x', { inputTokens: 7_000, cachedInputTokens: 1_800, outputTokens: 700 });
    continued.handle('thread/tokenUsage/updated', {
      tokenUsage: { total: { inputTokens: 9_000, cachedInputTokens: 2_000, outputTokens: 900 }, last: { inputTokens: 2_000, outputTokens: 200 } },
    });
    expect(continued.usageEvent()).toMatchObject({ data: { inputTokens: 2_000, outputTokens: 200, cacheReadTokens: 200 } });
  });

  it('records the final error of a turn instead of emitting a duplicate event', () => {
    const mapper = new CodexTurnMapper('gpt-x');
    expect(mapper.handle('error', { error: { message: 'retrying' }, willRetry: true })).toEqual([]);
    expect(mapper.lastError).toBeUndefined();
    expect(mapper.handle('error', { error: { message: 'Usage limit reached', codexErrorInfo: 'usageLimitExceeded' }, willRetry: false })).toEqual([]);
    expect(mapper.lastError).toEqual({ code: 'codex_usage_limit_exceeded', message: 'Usage limit reached' });
  });
});

describe('turn completion and routing helpers', () => {
  it('normalises turn/completed', () => {
    expect(parseTurnCompleted({ threadId: 't', turn: { id: 'u', status: 'completed' } })).toEqual({ turnId: 'u', status: 'ok' });
    expect(parseTurnCompleted({ turn: { id: 'u', status: 'interrupted' } })).toEqual({ turnId: 'u', status: 'interrupted' });
    expect(parseTurnCompleted({ turn: { id: 'u', status: 'failed', error: { message: 'context window exceeded', codexErrorInfo: 'contextWindowExceeded' } } })).toEqual({
      turnId: 'u',
      status: 'error',
      error: { code: 'codex_context_window_exceeded', message: 'context window exceeded' },
    });
    expect(parseTurnCompleted({ turn: { id: 'u', status: 'failed' } })).toEqual({ turnId: 'u', status: 'error' });
  });

  it('derives stable error codes from codexErrorInfo', () => {
    expect(codexErrorCode('unauthorized')).toBe('codex_unauthorized');
    expect(codexErrorCode({ httpConnectionFailed: { httpStatusCode: 502 } })).toBe('codex_http_connection_failed');
    expect(codexErrorCode(undefined)).toBe('codex_error');
  });

  it('extracts thread and turn ids', () => {
    expect(threadIdOf({ threadId: 'a' })).toBe('a');
    expect(threadIdOf({ conversationId: 'b' })).toBe('b');
    expect(threadIdOf({ thread: { id: 'c' } })).toBe('c');
    expect(threadIdOf({})).toBeUndefined();
    expect(turnIdOf({ turnId: 'x' })).toBe('x');
    expect(turnIdOf({ turn: { id: 'y' } })).toBe('y');
  });
});

describe('shared helpers', () => {
  it('caps UTF-8 output by bytes', () => {
    expect(capUtf8('short', 10)).toBe('short');
    expect(capUtf8('é'.repeat(10), 4)).toMatch(/^éé\n…\[truncated 16 bytes\]$/);
  });

  it('converts epoch seconds or milliseconds to ISO', () => {
    expect(epochToIso(1_790_000_000)).toBe(new Date(1_790_000_000_000).toISOString());
    expect(epochToIso(1_790_000_000_000)).toBe(new Date(1_790_000_000_000).toISOString());
    expect(epochToIso(0)).toBeUndefined();
    expect(epochToIso('soon')).toBeUndefined();
  });

  it('EngineError is an HttpError with contract statuses', () => {
    expect(new EngineError('engine_not_signed_in', 'x').status).toBe(409);
    expect(new EngineError('turn_in_progress', 'x').status).toBe(409);
    expect(new EngineError('engine_crashed', 'x').status).toBe(500);
    expect(new EngineError('engine_crashed', 'x').code).toBe('engine_crashed');
  });

  it('keeps only the first line of error messages', () => {
    expect(safeErrorMessage(new Error('first\nstack line'))).toBe('first');
    expect(safeErrorMessage('x'.repeat(400)).length).toBe(300);
    expect(safeErrorMessage({})).toBe('unknown error');
  });
});
