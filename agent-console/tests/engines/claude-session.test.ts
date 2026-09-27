import type { Query } from '@anthropic-ai/claude-agent-sdk';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AppConfig } from '../../src/config.js';
import { ASK_USER_DENIAL, CAN_USE_TOOL_DENIAL, createClaudeAdapter, type QueryFn } from '../../src/engines/claude.js';
import type { EngineAdapter, EngineEvent, EngineHooks, ToolCallRequest, ToolDecision } from '../../src/engines/types.js';
import type { RunResult } from '../../src/exec.js';

// A stand-in for the Agent SDK's Query: no CLI, no network. It consumes the streaming prompt
// and lets each test script the SDK messages (and hook calls) for every user turn.

type Script = (ctx: { text: string; turn: number; q: FakeQuery }) => Promise<void> | void;

interface QueryParams {
  prompt: AsyncIterable<{ message: { content: string } }>;
  options: Record<string, any>;
}

const MODELS = [
  { value: 'default', displayName: 'Default', description: 'Recommended', supportsEffort: true, supportedEffortLevels: ['low', 'medium', 'high', 'max'] },
  { value: 'haiku', displayName: 'Haiku', description: 'Fast', supportsEffort: false },
];
const ACCOUNT = { email: 'owner@example.test', organization: 'OET', subscriptionType: 'max', apiProvider: 'firstParty', apiKeySource: 'none' };

const tick = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

class FakeQuery {
  readonly userTexts: string[] = [];
  readonly hookResults: unknown[] = [];
  private readonly queue: unknown[] = [];
  private readonly waiters: ((r: IteratorResult<unknown>) => void)[] = [];
  closed = false;
  onInterrupt: (() => void) | undefined;
  readonly setModel = vi.fn(async (_model?: string) => undefined);
  readonly setPermissionMode = vi.fn(async (_mode: string) => undefined);
  readonly applyFlagSettings = vi.fn(async (_settings: Record<string, unknown>) => undefined);
  readonly interrupt = vi.fn(async () => {
    this.onInterrupt?.();
  });
  readonly supportedModels = vi.fn(async () => MODELS);
  readonly accountInfo = vi.fn(async () => ACCOUNT);
  readonly close = vi.fn(() => {
    this.closed = true;
    for (const waiter of this.waiters.splice(0)) waiter({ value: undefined, done: true });
  });

  constructor(
    readonly params: QueryParams,
    script: Script | undefined,
  ) {
    void (async () => {
      for await (const message of params.prompt) {
        const text = message.message.content;
        this.userTexts.push(text);
        await script?.({ text, turn: this.userTexts.length, q: this });
      }
    })();
  }

  get options(): Record<string, any> {
    return this.params.options;
  }

  /** The PreToolUse callback the adapter registered. */
  hook(input: Record<string, unknown>, toolUseId: string): Promise<any> {
    const matcher = this.options['hooks'].PreToolUse[0];
    return matcher.hooks[0](input, toolUseId, { signal: new AbortController().signal });
  }

  emit(message: unknown): void {
    const waiter = this.waiters.shift();
    if (waiter) waiter({ value: message, done: false });
    else this.queue.push(message);
  }

  next(): Promise<IteratorResult<unknown>> {
    if (this.queue.length > 0) return Promise.resolve({ value: this.queue.shift(), done: false });
    if (this.closed) return Promise.resolve({ value: undefined, done: true });
    return new Promise((resolve) => this.waiters.push(resolve));
  }

  [Symbol.asyncIterator](): FakeQuery {
    return this;
  }
}

const init = (sessionId = 'sess-1', apiKeySource = 'none') => ({ type: 'system', subtype: 'init', session_id: sessionId, apiKeySource, model: 'default' });
const resultMessage = (totalCostUsd: number, subtype = 'success', sessionId = 'sess-1') => ({
  type: 'result',
  subtype,
  is_error: subtype !== 'success',
  session_id: sessionId,
  total_cost_usd: totalCostUsd,
  result: subtype === 'success' ? 'Done.' : undefined,
  errors: subtype === 'success' ? [] : [`ended with ${subtype}`],
  usage: { input_tokens: 100, output_tokens: 20, cache_read_input_tokens: 50 },
});

