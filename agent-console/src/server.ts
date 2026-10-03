import { createHash, timingSafeEqual } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import type { IncomingHttpHeaders } from 'node:http';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import Fastify, { type FastifyInstance, type FastifyRequest } from 'fastify';
import { ulid } from 'ulid';
import {
  ApprovalRegistry,
  handleProxyApproval,
  validateProxyBody,
  type ProxyApprovalBody,
  type ProxyApprovalResponse,
} from './approvals.js';
import { ConfigError, loadConfig, type AppConfig } from './config.js';
import type { ConsoleStatus, Engine, GithubStatus, ShipState } from './contract.js';
import { ENGINES, SYSTEM_SESSION_ID, ULID_PATTERN } from './contract.js';
import { DockerClient, createHttpTransport } from './docker.js';
import { EngineRegistry, EngineUnavailableError } from './engine-registry.js';
import type { ConnectFlow, EngineAdapter, EngineAuth, EngineConnectOptions } from './engines/types.js';
import { buildAgentEnv } from './env.js';
import { HttpError, badRequest, errorEnvelope, notFound, unauthorized, forbidden } from './errors.js';
import { createRunner } from './exec.js';
import { GithubTokens } from './github.js';
import { ControlState, LeaseManager, killAgentProcesses, stopAll, stopSessionContainers } from './lease.js';
import { createLogger, type Logger } from './log.js';
import { ProxyGrants } from './proxies.js';
import { Redactor } from './redact.js';
import { pruneEngineTranscripts, pruneOpenCodeSessions, removeEngineTranscripts } from './retention.js';
import { SessionManager, parseSessionListQuery } from './sessions.js';
import { ShipExecutor } from './ship.js';
import { SnapshotService } from './snapshot.js';
import { parseAfter, streamSessionEvents } from './sse.js';
import { buildStatus } from './status.js';
import { Store } from './store.js';
import { asObject, reqString } from './validate.js';
import { Workspace } from './workspace.js';

// Fastify control server on :8410 (CONTRACT.md §3). Every route except
// GET /healthz requires X-Oet-Internal-Token (constant-time) and an
// X-Oet-Owner-Account from the env allow-list; /internal/approvals is for the
// egress/docker proxies and requires X-Oet-Proxy-Token instead (§6).

export interface ServerContext {
  config: Pick<
    AppConfig,
    'version' | 'internalToken' | 'proxyToken' | 'ownerAccountIds' | 'maxConcurrentTurns' | 'updatePendingFile' | 'proxyApprovalWaitMs' | 'agentUid'
  >;
  logger: Logger;
  store: Store;
  sessions: SessionManager;
  approvals: ApprovalRegistry;
  engines: Pick<EngineRegistry, 'get' | 'status' | 'invalidate'>;
  lease: LeaseManager;
  control: ControlState;
  github: { setTokens(input: { agentToken?: unknown; shipToken?: unknown }): Promise<GithubStatus>; status(): Promise<GithubStatus> };
  ship: {
    start(sessionId: string, body: unknown): Promise<ShipState>;
    get(sessionId: string): ShipState | null;
    dispatchConsoleUpdate(): Promise<{ dispatched: boolean; detail?: string }>;
  };
  stopAll(): Promise<{ stoppedTurns: number; killedProcesses: number }>;
  proxyApproval(body: ProxyApprovalBody, signal: AbortSignal): Promise<ProxyApprovalResponse>;
}

export interface ServerOptions {
  heartbeatMs?: number;
}

/** Constant-time token comparison (hash first so lengths never leak). */
export function tokenMatches(expected: string | null, provided: unknown): boolean {
  if (!expected || typeof provided !== 'string' || provided.length === 0) return false;
  const a = createHash('sha256').update(expected, 'utf8').digest();
  const b = createHash('sha256').update(provided, 'utf8').digest();
  return timingSafeEqual(a, b);
}

function headerValue(req: { headers: IncomingHttpHeaders }, name: string): string | undefined {
  const value = req.headers[name];
  return Array.isArray(value) ? value[0] : value;
}

function requireUlid(value: string, code: string, what: string): string {
  if (!ULID_PATTERN.test(value)) throw notFound(code, `No such ${what}.`);
  return value;
}

