import { spawn } from 'node:child_process';
import { createServer as createNetServer } from 'node:net';
import { createRunner, type Runner } from '../exec.js';
import { AuthFlowError, FlowRegistry, isTerminalFlow } from '../auth/claude.js';
import type { AppConfig } from '../config.js';
import type { EngineFactoryContext } from '../engine-registry.js';
import { SYSTEM_SESSION_ID } from '../contract.js';
import type {
  ConnectFlow,
  EngineAdapter,
  EngineAuth,
  EngineConnectOptions,
  EngineEvent,
  EngineHooks,
  EngineProvider,
  EngineSession,
  EngineStatus,
  Mode,
  ModelInfo,
  SessionEngineOptions,
  ToolCallRequest,
  TurnResult,
} from './types.js';
import { EngineError, consoleEngineLogger, safeErrorMessage, toEngineLogger, type EngineLogger } from './codex-protocol.js';
import { mapOpenCodeModels, mapOpenCodePermission, mapOpenCodeProviders, parseOpenCodeUserCode, type OpenCodePermission } from './opencode-protocol.js';

const DEFAULT_OPENCODE_BIN = '/usr/local/lib/oet-agent/opencode';
const SERVER_START_TIMEOUT_MS = 15_000;
const PERMISSION_FLOW_TTL_MS = 10 * 60_000;
const SERVER_PERMISSION = JSON.stringify({
  '*': 'ask',
  read: 'ask',
  edit: 'ask',
  glob: 'ask',
  grep: 'ask',
  bash: 'ask',
  task: 'ask',
  skill: 'ask',
  lsp: 'ask',
  webfetch: 'ask',
  websearch: 'ask',
  external_directory: 'ask',
  doom_loop: 'ask',
});

export interface OpenCodeChild {
  kill(signal?: NodeJS.Signals): boolean;
  once(event: 'exit', listener: (code: number | null, signal: NodeJS.Signals | null) => void): unknown;
  once(event: 'error', listener: (error: Error) => void): unknown;
}

export type OpenCodeSpawn = (file: string, args: string[], options: { cwd: string; env: Record<string, string> }) => OpenCodeChild;
export type OpenCodeFetch = typeof fetch;

export interface OpenCodeAdapterOverrides {
  baseEnv?: (sessionId?: string) => Record<string, string>;
  opencodeBinPath?: string;
  spawn?: OpenCodeSpawn;
  fetch?: OpenCodeFetch;
  runner?: Runner;
  port?: () => Promise<number>;
  now?: () => number;
  logger?: EngineLogger;
  startupTimeoutMs?: number;
}

export const nodeOpenCodeSpawn: OpenCodeSpawn = (file, args, options) =>
  spawn(file, args, { cwd: options.cwd, env: options.env, stdio: 'ignore', windowsHide: true });

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value : undefined;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    timer.unref?.();
  });
}

async function freePort(): Promise<number> {
  const listener = createNetServer();
  await new Promise<void>((resolve, reject) => {
    listener.once('error', reject);
    listener.listen(0, '127.0.0.1', () => resolve());
  });
  const address = listener.address();
  if (!address || typeof address === 'string') throw new Error('Could not allocate an OpenCode port.');
  await new Promise<void>((resolve, reject) => listener.close((error) => error ? reject(error) : resolve()));
  return address.port;
}

class OpenCodeHttpError extends Error {
  constructor(readonly status: number) {
    super(`OpenCode API returned HTTP ${status}.`);
    this.name = 'OpenCodeHttpError';
  }
}

interface OpenCodeServerOptions {
  binary: string;
  asAgentPath: string;
  cwd: string;
  env: Record<string, string>;
  fetch: OpenCodeFetch;
  spawn: OpenCodeSpawn;
  port: () => Promise<number>;
  startupTimeoutMs: number;
}

/** One headless OpenCode server per console session keeps every request attributable to that session. */
class OpenCodeServer {
  private child: OpenCodeChild | undefined;
  private starting: Promise<void> | undefined;
  private baseUrl: string | undefined;
  private exitError: Error | undefined;
  private closing = false;

  constructor(private readonly options: OpenCodeServerOptions) {}

  async ensureStarted(): Promise<void> {
    if (this.baseUrl && this.child && !this.exitError) return;
    if (!this.starting) {
      this.starting = this.start().finally(() => {
        this.starting = undefined;
      });
    }
    await this.starting;
  }

