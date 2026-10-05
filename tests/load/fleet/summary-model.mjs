// Turns the object k6 hands to handleSummary(data) into the stable `oet-load-summary/1` JSON the
// report generator consumes. Pure (no k6 imports): used by fleet-1000.k6.js and unit-tested in node.

export const SUMMARY_SCHEMA = 'oet-load-summary/1';

/** `{ "p(95)<500": { ok: true } }` (k6 >= 0.30) -> `{ "p(95)<500": true }`. Booleans pass through. */
export function normalizeThresholds(thresholds) {
  const out = {};
  if (thresholds === null || typeof thresholds !== 'object') return out;
  for (const [expression, result] of Object.entries(thresholds)) {
    if (result !== null && typeof result === 'object' && 'ok' in result) out[expression] = Boolean(result.ok);
    else if (typeof result === 'boolean') out[expression] = result;
  }
  return out;
}

export function buildSummary(data, meta = {}) {
  const metrics = {};
  const failed = [];
  for (const [name, metric] of Object.entries(data?.metrics ?? {})) {
    const thresholds = normalizeThresholds(metric?.thresholds);
    metrics[name] = {
      type: metric?.type ?? null,
      contains: metric?.contains ?? null,
      values: metric?.values ?? {},
      thresholds,
    };
    for (const [expression, ok] of Object.entries(thresholds)) {
      if (!ok) failed.push({ metric: name, expression });
    }
  }
  return {
    schema: SUMMARY_SCHEMA,
    meta,
    durationMs: data?.state?.testRunDurationMs ?? null,
    metrics,
    thresholdsFailed: failed,
    passed: failed.length === 0,
  };
}

/** `http_req_duration{class:x,phase:steady}` -> { name: 'http_req_duration', tags: { class: 'x', phase: 'steady' } }.
 * A tag value may itself contain ':' (only the first one splits). */
export function parseMetricKey(key) {
  const text = String(key);
  const open = text.indexOf('{');
  if (open === -1 || !text.endsWith('}')) return { name: text, tags: {} };
  const tags = {};
  for (const piece of text.slice(open + 1, -1).split(',')) {
    const colon = piece.indexOf(':');
    if (colon === -1) continue;
    tags[piece.slice(0, colon).trim()] = piece.slice(colon + 1).trim();
  }
  return { name: text.slice(0, open), tags };
}

const sameTags = (a, b) => {
  const ak = Object.keys(a);
  const bk = Object.keys(b);
  return ak.length === bk.length && ak.every((k) => b[k] === a[k]);
};

/** The metric with this name and EXACTLY this tag set (key order irrelevant), or null. */
export function findMetric(metrics, name, tags = {}) {
  for (const [key, value] of Object.entries(metrics ?? {})) {
    const parsed = parseMetricKey(key);
    if (parsed.name === name && sameTags(parsed.tags, tags)) return value;
  }
  return null;
}

/** Every sub-metric of `name` (any tag set), as [{ tags, metric }]. */
export function listSubmetrics(metrics, name) {
  const out = [];
  for (const [key, value] of Object.entries(metrics ?? {})) {
    const parsed = parseMetricKey(key);
    if (parsed.name === name && Object.keys(parsed.tags).length > 0) out.push({ tags: parsed.tags, metric: value });
  }
  return out;
}

/** Value helpers that tolerate a missing metric (a run that never exercised a path). */
export const valueOf = (metric, stat, fallback = null) => {
  const value = metric?.values?.[stat];
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
};
