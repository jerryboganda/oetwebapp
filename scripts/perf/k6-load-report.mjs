#!/usr/bin/env node
// Turns the `oet-load-summary/1` JSON that tests/load/fleet-1000.k6.js writes (one file per
// generator leg) into a markdown report with a PASS / FAIL / INCOMPLETE verdict.
//
// What this report is, and is not:
//   * It is APPLICATION and HARDWARE validation: does the OET API, web slot, SignalR path, database
//     and primary-VPS hardware meet the owner's targets for the simulated mix.
//   * It is NOT provider validation. OpenAI GPT-Live, Gemini Live, LiveKit, and the Claude Max and
//     Codex lanes are driven against simulators with configured latency; their real capacity is an
//     owner-supplied assumption this run cannot prove. The report says so on its first lines.
//
// Compute policy: pure file reads and writes, no network. Run it in GitHub Actions.
//
// Usage:
//   node scripts/perf/k6-load-report.mjs --input leg0.json [--input leg1.json ...] \
//        --out report.md [--json verdict.json] [--require-pass] [--allow-no-data]

import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import {
  SUMMARY_SCHEMA, findMetric, listSubmetrics, valueOf,
} from '../../tests/load/fleet/summary-model.mjs';
import { FLOW_NAMES, OWNER_TARGETS } from '../../tests/load/fleet/thresholds.mjs';

export const SCOPE_STATEMENT = 'This report is application and hardware validation of the OET platform. '
  + 'External provider capacity (OpenAI GPT-Live, Gemini Live, LiveKit, and the Claude Max / Codex lanes) is simulated '
  + 'with configured latency and is an owner-supplied assumption: this run does not validate it.';

export function validateSummary(summary, label = 'summary') {
  if (summary === null || typeof summary !== 'object' || summary.schema !== SUMMARY_SCHEMA) {
    throw new Error(`${label}: not an ${SUMMARY_SCHEMA} document`);
  }
  if (summary.metrics === null || typeof summary.metrics !== 'object') throw new Error(`${label}: missing metrics`);
  return summary;
}

// ---- merging legs -------------------------------------------------------------------------------

/** Sum of a counter across legs (a metric missing from a leg counts as 0). */
export function sumCounter(summaries, name, tags = {}) {
  return summaries.reduce((total, s) => total + (valueOf(findMetric(s.metrics, name, tags), 'count', 0) ?? 0), 0);
}

/** Pooled rate across legs: total passes over total samples. { rate, samples } or null with no samples. */
export function pooledRate(summaries, name, tags = {}) {
  let passes = 0;
  let samples = 0;
  for (const s of summaries) {
    const metric = findMetric(s.metrics, name, tags);
    if (!metric) continue;
    const p = valueOf(metric, 'passes');
    const f = valueOf(metric, 'fails');
    if (p !== null && f !== null) {
      passes += p;
      samples += p + f;
    }
  }
  return samples === 0 ? null : { rate: passes / samples, samples };
}

/** Worst (highest) value of a trend statistic across legs, plus how many legs reported it. */
export function worstTrend(summaries, name, tags, stat) {
  let worst = null;
  let legs = 0;
  for (const s of summaries) {
    const value = valueOf(findMetric(s.metrics, name, tags), stat);
    if (value === null) continue;
    legs += 1;
    worst = worst === null ? value : Math.max(worst, value);
  }
  return worst === null ? null : { value: worst, legs };
}

export function sumGauge(summaries, name, stat) {
  return summaries.reduce((total, s) => total + (valueOf(findMetric(s.metrics, name, {}), stat, 0) ?? 0), 0);
}

// ---- owner-target rows --------------------------------------------------------------------------

const fmtMs = (v) => (v === null ? 'n/a' : `${v.toFixed(v < 10 ? 2 : 0)} ms`);
const fmtPct = (v) => (v === null ? 'n/a' : `${(v * 100).toFixed(v < 0.01 ? 3 : 2)} %`);
const fmtInt = (v) => (v === null ? 'n/a' : String(Math.round(v)));