  async request(pathname: string, init: RequestInit = {}): Promise<Response> {
    await this.ensureStarted();
    const response = await this.options.fetch(`${this.baseUrl}${pathname}`, {
      ...init,
      headers: { Accept: 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}), ...init.headers },
    });
    if (!response.ok) throw new OpenCodeHttpError(response.status);
    return response;
  }

  async json<T>(pathname: string, init: RequestInit = {}): Promise<T> {
    const response = await this.request(pathname, init);
    if (response.status === 204) return undefined as T;
    try {
      return await response.json() as T;
    } catch {
      throw new EngineError('engine_error', 'OpenCode returned an unreadable response.');
    }
  }

  async events(signal: AbortSignal): Promise<Response> {
    return this.request('/event', { headers: { Accept: 'text/event-stream' }, signal });
  }

  async close(): Promise<void> {
    this.closing = true;
    const child = this.child;
    const baseUrl = this.baseUrl;
    this.child = undefined;
    this.baseUrl = undefined;
    if (!child) return;
    let exited = false;
    const exit = new Promise<void>((resolve) => child.once('exit', () => {
      exited = true;
      resolve();
    }));
    if (baseUrl) {
      await this.options.fetch(`${baseUrl}/instance/dispose`, { method: 'POST' }).catch(() => undefined);
    }
    child.kill('SIGTERM');
    const timedOut = await Promise.race([exit.then(() => false), delay(3_000).then(() => true)]);
    if (timedOut && !exited) child.kill('SIGKILL');
  }

  private async start(): Promise<void> {
    this.closing = false;
    this.exitError = undefined;
    const port = await this.options.port();
    this.baseUrl = `http://127.0.0.1:${port}`;
    const child = this.options.spawn(
      this.options.asAgentPath,
      [this.options.binary, '--pure', 'serve', '--hostname', '127.0.0.1', '--port', String(port)],
      { cwd: this.options.cwd, env: this.options.env },
    );
    this.child = child;
    child.once('error', (error) => {
      this.exitError = error;
    });
    child.once('exit', (code, signal) => {
      if (!this.closing) this.exitError = new Error(`OpenCode exited (${code ?? signal ?? 'unknown'}).`);
      if (this.child === child) {
        this.child = undefined;
        this.baseUrl = undefined;
      }
    });

    const deadline = Date.now() + this.options.startupTimeoutMs;
    while (Date.now() < deadline) {
      if (this.exitError) throw new EngineError('engine_unavailable', 'OpenCode server did not start.');
      try {
        const health = await this.options.fetch(`${this.baseUrl}/global/health`, { signal: AbortSignal.timeout(500) });
        if (health.ok) return;
      } catch {
        // The server is still binding its loopback listener.
      }
      await delay(100);
    }
    await this.close();
    throw new EngineError('engine_unavailable', 'OpenCode server startup timed out.');
  }
}

interface OpenCodeSettings {
  binary: string;
  asAgentPath: string;
  agentHome: string;
  controlCwd: string;
  baseEnv(sessionId?: string): Record<string, string>;
  fetch: OpenCodeFetch;
  spawn: OpenCodeSpawn;
  runner: Runner;
  port: () => Promise<number>;
  now: () => number;
  logger: EngineLogger;
  startupTimeoutMs: number;
}

function configuredEnv(base: Record<string, string>): Record<string, string> {
  return {
    ...base,
    OPENCODE_DISABLE_AUTOUPDATE: '1',
    OPENCODE_DISABLE_PRUNE: '1',
    OPENCODE_DISABLE_DEFAULT_PLUGINS: '1',
    OPENCODE_PERMISSION: SERVER_PERMISSION,
  };
}

function parseModel(value: string): { providerID: string; modelID: string } | null {
  const slash = value.indexOf('/');
  if (slash <= 0 || slash === value.length - 1) return null;
  return { providerID: value.slice(0, slash), modelID: value.slice(slash + 1) };
}

function safeProviderId(value: unknown): string | undefined {
  if (typeof value !== 'string' || value.length < 1 || value.length > 128 || /[\u0000-\u001f\u007f]/.test(value)) return undefined;
  return value;
}

interface OpenCodeFlowState {
  providerId: string;
  providerName: string;
  methodIndex: number;
  expiry?: ReturnType<typeof setTimeout>;
  callbackController?: AbortController;
}

class OpenCodeAdapter implements EngineAdapter {
  readonly engine = 'opencode' as const;
  private readonly flows = new FlowRegistry('opencode', Date.now);
  private readonly flowState = new Map<string, OpenCodeFlowState>();
  private readonly sessions = new Set<OpenCodeSession>();
  private controlServer: OpenCodeServer | undefined;
  private providerCache: { providers: EngineProvider[]; version: string | null } | undefined;

  constructor(private readonly settings: OpenCodeSettings) {}

