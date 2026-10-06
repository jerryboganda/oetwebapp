// Request wrapper for the fleet harness: sessions, auth, browser-faithful headers, classification
// and metric recording. Every request any flow makes goes through call(), so the unexpected-failure
// rate, the established-session rate and the per-class latency tags are measured one way only.

import http from 'k6/http';
import { sleep } from 'k6';
import exec from 'k6/execution';
import { CFG, TIMELINE, accountOf } from './config.js';
import { ACTIONS, isCsrfExemptHub } from './contract.js';
import { classifyResponse, errorCodeOf, headerOf, retryAfterSeconds } from './classify.mjs';
import * as M from './metrics.js';

/** Per-VU clock origin: seconds from the scenario start drive the timeline phase. */
const vu = { startMs: 0 };

export function beginIteration() {
  vu.startMs = exec.scenario.startTime;
}

export const elapsedSeconds = () => (Date.now() - vu.startMs) / 1000;

export const phaseNow = () => TIMELINE.phaseAt(elapsedSeconds());

export function newSession(kind, ordinal, g) {
  const account = accountOf(kind, ordinal);
  return {
    kind,
    ordinal,
    g,
    email: account.email,
    deviceId: account.deviceId,
    prefix: CFG.prefix,
    token: '',
    refreshToken: '',
    tokenExpMs: 0,
    established: false,
    cohort: kind === 'learner' && TIMELINE.isSurge(g) ? 'surge' : 'base',
    hub: null,
  };
}

function csrfToken() {
  try {
    const cookies = http.cookieJar().cookiesForURL(`${CFG.webUrl}/`);
    const values = cookies.oet_csrf;
    return values && values.length > 0 ? values[0] : '';
  } catch (error) {
    return '';
  }
}

function headersFor(sess, spec, method, options) {
  const headers = {
    Accept: spec.hub ? '*/*' : 'application/json',
    'X-OET-Device-Id': sess.deviceId,
  };
  if (sess.token && !spec.auth) headers.Authorization = `Bearer ${sess.token}`;
  // 'native' makes the API return the refresh token in the body, so a VU can refresh without cookies.
  if (spec.auth) headers['X-OET-Client-Platform'] = 'native';
  const mutating = method !== 'GET' && method !== 'HEAD';
  if (mutating && CFG.viaWeb) {
    // The Next.js BFF rejects a state-changing request without a same-origin Origin, and (once the
    // refresh cookie is in the jar, i.e. after sign-in) without the double-submit CSRF header
    // (validateProxyCsrf in lib/backend-proxy.ts). Exempt from the header there: the auth bootstrap
    // calls and the hubs in SIGNALR_HUB_PATH_PATTERN (contract.js CSRF_EXEMPT_HUBS: notifications and
    // the tutor-room hub). Any other hub would NOT be exempt, so its negotiate / send / close POSTs
    // carry the header here; a browser's SignalR client sends none (docs/ops/LOAD-TESTING.md section 2).
    // A hub request with no `hubPath` gets the header too.
    headers.Origin = CFG.webUrl;
    const exempt = spec.auth || (spec.hub && isCsrfExemptHub(options.hubPath));
    if (!exempt) {
      const csrf = csrfToken();
      if (csrf) headers['X-CSRF-Token'] = csrf;
    }
  }
  if (mutating && !options.multipart) {
    headers['Content-Type'] = spec.hub ? 'text/plain;charset=UTF-8' : 'application/json';
  }
  if (options.headers) Object.assign(headers, options.headers);
  return headers;
}

function record(sess, spec, status, c, phase) {
  const phaseTags = { phase: phase.phase };
  if (phase.stage !== null) phaseTags.stage = String(phase.stage);
  const classTags = { class: spec.class, phase: phase.phase };
  if (phase.stage !== null) classTags.stage = String(phase.stage);
  M.classTotal.add(1, classTags);
  M.statusTotal.add(1, { class: spec.class, ep: spec.id, status: String(status) });
  M.unexpectedFailure.add(c.unexpected, phaseTags);
  M.collapse.add(c.collapse, phaseTags);
  if (c.shed) M.gracefulShed.add(1, phaseTags);
  if (c.kind === 'domain') M.domainReject.add(1, { ep: spec.id });
  if (sess.established && sess.cohort === 'base' && spec.class !== 'auth') {
    M.establishedOk.add(!c.unexpected && !c.shed, phaseTags);
  }
}