/** One owner-target row: measured value (worst leg for latency, pooled for rates), verdict, k6 view. */
function trendRow(summaries, { id, label, klass, phase, stat, limit }) {
  const tags = { class: klass, phase };
  const samples = sumCounter(summaries, 'oet_class_total', tags);
  const worst = worstTrend(summaries, 'http_req_duration', tags, stat);
  const measured = worst?.value ?? null;
  const status = samples === 0 || measured === null ? 'NO DATA' : (measured < limit ? 'PASS' : 'FAIL');
  return { id, label, target: `${stat} < ${limit} ms`, measured: fmtMs(measured), samples, status };
}

function rateRow(summaries, { id, label, name, tags, op, limit, unit = 'rate' }) {
  const pooled = pooledRate(summaries, name, tags);
  const measured = pooled?.rate ?? null;
  let status = 'NO DATA';
  if (measured !== null) status = (op === '<' ? measured < limit : measured > limit) ? 'PASS' : 'FAIL';
  return {
    id, label, target: `${op} ${unit === 'rate' ? fmtPct(limit) : limit}`, measured: fmtPct(measured),
    samples: pooled?.samples ?? 0, status,
  };
}

function counterRow(summaries, { id, label, name, tags = {}, limit = 0 }) {
  const count = sumCounter(summaries, name, tags);
  return { id, label, target: `= ${limit}`, measured: fmtInt(count), samples: count, status: count === limit ? 'PASS' : 'FAIL' };
}

export function buildTargetRows(summaries, profile) {
  const phases = profile === 'overload' ? ['steady', 'recovery'] : ['steady'];
  const rows = [];
  for (const phase of phases) {
    const p = phase === 'steady' ? '' : ` (${phase})`;
    rows.push(
      trendRow(summaries, { id: `critical-read-p95-${phase}`, label: `Critical API reads p95${p}`, klass: 'critical-read', phase, stat: 'p(95)', limit: OWNER_TARGETS.criticalReadP95Ms }),
      trendRow(summaries, { id: `critical-read-p99-${phase}`, label: `Critical API reads p99${p}`, klass: 'critical-read', phase, stat: 'p(99)', limit: OWNER_TARGETS.criticalReadP99Ms }),
      trendRow(summaries, { id: `exam-save-p95-${phase}`, label: `Exam answer / draft save p95${p}`, klass: 'exam-save', phase, stat: 'p(95)', limit: OWNER_TARGETS.examSaveP95Ms }),
      trendRow(summaries, { id: `submission-p95-${phase}`, label: `Exam submission p95${p}`, klass: 'submission', phase, stat: 'p(95)', limit: OWNER_TARGETS.submissionP95Ms }),
      trendRow(summaries, { id: `live-setup-p95-${phase}`, label: `Live-session setup p95${p}`, klass: 'live-setup', phase, stat: 'p(95)', limit: OWNER_TARGETS.liveSetupP95Ms }),
      rateRow(summaries, { id: `unexpected-${phase}`, label: `Unexpected failures${p}`, name: 'oet_unexpected_failure', tags: { phase }, op: '<', limit: OWNER_TARGETS.unexpectedFailureRate }),
      rateRow(summaries, { id: `established-${phase}`, label: `Established sessions OK${p}`, name: 'oet_established_ok', tags: { phase }, op: '>', limit: OWNER_TARGETS.establishedOkRate }),
    );
  }
  rows.push(
    counterRow(summaries, { id: 'lost-saves', label: 'Lost acknowledged saves', name: 'oet_lost_ack_save' }),
    counterRow(summaries, { id: 'idempotency', label: 'Duplicate submit / charge (idempotency violations)', name: 'oet_idempotency_violation' }),
    counterRow(summaries, { id: 'queued-credit', label: 'Credits consumed while queued', name: 'oet_credit_consumed_while_queued' }),
  );
  if (profile === 'overload') {
    rows.push(
      rateRow(summaries, { id: 'overload-established', label: 'Established sessions OK during surge', name: 'oet_established_ok', tags: { phase: 'overload' }, op: '>', limit: OWNER_TARGETS.overloadEstablishedOkRate }),
      rateRow(summaries, { id: 'overload-collapse', label: 'Collapse responses during surge (5xx / no response)', name: 'oet_collapse', tags: { phase: 'overload' }, op: '<', limit: OWNER_TARGETS.overloadCollapseRate }),
    );
  }
  return rows;
}