  async status(): Promise<EngineStatus> {
    try {
      const server = await this.control();
      const [providerList, providerAuth, health] = await Promise.all([
        server.json<unknown>('/provider'),
        server.json<unknown>('/provider/auth'),
        server.json<unknown>('/global/health'),
      ]);
      const providers = mapOpenCodeProviders(providerList, providerAuth);
      const models = mapOpenCodeModels(providerList);
      const version = text(record(health)?.['version']) ?? null;
      this.providerCache = { providers, version };
      const connected = providers.filter((provider) => provider.connected);
      const auth: EngineAuth = connected.length > 0
        ? { state: 'signed_in', account: { workspace: connected.map((provider) => provider.name).join(', ') } }
        : { state: this.flows.active() ? 'signing_in' : 'signed_out' };
      return {
        engine: this.engine,
        version,
        auth,
        models,
        rateLimits: null,
        providers,
      };
    } catch (error) {
      this.settings.logger.warn('OpenCode status failed', { message: safeErrorMessage(error) });
      return {
        engine: this.engine,
        version: this.providerCache?.version ?? null,
        auth: { state: 'error', detail: 'Could not read OpenCode provider status.' },
        models: [],
        rateLimits: null,
        providers: this.providerCache?.providers ?? [],
      };
    }
  }

  async openSession(options: SessionEngineOptions): Promise<EngineSession> {
    const status = await this.status();
    if (status.auth.state !== 'signed_in') {
      throw new EngineError('engine_not_signed_in', 'Connect an OpenCode provider before starting a session.');
    }
    if (!status.models.some((model) => model.value === options.model)) {
      throw new EngineError('unknown_model', 'The selected OpenCode model is not available from a connected provider.');
    }
    const session = new OpenCodeSession(this.settings, options);
    this.sessions.add(session);
    return session;
  }

  async connect(options: EngineConnectOptions = {}): Promise<ConnectFlow> {
    const previous = this.flows.active();
    if (previous) await this.cancel(previous.flowId);
    const providerId = safeProviderId(options.providerId);
    if (!providerId) throw new EngineError('engine_auth_error', 'Select an OpenCode provider.');
    if (options.apiKey !== undefined) return this.connectWithApiKey(providerId, options.apiKey);
    const providerStatus = await this.status();
    const provider = providerStatus.providers?.find((item) => item.id === providerId);
    const method = options.methodIndex === undefined
      ? provider?.oauthMethods[0]
      : provider?.oauthMethods.find((item) => item.index === options.methodIndex);
    if (!provider || !method) throw new EngineError('engine_auth_error', 'The selected OpenCode provider has no OAuth sign-in method.');

    const server = await this.control();
    const authorization = await server.json<unknown>(`/provider/${encodeURIComponent(providerId)}/oauth/authorize`, {
      method: 'POST',
      body: JSON.stringify({ method: method.index }),
    });
    const response = record(authorization);
    const authMethod = response?.['method'];
    const instructions = text(response?.['instructions']);
    if (!response || (authMethod !== 'code' && authMethod !== 'auto') || !text(response['url'])) {
      throw new EngineError('engine_auth_error', 'OpenCode did not return a supported OAuth authorization flow.');
    }
    const userCode = authMethod === 'auto' ? parseOpenCodeUserCode(instructions ?? '') : null;
    const flow = this.flows.create(authMethod === 'code' ? 'paste_code' : 'device_code');
    if (authMethod === 'auto' && !userCode) {
      const unsupported = this.flows.update(flow.flowId, {
        state: 'failed',
        detail: 'This provider requires a browser callback that cannot reach the isolated console container. Choose a headless or device-code OAuth method.',
        providerId,
        providerName: provider.name,
      }) ?? flow;
      const currentServer = this.controlServer;
      this.controlServer = undefined;
      await currentServer?.close();
      return { ...unsupported, providerId, providerName: provider.name };
    }
    const updated = this.flows.update(flow.flowId, {
      state: authMethod === 'code' ? 'awaiting_code' : 'pending',
      verificationUrl: text(response?.['url']),
      ...(userCode ? { userCode } : {}),
      expiresAt: new Date(this.settings.now() + PERMISSION_FLOW_TTL_MS).toISOString(),
      detail: instructions?.slice(0, 500) ?? `Complete ${provider.name} sign-in in the opened authorization flow.`,
      providerId,
      providerName: provider.name,
    }) ?? flow;
    const flowState: OpenCodeFlowState = { providerId, providerName: provider.name, methodIndex: method.index };
    this.flowState.set(flow.flowId, flowState);
    flowState.expiry = setTimeout(() => void this.expireFlow(flow.flowId), PERMISSION_FLOW_TTL_MS);
    flowState.expiry.unref?.();
    if (authMethod === 'auto') {
      flowState.callbackController = new AbortController();
      void server.json(`/provider/${encodeURIComponent(providerId)}/oauth/callback`, {
        method: 'POST',
        body: JSON.stringify({ method: method.index }),
        signal: flowState.callbackController.signal,
      }).then(() => {
        this.providerCache = undefined;
        return this.finishFlow(flow.flowId, 'completed', 'OpenCode provider connected.');
      }).catch(() => {
        if (!isTerminalFlow(this.flows.get(flow.flowId) ?? { state: 'failed' })) {
          void this.finishFlow(flow.flowId, 'failed', 'OpenCode provider authorization failed. Start again.');
        }
      });
    }
    const result = { ...updated, providerId, providerName: provider.name };
    this.flows.update(flow.flowId, { detail: result.detail });
    return result;
  }

