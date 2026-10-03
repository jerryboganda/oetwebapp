import { describe, expect, it, vi } from 'vitest';
import type { AppConfig } from '../../src/config.js';
import type { EngineEvent, EngineHooks, ToolCallRequest, ToolDecision } from '../../src/engines/types.js';
import { createOpenCodeAdapter, type OpenCodeChild, type OpenCodeFetch } from '../../src/engines/opencode.js';
import { tempDir, testConfig } from '../helpers.js';

const CWD = '/workspace/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V';
const NATIVE_SESSION = 'ses_openCode123456';
const PERMISSION = 'per_openCode123456';
const CALL = 'call_openCode123456';

class FakeChild implements OpenCodeChild {
  private exitListeners: ((code: number | null, signal: NodeJS.Signals | null) => void)[] = [];
  private errorListeners: ((error: Error) => void)[] = [];
  readonly signals: NodeJS.Signals[] = [];

  kill(signal: NodeJS.Signals = 'SIGTERM'): boolean {
    this.signals.push(signal);
    queueMicrotask(() => this.exitListeners.forEach((listener) => listener(0, signal)));
    return true;
  }

  once(event: 'exit' | 'error', listener: ((code: number | null, signal: NodeJS.Signals | null) => void) | ((error: Error) => void)): unknown {
    if (event === 'exit') this.exitListeners.push(listener as (code: number | null, signal: NodeJS.Signals | null) => void);
    else this.errorListeners.push(listener as (error: Error) => void);
    return this;
  }
}

