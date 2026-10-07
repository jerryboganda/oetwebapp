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
 *     [--run <id>] [--repeats 2] [--no-audio] [--scope card|mock] [--pilot] [--sample <id>]... [--max-minutes 330] [--poll-seconds 30] [--smoke]
 *
 * --sample <id> (repeatable) grades ONLY those performances (a mock-sample id with --scope mock) instead of every marked,
 * usable one: the one-performance owner diagnostic. The report then carries, per grade, the ten items of that diagnostic.
 *
 * --scope card grades each expert-marked single card with the card grader (default); --scope mock grades each expert-marked
 * Full Mock with the combined grader (speaking.score.v4-combined). --pilot marks the run an OWNER PILOT: an informational
 * comparison whose report cannot pass by design — the score stays Provisional and the approved coverage/thresholds are
 * what the later validation run (no --pilot) must meet.
 *
 * --smoke signs in and reads the calibration overview and candidate list (counts only), then stops: the authenticated proof
 * that the Admin > Speaking > Grader calibration screen's API is live. It starts no run and writes nothing.
 *
 * Env: OET_API_BASE (default https://api.oetwithdrhesham.co.uk), OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD.
 * Output: stdout, \`grader-calibration-report.json\`, \`grader-calibration-report.md\` (the full per-performance comparison and,
 * per grade, the one-performance diagnostic), the same two files named \`grader-calibration-report-<run id>.*\`, and a markdown
 * summary in $GITHUB_STEP_SUMMARY when set.
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
const args = (name) =>
  process.argv.flatMap((value, i) =>
    value === `--${name}` && process.argv[i + 1] && !process.argv[i + 1].startsWith('--') ? [process.argv[i + 1]] : []);

const runIdArg = arg('run');
const sampleIds = args('sample');
const repeats = Number.parseInt(arg('repeats', '2'), 10) || 2;
const useAudio = !flag('no-audio');
const scope = arg('scope', 'card') === 'mock' ? 'mock' : 'card';
const pilot = flag('pilot');
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

if (flag('smoke')) {
  const overview = await api('GET', '/');
  const candidates = await api('GET', '/candidates?take=100');
  const mocks = await api('GET', '/mocks');
  const mockCandidates = await api('GET', '/mock-candidates?take=100');
  if (overview.status !== 200) throw new Error(`overview -> HTTP ${overview.status} ${JSON.stringify(overview.json).slice(0, 200)}`);
  if (candidates.status !== 200) throw new Error(`candidates -> HTTP ${candidates.status} ${JSON.stringify(candidates.json).slice(0, 200)}`);
  if (mocks.status !== 200) throw new Error(`mocks -> HTTP ${mocks.status} ${JSON.stringify(mocks.json).slice(0, 200)}`);
  if (mockCandidates.status !== 200) throw new Error(`mock-candidates -> HTTP ${mockCandidates.status} ${JSON.stringify(mockCandidates.json).slice(0, 200)}`);
  const c = overview.json.coverage;
  const m = mocks.json.coverage;
  const smoke = [
    'SMOKE OK: the grader calibration API answers for an admin',
    `candidates (finished AI cards under the calibration consent): ${candidates.json.length}, with audio: ${candidates.json.filter((x) => x.hasAudio).length}`,
    `promoted samples: ${c.total} (labelled ${c.labelled}, pending ${c.pending}, excluded ${c.excluded})`,
    `mock candidates (completed two-card AI exams): ${mockCandidates.json.length}, with audio: ${mockCandidates.json.filter((x) => x.hasAudio).length}`,
    `promoted Full Mocks: ${m.total} (labelled ${m.labelled}, pending ${m.pending}, excluded ${m.excluded})`,
    `coverage (what the approved VALIDATION run needs; a pilot needs none of it): ${c.requiredLabelled} marked, ${c.requiredPerGrade} per grade, ${c.requiredNearPassLine} at 320-380 with ${c.requiredEachSideOfPassLine} each side of 350, ${(c.requiredAudioShare * 100).toFixed(0)}% with audio`,
    ...(c.unmet.length ? ['still missing:', ...c.unmet.map((u) => `  - ${u}`)] : ['coverage is complete']),
  ];
  console.log(smoke.join('\n'));
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Speaking grader calibration: smoke\n\n\`\`\`\n${smoke.join('\n')}\n\`\`\`\n`);
  process.exit(0);
}