  private async connectWithApiKey(providerId: string, apiKey: string): Promise<ConnectFlow> {
    const value = apiKey.trim();
    if (!value || value.length > 512 || /[\u0000-\u001f\u007f]/.test(value)) {
      throw new EngineError('engine_auth_error', 'The API key is invalid.');
    }
    const providerStatus = await this.status();
    const provider = providerStatus.providers?.find((item) => item.id === providerId);
    if (!provider) throw new EngineError('engine_auth_error', 'Unknown OpenCode provider.');
    if (provider.apiMethods.length === 0) {
      throw new EngineError('engine_auth_error', 'This OpenCode provider does not accept an API key.');
    }
    const server = await this.control();
    try {
      await server.json<unknown>(`/auth/${encodeURIComponent(providerId)}`, {
        method: 'PUT',
        body: JSON.stringify({ type: 'api', key: value }),
      });
    } catch (error) {
      throw new EngineError('engine_auth_error', `OpenCode rejected the API key (${safeErrorMessage(error)}).`);
    }
    this.providerCache = undefined;
    const refreshed = await this.status();
    const nowConnected = refreshed.providers?.find((item) => item.id === providerId)?.connected === true;
    const flow = this.flows.create('api_key');
    const updated = this.flows.update(flow.flowId, {
      state: nowConnected ? 'completed' : 'failed',
      detail: nowConnected
        ? 'OpenCode provider connected with an API key.'
        : 'OpenCode did not report the provider as connected. Check the API key and try again.',
      providerId,
      providerName: provider.name,
    }) ?? flow;
    return { ...updated, providerId, providerName: provider.name };
  }

  getFlow(flowId: string): ConnectFlow | undefined {
    return this.flows.get(flowId);
  }

  async submitCode(flowId: string, code: string): Promise<ConnectFlow> {
    const flow = this.flows.get(flowId);
    const state = this.flowState.get(flowId);
    if (!flow || !state) throw new AuthFlowError('flow_not_found', 'Unknown OpenCode sign-in flow.');
    if (flow.kind !== 'paste_code' || isTerminalFlow(flow)) throw new AuthFlowError('flow_not_awaiting_code', 'This OpenCode provider does not accept a paste-back code.');
    const value = code.trim();
    if (!value || value.length > 4096 || /[\u0000-\u001f\u007f]/.test(value)) {
      throw new AuthFlowError('invalid_code', 'The OpenCode authorization code is invalid.');
    }
    const server = await this.control();
    await server.json(`/provider/${encodeURIComponent(state.providerId)}/oauth/callback`, {
      method: 'POST',
      body: JSON.stringify({ method: state.methodIndex, code: value }),
    });
    return this.finishFlow(flowId, 'completed', 'OpenCode provider connected.');
  }

  async cancel(flowId: string): Promise<ConnectFlow> {
    const state = this.flowState.get(flowId);
    if (!this.flows.get(flowId) || !state) throw new AuthFlowError('flow_not_found', 'Unknown OpenCode sign-in flow.');
    state.callbackController?.abort();
    const result = await this.finishFlow(flowId, 'cancelled', 'OpenCode sign-in cancelled.');
    await this.closeControl();
    return result;
  }

  async logout(): Promise<EngineAuth> {
    const active = this.flows.active();
    if (active) await this.finishFlow(active.flowId, 'cancelled', 'Signed out.');
    const server = await this.control();
    const providerList = await server.json<unknown>('/provider');
    const connected = Array.isArray(record(providerList)?.['connected'])
      ? (record(providerList)?.['connected'] as unknown[]).filter((id): id is string => typeof id === 'string')
      : [];
    for (const providerId of connected) {
      const result = await this.settings.runner(this.settings.binary, ['auth', 'logout', providerId], {
        asAgent: true,
        cwd: this.settings.agentHome,
        env: this.settings.baseEnv(SYSTEM_SESSION_ID),
        timeoutMs: 15_000,
        maxOutputBytes: 4096,
      });
      if (result.timedOut || result.code !== 0) throw new AuthFlowError('logout_failed', 'OpenCode could not sign out of a provider.');
    }
    this.providerCache = undefined;
    return { state: 'signed_out' };
  }