function requireEngine(value: string): Engine {
  if (!(ENGINES as readonly string[]).includes(value)) throw notFound('engine_not_found', 'Unknown engine.');
  return value as Engine;
}

function flowIdFrom(obj: Record<string, unknown>): string {
  const flowId = reqString(obj, 'flowId', 128);
  if (!/^[A-Za-z0-9_.:-]+$/.test(flowId)) throw badRequest('bad_request', 'flowId is invalid.');
  return flowId;
}

function stripProto(_key: string, value: unknown): unknown {
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    delete (value as Record<string, unknown>).__proto__;
  }
  return value;
}

export function buildServer(ctx: ServerContext, options: ServerOptions = {}): FastifyInstance {
  const app = Fastify({
    loggerInstance: ctx.logger,
    bodyLimit: 2 * 1024 * 1024,
    requestIdHeader: false,
    genReqId: (req) => {
      const provided = req.headers['x-request-id'];
      const id = Array.isArray(provided) ? provided[0] : provided;
      return typeof id === 'string' && /^[A-Za-z0-9._:-]{1,128}$/.test(id) ? id : ulid();
    },
  });

  // JSON bodies: empty body → {}; proto keys dropped.
  app.removeContentTypeParser('application/json');
  app.addContentTypeParser('application/json', { parseAs: 'string' }, (_req, body, done) => {
    const text = String(body);
    if (text.trim() === '') {
      done(null, {});
      return;
    }
    try {
      done(null, JSON.parse(text, stripProto));
    } catch {
      done(new HttpError(400, 'bad_json', 'Body is not valid JSON.'), undefined);
    }
  });

  app.setErrorHandler((error: Error, request, reply) => {
    if (error instanceof HttpError) {
      return reply.code(error.status).send(errorEnvelope(error.code, error.message));
    }
    if (error instanceof EngineUnavailableError) {
      return reply.code(500).send(errorEnvelope('engine_unavailable', error.message));
    }
    const status = (error as { statusCode?: unknown }).statusCode;
    if (typeof status === 'number' && status >= 400 && status < 500) {
      const code = status === 413 ? 'payload_too_large' : status === 415 ? 'unsupported_media_type' : 'bad_request';
      return reply.code(status).send(errorEnvelope(code, error.message));
    }
    request.log.error({ err: error }, 'unhandled error');
    return reply.code(500).send(errorEnvelope('internal_error', 'Internal error.'));
  });
  app.setNotFoundHandler((_request, reply) => reply.code(404).send(errorEnvelope('not_found', 'Route not found.')));

  app.addHook('onRequest', async (request: FastifyRequest) => {
    const route = request.routeOptions.url;
    if (request.method === 'GET' && route === '/healthz') return;
    if (route === '/internal/approvals') {
      if (!ctx.config.proxyToken) throw new HttpError(500, 'proxy_token_missing', 'Proxy approvals are disabled: no proxy token configured.');
      if (!tokenMatches(ctx.config.proxyToken, headerValue(request, 'x-oet-proxy-token'))) {
        throw unauthorized('Missing or invalid proxy token.');
      }
      return;
    }
    if (!tokenMatches(ctx.config.internalToken, headerValue(request, 'x-oet-internal-token'))) throw unauthorized();
    const account = headerValue(request, 'x-oet-owner-account')?.trim().toLowerCase();
    if (!account || !ctx.config.ownerAccountIds.has(account)) {
      throw forbidden('not_owner', 'This console is restricted to the owner account.');
    }
  });

  // ------------------------------------------------------------- health

  app.get('/healthz', async () => ({
    ok: true,
    version: ctx.config.version,
    activeTurns: ctx.sessions.activeTurnCount(),
    draining: ctx.control.draining,
  }));

  // ------------------------------------------------------------- status

  app.get('/v1/status', async (): Promise<ConsoleStatus> =>
    buildStatus({
      version: ctx.config.version,
      maxConcurrentTurns: ctx.config.maxConcurrentTurns,
      updatePendingFile: ctx.config.updatePendingFile,
      control: ctx.control,
      lease: ctx.lease,
      activeTurns: () => ctx.sessions.activeTurnCount(),
      engineStatus: (engine) => ctx.engines.status(engine),
      githubStatus: () => ctx.github.status(),
      systemApprovals: () => ctx.approvals.listPending(SYSTEM_SESSION_ID),
      logger: ctx.logger,
    }),
  );

  app.post('/v1/lease', async (request) => {
    const obj = asObject(request.body, false);
    const expiresAt = reqString(obj, 'expiresAt', 64);
    try {
      return { expiresAt: ctx.lease.set(expiresAt) };
    } catch {
      throw badRequest('bad_request', 'expiresAt must be an ISO-8601 timestamp.');
    }
  });

  // --------------------------------------------------------------- auth

  const adapterFor = (engineParam: string): Promise<EngineAdapter> => ctx.engines.get(requireEngine(engineParam));

  app.post<{ Params: { engine: string } }>('/v1/auth/:engine/connect', async (request): Promise<ConnectFlow> => {
    const engine = requireEngine(request.params.engine);
    let options: EngineConnectOptions | undefined;
    if (engine === 'opencode') {
      const obj = asObject(request.body, false);
      const providerId = reqString(obj, 'providerId', 128).trim();
      if (!/^[A-Za-z0-9_.-]{1,128}$/.test(providerId)) throw badRequest('bad_request', 'providerId is invalid.');
      const apiKey = obj['apiKey'];
      if (apiKey !== undefined) {
        if (typeof apiKey !== 'string' || !apiKey.trim() || apiKey.length > 512 || /[\u0000-\u001f\u007f]/.test(apiKey)) {
          throw badRequest('bad_request', 'apiKey is invalid.');
        }
        options = { providerId, apiKey: apiKey.trim() };
      } else {
        const methodIndex = obj['methodIndex'];
        if (typeof methodIndex !== 'number' || !Number.isInteger(methodIndex) || methodIndex < 0 || methodIndex > 100) {
          throw badRequest('bad_request', 'methodIndex must be an integer from 0 through 100.');
        }
        options = { providerId, methodIndex };
      }
    }
    const flow = await (await adapterFor(engine)).connect(options);
    ctx.engines.invalidate(engine);
    return flow;
  });

  app.get<{ Params: { engine: string; flowId: string } }>('/v1/auth/:engine/flows/:flowId', async (request): Promise<ConnectFlow> => {
    const flow = (await adapterFor(request.params.engine)).getFlow(request.params.flowId);
    if (!flow) throw notFound('flow_not_found', 'No such sign-in flow.');
    return flow;
  });

  app.post<{ Params: { engine: string } }>('/v1/auth/:engine/code', async (request): Promise<ConnectFlow> => {
    const engine = requireEngine(request.params.engine);
    if (engine !== 'claude' && engine !== 'opencode') throw badRequest('not_supported', 'Paste-back codes are only used by Claude and OpenCode OAuth flows.');
    const obj = asObject(request.body, false);
    const flowId = flowIdFrom(obj);
    const code = reqString(obj, 'code', 4096).trim();
    const flow = await (await adapterFor(engine)).submitCode(flowId, code);
    ctx.engines.invalidate(engine);
    return flow;
  });

  app.post<{ Params: { engine: string } }>('/v1/auth/:engine/cancel', async (request): Promise<ConnectFlow> => {
    const engine = requireEngine(request.params.engine);
    const flowId = flowIdFrom(asObject(request.body, false));
    const flow = await (await adapterFor(engine)).cancel(flowId);
    ctx.engines.invalidate(engine);
    return flow;
  });

  app.post<{ Params: { engine: string } }>('/v1/auth/:engine/logout', async (request): Promise<EngineAuth> => {
    const engine = requireEngine(request.params.engine);
    const auth = await (await adapterFor(engine)).logout();
    ctx.engines.invalidate(engine);
    return auth;
  });

  app.put('/v1/github-tokens', async (request): Promise<GithubStatus> => {
    const obj = asObject(request.body, false);
    const input: { agentToken?: unknown; shipToken?: unknown } = {};
    if ('agentToken' in obj) input.agentToken = obj.agentToken;
    if ('shipToken' in obj) input.shipToken = obj.shipToken;
    return ctx.github.setTokens(input);
  });

  // ----------------------------------------------------------- sessions

  // Optional filters: q, engine, status, includeArchived, before (updatedAt cursor), limit (CONTRACT.md §3).
  app.get('/v1/sessions', async (request) => ctx.sessions.list(parseSessionListQuery(request.query)));

  // The onRequest hook already checked X-Oet-Owner-Account against the allow-list.
  app.post('/v1/sessions', async (request) =>
    ctx.sessions.create(request.body, headerValue(request, 'x-oet-owner-account')?.trim().toLowerCase()),
  );

  app.get<{ Params: { id: string } }>('/v1/sessions/:id', async (request) =>
    ctx.sessions.get(requireUlid(request.params.id, 'session_not_found', 'session')),
  );

  app.patch<{ Params: { id: string } }>('/v1/sessions/:id', async (request) =>
    ctx.sessions.patch(requireUlid(request.params.id, 'session_not_found', 'session'), request.body),
  );

  app.post<{ Params: { id: string } }>('/v1/sessions/:id/messages', async (request) =>
    ctx.sessions.sendMessage(requireUlid(request.params.id, 'session_not_found', 'session'), request.body),
  );

  app.post<{ Params: { id: string } }>('/v1/sessions/:id/interrupt', async (request) =>
    ctx.sessions.interrupt(requireUlid(request.params.id, 'session_not_found', 'session')),
  );

  app.post<{ Params: { id: string } }>('/v1/sessions/:id/handoff', async (request) =>
    ctx.sessions.handoff(requireUlid(request.params.id, 'session_not_found', 'session'), request.body),
  );

  app.post<{ Params: { id: string; approvalId: string } }>('/v1/sessions/:id/approvals/:approvalId', async (request) => {
    const id = requireUlid(request.params.id, 'session_not_found', 'session');
    const approvalId = requireUlid(request.params.approvalId, 'approval_not_found', 'approval');
    return ctx.sessions.resolveApproval(id, approvalId, request.body);
  });

  app.get<{ Params: { id: string } }>('/v1/sessions/:id/diff', async (request) =>
    ctx.sessions.diff(requireUlid(request.params.id, 'session_not_found', 'session')),
  );

  app.post<{ Params: { id: string } }>('/v1/sessions/:id/ship', async (request) =>
    ctx.ship.start(requireUlid(request.params.id, 'session_not_found', 'session'), request.body),
  );

  app.get<{ Params: { id: string } }>('/v1/sessions/:id/ship', async (request, reply) => {
    const id = requireUlid(request.params.id, 'session_not_found', 'session');
    ctx.sessions.requireSession(id);
    const state = ctx.ship.get(id);
    return reply.type('application/json; charset=utf-8').send(JSON.stringify(state));
  });

  app.get<{ Params: { id: string }; Querystring: { after?: string } }>(
    '/v1/sessions/:id/events',
    async (request, reply) => {
      const id = requireUlid(request.params.id, 'session_not_found', 'session');
      if (!ctx.sessions.exists(id)) throw notFound('session_not_found', 'No such session.');
      const after = parseAfter(request.query.after ?? headerValue(request, 'last-event-id'));
      reply.hijack();
      reply.raw.writeHead(200, {
        'Content-Type': 'text/event-stream; charset=utf-8',
        'Cache-Control': 'no-cache, no-transform',
        Connection: 'keep-alive',
        'X-Accel-Buffering': 'no',
      });
      reply.raw.flushHeaders();
      try {
        await streamSessionEvents(ctx.store, id, after, request.raw, reply.raw, options.heartbeatMs ? { heartbeatMs: options.heartbeatMs } : {});
      } catch (error) {
        request.log.warn({ err: error }, 'event stream ended with an error');
      } finally {
        if (!reply.raw.writableEnded) reply.raw.end();
      }
    },
  );

  // -------------------------------------------------------------- admin

  app.post('/v1/admin/stop-all', async (request) => {
    request.log.warn('kill switch: stop-all requested');
    return ctx.stopAll();
  });

  app.post('/v1/admin/drain', async (request) => {
    const obj = asObject(request.body, false);
    const draining = obj.draining;
    if (typeof draining !== 'boolean') throw badRequest('bad_request', 'draining must be a boolean.');
    ctx.control.draining = draining;
    // Resuming (draining:false) also clears a previous kill-switch stop.
    if (!draining) ctx.control.killed = false;
    return { draining: ctx.control.draining, activeTurns: ctx.sessions.activeTurnCount() };
  });

  // GDPR erasure of one session (runbook §11.2). Control-plane only: not relayed by
  // the public API; run from SSH with bin/oet-console-erase (reads the token file).
  app.post<{ Params: { id: string } }>('/v1/admin/sessions/:id/erase', async (request) => {
    const id = requireUlid(request.params.id, 'session_not_found', 'session');
    if (id === SYSTEM_SESSION_ID) throw notFound('session_not_found', 'No such session.');
    return ctx.sessions.erase(id);
  });

  // "Apply update" (CONTRACT.md §5 → API POST /apply-update): drain (no new turns),
  // then dispatch agent-console.yml with apply=true using the Ship PAT. The rollout
  // recreates the stack; a failed dispatch still leaves the console draining so the
  // owner can run the workflow by hand (or POST /v1/admin/drain {draining:false}).
  app.post('/v1/admin/apply-update', async (request) => {
    ctx.control.draining = true;
    request.log.warn('apply-update: draining and dispatching the console workflow');
    const result = await ctx.ship.dispatchConsoleUpdate();
    return {
      draining: ctx.control.draining,
      activeTurns: ctx.sessions.activeTurnCount(),
      dispatched: result.dispatched,
      ...(result.detail ? { detail: result.detail } : {}),
    };
  });

  // ------------------------------------------------------ proxy bridge

  app.post('/internal/approvals', async (request, reply) => {
    let body: ProxyApprovalBody;
    try {
      body = validateProxyBody(request.body);
    } catch (error) {
      throw badRequest('bad_request', error instanceof Error ? error.message : 'Invalid body.');
    }
    const controller = new AbortController();
    const onClose = (): void => {
      if (!reply.raw.writableFinished) controller.abort();
    };
    reply.raw.on('close', onClose);
    try {
      return await ctx.proxyApproval(body, controller.signal);
    } finally {
      reply.raw.off('close', onClose);
    }
  });

  return app;
}

