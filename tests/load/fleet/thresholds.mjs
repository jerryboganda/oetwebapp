// k6 thresholds for the fleet load harness, from the owner's targets (2026-10-05). Every threshold
// here GATES: a breached one makes `k6 run` exit 99, and nothing swallows that exit code. Pure;
// unit-tested under node (manual tools: no CI runs them).
//
// Tag keys inside `{...}` are written in alphabetical order (class, flow, phase, reason, ...) because
// k6 stores sub-metrics under their written name and the report generator looks them up as tag SETS.

/** The owner's targets in one place; the report prints this table verbatim. */
export const OWNER_TARGETS = Object.freeze({
  criticalReadP95Ms: 500,
  criticalReadP99Ms: 1500,
  examSaveP95Ms: 500,
  submissionP95Ms: 500,
  liveSetupP95Ms: 1000,
  unexpectedFailureRate: 0.001,
  // Everything below is derived from the owner's wording, not separately stated numbers:
  establishedOkRate: 0.999, // "existing sessions survive" in steady state
  overloadEstablishedOkRate: 0.99, // ... and while a 50 % surge is being queued / shed
  overloadCollapseRate: 0.01, // 5xx (non-503) / no response during the surge
  aiAssessP95Ms: 600000, // grading is judged on queue drain: oldest job age < 10 min, not request p95
  signalrConnectFailureRate: 0.01,
});

/** Flows whose completion is required for the run to count as covering the whole product mix. */
export const FLOW_NAMES = Object.freeze(['browse', 'reading', 'listening', 'writing', 'speaking', 'room']);

const tagSet = (tags) => Object.entries(tags)
  .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
  .map(([k, v]) => `${k}:${v}`)
  .join(',');

/** `metric{k:v,...}` with the tags sorted. */
export const sub = (metric, tags) => `${metric}{${tagSet(tags)}}`;

const latencyGates = (phase) => ({
  [sub('http_req_duration', { class: 'critical-read', phase })]: [
    `p(95)<${OWNER_TARGETS.criticalReadP95Ms}`,
    `p(99)<${OWNER_TARGETS.criticalReadP99Ms}`,
  ],
  [sub('http_req_duration', { class: 'exam-save', phase })]: [`p(95)<${OWNER_TARGETS.examSaveP95Ms}`],
  [sub('http_req_duration', { class: 'submission', phase })]: [`p(95)<${OWNER_TARGETS.submissionP95Ms}`],
  [sub('http_req_duration', { class: 'live-setup', phase })]: [`p(95)<${OWNER_TARGETS.liveSetupP95Ms}`],
  [sub('oet_ai_assess_ms', { phase })]: [`p(95)<${OWNER_TARGETS.aiAssessP95Ms}`],
});

/** Always-true thresholds: they exist only so k6 prints a sub-metric (counts, per-stage latency). */
const info = (name, stat) => ({ [name]: [`${stat}>=0`] });

/**
 * @param {string} profile smoke | capacity | steady | overload
 * @param {{stages?: number[], expect?: Record<string, number>, requiredFlows?: string[]}} [options]
 *   stages: capacity stage targets (per-stage latency is published as info thresholds);
 *   expect: how many sessions of each flow THIS leg runs (a required flow with zero sessions here is
 *   not required of this leg);
 *   requiredFlows: flows that must complete at least once on this leg.
 */