  async shutdown(): Promise<void> {
    for (const state of this.flowState.values()) this.clearFlowTimers(state);
    for (const state of this.flowState.values()) state.callbackController?.abort();
    this.flowState.clear();
    await Promise.allSettled([...this.sessions].map((session) => session.close()));
    await this.closeControl();
  }

  private async control(): Promise<OpenCodeServer> {
    if (this.controlServer) return this.controlServer;
    const server = this.createServer(this.settings.controlCwd, this.settings.baseEnv(SYSTEM_SESSION_ID));
    await server.ensureStarted();
    this.controlServer = server;
    return server;
  }

  private createServer(cwd: string, baseEnv: Record<string, string>): OpenCodeServer {
    return new OpenCodeServer({
      binary: this.settings.binary,
      asAgentPath: this.settings.asAgentPath,
      cwd,
      env: configuredEnv(baseEnv),
      fetch: this.settings.fetch,
      spawn: this.settings.spawn,
      port: this.settings.port,
      startupTimeoutMs: this.settings.startupTimeoutMs,
    });
  }

  private async expireFlow(flowId: string): Promise<void> {
    const state = this.flowState.get(flowId);
    state?.callbackController?.abort();
    await this.finishFlow(flowId, 'expired', 'OpenCode sign-in expired. Start again.');
    await this.closeControl();
  }

  private async finishFlow(flowId: string, state: ConnectFlow['state'], detail: string): Promise<ConnectFlow> {
    const flowState = this.flowState.get(flowId);
    if (flowState) this.clearFlowTimers(flowState);
    const flow = this.flows.update(flowId, {
      state,
      detail,
      ...(flowState ? { providerId: flowState.providerId, providerName: flowState.providerName } : {}),
    });
    if (!flow) throw new AuthFlowError('flow_not_found', 'Unknown OpenCode sign-in flow.');
    this.flowState.delete(flowId);
    return flow;
  }

  private clearFlowTimers(state: OpenCodeFlowState): void {
    if (state.expiry) clearTimeout(state.expiry);
  }

  private async closeControl(): Promise<void> {
    const server = this.controlServer;
    this.controlServer = undefined;
    await server?.close();
  }
}

interface OpenCodeTurn {
  hooks: EngineHooks;
  controller: AbortController;
  eventController: AbortController;
  done: Promise<{ status: TurnResult['status']; error?: { code: string; message: string } }>;
  resolve(value: { status: TurnResult['status']; error?: { code: string; message: string } }): void;
  nativeSessionId: string;
  promptSubmitted: boolean;
  activity: boolean;
  settled: boolean;
  error?: { code: string; message: string };
  readonly textByMessage: Map<string, string>;
  readonly completedMessages: Set<string>;
  readonly completedTools: Set<string>;
  readonly toolOutput: Map<string, string>;
}

class OpenCodeSession implements EngineSession {
  readonly engine = 'opencode' as const;
  private nativeSessionId: string | undefined;
  private server: OpenCodeServer | undefined;
  private turn: OpenCodeTurn | undefined;

  constructor(private readonly settings: OpenCodeSettings, private readonly options: SessionEngineOptions) {
    this.nativeSessionId = options.resumeId;
  }