function run(code: number, stdout: string, stderr = ''): RunResult {
  return { code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr, stdoutTruncated: false, timedOut: false };
}

const config = {
  asAgentPath: '/usr/local/bin/as-agent',
  agentHome: '/home/agent',
  claudeConfigDir: '/home/agent/.claude',
  idleCloseMs: 600_000,
} as unknown as AppConfig;

const sessionOptions = {
  sessionId: '01J8Z0000000000000000000S1',
  cwd: '/workspace/sessions/S1',
  model: 'default',
  effort: 'high',
  mode: 'guarded' as const,
  appendSystemPrompt: '# OET operating manual',
  env: { PATH: '/usr/bin', HOME: '/home/agent', ANTHROPIC_API_KEY: 'test-value-that-must-be-stripped', HTTPS_PROXY: 'http://s:x@oet-agent-egress:3128' },
};

function setup(script?: Script, authStatus: Record<string, unknown> = { loggedIn: true, authMethod: 'claude.ai', apiProvider: 'firstParty', email: 'owner@example.test', subscriptionType: 'max' }) {
  const queries: FakeQuery[] = [];
  const runner = vi.fn(async (_command: string, args: readonly string[]) => {
    const joined = args.join(' ');
    if (joined === '--version') return run(0, '2.1.283 (Claude Code)\n');
    if (joined === 'auth status') return run(authStatus['loggedIn'] ? 0 : 1, JSON.stringify(authStatus));
    if (joined === 'auth logout') return run(0, 'Successfully logged out\n');
    return run(1, '', `unexpected ${joined}`);
  });
  const queryFn = ((params: QueryParams) => {
    const q = new FakeQuery(params, script);
    queries.push(q);
    return q as unknown as Query;
  }) as unknown as QueryFn;
  const adapter = createClaudeAdapter(config, undefined, {
    baseEnv: () => ({ PATH: '/usr/bin', HOME: '/home/agent' }),
    queryFn,
    runner: runner as never,
    interruptGraceMs: 2_000,
    logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
  });
  const sessionQueries = (): FakeQuery[] => queries.filter((q) => q.options['persistSession'] !== false);
  return { adapter, queries, sessionQueries, runner };
}

function recordingHooks(decide: (req: ToolCallRequest) => ToolDecision = () => ({ behavior: 'allow' })) {
  const events: EngineEvent[] = [];
  const requests: ToolCallRequest[] = [];
  const hooks: EngineHooks = {
    emit: (event) => events.push(event),
    onToolCall: async (req) => {
      requests.push(req);
      return decide(req);
    },
  };
  return { hooks, events, requests };
}

let current: EngineAdapter | undefined;
afterEach(async () => {
  await current?.shutdown();
  current = undefined;
});

