// SignalR long-polling client for k6 (the transport browsers use through the web origin: the Next.js
// BFF proxy cannot upgrade WebSockets, and lib/env.ts forces LongPolling when the API base is
// '/api/backend'). One connection per learner, held for the whole session; think time is spent
// polling it, exactly like a browser tab that is open but idle.
//
// Wire protocol (TransportProtocols.md, long polling):
//   POST {hub}/negotiate?negotiateVersion=1        -> { connectionToken, availableTransports }
//   GET  {hub}?id=token                            -> first poll returns at once, no data
//   POST {hub}?id=token  <handshake>0x1E           -> server answers {}0x1E on the next poll
//   GET  {hub}?id=token                            -> blocks until a frame (server pings every 15 s)
//   POST {hub}?id=token  <frame>0x1E               -> client ping / invocation
// The server drops a client silent for 45 s (ClientTimeoutInterval), so a ping is sent at least
// every 10 s. HubConnect allows 30 requests / min / user; one poll + one ping per ~15 s stays far
// below it.

import { sleep } from 'k6';
import { ACTIONS } from './contract.js';
import { retryAfterSeconds } from './classify.mjs';
import { call, phaseNow } from './http.js';
import * as M from './metrics.js';
import {
  handshakeFrame, invocationFrame, negotiateUrl, parseFrames, parseNegotiate, pingFrame, summariseFrames,
  transportUrl,
} from './signalr-frames.mjs';

const POLL_TIMEOUT = '45s';
const PING_EVERY_MS = 10000;

function connectFailed(failed) {
  const tags = { phase: phaseNow().phase };
  M.signalrConnectFailed.add(failed, tags);
}

function newConnection(sess, hubUrl, token) {
  return {
    hubUrl,
    url: transportUrl(hubUrl, token),
    token,
    handshaken: false,
    closed: false,
    nextInvocation: 1,
    lastPingMs: 0,
    completions: {},
    invocations: [],
    sess,
  };
}

/** One long poll: blocks until the server has a frame (<= ~15 s with pings). Updates `conn`. */
export function pollOnce(sess, conn) {
  const r = call(sess, ACTIONS.hubPoll, { url: `${conn.url}&_=${Date.now()}`, timeout: POLL_TIMEOUT });
  if (r.status === 204 || r.status === 404 || r.status === 410) {
    // 204: the server ended the connection; 404/410: it no longer knows the token (slot cutover).
    conn.closed = true;
    return null;
  }
  if (!r.ok) {
    if (r.status === 0 || r.status >= 500) conn.closed = true;
    // 429 from the HubConnect limiter (or any other refusal): never spin on it.
    else sleep(r.status === 429 ? Math.min(10, retryAfterSeconds(r.retryAfter)) : 2);
    return null;
  }
  const { frames } = parseFrames(r.text);
  const summary = summariseFrames(frames);
  if (summary.handshakeOk) conn.handshaken = true;
  if (summary.closed) conn.closed = true;
  for (const completion of summary.completions) conn.completions[completion.invocationId] = completion;
  for (const invocation of summary.invocations) conn.invocations.push(invocation);
  const messages = summary.invocations.length + summary.completions.length;
  if (messages > 0) M.signalrMessages.add(messages);
  return summary;
}

/** Negotiate, start the transport and complete the JSON handshake. Returns the connection or null. */
export function hubConnect(sess, hubPath) {
  const hubUrl = `${sess.prefix}${hubPath}`;
  const negotiated = call(sess, ACTIONS.hubNegotiate, { url: negotiateUrl(hubUrl), body: '' });
  if (!negotiated.ok) {
    connectFailed(1);
    return null;
  }
  const parsed = parseNegotiate(negotiated.text);
  if (parsed === null || !parsed.supportsLongPolling) {
    connectFailed(1);
    return null;
  }
  const conn = newConnection(sess, hubUrl, parsed.connectionToken);

  // First poll "finishes initialising the connection" and returns without data.
  const first = call(sess, ACTIONS.hubPoll, { url: `${conn.url}&_=${Date.now()}`, timeout: '30s' });
  if (!first.ok) {
    connectFailed(1);
    return null;
  }
  const sent = call(sess, ACTIONS.hubSend, { url: conn.url, body: handshakeFrame() });
  if (!sent.ok) {
    connectFailed(1);
    return null;
  }
  for (let attempt = 0; attempt < 4 && !conn.handshaken && !conn.closed; attempt += 1) pollOnce(sess, conn);
  connectFailed(conn.handshaken ? 0 : 1);
  if (!conn.handshaken) return null;
  conn.lastPingMs = Date.now();
  return conn;
}

export function hubPing(sess, conn) {
  const r = call(sess, ACTIONS.hubSend, { url: conn.url, body: pingFrame() });
  conn.lastPingMs = Date.now();
  if (r.status === 404 || r.status === 410 || r.status === 0) conn.closed = true;
  return r.ok;
}

/** Send a hub invocation. Returns the invocation id (string) or null when the send failed. */
export function hubInvoke(sess, conn, target, args) {
  const id = String(conn.nextInvocation);
  conn.nextInvocation += 1;
  const r = call(sess, ACTIONS.hubSend, { url: conn.url, body: invocationFrame(id, target, args) });
  conn.lastPingMs = Date.now();
  if (r.status === 404 || r.status === 410 || r.status === 0) conn.closed = true;
  return r.ok ? id : null;
}

/** Poll until the completion for `invocationId` arrives or `maxMs` passes. */
export function hubAwaitCompletion(sess, conn, invocationId, maxMs) {
  const deadline = Date.now() + maxMs;
  while (conn.completions[invocationId] === undefined && !conn.closed && Date.now() < deadline) {
    pollOnce(sess, conn);
  }
  const completion = conn.completions[invocationId];
  return completion === undefined ? { done: false, error: 'timeout' } : { done: true, error: completion.error };
}

/** Spend `seconds` holding the connection: ping when due, poll for frames. Overshoots by <= one poll. */
export function hubHold(sess, conn, seconds) {
  const end = Date.now() + seconds * 1000;
  while (Date.now() < end && !conn.closed) {
    if (Date.now() - conn.lastPingMs >= PING_EVERY_MS) hubPing(sess, conn);
    if (conn.closed) break;
    pollOnce(sess, conn);
  }
}

/** Best-effort teardown (DELETE ends a long-polling connection). */
export function hubClose(sess, conn) {
  if (conn === null || conn === undefined) return;
  call(sess, ACTIONS.hubClose, { url: conn.url });
}

/** Re-establish a dropped connection (counts a reconnect). Backs off when the limiter pushes back. */
export function hubReconnect(sess, hubPath) {
  M.signalrReconnects.add(1);
  sleep(1 + Math.random() * 3);
  return hubConnect(sess, hubPath);
}