// ----------------------------------------------------------- composition

export interface Runtime {
  context: ServerContext;
  boot(): Promise<void>;
  shutdown(): Promise<void>;
}

export function createRuntime(config: AppConfig, logger: Logger): Runtime {
  for (const dir of [config.dataDir, config.sessionsDir, config.controlHome, config.ship.workDir]) {
    mkdirSync(dir, { recursive: true, mode: 0o700 });
  }
  const redactor = new Redactor([config.internalToken, config.proxyToken, config.agentDatabaseUrl]);
  const store = new Store({ dbPath: config.dbPath, sessionsDir: config.sessionsDir, redactor });
  const lease = new LeaseManager({ maxMs: config.leaseMaxMs });
  const control = new ControlState();
  const run = createRunner(config.asAgentPath);

  const approvals = new ApprovalRegistry({
    ttlMs: config.approvalTtlMs,
    emit: (sessionId, type, data, turnId) => {
      try {
        store.appendEvent(sessionId, type, data, turnId);
      } catch (error) {
        logger.error({ err: error, sessionId, type }, 'failed to persist approval event');
      }
    },
    onOpen: ({ request, sessionId, turnId, source }) =>
      store.recordApproval({
        id: request.approvalId,
        sessionId,
        turnId: turnId ?? null,
        toolCallId: request.toolCallId,
        source,
        summary: request.summary,
        createdAt: new Date().toISOString(),
        expiresAt: request.expiresAt,
      }),
    onResolve: (approvalId, resolution) => store.resolveApprovalRecord(approvalId, resolution.decision, resolution.by),
    expiryBy: () => (lease.isActive() ? 'timeout' : 'lease_expired'),
    logger,
  });

  const engines = new EngineRegistry(config, logger, redactor);
  const workspace = new Workspace(config, run, logger);
  const docker = new DockerClient(createHttpTransport(config.dockerHost, config.proxyToken));
  const snapshots = new SnapshotService({
    docker,
    container: config.snapshot.container,
    script: config.snapshot.script,
    fullDumpMinIntervalMs: config.snapshot.fullDumpMinIntervalMs,
    timeoutMs: config.snapshot.timeoutMs,
    logger,
  });
  const github = new GithubTokens(config, run, redactor, logger);
  const proxyGrants = new ProxyGrants({
    egressProxyUrl: config.egressProxyUrl,
    dockerHost: config.dockerHost,
    controlToken: config.proxyToken,
    logger,
  });
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
    readManual: () => readFile(config.manualPath, 'utf8'),
    revokeProxyGrants: (sessionId) => proxyGrants.revokeSession(sessionId),
    pruneEngineTranscripts: async (days) =>
      (await pruneEngineTranscripts(run, config, days, logger)) + (await pruneOpenCodeSessions(run, config, days, logger)),
    removeEngineTranscripts: (ids, engine, cwd) => removeEngineTranscripts(run, config, ids, logger, engine, cwd),
  });
  const ship = new ShipExecutor({
    config,
    store,
    run,
    credentials: github,
    sessions,
    agentEnv: () => buildAgentEnv(config),
    emit: (sessionId, data) => {
      try {
        store.appendEvent(sessionId, 'ship', data);
      } catch (error) {
        logger.error({ err: error, sessionId }, 'failed to persist ship event');
      }
    },
    redactor,
    logger,
  });

  lease.onExpired(() => {
    logger.warn('owner lease lapsed: autopilot sessions drop to guarded; new turns blocked');
    sessions.onLeaseExpired();
  });

  const context: ServerContext = {
    config,
    logger,
    store,
    sessions,
    approvals,
    engines,
    lease,
    control,
    github,
    ship,
    stopAll: () =>
      stopAll(control, {
        abortAllTurns: () => sessions.abortAllTurns(),
        cancelApprovals: () => approvals.cancelAll('kill'),
        killAgentProcesses: () => killAgentProcesses(run, config.agentUid),
        stopSessionContainers: () => stopSessionContainers(docker, logger),
        revokeProxyGrants: () => proxyGrants.revokeAll(),
        logger,
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
          logger,
        },
        signal,
      ),
  };

  let sweepTimer: NodeJS.Timeout | null = null;
  return {
    context,
    async boot() {
      sessions.boot();
      ship.resume();
      ship.startWatchdog();
      lease.start();
      await github.loadKnownSecrets().catch((error: unknown) => logger.warn({ err: error }, 'could not load GitHub token values for redaction'));
      void workspace.ensureRepo().catch((error: unknown) =>
        logger.warn({ err: redactor.redact(String(error)) }, 'workspace repository not ready yet (set the agent token); retrying on first session'),
      );
      const sweep = (): void => {
        void sessions
          .sweep()
          .then((removed) => {
            if (removed > 0) logger.info({ removed }, 'retention sweep removed sessions');
          })
          .catch((error: unknown) => logger.warn({ err: error }, 'retention sweep failed'));
      };
      setTimeout(sweep, 60_000).unref();
      sweepTimer = setInterval(sweep, 6 * 60 * 60_000);
      sweepTimer.unref();
    },
    async shutdown() {
      if (sweepTimer) clearInterval(sweepTimer);
      ship.stop();
      lease.stop();
      await sessions.shutdown();
      await engines.shutdown();
      store.close();
    },
  };
}