describe('claude adapter — status and startup assertion', () => {
  it('reports subscription auth, models with efforts and the CLI version', async () => {
    const { adapter, queries, runner } = setup();
    current = adapter;
    const status = await adapter.status();
    expect(status).toEqual({
      engine: 'claude',
      version: '2.1.283',
      auth: { state: 'signed_in', account: { plan: 'max', email: 'owner@example.test', workspace: 'OET' } },
      models: [
        { value: 'default', displayName: 'Default', description: 'Recommended', supportsEffort: true, efforts: ['low', 'medium', 'high', 'max'], defaultEffort: 'high' },
        { value: 'haiku', displayName: 'Haiku', description: 'Fast', supportsEffort: false, efforts: [] },
      ],
      rateLimits: null,
    });
    const probe = queries[0];
    expect(probe?.options).toMatchObject({
      pathToClaudeCodeExecutable: '/usr/local/bin/claude-as-agent',
      cwd: '/home/agent',
      settingSources: [],
      persistSession: false,
      permissionMode: 'dontAsk',
    });
    expect(probe?.close).toHaveBeenCalled();
    expect(runner).toHaveBeenCalledWith(
      '/usr/local/lib/oet-agent/claude',
      ['auth', 'status'],
      expect.objectContaining({ asAgent: true, env: expect.objectContaining({ CLAUDE_CONFIG_DIR: '/home/agent/.claude' }) }),
    );

    await adapter.status();
    expect(queries).toHaveLength(1); // models cached for 10 minutes
  });

  it('is signed_out (no models) when claude auth status says so, and refuses sessions', async () => {
    const { adapter, queries } = setup(undefined, { loggedIn: false, authMethod: 'none' });
    current = adapter;
    const status = await adapter.status();
    expect(status.auth).toEqual({ state: 'signed_out' });
    expect(status.models).toEqual([]);
    expect(queries).toHaveLength(0);
    await expect(adapter.openSession(sessionOptions)).rejects.toMatchObject({ code: 'engine_not_signed_in', status: 409 });
  });

  it('refuses a console/API-key login', async () => {
    const { adapter } = setup(undefined, { loggedIn: true, authMethod: 'console', apiProvider: 'firstParty' });
    current = adapter;
    const status = await adapter.status();
    expect(status.auth.state).toBe('error');
    expect(status.models).toEqual([]);
    await expect(adapter.openSession(sessionOptions)).rejects.toMatchObject({ code: 'engine_auth_error' });
  });
});