// ---- model --------------------------------------------------------------------------------------

export function buildModel(summaries, { allowNoData = false } = {}) {
  if (summaries.length === 0) throw new Error('at least one summary is required');
  const meta = summaries[0].meta ?? {};
  const profile = meta.profile ?? 'steady';
  const targetRows = buildTargetRows(summaries, profile);

  const thresholdFailures = [];
  summaries.forEach((s, index) => {
    for (const failure of s.thresholdsFailed ?? []) thresholdFailures.push({ leg: s.meta?.leg ?? index, ...failure });
  });

  const flows = FLOW_NAMES.map((flow) => ({
    flow,
    started: sumCounter(summaries, 'oet_flow_started', { flow }),
    completed: sumCounter(summaries, 'oet_flow_completed', { flow }),
    skipped: sumCounter(summaries, 'oet_flow_skipped', { flow }),
  }));

  const stages = [];
  if (profile === 'capacity') {
    const targets = new Set();
    for (const s of summaries) {
      for (const { tags } of listSubmetrics(s.metrics, 'oet_unexpected_failure')) if (tags.stage) targets.add(Number(tags.stage));
    }
    for (const target of [...targets].sort((a, b) => a - b)) {
      const t = (klass) => worstTrend(summaries, 'http_req_duration', { class: klass, phase: 'steady', stage: String(target) }, 'p(95)')?.value ?? null;
      const failures = pooledRate(summaries, 'oet_unexpected_failure', { phase: 'steady', stage: String(target) });
      const row = { target, read: t('critical-read'), save: t('exam-save'), submit: t('submission'), failures: failures?.rate ?? null };
      row.pass = row.read !== null && row.read < OWNER_TARGETS.criticalReadP95Ms
        && (row.save === null || row.save < OWNER_TARGETS.examSaveP95Ms)
        && (row.submit === null || row.submit < OWNER_TARGETS.submissionP95Ms)
        && (row.failures === null || row.failures < OWNER_TARGETS.unexpectedFailureRate);
      stages.push(row);
    }
  }

  const anyFail = targetRows.some((r) => r.status === 'FAIL') || thresholdFailures.length > 0;
  const anyNoData = targetRows.some((r) => r.status === 'NO DATA');
  let verdict = 'PASS';
  if (anyFail) verdict = 'FAIL';
  else if (anyNoData && !allowNoData) verdict = 'INCOMPLETE';

  return {
    verdict, profile, legs: summaries.length, meta, targetRows, thresholdFailures, flows, stages,
    durationMs: Math.max(...summaries.map((s) => s.durationMs ?? 0)),
    summaries,
  };
}

// ---- rendering ----------------------------------------------------------------------------------

const table = (headers, rows) => [
  `| ${headers.join(' | ')} |`,
  `| ${headers.map(() => '---').join(' | ')} |`,
  ...rows.map((r) => `| ${r.map((c) => String(c ?? '')).join(' | ')} |`),
].join('\n');

