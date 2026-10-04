#!/usr/bin/env node
/**
 * Speaking grader calibration: the driver of the harness (owner spec 4 Oct 2026; docs/speaking/grader-calibration.md).
 *
 * Starts (or resumes) a calibration run over the expert-marked performances and keeps asking the API to queue the next
 * grade until every grade has finished, then finalises the run and writes the report. Each grade is a durable operation run
 * by the API's own worker, so this script only polls: nothing is graded here and the run survives this script stopping.
 * The API yields to learners (a calibration grade never starts while a learner's grade is queued or running).
 *
 * Prints numbers and ids only: never a transcript, a learner name or audio.
 *
 * Usage:
 *   OET_ADMIN_EMAIL=... OET_ADMIN_PASSWORD=... node scripts/speaking/grader-calibration.mjs \\
 *     [--run <id>] [--repeats 2] [--no-audio] [--max-minutes 330] [--poll-seconds 30]
 *
 * Env: OET_API_BASE (default https://api.oetwithdrhesham.co.uk), OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD.
 * Output: stdout, \`grader-calibration-report.json\`, and a markdown summary in $GITHUB_STEP_SUMMARY when set.
 * Exit code: 0 when the run finished or is still in progress (re-run with --run <id>); 1 on an error.
 */

import { appendFileSync, writeFileSync } from 'node:fs';

const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const BASE = `${API_BASE}/v1/admin/speaking/grader-calibration`;

function arg(name, fallback = '') {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--') ? process.argv[i + 1] : fallback;
}
const flag = (name) => process.argv.includes(`--${name}`);

const runIdArg = arg('run');
const repeats = Number.parseInt(arg('repeats', '2'), 10) || 2;
const useAudio = !flag('no-audio');
const maxMinutes = Number.parseInt(arg('max-minutes', '330'), 10) || 330;
const pollSeconds = Math.max(5, Number.parseInt(arg('poll-seconds', '30'), 10) || 30);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ── Auth: access tokens last 15 minutes and a run lasts hours, so refresh ───
let accessToken = '';
let refreshToken = '';
let expiresAt = 0;

function applySession(json) {
  accessToken = json.accessToken;
  if (json.refreshToken) refreshToken = json.refreshToken;
  expiresAt = json.accessTokenExpiresAt ? new Date(json.accessTokenExpiresAt).getTime() : Date.now() + 15 * 60_000;
  if (!accessToken) throw new Error('Auth response carried no accessToken.');
}

async function authPost(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`POST ${path} -> HTTP ${res.status}: ${text.slice(0, 160)}`);
  return JSON.parse(text);
}

async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL || '';
  const password = process.env.OET_ADMIN_PASSWORD || '';
  if (!email || !password) throw new Error('OET_ADMIN_EMAIL and OET_ADMIN_PASSWORD are required.');
  applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true }));
}

async function api(method, path, body) {
  if (Date.now() > expiresAt - 60_000) {
    if (refreshToken) applySession(await authPost('/v1/auth/refresh', { refreshToken }));
    else await signIn();
  }
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: { Accept: 'application/json', 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = { raw: text.slice(0, 200) };
  }
  return { status: res.status, json };
}

const line = (progress) =>
  `graded ${progress.done}/${progress.total} (pending ${progress.pending}, running ${progress.queued}, failed ${progress.failed})`;

// ── Run ─────────────────────────────────────────────────────────────────────
await signIn();

let runId = runIdArg;
if (!runId) {
  const created = await api('POST', '/runs', { repeats, useAudio });
  if (created.status === 200) {
    runId = created.json.id;
    console.log(`started run ${runId}: repeats=${created.json.repeats} audio=${created.json.useAudio} ${line(created.json.progress)}`);
  } else if (created.status === 409) {
    const runs = await api('GET', '/runs');
    const running = (runs.json ?? []).find((r) => r.status === 'running');
    if (!running) throw new Error(`could not start a run (HTTP 409) and none is running: ${JSON.stringify(created.json).slice(0, 200)}`);
    runId = running.id;
    console.log(`resuming the running run ${runId}: ${line(running.progress)}`);
  } else {
    throw new Error(`could not start a run: HTTP ${created.status} ${JSON.stringify(created.json).slice(0, 200)}`);
  }
} else {
  console.log(`resuming run ${runId}`);
}