function response(value: unknown, status = 200): Response {
  return new Response(status === 204 ? null : JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function providerPayload() {
  return {
    all: [{
      id: 'github-copilot',
      name: 'GitHub Copilot',
      key: 'must-never-leave-opencode',
      models: {
        'model/opaque': {
          id: 'model/opaque',
          name: 'Model One',
          variants: { high: {} },
          defaultVariant: 'high',
        },
      },
    }],
    connected: ['github-copilot'],
  };
}

function event(directory: string, type: string, properties: Record<string, unknown>): Uint8Array {
  return new TextEncoder().encode(`data: ${JSON.stringify({ directory, payload: { type, properties } })}\n\n`);
}

describe('OpenCode adapter', () => {
  it('keeps provider labels on completed OAuth flows after releasing callback state', async () => {
    const config = testConfig(tempDir()) as AppConfig;
    const children: FakeChild[] = [];
    const callbacks: unknown[] = [];
    const fakeFetch: OpenCodeFetch = async (input, init = {}) => {
      const url = new URL(String(input));
      if (url.pathname === '/global/health') return response({ healthy: true, version: '1.18.34' });
      if (url.pathname === '/provider') return response(providerPayload());
      if (url.pathname === '/provider/auth') return response({ 'github-copilot': [{ type: 'oauth', label: 'Device code' }] });
      if (url.pathname === '/provider/github-copilot/oauth/authorize') return response({ url: 'https://github.com/login/device', method: 'code', instructions: 'Paste the code from the browser.' });
      if (url.pathname === '/provider/github-copilot/oauth/callback') {
        callbacks.push(JSON.parse(String(init.body)) as unknown);
        return response(true);
      }
      if (url.pathname === '/instance/dispose') return response(true);
      return response({ error: 'unexpected route' }, 404);
    };
    const adapter = createOpenCodeAdapter(config, undefined, {
      baseEnv: () => ({ HOME: '/home/agent', PATH: '/usr/bin' }),
      fetch: fakeFetch,
      port: async () => 4097,
      spawn: () => {
        const child = new FakeChild();
        children.push(child);
        return child;
      },
      runner: async () => ({ code: 0, signal: null, stdout: '', stdoutBuffer: Buffer.alloc(0), stderr: '', stdoutTruncated: false, timedOut: false }),
      logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
    });

    try {
      await expect(adapter.connect({ providerId: 'github-copilot', methodIndex: 99 })).rejects.toThrow('no OAuth sign-in method');
      const flow = await adapter.connect({ providerId: 'github-copilot', methodIndex: 0 });
      expect(flow).toMatchObject({ kind: 'paste_code', state: 'awaiting_code', providerId: 'github-copilot', providerName: 'GitHub Copilot' });
      const completed = await adapter.submitCode(flow.flowId, 'device-code-1234');
      expect(completed).toMatchObject({ state: 'completed', providerId: 'github-copilot', providerName: 'GitHub Copilot' });
      expect(adapter.getFlow(flow.flowId)).toMatchObject({ state: 'completed', providerId: 'github-copilot', providerName: 'GitHub Copilot' });
      expect(callbacks).toEqual([{ method: 0, code: 'device-code-1234' }]);
    } finally {
      await adapter.shutdown();
    }
  });

  it('polls OpenCode auto OAuth only when instructions contain an explicit device code', async () => {
    const config = testConfig(tempDir()) as AppConfig;
    let callbackBody: unknown;
    let finishCallback: (() => void) | undefined;
    const fakeFetch: OpenCodeFetch = async (input, init = {}) => {
      const url = new URL(String(input));
      if (url.pathname === '/global/health') return response({ healthy: true, version: '1.18.34' });
      if (url.pathname === '/provider') return response(providerPayload());
      if (url.pathname === '/provider/auth') return response({ 'github-copilot': [{ type: 'oauth', label: 'Device code' }] });
      if (url.pathname === '/provider/github-copilot/oauth/authorize') return response({
        url: 'https://github.com/login/device', method: 'auto', instructions: 'Enter verification code: ABCD-1234',
      });
      if (url.pathname === '/provider/github-copilot/oauth/callback') {
        callbackBody = JSON.parse(String(init.body)) as unknown;
        return new Promise<Response>((resolve, reject) => {
          const signal = init.signal;
          if (!signal) return resolve(response(true));
          finishCallback = () => resolve(response(true));
          signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
        });
      }
      if (url.pathname === '/instance/dispose') return response(true);
      return response({ error: 'unexpected route' }, 404);
    };
    const adapter = createOpenCodeAdapter(config, undefined, {
      baseEnv: () => ({ HOME: '/home/agent', PATH: '/usr/bin' }),
      fetch: fakeFetch,
      port: async () => 4098,
      spawn: () => new FakeChild(),
      runner: async () => ({ code: 0, signal: null, stdout: '', stdoutBuffer: Buffer.alloc(0), stderr: '', stdoutTruncated: false, timedOut: false }),
      logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
    });

    try {
      const flow = await adapter.connect({ providerId: 'github-copilot', methodIndex: 0 });
      expect(flow).toMatchObject({ kind: 'device_code', state: 'pending', userCode: 'ABCD-1234' });
      await vi.waitFor(() => expect(finishCallback).toBeTypeOf('function'));
      expect(callbackBody).toEqual({ method: 0 });
      finishCallback?.();
      await vi.waitFor(() => expect(adapter.getFlow(flow.flowId)).toMatchObject({ state: 'completed', providerId: 'github-copilot' }));
    } finally {
      await adapter.shutdown();
    }
  });

  it('keeps provider credentials private and routes native permissions through Guard before replying', async () => {
    const config = testConfig(tempDir()) as AppConfig;
    const children: FakeChild[] = [];
    const spawned: { args: string[]; env: Record<string, string> }[] = [];
    const requests: { url: URL; init: RequestInit }[] = [];
    let eventController: ReadableStreamDefaultController<Uint8Array> | undefined;
    let permissionDecision: ToolDecision | undefined;
    const permissionRequest = vi.fn(async (request: ToolCallRequest) => {
      expect(permissionDecision).toBeUndefined();
      expect(request.command).toBe('git status');
      permissionDecision = { behavior: 'allow' };
      return permissionDecision;
    });
    const fakeFetch: OpenCodeFetch = async (input, init = {}) => {
      const url = new URL(String(input));
      requests.push({ url, init });
      if (url.pathname === '/global/health') return response({ healthy: true, version: '1.18.34' });
      if (url.pathname === '/provider') return response(providerPayload());
      if (url.pathname === '/provider/auth') return response({ 'github-copilot': [{ type: 'oauth', label: 'Device code' }, { type: 'api', label: 'API key' }] });
      if (url.pathname === '/session' && init.method === 'POST') return response({ id: NATIVE_SESSION, directory: CWD, title: 'OpenCode test' });
      if (url.pathname === '/event') {
        const stream = new ReadableStream<Uint8Array>({ start(controller) { eventController = controller; } });
        return new Response(stream, { status: 200, headers: { 'content-type': 'text/event-stream' } });
      }
      if (url.pathname === `/session/${NATIVE_SESSION}/prompt_async`) {
        eventController?.enqueue(event(CWD, 'session.status', { sessionID: NATIVE_SESSION, status: { type: 'busy' } }));
        eventController?.enqueue(event(CWD, 'permission.updated', {
          id: PERMISSION,
          callID: CALL,
          sessionID: NATIVE_SESSION,
          type: 'bash',
          title: 'Run command',
          metadata: { command: 'git status', cwd: CWD },
        }));
        return response(undefined, 204);
      }
      if (url.pathname === `/session/${NATIVE_SESSION}/permissions/${PERMISSION}`) {
        const body = JSON.parse(String(init.body)) as { response: string };
        expect(body.response).toBe('once');
        eventController?.enqueue(event(CWD, 'message.part.updated', {
          sessionID: NATIVE_SESSION,
          part: {
            id: 'part_tool', sessionID: NATIVE_SESSION, messageID: 'message_assistant', type: 'tool',
            callID: CALL, tool: 'bash',
            state: { status: 'completed', title: 'Run command', input: { command: 'git status' }, output: 'clean', metadata: { exitCode: 0 } },
          },
        }));
        eventController?.enqueue(event(CWD, 'message.part.updated', {
          sessionID: NATIVE_SESSION,
          part: { id: 'part_text', sessionID: NATIVE_SESSION, messageID: 'message_assistant', type: 'text', text: 'Done.' },
          delta: 'Done.',
        }));
        eventController?.enqueue(event(CWD, 'message.updated', {
          info: {
            id: 'message_assistant', sessionID: NATIVE_SESSION, role: 'assistant', time: { created: 1, completed: 2 },
            providerID: 'github-copilot', modelID: 'model/opaque', cost: 0.01,
            tokens: { input: 10, output: 5, reasoning: 0, cache: { read: 0, write: 0 } },
          },
        }));
        eventController?.enqueue(event(CWD, 'session.idle', { sessionID: NATIVE_SESSION }));
        return response(true);
      }
      if (url.pathname === `/session/${NATIVE_SESSION}/diff`) return response([]);
      if (url.pathname === `/session/${NATIVE_SESSION}`) return response({ id: NATIVE_SESSION, directory: CWD });
      if (url.pathname === '/instance/dispose') return response(true);
      return response({ error: 'unexpected route' }, 404);
    };
    const adapter = createOpenCodeAdapter(config, undefined, {
      baseEnv: (sessionId) => ({ HOME: '/home/agent', PATH: '/usr/bin', ...(sessionId ? { sessionId } : {}) }),
      fetch: fakeFetch,
      port: async () => 4096 + children.length,
      spawn: (_file, args, options) => {
        const child = new FakeChild();
        children.push(child);
        spawned.push({ args, env: options.env });
        return child;
      },
      runner: async () => ({ code: 0, signal: null, stdout: '', stdoutBuffer: Buffer.alloc(0), stderr: '', stdoutTruncated: false, timedOut: false }),
      logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
    });

    try {
      const status = await adapter.status();
      expect(status).toMatchObject({ engine: 'opencode', version: '1.18.34', auth: { state: 'signed_in' } });
      expect(status.models).toEqual([{
        value: 'github-copilot/model/opaque',
        displayName: 'GitHub Copilot · Model One',
        supportsEffort: false,
        efforts: [],
      }]);
      expect(JSON.stringify(status)).not.toContain('must-never-leave-opencode');

      const session = await adapter.openSession({
        sessionId: '01J9ZQ4X7V3N8K2M5P6R7S8T9V',
        cwd: CWD,
        model: 'github-copilot/model/opaque',
        mode: 'guarded',
        appendSystemPrompt: 'Owner manual',
        env: { HOME: '/home/agent', PATH: '/usr/bin', HTTPS_PROXY: 'http://session-proxy' },
      });
      const events: EngineEvent[] = [];
      const hooks: EngineHooks = { emit: (value) => events.push(value), onToolCall: permissionRequest };
      const turn = await session.runTurn('Inspect the repository', { model: 'github-copilot/model/opaque', mode: 'guarded' }, hooks, new AbortController().signal);

      expect(turn).toEqual({ status: 'ok', resumeId: NATIVE_SESSION });
      expect(permissionRequest).toHaveBeenCalledTimes(1);
      expect(events.map((item) => item.type)).toEqual(['tool_call', 'tool_output_delta', 'tool_result', 'text_delta', 'text', 'usage']);
      expect(events.find((item) => item.type === 'usage')).toMatchObject({ data: { inputTokens: 10, outputTokens: 5, costUsd: 0.01 } });
      const sessionServer = spawned.find(({ args }) => args.includes('serve'));
      expect(sessionServer?.args).toContain('--pure');
      expect(sessionServer?.args).toContain('127.0.0.1');
      expect(sessionServer?.args).not.toContain('--auto');
      expect(sessionServer?.env.HTTPS_PROXY).toBe('http://session-proxy');
      expect(JSON.parse(sessionServer?.env.OPENCODE_PERMISSION ?? '{}')).toMatchObject({ '*': 'ask', bash: 'ask', write: 'ask' });
      const spawn = requests.find((request) => request.url.pathname === '/session');
      expect(spawn).toBeDefined();
      await session.close();
      await adapter.shutdown();
      expect(children.every((child) => child.signals.length > 0)).toBe(true);
    } finally {
      await adapter.shutdown();
    }
  });
});
