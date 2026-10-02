// Minimal shared HTTP shell for the writing AI sidecars. No framework — the
// surface is four routes (the completion endpoint, /usage, /healthz, /readyz)
// so express is not worth the dependency.

import http from 'node:http';
import { AuthExpiredError, LaneBusyError, QuotaExceededError } from './engine.mjs';

const MAX_BODY_BYTES = 4 * 1024 * 1024; // 4 MiB — a tier-1 writing prompt is ~36k tokens.
const LANE_BUSY_RETRY_AFTER_S = '30'; // the backend honours at most 30 s

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (c) => {
      size += c.length;
      if (size > MAX_BODY_BYTES) {
        reject(new Error('payload_too_large'));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function sendJson(res, status, obj, headers = {}) {
  const body = JSON.stringify(obj);
  res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(body), ...headers });
  res.end(body);
}

// Typed engine failures -> [status, code, type] in shapes the .NET AiProviderErrorParser classifies:
// quota_exceeded = QuotaExhausted and 401 auth_expired/authentication_error = Auth (the grade moves to its
// next route; the Claude Max provider's circuit is exempt and never opens, rule MAX-ALWAYS-ON);
// 503 lane_busy/overloaded_error = Overloaded (retried, then failed over). All are per-request answers;
// the sidecar keeps serving.
function failure(err) {
  if (err instanceof QuotaExceededError || err?.quotaExceeded) return [429, 'quota_exceeded', 'rate_limit_error'];
  if (err instanceof AuthExpiredError) return [401, 'auth_expired', 'authentication_error'];
  if (err instanceof LaneBusyError) return [503, 'lane_busy', 'overloaded_error'];
  return [502, 'engine_error'];
}

// One line per failed request so `docker logs` explains a 502/429: method, path, status, error
// code and duration ONLY. err.message can carry a tail of the CLI output (model text, prompt
// quotes, learner content), so it is never logged here; a duration near the CLI timeout means
// the engine timed out.
function logFailure(engineName, req, url, status, code, startedAt) {
  // eslint-disable-next-line no-console
  console.error(`[writing-ai:${engineName}] ${req.method} ${url.pathname} -> ${status} ${code} in ${Date.now() - startedAt}ms`);
}

/**
 * @param {object} opts
 * @param {string} opts.engineName            e.g. "claude" | "codex"
 * @param {(req:object, ctx:{signal:AbortSignal}) => Promise<object>} opts.onCompletion  returns the provider-native body; `signal` aborts when the client goes away
 * @param {() => Promise<object>} opts.onUsage                returns the quota snapshot
 * @param {{full:boolean, queueDepth:number}} [opts.lane]     the engine lane, for /readyz
 * @param {{status:() => Promise<{authOk:boolean|null, plan:string|null}>}} [opts.login]  login probe, for /readyz
 * @param {string} [opts.completionPath]      e.g. "/v1/messages" or "/v1/chat/completions"
 * @param {number} [opts.port]
 */
export function createSidecarServer({ engineName, onCompletion, onUsage, lane, login, completionPath, port = 8080 }) {
  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://x');
    const startedAt = Date.now();
    // Aborted when the client disconnects before the response is written (it timed out or gave up):
    // a queued request is then dropped before it runs and a running CLI is killed.
    const abort = new AbortController();
    res.on('close', () => { if (!res.writableFinished) abort.abort(); });
    try {
      // Liveness only: the container healthcheck stays here, so a dead login never restart-loops it.
      if (req.method === 'GET' && url.pathname === '/healthz') {
        sendJson(res, 200, { ok: true, engine: engineName });
        return;
      }
      // Readiness for display: not ready only on a KNOWN dead login or a full queue (an unknown
      // login is ready). It never changes what the completion route accepts.
      if (req.method === 'GET' && url.pathname === '/readyz') {
        const { authOk, plan } = await login.status();
        const reason = authOk === false ? 'auth_expired' : lane.full ? 'lane_full' : null;
        sendJson(res, reason ? 503 : 200, { ready: !reason, reason, queueDepth: lane.queueDepth, authOk, plan });
        return;
      }
      if (req.method === 'GET' && url.pathname === '/usage') {
        const usage = await onUsage();
        sendJson(res, 200, usage);
        return;
      }
      if (req.method === 'POST' && (url.pathname === completionPath || `/v1${url.pathname}` === completionPath)) {
        let parsed;
        try {
          parsed = JSON.parse(await readBody(req) || '{}');
        } catch (err) {
          sendJson(res, err.message === 'payload_too_large' ? 413 : 400, { error: { code: 'bad_request', message: 'Invalid JSON body' } });
          return;
        }
        const body = await onCompletion(parsed, { signal: abort.signal });
        sendJson(res, 200, body);
        return;
      }
      sendJson(res, 404, { error: { code: 'not_found', message: 'Unknown route' } });
    } catch (err) {
      if (abort.signal.aborted) {
        logFailure(engineName, req, url, 499, 'client_closed', startedAt);
        return;
      }
      const [status, code, type] = failure(err);
      logFailure(engineName, req, url, status, code, startedAt);
      sendJson(
        res,
        status,
        { error: { code, message: String(err?.message || err), ...(type && { type }) } },
        status === 503 ? { 'retry-after': LANE_BUSY_RETRY_AFTER_S } : {},
      );
    }
  });
  server.listen(port, '0.0.0.0', () => {
    // eslint-disable-next-line no-console
    console.log(`[writing-ai:${engineName}] listening on :${port}`);
  });
  return server;
}