export async function main(): Promise<void> {
  let config: AppConfig;
  try {
    config = loadConfig();
  } catch (error) {
    // Refuse to start: never fail open without the internal token.
    const message = error instanceof ConfigError ? error.message : String(error);
    process.stderr.write(`[oet-agent-console] refusing to start: ${message}\n`);
    process.exit(1);
  }
  const logger = createLogger(config.logLevel);
  const runtime = createRuntime(config, logger);
  const app = buildServer(runtime.context);
  await runtime.boot();
  await app.listen({ host: config.host, port: config.port });
  logger.info({ port: config.port, version: config.version }, 'oet-agent-console listening');

  let stopping = false;
  const stop = (signal: string): void => {
    if (stopping) return;
    stopping = true;
    logger.info({ signal }, 'shutting down');
    void (async () => {
      try {
        await app.close();
        await runtime.shutdown();
      } finally {
        process.exit(0);
      }
    })();
  };
  process.on('SIGTERM', () => stop('SIGTERM'));
  process.on('SIGINT', () => stop('SIGINT'));
}

const entry = process.argv[1];
if (entry && import.meta.url === pathToFileURL(entry).href) {
  void main().catch((error: unknown) => {
    process.stderr.write(`[oet-agent-console] fatal: ${error instanceof Error ? error.message : String(error)}\n`);
    process.exit(1);
  });
}