function statusMatrixSection(summaries) {
  const byEndpoint = new Map();
  const statuses = new Set();
  for (const s of summaries) {
    for (const { tags, metric } of listSubmetrics(s.metrics, 'oet_status_total')) {
      if (!tags.ep) continue;
      const count = valueOf(metric, 'count', 0) ?? 0;
      if (count === 0) continue;
      statuses.add(tags.status);
      const row = byEndpoint.get(tags.ep) ?? new Map();
      row.set(tags.status, (row.get(tags.status) ?? 0) + count);
      byEndpoint.set(tags.ep, row);
    }
  }
  if (byEndpoint.size === 0) return '';
  const cols = [...statuses].sort((a, b) => Number(a) - Number(b));
  const rows = [...byEndpoint.entries()].sort(([a], [b]) => (a < b ? -1 : 1))
    .map(([ep, counts]) => [`\`${ep}\``, ...cols.map((c) => counts.get(c) ?? '')]);
  return `\n### Endpoint status matrix (contract probe)\n\n${table(['endpoint', ...cols.map((c) => (c === '0' ? 'no response' : c))], rows)}\n\n`
    + 'Any status in a column that the endpoint is not documented to return means the harness contract (tests/load/fleet/contract.js) and the API disagree: fix the contract before trusting latency numbers.\n';
}

export function renderReport(model, options = {}) {
  const { meta } = model;
  const lines = [];
  const stamp = options.generatedAt ?? new Date().toISOString();
  lines.push(`# OET fleet load test report`);
  lines.push('');
  lines.push(`**Verdict: ${model.verdict}**`);
  lines.push('');
  lines.push(`> ${SCOPE_STATEMENT}`);
  lines.push('');
  lines.push(table(['field', 'value'], [
    ['Profile', model.profile],
    ['Generator legs', model.legs],
    ['Learners (planned)', meta.totalLearners ?? 'n/a'],
    ['Duration', `${Math.round(model.durationMs / 60000)} min`],
    ['Target', meta.targetLabel ?? 'n/a (non-production stack)'],
    ['k6', meta.k6Version ?? 'n/a'],
    ['Run', meta.runId ?? 'n/a'],
    ['Commit', meta.sha ?? 'n/a'],
    ['Provider simulators', meta.simulators ?? 'configured latency (see docs/ops/LOAD-TESTING.md)'],
    ['Generated', stamp],
  ]));
  lines.push('');
  if (model.verdict === 'INCOMPLETE') {
    lines.push('> **INCOMPLETE:** at least one owner target had no samples, so it was not measured. A target that was not exercised is not a pass.');
    lines.push('');
  }

  lines.push('## Owner targets');
  lines.push('');
  lines.push(table(
    ['target', 'limit', 'measured (worst leg / pooled)', 'samples', 'result'],
    model.targetRows.map((r) => [r.label, r.target, r.measured, r.samples, r.status === 'PASS' ? 'PASS' : `**${r.status}**`]),
  ));
  lines.push('');
  lines.push('Latency is the worst value across generator legs (percentiles cannot be merged exactly, so the worst leg is the conservative bound). Rates are pooled over all legs.');
  lines.push('');

  lines.push('## k6 threshold results');
  lines.push('');
  if (model.thresholdFailures.length === 0) {
    lines.push('Every k6 threshold passed on every leg.');
  } else {
    lines.push(table(['leg', 'metric', 'threshold'], model.thresholdFailures.map((f) => [f.leg, `\`${f.metric}\``, `\`${f.expression}\``])));
  }
  lines.push('');

  lines.push('## Flow coverage');
  lines.push('');
  lines.push(table(['flow', 'started', 'completed', 'skipped'], model.flows.map((f) => [f.flow, f.started, f.completed, f.skipped])));
  lines.push('');
  lines.push('A skipped flow means its precondition was missing (no published content, no live-voice ready card, no rooms manifest). Skips are coverage gaps, not passes.');
  lines.push('');

  if (model.stages.length > 0) {
    lines.push('## Capacity stages');
    lines.push('');
    lines.push(table(
      ['learners', 'read p95', 'save p95', 'submit p95', 'unexpected failures', 'result'],
      model.stages.map((s) => [s.target, fmtMs(s.read), fmtMs(s.save), fmtMs(s.submit), fmtPct(s.failures), s.pass ? 'PASS' : '**FAIL**']),
    ));
    const passing = model.stages.filter((s) => s.pass).map((s) => s.target);
    const first = model.stages.find((s) => !s.pass);
    lines.push('');
    lines.push(first
      ? `Highest passing stage: ${passing.length ? passing[passing.length - 1] : 'none'} learners; first failing stage: ${first.target}.`
      : `Every stage met the targets up to ${model.stages[model.stages.length - 1].target} learners.`);
    lines.push('');
  }

  if (model.profile === 'overload') {
    const shed = sumCounter(model.summaries, 'oet_graceful_shed', { phase: 'overload' });
    const collapse = pooledRate(model.summaries, 'oet_collapse', { phase: 'overload' });
    lines.push('## Overload behaviour');
    lines.push('');
    lines.push(`- Graceful sheds during the surge (429 / 503 with Retry-After): **${shed}**.`);
    lines.push(`- Collapse responses during the surge (5xx other than a graceful 503, or no response): **${fmtPct(collapse?.rate ?? null)}** of ${collapse?.samples ?? 0} requests.`);
    lines.push('- "Queues rather than collapses" holds when sheds are answered with Retry-After, established sessions stay above 99 % OK, and the recovery phase meets the steady-state targets again.');
    lines.push('');
  }

  const matrix = statusMatrixSection(model.summaries);
  if (matrix) lines.push(matrix);

  lines.push('## Method and caveats');
  lines.push('');
  lines.push('- Every learner is a distinct disposable account with its own device id; sign-ins are paced to stay under the per-IP limit of 100 / min.');
  lines.push('- Notification hub connections use long-polling through the web origin, as browsers do in production (the BFF proxy cannot upgrade WebSockets).');
  lines.push('- Think times are randomised around realistic dwell times; request mix and cadence are defined in `tests/load/fleet/flows.js`.');
  lines.push('- Provider simulators keep the real Writing lane bounds (one CLI at a time, queue 40, 7-minute wait) so the true grading bottleneck is exercised.');
  lines.push('- This is evidence for one commit on one non-production stack. It is not a release proof and does not replace the production deploy gates.');
  lines.push('');
  return lines.join('\n');
}

