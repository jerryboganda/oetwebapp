// Response classification for the fleet load harness (pure; unit-tested under node).
//
// "Unexpected failure" is the owner's reliability number (< 0.1 %), so it has to be exact:
//   * a documented business refusal (HTTP 4xx with a known code, e.g. the Reading Part B/C window not
//     being open yet) is NOT a failure: it is the product answering correctly;
//   * a graceful shed (429 / 503 carrying Retry-After) is NOT a failure while the fleet is being
//     deliberately overloaded or while an admission queue is allowed, because "overload queues rather
//     than collapses" is exactly what the overload profile asserts;
//   * anything else, including a 429 in steady state, a 5xx, a timeout or a connection error, IS.
// `collapse` is the stricter signal for the overload profile: 5xx that is not a graceful 503, or no
// response at all.

export const PHASES_WHERE_SHEDDING_IS_EXPECTED = Object.freeze(['overload', 'subside']);

/**
 * @param {{status:number, retryAfter?:string|null, errorCode?:string|null}} response
 * @param {{ok?:number[], domain?:number[], domainCodes?:string[], shedOk?:boolean}} spec
 * @param {{phase?:string}} context
 */
export function classifyResponse(response, spec = {}, context = {}) {
  const status = Number(response?.status ?? 0);
  const ok = spec.ok ?? [200];
  const domain = spec.domain ?? [];
  const domainCodes = spec.domainCodes ?? [];
  const hasRetryAfter = response?.retryAfter !== undefined && response?.retryAfter !== null && String(response.retryAfter) !== '';
  const base = { status, kind: 'unexpected', unexpected: true, collapse: false, shed: false };

  if (!Number.isFinite(status) || status === 0) {
    return { ...base, kind: 'collapse', collapse: true };
  }
  if (ok.includes(status)) return { ...base, kind: 'ok', unexpected: false };
  if (status === 401) return { ...base, kind: 'unauthorized', unexpected: false };
  if (domain.includes(status)) {
    const code = response?.errorCode ?? null;
    if (domainCodes.length === 0 || (code !== null && domainCodes.includes(code))) {
      return { ...base, kind: 'domain', unexpected: false };
    }
  }
  const sheddable = status === 429 || status === 503;
  const sheddingAllowed = spec.shedOk === true || PHASES_WHERE_SHEDDING_IS_EXPECTED.includes(context.phase ?? '');
  if (sheddable && hasRetryAfter && sheddingAllowed) {
    return { ...base, kind: 'shed', unexpected: false, shed: true };
  }
  if (status >= 500) return { ...base, kind: 'collapse', collapse: true };
  return base;
}

/** First non-empty error code a problem+json / ApiException body carries. */
export function errorCodeOf(json) {
  if (json === null || typeof json !== 'object') return null;
  const candidates = [json.code, json.errorCode, json.error?.code, json.error];
  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate) return candidate;
  }
  return null;
}

/** Retry-After as seconds (integer form only; an HTTP-date is treated as 5 s), clamped to [1, 120]. */
export function retryAfterSeconds(value, fallback = 5) {
  if (value === undefined || value === null || value === '') return fallback;
  const seconds = Number(value);
  if (!Number.isFinite(seconds)) return fallback;
  return Math.min(120, Math.max(1, Math.ceil(seconds)));
}

/**
 * The live AI Speaking admission line. While the live-session cap is full, finish-warmup answers HTTP 200
 * with the session still in warm-up and `admission: { status: 'waiting', position, queueLength,
 * estimatedWaitSeconds, pollAfterSeconds }` (backend Contracts/SpeakingLiveAdmissionContracts.cs,
 * SpeakingSessionService.FinishWarmupAsync): nothing is held or timed, and the caller repeats the call.
 * Returns the seconds to wait before repeating it, or null when the body is not a waiting answer.
 */
export function admissionWaitSeconds(json, fallback = 5) {
  const view = json !== null && typeof json === 'object' ? json.admission : null;
  if (view === null || view === undefined || typeof view !== 'object') return null;
  if (String(view.status ?? '').toLowerCase() !== 'waiting') return null;
  return retryAfterSeconds(view.pollAfterSeconds, fallback);
}

/** Case-insensitive header lookup over a k6 `res.headers` object (or any plain object). */
export function headerOf(headers, name) {
  if (!headers) return null;
  const wanted = name.toLowerCase();
  for (const key of Object.keys(headers)) {
    if (key.toLowerCase() === wanted) return headers[key];
  }
  return null;
}