  async runTurn(
    textValue: string,
    turnOptions: { model: string; effort?: string; mode: Mode },
    hooks: EngineHooks,
    signal: AbortSignal,
  ): Promise<TurnResult> {
    if (this.turn) throw new EngineError('turn_in_progress', 'An OpenCode turn is already running.');
    const resume = (): { resumeId?: string } => this.nativeSessionId ? { resumeId: this.nativeSessionId } : {};
    if (signal.aborted) return { status: 'interrupted', ...resume() };

    try {
      const server = await this.ensureServer();
      await this.ensureNativeSession(server);
      if (signal.aborted) return { status: 'interrupted', ...resume() };
      const nativeSessionId = this.nativeSessionId;
      if (!nativeSessionId) throw new EngineError('engine_error', 'OpenCode did not return a session id.');
      const model = parseModel(turnOptions.model);
      if (!model) throw new EngineError('unknown_model', 'OpenCode model IDs must include a provider and model ID.');

      let resolveTurn!: OpenCodeTurn['resolve'];
      const done = new Promise<{ status: TurnResult['status']; error?: { code: string; message: string } }>((resolve) => {
        resolveTurn = resolve;
      });
      const turn: OpenCodeTurn = {
        hooks,
        controller: new AbortController(),
        eventController: new AbortController(),
        done,
        resolve: resolveTurn,
        nativeSessionId,
        promptSubmitted: false,
        activity: false,
        settled: false,
        textByMessage: new Map(),
        completedMessages: new Set(),
        completedTools: new Set(),
        toolOutput: new Map(),
      };
      this.turn = turn;
      const onAbort = (): void => void this.interrupt();
      signal.addEventListener('abort', onAbort, { once: true });

      let eventTask: Promise<void> | undefined;
      try {
        const eventResponse = await server.events(turn.eventController.signal);
        eventTask = consumeOpenCodeEvents(eventResponse, (event) => this.handleEvent(turn, event), turn.eventController.signal)
          .catch((error: unknown) => {
            if (!turn.eventController.signal.aborted) this.finishTurn(turn, 'error', 'engine_error', safeErrorMessage(error));
          });
        turn.promptSubmitted = true;
        const body: Record<string, unknown> = {
          model,
          system: this.options.appendSystemPrompt,
          parts: [{ type: 'text', text: textValue }],
        };
        await server.json(`/session/${encodeURIComponent(nativeSessionId)}/prompt_async`, {
          method: 'POST',
          body: JSON.stringify(body),
          signal,
        });
        const outcome = await done;
        if (outcome.status === 'ok') await this.emitFileChanges(server, nativeSessionId, hooks);
        return { status: outcome.status, ...resume(), ...(outcome.error ? { error: outcome.error } : {}) };
      } catch (error) {
        const interrupted = signal.aborted || turn.controller.signal.aborted;
        const failure = interrupted ? undefined : { code: 'engine_error', message: safeErrorMessage(error) };
        this.finishTurn(turn, interrupted ? 'interrupted' : 'error', failure?.code, failure?.message);
        return { status: interrupted ? 'interrupted' : 'error', ...resume(), ...(failure ? { error: failure } : {}) };
      } finally {
        signal.removeEventListener('abort', onAbort);
        turn.eventController.abort();
        await eventTask?.catch(() => undefined);
        if (this.turn === turn) this.turn = undefined;
      }
    } catch (error) {
      const interrupted = signal.aborted;
      return {
        status: interrupted ? 'interrupted' : 'error',
        ...resume(),
        ...(!interrupted ? { error: { code: error instanceof EngineError ? error.code : 'engine_error', message: safeErrorMessage(error) } } : {}),
      };
    }
  }

  async interrupt(): Promise<void> {
    const turn = this.turn;
    if (!turn || turn.settled) return;
    turn.controller.abort();
    const server = this.server;
    if (server) {
      await server.json(`/session/${encodeURIComponent(turn.nativeSessionId)}/abort`, { method: 'POST' }).catch(() => undefined);
    }
    this.finishTurn(turn, 'interrupted');
  }

  async close(): Promise<void> {
    if (this.turn) await this.interrupt();
    const server = this.server;
    this.server = undefined;
    await server?.close();
  }

  private async ensureServer(): Promise<OpenCodeServer> {
    if (this.server) return this.server;
    const server = new OpenCodeServer({
      binary: this.settings.binary,
      asAgentPath: this.settings.asAgentPath,
      cwd: this.options.cwd,
      env: configuredEnv(this.options.env),
      fetch: this.settings.fetch,
      spawn: this.settings.spawn,
      port: this.settings.port,
      startupTimeoutMs: this.settings.startupTimeoutMs,
    });
    await server.ensureStarted();
    this.server = server;
    return server;
  }

  private async ensureNativeSession(server: OpenCodeServer): Promise<void> {
    if (this.nativeSessionId) {
      try {
        const session = record(await server.json(`/session/${encodeURIComponent(this.nativeSessionId)}`));
        if (session && session['directory'] === this.options.cwd) return;
        throw new EngineError('resume_mismatch', 'The OpenCode session belongs to another workspace.');
      } catch (error) {
        if (!(error instanceof OpenCodeHttpError) || error.status !== 404) throw error;
      }
    }
    const session = record(await server.json('/session', { method: 'POST', body: JSON.stringify({ title: this.options.sessionId }) }));
    const id = text(session?.['id']);
    if (!id) throw new EngineError('engine_error', 'OpenCode did not return a session id.');
    this.nativeSessionId = id;
  }

