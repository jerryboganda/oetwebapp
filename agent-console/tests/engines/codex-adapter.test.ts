import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AppConfig } from '../../src/config.js';
import {
  checkCodexPolicyFiles,
  type CodexChild,
  type CodexEngineAdapter,
  createCodexAdapter,
  type PolicyFs,
  type PolicyStat,
} from '../../src/engines/codex.js';
import type { EngineEvent, EngineHooks, ToolCallRequest, ToolDecision } from '../../src/engines/types.js';

// A scripted stand-in for `codex app-server`: JSONL over stdio, no network, no binary.

type Message = Record<string, unknown>;
type Script = (message: Message, server: FakeAppServer) => void;

class FakeAppServer extends EventEmitter {
  readonly stdin = new PassThrough();
  readonly stdout = new PassThrough();
  readonly stderr = new PassThrough();
  readonly received: Message[] = [];
  exited = false;

  constructor(private readonly script: Script) {
    super();
    let buffer = '';
    this.stdin.on('data', (chunk: Buffer) => {
      buffer += chunk.toString('utf8');
      let newline = buffer.indexOf('\n');
      while (newline !== -1) {
        const line = buffer.slice(0, newline);
        buffer = buffer.slice(newline + 1);
        if (line.trim()) {
          const message = JSON.parse(line) as Message;
          this.received.push(message);
          setImmediate(() => this.script(message, this));
        }
        newline = buffer.indexOf('\n');
      }
    });
  }

  send(message: Message): void {
    if (!this.exited) this.stdout.write(`${JSON.stringify(message)}\n`);
  }

  reply(request: Message, result: unknown): void {
    this.send({ id: request['id'], result });
  }

  crash(code = 101): void {
    this.exited = true;
    this.stderr.write('thread panicked at codex-core\n');
    this.emit('exit', code, null);
  }

  kill(signal?: NodeJS.Signals): boolean {
    if (!this.exited) {
      this.exited = true;
      setImmediate(() => this.emit('exit', null, signal ?? 'SIGTERM'));
    }
    return true;
  }

  requests(method: string): Message[] {
    return this.received.filter((m) => m['method'] === method);
  }
}

const THREAD = 'thr_test_1';
const TURN = 'turn_test_1';

interface ServerState {
  signedIn: boolean;
  approvalReplies: Message[];
}