const deadline = Date.now() + maxMinutes * 60_000;
let lastLine = '';
let finished = false;
while (Date.now() < deadline) {
  const next = await api('POST', `/runs/${encodeURIComponent(runId)}/next`);
  if (next.status !== 200) throw new Error(`next -> HTTP ${next.status} ${JSON.stringify(next.json).slice(0, 200)}`);
  const state = next.json.state;
  const text = `${state}: ${line(next.json.progress)}`;
  if (text !== lastLine) {
    console.log(`${new Date().toISOString()} ${text}`);
    lastLine = text;
  }

  if (state === 'done' || state === 'complete') {
    finished = true;
    break;
  }

  // yield: a learner's grade is waiting, so look again soon; otherwise a grade takes a few minutes.
  await sleep((state === 'yield' ? Math.min(pollSeconds, 20) : pollSeconds) * 1000);
}

if (!finished) {
  console.log(`still in progress after ${maxMinutes} minutes: re-run with --run ${runId} (the grades keep running on the server)`);
  process.exit(0);
}

const finalised = await api('POST', `/runs/${encodeURIComponent(runId)}/finalize`);
if (finalised.status !== 200) throw new Error(`finalize -> HTTP ${finalised.status} ${JSON.stringify(finalised.json).slice(0, 200)}`);
const view = await api('GET', `/runs/${encodeURIComponent(runId)}`);
const report = view.json?.report;
writeFileSync('grader-calibration-report.json', JSON.stringify(view.json, null, 2));

if (!report) {
  console.log('the run finished but produced no report (no grade completed)');
  process.exit(0);
}

const pct = (rate) => `${(rate.share * 100).toFixed(0)}% [${(rate.low * 100).toFixed(0)}-${(rate.high * 100).toFixed(0)}]`;
const out = [];
out.push(`RUN ${runId} grader=${view.json.graderVersion} performances=${report.performances} grades=${report.observations} repeats=${report.repeats}`);
out.push(`VERDICT: ${report.verdict.passed ? 'PASS' : 'FAIL'}`);
for (const failure of report.verdict.failures) out.push(`  - ${failure}`);
out.push('criterion            n   mean-err  bias   exact           within-1');
for (const c of report.criteria) {
  out.push(`${c.code.padEnd(20)} ${String(c.n).padStart(3)} ${String(c.mae).padStart(8)} ${String(c.bias).padStart(6)}   ${pct(c.exact).padEnd(15)} ${pct(c.adjacent)}`);
}
if (report.intelligibilityFromAudio) out.push(`intelligibility from audio: n=${report.intelligibilityFromAudio.n} mean-err=${report.intelligibilityFromAudio.mae} bias=${report.intelligibilityFromAudio.bias}`);
if (report.intelligibilityFromTranscript) out.push(`intelligibility from transcript: n=${report.intelligibilityFromTranscript.n} mean-err=${report.intelligibilityFromTranscript.mae} bias=${report.intelligibilityFromTranscript.bias}`);
out.push(`raw total: mean-err=${report.rawTotal.mae} bias=${report.rawTotal.bias}`);
const m = report.mapping;
out.push(`score today (v0 map): mean-err=${m.v0EndToEnd.mae} bias=${m.v0EndToEnd.bias} within40=${pct(m.v0EndToEnd.within40)}`);
out.push(`score with a fitted map (leave-one-out): mean-err=${m.leaveOneOut.mae} bias=${m.leaveOneOut.bias} within40=${pct(m.leaveOneOut.within40)}`);
out.push(`v0 map on the expert's own criteria: mean-err=${m.v0OnExpert.mae} bias=${m.v0OnExpert.bias}`);
out.push(`grade: exact=${pct(report.grade.exact)} within-1=${pct(report.grade.adjacent)}; pass/fail agreement=${pct(report.passFail.agreement)} false-pass=${pct(report.passFail.falsePass)} false-fail=${pct(report.passFail.falseFail)}`);
if (report.repeatability) {
  const r = report.repeatability;
  out.push(`repeatability: criterion-repeat=${pct(r.criterionRepeat)} score-within-20=${pct(r.scaledWithin20)} grade-stable=${pct(r.gradeStable)} pass-stable=${pct(r.passStable)}`);
}
out.push(`fitted map raw 0..39: ${m.fitted.join(',')}`);
console.log(out.join('\n'));

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Speaking grader calibration: ${report.verdict.passed ? 'PASS' : 'FAIL'}\n\n\`\`\`\n${out.join('\n')}\n\`\`\`\n`);
}