export function buildThresholds(profile, { stages = [], expect = {}, requiredFlows = [] } = {}) {
  const thresholds = {};

  // Steady-state gates apply to every profile (capacity pools its stage holds into `steady`).
  Object.assign(thresholds, latencyGates('steady'));
  thresholds[sub('oet_unexpected_failure', { phase: 'steady' })] = [`rate<${OWNER_TARGETS.unexpectedFailureRate}`];
  thresholds[sub('oet_established_ok', { phase: 'steady' })] = [`rate>${OWNER_TARGETS.establishedOkRate}`];
  thresholds[sub('oet_signalr_connect_failed', { phase: 'steady' })] = [`rate<${OWNER_TARGETS.signalrConnectFailureRate}`];

  // Correctness gates over the whole run (a lost save or a double charge is never acceptable).
  thresholds.oet_lost_ack_save = ['count==0'];
  thresholds.oet_idempotency_violation = ['count==0'];
  thresholds.oet_credit_consumed_while_queued = ['count==0'];
  thresholds.oet_timer_started_while_queued = ['count==0'];

  // Stop a run that is plainly broken instead of burning an hour of generator time.
  thresholds.oet_unexpected_failure = [
    { threshold: 'rate<0.10', abortOnFail: true, delayAbortEval: '5m' },
  ];

  if (profile === 'overload') {
    Object.assign(thresholds, {
      [sub('oet_established_ok', { phase: 'overload' })]: [`rate>${OWNER_TARGETS.overloadEstablishedOkRate}`],
      [sub('oet_collapse', { phase: 'overload' })]: [`rate<${OWNER_TARGETS.overloadCollapseRate}`],
      // Recovery must meet the same latency and reliability targets as steady state.
      ...latencyGates('recovery'),
      [sub('oet_unexpected_failure', { phase: 'recovery' })]: [`rate<${OWNER_TARGETS.unexpectedFailureRate}`],
      [sub('oet_established_ok', { phase: 'recovery' })]: [`rate>${OWNER_TARGETS.establishedOkRate}`],
    });
    Object.assign(
      thresholds,
      info(sub('oet_graceful_shed', { phase: 'overload' }), 'count'),
      info(sub('oet_collapse', { phase: 'recovery' }), 'rate'),
    );
    // Sample counts for the recovery rows of the report (a target without samples is not a pass).
    for (const klass of ['critical-read', 'exam-save', 'submission', 'live-setup']) {
      Object.assign(thresholds, info(sub('oet_class_total', { class: klass, phase: 'recovery' }), 'count'));
    }
  }

  if (profile === 'capacity') {
    for (const stage of stages) {
      Object.assign(
        thresholds,
        info(sub('http_req_duration', { class: 'critical-read', phase: 'steady', stage }), 'p(95)'),
        info(sub('http_req_duration', { class: 'exam-save', phase: 'steady', stage }), 'p(95)'),
        info(sub('http_req_duration', { class: 'submission', phase: 'steady', stage }), 'p(95)'),
        info(sub('oet_unexpected_failure', { phase: 'steady', stage }), 'rate'),
      );
    }
  }

  // Counts the report prints for coverage and context (always-true; no gating).
  for (const klass of ['critical-read', 'exam-save', 'submission', 'live-setup', 'live-turn', 'search', 'hub']) {
    Object.assign(thresholds, info(sub('oet_class_total', { class: klass, phase: 'steady' }), 'count'));
  }
  for (const flow of FLOW_NAMES) {
    Object.assign(
      thresholds,
      info(sub('oet_flow_started', { flow }), 'count'),
      info(sub('oet_flow_completed', { flow }), 'count'),
      info(sub('oet_flow_skipped', { flow }), 'count'),
    );
  }

  // Required coverage: a flow this leg should have run must have completed at least once.
  for (const flow of requiredFlows) {
    if ((expect[flow] ?? 0) > 0) thresholds[sub('oet_flow_completed', { flow })] = ['count>0'];
  }

  return thresholds;
}

/** Statuses the status matrix counts (everything else lands in the unlisted remainder). */
export const MATRIX_STATUSES = Object.freeze([200, 201, 202, 204, 400, 401, 403, 404, 409, 429, 500, 502, 503, 504, 0]);

export const REQUEST_CLASSES = Object.freeze([
  'critical-read', 'exam-start', 'exam-save', 'submission', 'live-setup', 'live-turn', 'ai-roundtrip', 'search', 'auth',
  'hub', 'other',
]);

/**
 * Always-true Counter thresholds that make k6 publish `oet_status_total` per request class and
 * status, and (smoke only) per endpoint id and status. This is the harness's contract probe: the first
 * run shows exactly which endpoint answered which status, so a wrong path or body shape is one line
 * in the report instead of a mystery failure rate.
 */
export function statusMatrixThresholds({ endpoints = [] } = {}) {
  const out = {};
  for (const klass of REQUEST_CLASSES) {
    for (const status of MATRIX_STATUSES) Object.assign(out, info(sub('oet_status_total', { class: klass, status }), 'count'));
  }
  for (const ep of endpoints) {
    for (const status of MATRIX_STATUSES) Object.assign(out, info(sub('oet_status_total', { ep, status }), 'count'));
  }
  return out;
}

/** Thresholds only list a metric once; merge duplicates defensively (later arrays are appended). */
export function mergeThresholds(...sets) {
  const merged = {};
  for (const set of sets) {
    for (const [name, rules] of Object.entries(set)) merged[name] = [...(merged[name] ?? []), ...rules];
  }
  return merged;
}
