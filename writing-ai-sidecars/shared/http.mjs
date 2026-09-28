// Minimal shared HTTP shell for the writing AI sidecars. No framework — the
// surface is two routes (the completion endpoint + /usage) so express is not
// worth the dependency.

import http from 'node:http';
import { QuotaExceededError } from './engine.mjs';

const MAX_BODY_BYTES = 4 * 1024 * 1024; // 4 MiB — a tier-1 writing prompt is ~36k tokens.

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

function sendJson(res, status, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(status, { 'content-type': 'application/json', 'content-length': Buffer.byteLength(body) });
  res.end(body);
}

/**
 * @param {object} opts
 * @param {string} opts.engineName            e.g. "claude" | "codex"
 * @param {(req:object) => Promise<object>} opts.onCompletion  returns the provider-native response body
 * @param {() => Promise<object>} opts.onUsage                returns the quota snapshot
 * @param {string} [opts.completionPath]      e.g. "/v1/messages" or "/v1/chat/completions"
 * @param {number} [opts.port]
 */
export function createSidecarServer({ engineName, onCompletion, onUsage, completionPath, port = 8080 }) {
  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://x');
    try {
      if (req.method === 'GET' && url.pathname === '/healthz') {
        sendJson(res, 200, { ok: true, engine: engineName });
        return;
      }
      if (req.method === 'GET' && url.pathname === '/usage') {
        const usage = await onUsage();
        sendJson(res, 200, usage);
        return;
      }
      if (req.method === 'POST' && url.pathname === completionPath) {
        let parsed;
        try {
          parsed = JSON.parse(await readBody(req) || '{}');
        } catch (err) {
          sendJson(res, err.message === 'payload_too_large' ? 413 : 400, { error: { code: 'bad_request', message: 'Invalid JSON body' } });
          return;
        }
        const body = await onCompletion(parsed);
        sendJson(res, 200, body);
        return;
      }
      sendJson(res, 404, { error: { code: 'not_found', message: 'Unknown route' } });
    } catch (err) {
      if (err instanceof QuotaExceededError || err?.quotaExceeded) {
        sendJson(res, 429, { error: { code: 'quota_exceeded', message: err.message, type: 'rate_limit_error' } });
        return;
      }
      sendJson(res, 502, { error: { code: 'engine_error', message: String(err?.message || err) } });
    }
  });
  server.listen(port, '0.0.0.0', () => {
    // eslint-disable-next-line no-console
    console.log(`[writing-ai:${engineName}] listening on :${port}`);
  });
  return server;
}