let runId = runIdArg;
if (!runId) {
  const created = await api('POST', '/runs', { repeats, useAudio, scope, pilot, ...(sampleIds.length ? { sampleIds } : {}) });
  if (created.status === 200) {
    runId = created.json.id;
    console.log(`started run ${runId}: scope=${created.json.scope} pilot=${created.json.pilot} repeats=${created.json.repeats} audio=${created.json.useAudio} ${line(created.json.progress)}`);
  } else if (created.status === 409 && sampleIds.length === 0) {
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
// One copy named for the run (a second run must not overwrite the first's files) and the stable name the workflow uploads.
writeFileSync('grader-calibration-report.json', JSON.stringify(view.json, null, 2));
writeFileSync(`grader-calibration-report-${runId}.json`, JSON.stringify(view.json, null, 2));

if (!report) {
  console.log('the run finished but produced no report (no grade completed)');
  process.exit(0);
}

const pct = (rate) => `${(rate.share * 100).toFixed(0)}% [${(rate.low * 100).toFixed(0)}-${(rate.high * 100).toFixed(0)}]`;
const isPilot = report.verdict.mode === 'pilot';
const kind = view.json.scope === 'mock' ? 'Full Mock (combined grader)' : 'single cards';
const out = [];
out.push(`RUN ${runId} grader=${view.json.graderVersion} scope=${view.json.scope} pilot=${view.json.pilot} performances=${report.performances} grades=${report.observations} repeats=${report.repeats}`);
if (isPilot) {
  out.push('VERDICT: PILOT — cannot pass by design. An informational comparison on a small real sample: not statistical validation, and the Speaking score stays Provisional.');
} else {
  out.push(`VERDICT: ${report.verdict.passed ? 'PASS' : 'FAIL'}`);
}
for (const failure of report.verdict.failures) out.push(`  - ${failure}`);
for (const note of report.verdict.advisory ?? []) out.push(`  · ${note}`);
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
if (report.graderVersions) {
  out.push('grades per exact grader version + model (the label is earned per exact version):');
  for (const [version, count] of Object.entries(report.graderVersions)) out.push(`  ${count} x ${version}`);
}
console.log(out.join('\n'));

// The full per-performance comparison (ids and numbers only): the expert's nine marks and overall beside every grade.
// A Pass? column shows the pass/fail (350) agreement of each grade — often the first thing an owner pilot reads.
const codes = report.criteria.map((c) => c.code);
const md = [];
md.push(`# Speaking grader calibration: run ${runId}`, '');
md.push(`Scope: ${kind}. Grader: ${view.json.graderVersion}. Performances ${report.performances}, grades ${report.observations}, repeats ${report.repeats}.`);
if (isPilot) {
  md.push('**OWNER PILOT — informational comparison; not statistical validation; the Speaking score stays Provisional. This run cannot pass by design.**', '');
} else {
  md.push(`Verdict: **${report.verdict.passed ? 'PASS' : 'FAIL'}**.`, '');
}
if (report.verdict.failures.length) md.push(...report.verdict.failures.map((f) => `- ${f}`), '');
if (report.verdict.advisory?.length) md.push(...report.verdict.advisory.map((a) => `· ${a}`), '');
md.push('## Expert vs grader, per performance', '');
md.push(`| Performance | Audio | Expert (${codes.map((c) => c.slice(0, 4)).join('/')}) | Expert raw | Expert /500 | Grade | Repeat | Grader criteria | Grader raw | Grader /500 (leave-one-out) | Grade | Error | Intelligibility from | Pass? |`);
md.push('| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |');
const passOf = (scaled) => (scaled >= 350 ? 'pass' : 'fail');
for (const p of report.detail ?? []) {
  const expert = codes.map((c) => p.expertScores[c]).join('/');
  if (!p.grades.length) {
    md.push(`| ${p.sampleId} | ${p.hasAudio ? 'yes' : 'no'} | ${expert} | ${p.expertRaw} | ${p.expertOverall} | ${p.expertGrade} | not graded | | | | | | | |`);
  }
  for (const g of p.grades) {
    const agree = passOf(g.scaledLeaveOneOut) === passOf(p.expertOverall) ? 'same' : 'DIFFERENT';
    md.push(`| ${p.sampleId} | ${p.hasAudio ? 'yes' : 'no'} | ${expert} | ${p.expertRaw} | ${p.expertOverall} | ${p.expertGrade} | ${g.repeat} | ${codes.map((c) => g.scores[c]).join('/')} | ${g.raw} | ${g.scaledLeaveOneOut} | ${g.grade} | ${g.scaledError > 0 ? '+' : ''}${g.scaledError} | ${g.intelligibilitySource} | ${agree} (${passOf(p.expertOverall)} vs ${passOf(g.scaledLeaveOneOut)}) |`);
  }
}
md.push('', '## Grade confusion (rows: expert grade A,B,C+,C,D,E; columns: grader grade)', '', '```');
for (const row of report.grade.confusion) md.push(row.join('\t'));

// The one-performance diagnostic (owner request 7 Oct 2026): for every grade, the ten things needed to tell WHERE a
// difference from the expert comes from: the grader's judgement, missing audio, the secondary reviewer or the mapping.
const mappingVersion = report.mappingVersion ?? 'unknown';
const cardLabel = (i, n) => (n > 1 ? `card ${String.fromCharCode(65 + i)}` : 'card');
const audioText = (a) =>
  !a
    ? 'not run'
    : a.source === 'audio'
      ? `AUDIO (${a.model ?? 'model unknown'}; ${a.clips} clips, ${(a.audioMs / 1000).toFixed(0)} s of audio for ${(a.speechMs / 1000).toFixed(0)} s of speech${a.coverage != null ? ` = ${(a.coverage * 100).toFixed(0)}%` : ''}; confidence ${a.confidence})`
      : `TRANSCRIPT ONLY (${a.reason ?? 'no reason recorded'}; ${a.clips} clips, ${(a.audioMs / 1000).toFixed(0)} s of audio for ${(a.speechMs / 1000).toFixed(0)} s of speech${a.coverage != null ? ` = ${(a.coverage * 100).toFixed(0)}%` : ''}, ${a.turnsWithClip}/${a.turns} turns with a clip)`;
md.push('```', '', '## One-performance diagnostic (per grade)', '');
md.push(`Raw-to-reported mapping used for the score a learner sees: \`${mappingVersion}\` (a provisional heuristic, not an official OET conversion).`, '');
for (const p of report.detail ?? []) {
  for (const g of p.grades) {
    const d = g.diagnostics ?? null;
    const review = d?.review ?? null;
    md.push(`### ${p.sampleId}, repeat ${g.repeat}`, '');
    md.push(`1. Expert vs final AI criteria (${codes.map((c) => c.slice(0, 4)).join('/')}): expert ${codes.map((c) => p.expertScores[c]).join('/')} | AI ${codes.map((c) => g.scores[c]).join('/')}`);
    md.push(`2. Overall /500: expert ${p.expertOverall} (${p.expertGrade}) | AI as a learner sees it ${g.reportedScaled ?? 'n/a'}${g.reportedGrade ? ` (${g.reportedGrade})` : ''}${g.reportedError != null ? `, error ${g.reportedError > 0 ? '+' : ''}${g.reportedError}` : ''} | AI through a fitted map (leave-one-out) ${g.scaledLeaveOneOut} (${g.grade})`);
    md.push(`3. AI raw criterion total before mapping: ${g.raw} / 39 (expert ${p.expertRaw})`);
    md.push(`4. Mapping version: \`${d?.mappingVersion ?? mappingVersion}\``);
    const cards = d?.audio?.cards ?? [];
    if (cards.length > 0) {
      cards.forEach((c, i) => md.push(`${5 + i}. Real audio judgement, ${cardLabel(i, cards.length)}: ${audioText(c)}`));
    } else {
      md.push(`5-6. Real audio judgement: ${d ? audioText(d.audio?.combined) : 'not recorded (grade made before diagnostics existed)'}`);
    }
    md.push(`7. Combined Intelligibility was: ${g.intelligibilitySource === 'audio' ? 'AUDIO-based' : 'TRANSCRIPT-only'}${d?.audio?.combined ? ` (${audioText(d.audio.combined)})` : ''}`);
    md.push(`8. GraderVersion: \`${view.json.graderVersion}\` (a grade made by another version shows in the per-version counts above)`);
    md.push(`9. Primary Claude scores BEFORE the secondary reviewer: ${d?.primaryScores ? codes.map((c) => d.primaryScores[c] ?? '?').join('/') : 'not recorded (grade made before diagnostics existed)'}`);
    md.push(`10. Secondary reviewer: ${review ? `${review.status}${review.model ? ` (${review.model})` : ''}${review.changes?.length ? '; changes: ' + review.changes.map((c) => `${c.criterion} ${c.from}->${c.to}`).join(', ') : '; no criterion changed'}` : 'not recorded (grade made before diagnostics existed)'}`);
    md.push('');
  }
}
md.push('## Summary', '', '```', ...out, '```');
writeFileSync('grader-calibration-report.md', md.join('\n'));
writeFileSync(`grader-calibration-report-${runId}.md`, md.join('\n'));

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Speaking grader calibration: ${isPilot ? 'PILOT (cannot pass by design)' : report.verdict.passed ? 'PASS' : 'FAIL'}\n\n\`\`\`\n${out.join('\n')}\n\`\`\`\n`);
}