/** Default behaviour: signed in, one model, a turn that runs one approved command. */
function defaultScript(state: ServerState): Script {
  return (msg, server) => {
    const method = msg['method'];
    if (method === undefined && msg['id'] === 'approval-1') {
      state.approvalReplies.push(msg);
      const decision = (msg['result'] as { decision?: string } | undefined)?.decision;
      const accepted = decision === 'accept';
      server.send({
        method: 'item/completed',
        params: {
          threadId: THREAD,
          turnId: TURN,
          item: {
            type: 'commandExecution',
            id: 'item_cmd',
            status: accepted ? 'completed' : 'declined',
            ...(accepted ? { exitCode: 0, aggregatedOutput: 'On branch agent/test\n' } : {}),
          },
        },
      });
      server.send({ method: 'item/agentMessage/delta', params: { threadId: THREAD, turnId: TURN, itemId: 'item_msg', delta: 'Done' } });
      server.send({ method: 'item/completed', params: { threadId: THREAD, turnId: TURN, item: { type: 'agentMessage', id: 'item_msg', text: 'Done.' } } });
      server.send({
        method: 'thread/tokenUsage/updated',
        params: {
          threadId: THREAD,
          turnId: TURN,
          tokenUsage: {
            total: { inputTokens: 1_200, cachedInputTokens: 200, outputTokens: 300 },
            last: { inputTokens: 1_200, cachedInputTokens: 200, outputTokens: 300 },
          },
        },
      });
      server.send({ method: 'turn/completed', params: { threadId: THREAD, turn: { id: TURN, status: 'completed', items: [] } } });
      return;
    }
    switch (method) {
      case 'initialize':
        server.reply(msg, { userAgent: 'codex_cli_rs/0.157.1 (Debian 12; x86_64) oet_agent_console/test' });
        return;
      case 'account/read':
        server.reply(msg, {
          account: state.signedIn ? { type: 'chatgpt', email: 'owner@example.test', planType: 'business' } : null,
          requiresOpenaiAuth: true,
        });
        return;
      case 'model/list':
        server.reply(msg, {
          data: [
            {
              id: 'model-one',
              model: 'model-one',
              displayName: 'Model One',
              supportedReasoningEfforts: [{ reasoningEffort: 'medium' }, { reasoningEffort: 'high' }],
              defaultReasoningEffort: 'medium',
            },
          ],
          nextCursor: null,
        });
        return;
      case 'account/rateLimits/read':
        server.reply(msg, { rateLimits: { primary: { usedPercent: 12, windowDurationMins: 300 } } });
        return;
      case 'thread/start':
      case 'thread/resume':
        server.reply(msg, { thread: { id: THREAD } });
        return;
      case 'turn/start':
        server.reply(msg, { turn: { id: TURN, status: 'inProgress', items: [] } });
        server.send({ method: 'turn/started', params: { threadId: THREAD, turn: { id: TURN } } });
        server.send({
          method: 'item/started',
          params: {
            threadId: THREAD,
            turnId: TURN,
            item: { type: 'commandExecution', id: 'item_cmd', command: "bash -lc 'git status'", cwd: '/workspace/sessions/S1', status: 'inProgress' },
          },
        });
        server.send({
          id: 'approval-1',
          method: 'item/commandExecution/requestApproval',
          params: { threadId: THREAD, turnId: TURN, itemId: 'item_cmd', command: "bash -lc 'git status'", cwd: '/workspace/sessions/S1' },
        });
        return;
      case 'turn/interrupt':
        server.reply(msg, {});
        server.send({ method: 'turn/completed', params: { threadId: THREAD, turn: { id: TURN, status: 'interrupted', items: [] } } });
        return;
      default:
        if (typeof method === 'string' && msg['id'] !== undefined) server.reply(msg, {});
    }
  };
}

const config = {
  asAgentPath: '/usr/local/bin/as-agent',
  agentHome: '/home/agent',
  codexHome: '/home/agent/.codex',
  version: '0.1.0-test',
} as unknown as AppConfig;

const quietLogger = { info: vi.fn(), warn: vi.fn(), error: vi.fn() };