// ---- CLI ----------------------------------------------------------------------------------------

export function parseArgs(argv) {
  const args = { inputs: [], out: null, json: null, requirePass: false, allowNoData: false };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--input') args.inputs.push(argv[++i]);
    else if (arg === '--out') args.out = argv[++i];
    else if (arg === '--json') args.json = argv[++i];
    else if (arg === '--require-pass') args.requirePass = true;
    else if (arg === '--allow-no-data') args.allowNoData = true;
    else throw new Error(`unknown argument ${arg}`);
  }
  if (args.inputs.length === 0 || args.inputs.some((v) => !v)) throw new Error('--input <summary.json> is required (repeatable)');
  return args;
}

export function main(argv, io = { read: (p) => readFileSync(p, 'utf8'), write: (p, c) => writeFileSync(p, c) }) {
  const args = parseArgs(argv);
  const summaries = args.inputs.map((p) => validateSummary(JSON.parse(io.read(p)), p));
  const model = buildModel(summaries, { allowNoData: args.allowNoData });
  const markdown = renderReport(model);
  if (args.out) io.write(args.out, markdown);
  if (args.json) {
    io.write(args.json, JSON.stringify({
      verdict: model.verdict, profile: model.profile, legs: model.legs,
      targets: model.targetRows, thresholdFailures: model.thresholdFailures, flows: model.flows, stages: model.stages,
    }, null, 2));
  }
  return { model, markdown, exitCode: args.requirePass && model.verdict !== 'PASS' ? 1 : 0 };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  try {
    const { model, markdown, exitCode } = main(process.argv.slice(2));
    if (!process.argv.includes('--out')) process.stdout.write(markdown);
    process.stderr.write(`load report verdict: ${model.verdict}\n`);
    process.exitCode = exitCode;
  } catch (error) {
    process.stderr.write(`k6-load-report: ${error.message}\n`);
    process.exitCode = 2;
  }
}