  private async handleEvent(turn: OpenCodeTurn, envelope: unknown): Promise<void> {
    const outer = record(envelope);
    const event = record(outer?.['payload']) ?? outer;
    const properties = record(event?.['properties']) ?? {};
    const type = text(event?.['type']);
    if (!type) return;
    if (outer?.['directory'] && outer['directory'] !== this.options.cwd) return;
    const part = record(properties['part']);
    const info = record(properties['info']);
    const messageId = text(info?.['id']) ?? text(part?.['messageID']);
    const eventSessionId = text(properties['sessionID']) ?? text(part?.['sessionID']) ?? text(info?.['sessionID']);
    if (eventSessionId && eventSessionId !== turn.nativeSessionId) return;

    if (type === 'permission.updated') {
      turn.activity = true;
      await this.handlePermission(turn, properties);
      return;
    }
    if (type === 'session.status') {
      const status = record(properties['status']);
      if (status?.['type'] === 'busy') turn.activity = true;
      if (status?.['type'] === 'idle' && turn.promptSubmitted && turn.activity) this.finishTurn(turn, 'ok');
      return;
    }
    if (type === 'session.idle') {
      if (turn.promptSubmitted && turn.activity) this.finishTurn(turn, turn.error ? 'error' : 'ok', turn.error?.code, turn.error?.message);
      return;
    }
    if (type === 'session.error') {
      const providerError = record(properties['error']);
      const data = record(providerError?.['data']);
      const message = text(data?.['message']) ?? 'OpenCode reported a session error.';
      turn.error = { code: 'engine_error', message: message.slice(0, 1000) };
      if (turn.promptSubmitted) this.finishTurn(turn, 'error', turn.error.code, turn.error.message);
      return;
    }
    if (type === 'message.part.updated' && part) {
      turn.activity = true;
      this.handlePartUpdate(turn, part, text(properties['delta']));
      return;
    }
    if (type === 'message.updated' && info) {
      turn.activity = true;
      this.handleMessageUpdate(turn, info, messageId);
    }
  }

  private async handlePermission(turn: OpenCodeTurn, permission: Record<string, unknown>): Promise<void> {
    const permissionId = text(permission['id']);
    if (!permissionId) return;
    const request = mapOpenCodePermission(permission as OpenCodePermission, this.options.cwd, turn.nativeSessionId);
    let response = 'reject';
    if (request && !turn.controller.signal.aborted) {
      turn.hooks.emit({
        type: 'tool_call',
        data: {
          toolCallId: request.toolCallId,
          name: request.name,
          input: request.input,
          ...(request.command ? { command: request.command } : {}),
          ...(request.cwd ? { cwd: request.cwd } : {}),
        },
      });
      try {
        const decision = await turn.hooks.onToolCall(request, turn.controller.signal);
        if (decision.behavior === 'allow' && !turn.controller.signal.aborted) response = 'once';
      } catch {
        response = 'reject';
      }
    }
    const server = this.server;
    if (!server) return;
    await server.json(`/session/${encodeURIComponent(turn.nativeSessionId)}/permissions/${encodeURIComponent(permissionId)}`, {
      method: 'POST',
      body: JSON.stringify({ response }),
    }).catch(() => undefined);
  }

  private handlePartUpdate(turn: OpenCodeTurn, part: Record<string, unknown>, delta: string | undefined): void {
    const partType = text(part['type']);
    const messageId = text(part['messageID']);
    if (!messageId) return;
    if (partType === 'text') {
      const partText = text(part['text']) ?? '';
      if (delta) {
        turn.textByMessage.set(messageId, (turn.textByMessage.get(messageId) ?? '') + delta);
        turn.hooks.emit({ type: 'text_delta', data: { messageId, text: delta } });
      } else if (partText && !turn.textByMessage.has(messageId)) {
        turn.textByMessage.set(messageId, partText);
      }
      return;
    }
    if (partType === 'reasoning') {
      const reasoning = delta ?? text(part['text']);
      if (reasoning) turn.hooks.emit({ type: 'thinking_delta', data: { text: reasoning } });
      return;
    }
    if (partType !== 'tool') return;
    const callId = text(part['callID']);
    const state = record(part['state']);
    if (!callId || !state) return;
    const output = text(state['output']);
    if (output) {
      const previous = turn.toolOutput.get(callId) ?? '';
      if (output.startsWith(previous) && output.length > previous.length) {
        turn.hooks.emit({ type: 'tool_output_delta', data: { toolCallId: callId, text: output.slice(previous.length) } });
      }
      turn.toolOutput.set(callId, output);
    }
    const status = state['status'];
    if ((status === 'completed' || status === 'error') && !turn.completedTools.has(callId)) {
      turn.completedTools.add(callId);
      const metadata = record(state['metadata']);
      const exitCode = typeof metadata?.['exitCode'] === 'number' ? metadata['exitCode'] : undefined;
      const error = text(state['error']);
      turn.hooks.emit({
        type: 'tool_result',
        data: {
          toolCallId: callId,
          ok: status === 'completed',
          output: status === 'error' ? error ?? output ?? 'OpenCode tool failed.' : output ?? '',
          ...(exitCode !== undefined ? { exitCode } : {}),
        },
      });
    }
  }