function setup(overrides: { script?: Script; signedIn?: boolean } = {}) {
  const state: ServerState = { signedIn: overrides.signedIn ?? true, approvalReplies: [] };
  const servers: FakeAppServer[] = [];
  const spawns: { file: string; args: string[]; env: Record<string, string>; cwd: string }[] = [];
  const adapter = createCodexAdapter(config, undefined, {
    baseEnv: () => ({ PATH: '/usr/bin', HTTPS_PROXY: 'http://oet-agent-egress:3128', OPENAI_API_KEY: 'test-value-that-must-be-stripped' }),
    spawn: (file, args, options) => {
      spawns.push({ file, args, env: options.env, cwd: options.cwd });
      const server = new FakeAppServer(overrides.script ?? defaultScript(state));
      servers.push(server);
      return server as unknown as CodexChild;
    },
    verifyPolicyFiles: false,
    restartInitialMs: 5,
    restartMaxMs: 20,
    interruptGraceMs: 2_000,
    logger: quietLogger,
  });
  return { adapter, servers, spawns, state };
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

const sessionOptions = {
  sessionId: '01J8Z0000000000000000000S1',
  cwd: '/workspace/sessions/S1',
  model: 'model-one',
  mode: 'guarded' as const,
  appendSystemPrompt: '# manual',
  env: { PATH: '/usr/bin' },
};

let current: CodexEngineAdapter | undefined;

afterEach(async () => {
  await current?.shutdown();
  current = undefined;
  vi.clearAllMocks();
});

describe('codex adapter — app-server lifecycle and status', () => {
  it('spawns one app-server as the agent with ChatGPT-only login and a scrubbed env', async () => {
    const { adapter, spawns, servers } = setup();
    current = adapter;
    const status = await adapter.status();
    expect(spawns).toHaveLength(1);
    expect(spawns[0]).toMatchObject({
      file: '/usr/local/bin/as-agent',
      args: ['/usr/local/lib/oet-agent/codex', '-c', 'forced_login_method="chatgpt"', 'app-server'],
      cwd: '/home/agent',
    });
    expect(spawns[0]?.env).toMatchObject({ CODEX_HOME: '/home/agent/.codex', HTTPS_PROXY: 'http://oet-agent-egress:3128', HOME: '/home/agent' });
    expect(spawns[0]?.env).not.toHaveProperty('OPENAI_API_KEY');

    const server = servers[0];
    expect(server?.requests('initialize')[0]?.['params']).toEqual({
      clientInfo: { name: 'oet_agent_console', title: 'OET Owner Agent Console', version: '0.1.0-test' },
    });
    expect(server?.requests('initialized')).toHaveLength(1);
    expect(server?.received.some((m) => m['method'] === 'account/login/start' && (m['params'] as Message)['type'] === 'apiKey')).toBe(false);

    expect(status).toEqual({
      engine: 'codex',
      version: '0.157.1',
      auth: { state: 'signed_in', account: { email: 'owner@example.test', plan: 'business' } },
      models: [{ value: 'model-one', displayName: 'Model One', supportsEffort: true, efforts: ['medium', 'high'], defaultEffort: 'medium' }],
      rateLimits: [{ label: '5-hour window', status: 'ok', usedPercent: 12 }],
    });

    await adapter.status();
    expect(spawns).toHaveLength(1);
  });

  it('reports signed_out with no models and refuses to open a session', async () => {
    const { adapter } = setup({ signedIn: false });
    current = adapter;
    const status = await adapter.status();
    expect(status.auth.state).toBe('signed_out');
    expect(status.models).toEqual([]);
    await expect(adapter.openSession(sessionOptions)).rejects.toMatchObject({ code: 'engine_not_signed_in', status: 409 });
  });
});

describe('codex adapter — turns', () => {
  it('runs a turn: thread/start, turn/start, Guard approval, events and usage', async () => {
    const { adapter, servers, state } = setup();
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks, events, requests } = recordingHooks();
    const result = await session.runTurn('check git status', { model: 'model-one', effort: 'high', mode: 'guarded' }, hooks, new AbortController().signal);

    expect(result).toEqual({ status: 'ok', resumeId: THREAD });
    const server = servers[0];
    expect(server?.requests('thread/start')[0]?.['params']).toEqual({
      model: 'model-one',
      cwd: '/workspace/sessions/S1',
      approvalPolicy: 'untrusted',
      sandbox: 'danger-full-access',
      developerInstructions: '# manual',
    });
    expect(server?.requests('turn/start')[0]?.['params']).toEqual({
      threadId: THREAD,
      input: [{ type: 'text', text: 'check git status' }],
      cwd: '/workspace/sessions/S1',
      model: 'model-one',
      effort: 'high',
      summary: 'auto',
      approvalPolicy: 'untrusted',
      sandboxPolicy: { type: 'dangerFullAccess' },
    });

    expect(requests).toEqual([
      {
        toolCallId: 'item_cmd',
        name: 'shell',
        input: { command: 'git status', cwd: '/workspace/sessions/S1' },
        command: 'git status',
        cwd: '/workspace/sessions/S1',
      },
    ]);
    expect(state.approvalReplies).toEqual([{ id: 'approval-1', result: { decision: 'accept' } }]);

    expect(events.map((e) => e.type)).toEqual(['tool_call', 'tool_result', 'text_delta', 'text', 'usage']);
    expect(events.filter((e) => e.type === 'tool_call')).toHaveLength(1);
    expect(events.at(-1)).toEqual({ type: 'usage', data: { model: 'model-one', inputTokens: 1_200, outputTokens: 300, cacheReadTokens: 200 } });
  });

  it('declines the approval when the Guard denies', async () => {
    const { adapter, state } = setup();
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks, events } = recordingHooks(() => ({ behavior: 'deny', message: 'read_only mode' }));
    const result = await session.runTurn('x', { model: 'model-one', mode: 'read_only' }, hooks, new AbortController().signal);
    expect(result.status).toBe('ok');
    expect(state.approvalReplies[0]).toEqual({ id: 'approval-1', result: { decision: 'decline' } });
    expect(events.find((e) => e.type === 'tool_result')).toMatchObject({ data: { toolCallId: 'item_cmd', ok: false } });
  });

  it('interrupts a running turn with turn/interrupt', async () => {
    const state: ServerState = { signedIn: true, approvalReplies: [] };
    const base = defaultScript(state);
    // Hold the approval so the turn is still running when we abort.
    const script: Script = (msg, server) => {
      if (msg['method'] === 'turn/start') {
        server.reply(msg, { turn: { id: TURN, status: 'inProgress', items: [] } });
        return;
      }
      base(msg, server);
    };
    const { adapter, servers } = setup({ script });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const controller = new AbortController();
    const { hooks } = recordingHooks();
    const running = session.runTurn('long task', { model: 'model-one', mode: 'guarded' }, hooks, controller.signal);
    await vi.waitFor(() => expect(servers[0]?.requests('turn/start')).toHaveLength(1));
    await new Promise((resolve) => setTimeout(resolve, 20));
    controller.abort();
    await expect(running).resolves.toEqual({ status: 'interrupted', resumeId: THREAD });
    expect(servers[0]?.requests('turn/interrupt')[0]?.['params']).toEqual({ threadId: THREAD, turnId: TURN });
  });

  it('fails the in-flight turn when the app-server crashes, restarts it and resumes the thread', async () => {
    const state: ServerState = { signedIn: true, approvalReplies: [] };
    const base = defaultScript(state);
    let crashNext = true;
    const script: Script = (msg, server) => {
      if (msg['method'] === 'turn/start' && crashNext) {
        crashNext = false;
        server.reply(msg, { turn: { id: 'turn_crash', status: 'inProgress', items: [] } });
        setImmediate(() => server.crash());
        return;
      }
      base(msg, server);
    };
    const { adapter, servers, spawns } = setup({ script });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks } = recordingHooks();
    const crashed = await session.runTurn('first', { model: 'model-one', mode: 'guarded' }, hooks, new AbortController().signal);
    expect(crashed.status).toBe('error');
    expect(crashed.error?.code).toBe('engine_crashed');
    expect(crashed.error?.message).toMatch(/exited \(101\)/);
    expect(crashed.resumeId).toBe(THREAD);

    await vi.waitFor(() => expect(spawns).toHaveLength(2));
    const retried = await session.runTurn('second', { model: 'model-one', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal);
    expect(retried).toEqual({ status: 'ok', resumeId: THREAD });
    expect(servers[1]?.requests('thread/resume')[0]?.['params']).toMatchObject({
      threadId: THREAD,
      approvalPolicy: 'untrusted',
      sandbox: 'danger-full-access',
      developerInstructions: '# manual',
    });
    expect(servers[1]?.requests('thread/start')).toHaveLength(0);
  });

  it('rejects a second concurrent turn on the same session', async () => {
    const state: ServerState = { signedIn: true, approvalReplies: [] };
    const base = defaultScript(state);
    const script: Script = (msg, server) => {
      if (msg['method'] === 'turn/start') {
        server.reply(msg, { turn: { id: TURN, status: 'inProgress', items: [] } });
        return;
      }
      base(msg, server);
    };
    const { adapter } = setup({ script });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const controller = new AbortController();
    const first = session.runTurn('a', { model: 'model-one', mode: 'guarded' }, recordingHooks().hooks, controller.signal);
    await expect(session.runTurn('b', { model: 'model-one', mode: 'guarded' }, recordingHooks().hooks, new AbortController().signal)).rejects.toMatchObject({
      code: 'turn_in_progress',
    });
    controller.abort();
    await first;
  });
});