describe('claude adapter — turns', () => {
  const firstTurn: Script = async ({ q }) => {
    q.emit(init());
    q.emit({ type: 'stream_event', parent_tool_use_id: null, session_id: 'sess-1', event: { type: 'message_start', message: { id: 'msg_1' } } });
    q.emit({ type: 'stream_event', parent_tool_use_id: null, session_id: 'sess-1', event: { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'Checking.' } } });
    q.emit({ type: 'assistant', parent_tool_use_id: null, session_id: 'sess-1', message: { id: 'msg_1', content: [{ type: 'text', text: 'Checking.' }] } });
    await tick(); // let the adapter drain the text before the tool call starts
    const decision = await q.hook(
      { hook_event_name: 'PreToolUse', session_id: 'sess-1', transcript_path: '/t', cwd: '/workspace/sessions/S1', tool_name: 'Bash', tool_input: { command: 'git status' } },
      'toolu_1',
    );
    q.hookResults.push(decision);
    q.emit({
      type: 'assistant',
      parent_tool_use_id: null,
      session_id: 'sess-1',
      message: { id: 'msg_1', content: [{ type: 'tool_use', id: 'toolu_1', name: 'Bash', input: { command: 'git status' } }] },
    });
    q.emit({ type: 'user', session_id: 'sess-1', message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 'toolu_1', content: 'clean', is_error: false }] } });
    q.emit({ type: 'rate_limit_event', session_id: 'sess-1', rate_limit_info: { status: 'allowed', rateLimitType: 'five_hour', resetsAt: 1_790_000_000 } });
    q.emit(resultMessage(0.12));
  };

  it('opens a hardened streaming query and runs a turn through the Guard hook', async () => {
    const { adapter, sessionQueries } = setup(firstTurn);
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks, events, requests } = recordingHooks();
    const result = await session.runTurn('What is the git status?', { model: 'default', effort: 'high', mode: 'guarded' }, hooks, new AbortController().signal);

    expect(result).toEqual({ status: 'ok', resumeId: 'sess-1' });
    const q = sessionQueries()[0];
    expect(q).toBeDefined();
    const options = q!.options;
    expect(options).toMatchObject({
      pathToClaudeCodeExecutable: '/usr/local/bin/claude-as-agent',
      cwd: '/workspace/sessions/S1',
      settingSources: [],
      systemPrompt: { type: 'preset', preset: 'claude_code', append: '# OET operating manual' },
      includePartialMessages: true,
      thinking: { type: 'adaptive', display: 'summarized' },
      model: 'default',
      effort: 'high',
      permissionMode: 'default',
      disallowedTools: ['AskUserQuestion'],
      maxTurns: 200,
    });
    expect(options['resume']).toBeUndefined();
    expect(options['allowedTools']).toBeUndefined();
    expect(options['permissionMode']).not.toMatch(/bypassPermissions|acceptEdits/);
    expect(options['env']).toMatchObject({ CLAUDE_CONFIG_DIR: '/home/agent/.claude', CLAUDE_AGENT_SDK_CLIENT_APP: 'oet-agent-console', HTTPS_PROXY: 'http://s:x@oet-agent-egress:3128' });
    expect(options['env']).not.toHaveProperty('ANTHROPIC_API_KEY');
    expect(options['hooks'].PreToolUse).toHaveLength(1);
    expect(options['hooks'].PreToolUse[0].matcher).toBeUndefined();
    expect(options['hooks'].PreToolUse[0].timeout).toBe(900);
    expect(q!.userTexts).toEqual(['What is the git status?']);

    expect(requests).toEqual([
      { toolCallId: 'toolu_1', name: 'Bash', input: { command: 'git status' }, command: 'git status', cwd: '/workspace/sessions/S1' },
    ]);
    expect(q!.hookResults).toEqual([
      { hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'allow', permissionDecisionReason: 'Approved by the OET console guard.' } },
    ]);
    expect(events.filter((e) => e.type === 'tool_call')).toHaveLength(1);
    expect(events.map((e) => e.type)).toEqual(['text_delta', 'text', 'tool_call', 'tool_result', 'rate_limit', 'usage']);
    expect(events.at(-1)).toEqual({ type: 'usage', data: { model: 'default', inputTokens: 100, outputTokens: 20, cacheReadTokens: 50, costUsd: 0.12 } });
    expect((await adapter.status()).rateLimits).toEqual([{ label: '5-hour window', status: 'ok', resetsAt: new Date(1_790_000_000_000).toISOString() }]);
  });

  it('turns a Guard denial into a PreToolUse deny with the reason', async () => {
    const script: Script = async ({ q }) => {
      q.emit(init());
      const decision = await q.hook({ hook_event_name: 'PreToolUse', cwd: '/w', tool_name: 'Bash', tool_input: { command: 'psql -c "DROP TABLE x"' } }, 'toolu_9');
      q.hookResults.push(decision);
      q.emit(resultMessage(0.01));
    };
    const { adapter, sessionQueries } = setup(script);
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks } = recordingHooks(() => ({ behavior: 'deny', message: 'The owner denied this destructive command.' }));
    await session.runTurn('drop it', { model: 'default', mode: 'guarded' }, hooks, new AbortController().signal);
    expect(sessionQueries()[0]!.hookResults).toEqual([
      { hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'deny', permissionDecisionReason: 'The owner denied this destructive command.' } },
    ]);
  });

  it('denies whenever the Guard throws', async () => {
    const script: Script = async ({ q }) => {
      q.emit(init());
      q.hookResults.push(await q.hook({ tool_name: 'Write', tool_input: { file_path: '/w/a' } }, 'toolu_x'));
      q.emit(resultMessage(0.01));
    };
    const { adapter, sessionQueries } = setup(script);
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const hooks: EngineHooks = { emit: () => undefined, onToolCall: async () => Promise.reject(new Error('guard exploded')) };
    await session.runTurn('x', { model: 'default', mode: 'autopilot' }, hooks, new AbortController().signal);
    expect(sessionQueries()[0]!.hookResults?.[0]).toMatchObject({
      hookSpecificOutput: { permissionDecision: 'deny' },
    });
  });

  it('never lets canUseTool allow anything', async () => {
    const { adapter, sessionQueries } = setup(async ({ q }) => {
      q.emit(init());
      q.emit(resultMessage(0));
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    await session.runTurn('x', { model: 'default', mode: 'autopilot' }, recordingHooks().hooks, new AbortController().signal);
    const canUseTool = sessionQueries()[0]!.options['canUseTool'];
    const ctx = { signal: new AbortController().signal, suggestions: [], toolUseID: 'toolu_p' };
    await expect(canUseTool('Edit', { file_path: '/w/.claude/settings.json' }, ctx)).resolves.toEqual({ behavior: 'deny', message: CAN_USE_TOOL_DENIAL });
    await expect(canUseTool('AskUserQuestion', { questions: [] }, ctx)).resolves.toEqual({ behavior: 'deny', message: ASK_USER_DENIAL });
  });

  it('switches model, effort and mode between turns and reports per-turn cost', async () => {
    const script: Script = async ({ q, turn }) => {
      if (turn === 1) q.emit(init());
      q.emit(resultMessage(turn === 1 ? 0.12 : 0.2));
    };
    const { adapter, sessionQueries } = setup(script);
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const first = recordingHooks();
    await session.runTurn('one', { model: 'default', effort: 'high', mode: 'guarded' }, first.hooks, new AbortController().signal);
    const second = recordingHooks();
    const result = await session.runTurn('two', { model: 'haiku', effort: 'max', mode: 'read_only' }, second.hooks, new AbortController().signal);

    expect(result.status).toBe('ok');
    expect(sessionQueries()).toHaveLength(1);
    const q = sessionQueries()[0]!;
    expect(q.setModel).toHaveBeenCalledWith('haiku');
    expect(q.applyFlagSettings).toHaveBeenCalledWith({ effortLevel: 'max' });
    expect(q.setPermissionMode).toHaveBeenCalledWith('dontAsk');
    expect(first.events.at(-1)).toMatchObject({ type: 'usage', data: { costUsd: 0.12 } });
    expect(second.events.at(-1)).toEqual({ type: 'usage', data: { model: 'haiku', inputTokens: 100, outputTokens: 20, cacheReadTokens: 50, costUsd: 0.08 } });
  });

  it('opens read_only sessions in dontAsk mode with read-only tools', async () => {
    const { adapter, sessionQueries } = setup(async ({ q }) => {
      q.emit(init());
      q.emit(resultMessage(0));
    });
    current = adapter;
    const session = await adapter.openSession({ ...sessionOptions, mode: 'read_only' });
    await session.runTurn('x', { model: 'default', mode: 'read_only' }, recordingHooks().hooks, new AbortController().signal);
    expect(sessionQueries()[0]!.options).toMatchObject({ permissionMode: 'dontAsk', allowedTools: ['Read', 'Glob', 'Grep'] });
  });

  it('resumes by session_id after the session layer closes an idle session', async () => {
    let calls = 0;
    const script: Script = async ({ q }) => {
      calls += 1;
      q.emit(init('sess-1'));
      q.emit(resultMessage(calls === 1 ? 0.5 : 0.65));
    };
    const { adapter, sessionQueries } = setup(script);
    current = adapter;
    const first = await adapter.openSession(sessionOptions);
    const one = recordingHooks();
    const r1 = await first.runTurn('one', { model: 'default', mode: 'guarded' }, one.hooks, new AbortController().signal);
    await first.close();
    expect(sessionQueries()[0]!.close).toHaveBeenCalled();

    const reopened = await adapter.openSession({ ...sessionOptions, resumeId: r1.resumeId });
    const two = recordingHooks();
    await reopened.runTurn('two', { model: 'default', mode: 'guarded' }, two.hooks, new AbortController().signal);
    expect(sessionQueries()).toHaveLength(2);
    expect(sessionQueries()[1]!.options['resume']).toBe('sess-1');
    expect(two.events.at(-1)).toMatchObject({ type: 'usage', data: { costUsd: 0.15 } });
  });

  it('reports unknown cost for the first turn of a session resumed after a restart', async () => {
    const { adapter } = setup(async ({ q }) => {
      q.emit(init('sess-old'));
      q.emit(resultMessage(3, 'success', 'sess-old'));
    });
    current = adapter;
    const session = await adapter.openSession({ ...sessionOptions, resumeId: 'sess-old' });
    const { hooks, events } = recordingHooks();
    const result = await session.runTurn('continue', { model: 'default', mode: 'guarded' }, hooks, new AbortController().signal);
    expect(result).toEqual({ status: 'ok', resumeId: 'sess-old' });
    expect(events.at(-1)).toEqual({ type: 'usage', data: { model: 'default', inputTokens: 100, outputTokens: 20, cacheReadTokens: 50 } });
  });

  it('refuses to run on an API key detected at session start', async () => {
    const { adapter } = setup(async ({ q }) => {
      q.emit(init('sess-1', 'ANTHROPIC_API_KEY'));
      q.emit(resultMessage(0));
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const result = await session.runTurn('x', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(result.status).toBe('error');
    expect(result.error?.code).toBe('engine_auth_error');
    const status = await adapter.status();
    expect(status.auth.state).toBe('error');
    expect(status.auth.detail).toMatch(/API key/);
  });

  it('re-reads claude auth status after an auth-looking turn error without latching an error state', async () => {
    const { adapter, runner } = setup(async ({ q }) => {
      q.emit(init());
      q.emit({ ...resultMessage(0, 'error_during_execution'), errors: ['OAuth token has expired. Please run /login.'] });
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const statusCalls = (): number => runner.mock.calls.filter((call) => (call[1] as readonly string[]).join(' ') === 'auth status').length;
    const before = statusCalls();
    const result = await session.runTurn('x', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(result).toMatchObject({ status: 'error', error: { message: expect.stringMatching(/OAuth token has expired/) } });
    const status = await adapter.status();
    expect(statusCalls()).toBe(before + 1);
    expect(status.auth.state).toBe('signed_in');
  });

  it('interrupts through query.interrupt()', async () => {
    let fake: FakeQuery | undefined;
    const { adapter } = setup(async ({ q }) => {
      fake = q;
      q.onInterrupt = () => q.emit(resultMessage(0.05, 'error_during_execution'));
      q.emit(init());
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const controller = new AbortController();
    const running = session.runTurn('long job', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, controller.signal);
    await vi.waitFor(() => expect(fake?.userTexts).toEqual(['long job']));
    controller.abort();
    await expect(running).resolves.toEqual({ status: 'interrupted', resumeId: 'sess-1' });
    expect(fake?.interrupt).toHaveBeenCalledTimes(1);
  });

  it('maps error_max_turns (and reopens the query by resume) and an unexpected CLI exit', async () => {
    let userTurns = 0;
    const { adapter, sessionQueries } = setup(async ({ q }) => {
      userTurns += 1;
      if (userTurns === 1) {
        q.emit(init());
        q.emit(resultMessage(0.1, 'error_max_turns'));
      } else if (userTurns === 2) {
        q.emit(init());
        q.emit(resultMessage(0.15));
      } else {
        q.close();
      }
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const maxTurns = await session.runTurn('a', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(maxTurns).toEqual({ status: 'max_turns', resumeId: 'sess-1' });
    expect(sessionQueries()[0]!.close).toHaveBeenCalled();
    const next = await session.runTurn('b', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(next).toEqual({ status: 'ok', resumeId: 'sess-1' });
    expect(sessionQueries()).toHaveLength(2);
    expect(sessionQueries()[1]!.options['resume']).toBe('sess-1');
    const exited = await session.runTurn('c', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(exited).toMatchObject({ status: 'error', error: { code: 'engine_exited' } });
  });

  it('rejects a concurrent turn on the same session', async () => {
    let fake: FakeQuery | undefined;
    const { adapter } = setup(async ({ q }) => {
      fake = q;
      q.onInterrupt = () => q.emit(resultMessage(0, 'error_during_execution'));
      q.emit(init());
    });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const controller = new AbortController();
    const first = session.runTurn('a', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, controller.signal);
    await expect(session.runTurn('b', { model: 'default', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal)).rejects.toMatchObject({
      code: 'turn_in_progress',
    });
    await vi.waitFor(() => expect(fake?.userTexts).toEqual(['a']));
    controller.abort();
    await expect(first).resolves.toMatchObject({ status: 'interrupted' });
  });
});