  private handleMessageUpdate(turn: OpenCodeTurn, info: Record<string, unknown>, messageId: string | undefined): void {
    if (!messageId || turn.completedMessages.has(messageId) || !record(info['time'])?.['completed']) return;
    turn.completedMessages.add(messageId);
    const finalText = turn.textByMessage.get(messageId);
    if (finalText) turn.hooks.emit({ type: 'text', data: { messageId, text: finalText } });
    if (info['role'] !== 'assistant') return;
    const tokens = record(info['tokens']);
    if (!tokens) return;
    const cache = record(tokens['cache']);
    const model = `${text(info['providerID']) ?? ''}/${text(info['modelID']) ?? ''}`.replace(/^\//, '');
    const usage: Extract<EngineEvent, { type: 'usage' }>['data'] = {
      model,
      inputTokens: typeof tokens['input'] === 'number' ? tokens['input'] : 0,
      outputTokens: typeof tokens['output'] === 'number' ? tokens['output'] : 0,
    };
    if (typeof cache?.['read'] === 'number') usage.cacheReadTokens = cache['read'];
    if (typeof info['cost'] === 'number') usage.costUsd = info['cost'];
    turn.hooks.emit({ type: 'usage', data: usage });
  }

  private finishTurn(
    turn: OpenCodeTurn,
    status: TurnResult['status'],
    code?: string,
    message?: string,
  ): void {
    if (turn.settled) return;
    turn.settled = true;
    const error = code && message ? { code, message } : undefined;
    if (error) turn.hooks.emit({ type: 'error', data: error });
    turn.resolve({ status, ...(error ? { error } : {}) });
    turn.eventController.abort();
  }

  private async emitFileChanges(server: OpenCodeServer, sessionId: string, hooks: EngineHooks): Promise<void> {
    try {
      const raw = await server.json<unknown>(`/session/${encodeURIComponent(sessionId)}/diff`);
      if (!Array.isArray(raw)) return;
      for (const value of raw) {
        const file = record(value);
        const name = text(file?.['file']);
        if (!name) continue;
        const before = text(file?.['before']) ?? '';
        const after = text(file?.['after']) ?? '';
        const changeKind = !before && after ? 'add' : before && !after ? 'delete' : 'modify';
        hooks.emit({ type: 'file_change', data: { path: name, changeKind } });
      }
    } catch {
      // A completed response remains useful if the optional diff endpoint is unavailable.
    }
  }
}

async function consumeOpenCodeEvents(
  response: Response,
  onEvent: (event: unknown) => Promise<void>,
  signal: AbortSignal,
): Promise<void> {
  const reader = response.body?.getReader();
  if (!reader) throw new EngineError('engine_error', 'OpenCode did not provide an event stream.');
  const decoder = new TextDecoder();
  let buffer = '';
  try {
    while (!signal.aborted) {
      const chunk = await reader.read();
      if (chunk.done) return;
      buffer += decoder.decode(chunk.value, { stream: true }).replace(/\r\n/g, '\n');
      if (buffer.length > 1_000_000) throw new EngineError('engine_error', 'OpenCode event exceeded the size limit.');
      let separator = buffer.indexOf('\n\n');
      while (separator >= 0) {
        const frame = buffer.slice(0, separator);
        buffer = buffer.slice(separator + 2);
        const payload = frame.split('\n').filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trim()).join('\n');
        if (payload) {
          try {
            await onEvent(JSON.parse(payload) as unknown);
          } catch (error) {
            if (error instanceof SyntaxError) throw new EngineError('engine_error', 'OpenCode sent an invalid event.');
            throw error;
          }
        }
        separator = buffer.indexOf('\n\n');
      }
    }
  } finally {
    await reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}

/** Factory used by src/engine-registry.ts. */
export function createOpenCodeAdapter(
  config: AppConfig,
  context?: EngineFactoryContext,
  overrides: OpenCodeAdapterOverrides = {},
): EngineAdapter {
  const baseEnv = overrides.baseEnv ?? ((sessionId?: string) => context?.buildEnv(sessionId) ?? {});
  return new OpenCodeAdapter({
    binary: overrides.opencodeBinPath ?? config.opencodeBinPath ?? DEFAULT_OPENCODE_BIN,
    asAgentPath: config.asAgentPath,
    agentHome: config.agentHome,
    controlCwd: config.repoDir,
    baseEnv,
    fetch: overrides.fetch ?? globalThis.fetch,
    spawn: overrides.spawn ?? nodeOpenCodeSpawn,
    runner: overrides.runner ?? createRunner(config.asAgentPath),
    port: overrides.port ?? freePort,
    now: overrides.now ?? Date.now,
    logger: overrides.logger ?? (context ? toEngineLogger(context.logger, 'engine.opencode') : consoleEngineLogger('engine.opencode')),
    startupTimeoutMs: overrides.startupTimeoutMs ?? SERVER_START_TIMEOUT_MS,
  });
}