describe('codex adapter — rate limits and unsupported server requests', () => {
  it('pushes account/rateLimits/updated into running turns and status', async () => {
    const state: ServerState = { signedIn: true, approvalReplies: [] };
    const base = defaultScript(state);
    const script: Script = (msg, server) => {
      if (msg['method'] === 'turn/start') {
        server.reply(msg, { turn: { id: TURN, status: 'inProgress', items: [] } });
        server.send({ method: 'account/rateLimits/updated', params: { rateLimits: { primary: { usedPercent: 91, windowDurationMins: 300 } } } });
        server.send({ id: 'odd-1', method: 'item/tool/requestUserInput', params: { threadId: THREAD } });
        server.send({ method: 'turn/completed', params: { threadId: THREAD, turn: { id: TURN, status: 'completed', items: [] } } });
        return;
      }
      base(msg, server);
    };
    const { adapter, servers } = setup({ script });
    current = adapter;
    const session = await adapter.openSession(sessionOptions);
    const { hooks, events } = recordingHooks();
    await session.runTurn('x', { model: 'model-one', mode: 'guarded' }, hooks, new AbortController().signal);
    expect(events).toContainEqual({ type: 'rate_limit', data: { engine: 'codex', limits: [{ label: '5-hour window', status: 'warning', usedPercent: 91 }] } });
    await vi.waitFor(() => expect(servers[0]?.received.find((m) => m['id'] === 'odd-1')).toMatchObject({ error: { code: -32601 } }));
    expect((await adapter.status()).rateLimits).toEqual([{ label: '5-hour window', status: 'warning', usedPercent: 91 }]);
  });
});