/**
 * Make one classified request.
 * options: path (override the template, e.g. with ids filled), body (object or string), url (absolute),
 *          direct (use the API origin instead of the web proxy), multipart, headers, timeout,
 *          hubPath (hub requests: which hub, so the CSRF exemption can be decided), _retried.
 * Returns { res, status, ok, kind, domain, shed, unexpected, errorCode, retryAfter, text, json(), ms }.
 */
export function call(sess, spec, options = {}) {
  if (!spec.auth && sess.token) ensureFresh(sess);
  const phase = phaseNow();
  const method = spec.method;
  const base = options.direct ? CFG.apiUrl : sess.prefix;
  const url = options.url !== undefined ? options.url : `${base}${options.path !== undefined ? options.path : spec.path}`;
  let body = options.body;
  if (body !== undefined && body !== null && typeof body === 'object' && !options.multipart) body = JSON.stringify(body);
  const tags = { name: spec.name, ep: spec.id, class: spec.class, phase: phase.phase };
  if (phase.stage !== null) tags.stage = String(phase.stage);
  const params = {
    headers: headersFor(sess, spec, method, options),
    tags,
    timeout: options.timeout !== undefined ? options.timeout : (spec.class === 'ai-roundtrip' ? '620s' : '60s'),
    responseType: 'text',
  };
  const started = Date.now();
  const payload = method === 'GET' || method === 'HEAD' || body === undefined ? null : body;
  const res = http.request(method, url, payload, params);

  const status = res.status;
  const retryAfter = headerOf(res.headers, 'Retry-After');
  let parsed;
  const json = () => {
    if (parsed === undefined) {
      try {
        parsed = JSON.parse(res.body);
      } catch (error) {
        parsed = null;
      }
    }
    return parsed;
  };
  const errorCode = status >= 400 ? errorCodeOf(json()) : null;
  let c = classifyResponse({ status, retryAfter, errorCode }, spec, { phase: phase.phase });

  if (c.kind === 'unauthorized') {
    if (!spec.auth && !options._retried && sess.refreshToken !== undefined && refreshSession(sess)) {
      // The access token was rejected (rotated or expired early): refresh once and replay. The replay,
      // not the rejected first attempt, decides whether this counts as a failure.
      return call(sess, spec, Object.assign({}, options, { _retried: true }));
    }
    c = Object.assign({}, c, { kind: 'unexpected', unexpected: true });
  }

  record(sess, spec, status, c, phase);
  return {
    res,
    status,
    ok: c.kind === 'ok',
    kind: c.kind,
    domain: c.kind === 'domain',
    shed: c.shed,
    unexpected: c.unexpected,
    errorCode,
    retryAfter,
    text: res.body === null || res.body === undefined ? '' : String(res.body),
    json,
    ms: Date.now() - started,
  };
}

function applyTokens(sess, body) {
  sess.token = body.accessToken;
  if (body.refreshToken) sess.refreshToken = body.refreshToken;
  const expires = Date.parse(body.accessTokenExpiresAt);
  sess.tokenExpMs = Number.isFinite(expires) ? expires : Date.now() + 15 * 60 * 1000;
}

/** Sign in with bounded retries. Credentials / device-policy refusals are final; the rest back off. */
export function signInSession(sess) {
  if (CFG.password === '') throw new Error('OET_LOAD_PASSWORD is required');
  for (let attempt = 1; attempt <= 5; attempt += 1) {
    const r = call(sess, ACTIONS.signIn, { body: { email: sess.email, password: CFG.password, rememberMe: false } });
    if (r.ok) {
      const body = r.json();
      if (body && body.accessToken) {
        applyTokens(sess, body);
        return true;
      }
    }
    if (r.status === 400 || r.status === 401 || r.status === 403) return false;
    sleep(Math.min(30, r.retryAfter ? retryAfterSeconds(r.retryAfter) : attempt * 3));
  }
  return false;
}

/** Refresh the access token; fall back to a fresh sign-in when the refresh token was rejected. */
export function refreshSession(sess) {
  if (!sess.refreshToken) return signInSession(sess);
  const r = call(sess, ACTIONS.refresh, { body: { refreshToken: sess.refreshToken } });
  if (r.ok) {
    const body = r.json();
    if (body && body.accessToken) {
      applyTokens(sess, body);
      return true;
    }
  }
  return signInSession(sess);
}

export function ensureFresh(sess) {
  if (sess.tokenExpMs - Date.now() > 120000) return;
  refreshSession(sess);
}

/** Sleep until `targetS` seconds after the scenario start (no-op if already past). */
export function sleepUntil(targetS) {
  const wait = targetS - elapsedSeconds();
  if (wait > 0) sleep(wait);
}