describe('codex policy-file verification', () => {
  const home = '/home/agent/.codex';
  function stat(kind: 'dir' | 'file' | 'link', uid: number, mode: number): PolicyStat {
    return { uid, mode, isDirectory: () => kind === 'dir', isFile: () => kind === 'file', isSymbolicLink: () => kind === 'link' };
  }
  function fakeFs(tree: Record<string, PolicyStat>, rulesEntries: string[]): PolicyFs {
    return {
      lstat: async (p) => {
        const s = tree[p];
        if (!s) throw Object.assign(new Error('ENOENT'), { code: 'ENOENT' });
        return s;
      },
      readdir: async () => rulesEntries,
    };
  }
  const good: Record<string, PolicyStat> = {
    [home]: stat('dir', 0, 0o41770),
    [`${home}/config.toml`]: stat('file', 0, 0o100644),
    [`${home}/AGENTS.md`]: stat('file', 0, 0o100444),
    [`${home}/AGENTS.override.md`]: stat('file', 0, 0o100444),
    [`${home}/skills`]: stat('dir', 0, 0o40755),
    [`${home}/prompts`]: stat('dir', 0, 0o40755),
    [`${home}/rules`]: stat('dir', 0, 0o40755),
    [`${home}/rules/oet.rules`]: stat('file', 0, 0o100444),
  };

  it('accepts the layout written by bin/entrypoint.sh', async () => {
    await expect(checkCodexPolicyFiles(home, fakeFs(good, ['oet.rules']))).resolves.toEqual([]);
  });

  it('flags tampering', async () => {
    const tampered = {
      ...good,
      [home]: stat('dir', 0, 0o40770),
      [`${home}/config.toml`]: stat('file', 10002, 0o100600),
      [`${home}/AGENTS.override.md`]: stat('link', 10002, 0o120777),
      [`${home}/rules/oet.rules`]: stat('file', 0, 0o100666),
    };
    delete (tampered as Record<string, PolicyStat>)[`${home}/skills`];
    const problems = await checkCodexPolicyFiles(home, fakeFs(tampered, ['oet.rules', 'default.rules']));
    expect(problems).toEqual(
      expect.arrayContaining([
        'CODEX_HOME is group-writable without the sticky bit',
        'config.toml is not owned by root',
        'AGENTS.override.md is a symlink',
        'skills/ is missing',
        'unexpected rules/default.rules',
        'rules/oet.rules is writable by non-root',
      ]),
    );
  });

  it('refuses to start the app-server when verification fails', async () => {
    const spawn = vi.fn();
    const adapter = createCodexAdapter(config, undefined, {
      baseEnv: () => ({}),
      spawn,
      policyFs: fakeFs({}, []),
      logger: quietLogger,
    });
    current = adapter;
    const status = await adapter.status();
    expect(status.auth.state).toBe('error');
    expect(status.auth.detail).toMatch(/CODEX_HOME is missing/);
    expect(spawn).not.toHaveBeenCalled();
  });
});
