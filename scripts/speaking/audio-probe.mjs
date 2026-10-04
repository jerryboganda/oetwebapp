#!/usr/bin/env node
/**
 * Speaking audio judge — release probe (owner spec 4 Oct 2026).
 *
 * Sends short clips of the SAME known phrase to the production probe endpoint
 * (POST /v1/admin/speaking/audio-assess/probe) and checks the gate the audio stage must pass before the admin flag
 * `speaking_audio_assessment` is turned on for real grades:
 *
 *   1. clean clip      the model heard THIS recording (what it heard matches the phrase, similarity >= 0.6)
 *   2. clean clip      it came back as a verified, scored judgement from a named model
 *   3. noise only      a clip with no speech in it is NOT accepted as a judgement (the judge invents nothing)
 *   4. degraded speech the same speech buried in noise is not trusted like the clean one: refused, or flagged as a
 *                      weaker recording, or scored below the clean clip
 *
 * The clips are synthetic (a speech synthesiser reading the phrase; ffmpeg makes the noise and the degraded copy),
 * so no learner audio is involved. Optional accented clips (a different-language voice reading the English phrase)
 * are printed for information only: a synthetic voice is usually fully intelligible, so they cannot gate anything.
 * This is plumbing and sanity evidence; accuracy against human experts is what the calibration harness measures.
 *
 * Usage:
 *   OET_ADMIN_EMAIL=... OET_ADMIN_PASSWORD=... node scripts/speaking/audio-probe.mjs \
 *     --clean clean.wav --noise noise.wav --degraded degraded.wav [--accented a.wav,b.wav] --phrase "Good afternoon, ..."
 *
 * Env: OET_API_BASE (default https://api.oetwithdrhesham.co.uk), OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD.
 * Credentials are read from the environment only and never printed. Output: a table on stdout, `audio-probe-result.json`,
 * and a markdown summary appended to $GITHUB_STEP_SUMMARY when set. Exit code 1 when any check fails.
 */

import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { basename } from 'node:path';

const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');

function arg(name) {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : '';
}

const cleanFile = arg('clean');
const noiseFile = arg('noise');
const degradedFile = arg('degraded');
const accentedFiles = arg('accented').split(',').map((f) => f.trim()).filter(Boolean);
const phrase = arg('phrase');
if (!cleanFile || !noiseFile || !degradedFile || !phrase) {
  console.error('Usage: audio-probe.mjs --clean <file> --noise <file> --degraded <file> [--accented <file>[,<file>...]] --phrase "<what was said>"');
  process.exit(2);
}

async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL || '';
  const password = process.env.OET_ADMIN_PASSWORD || '';
  if (!email || !password) throw new Error('OET_ADMIN_EMAIL and OET_ADMIN_PASSWORD are required.');
  const res = await fetch(`${API_BASE}/v1/auth/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email, password, rememberMe: true }),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`sign-in -> HTTP ${res.status}: ${text.slice(0, 160)}`);
  const json = JSON.parse(text);
  if (!json.accessToken) throw new Error('Sign-in response carried no accessToken.');
  return json.accessToken;
}

async function probe(token, file) {
  const form = new FormData();
  form.append('audio', new Blob([readFileSync(file)], { type: 'audio/wav' }), basename(file));
  form.append('phrase', phrase);
  const res = await fetch(`${API_BASE}/v1/admin/speaking/audio-assess/probe`, {
    method: 'POST',
    headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
    body: form,
  });
  const text = await res.text();
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    body = { raw: text.slice(0, 300) };
  }
  return { file: basename(file), http: res.status, ...body };
}

const token = await signIn();
const clean = await probe(token, cleanFile);
const noise = await probe(token, noiseFile);
const degraded = await probe(token, degradedFile);
const accented = [];
for (const file of accentedFiles) accented.push(await probe(token, file));

const judged = (r) => r.http === 200 && r.status === 'audio';
const checks = [
  ['clean clip: the model heard this recording (similarity >= 0.6)', judged(clean) && clean.openingSimilarity >= 0.6],
  ['clean clip: a verified, scored judgement from a named model', judged(clean) && Number.isInteger(clean.intelligibilityScore) && !!clean.model],
  // A clip with no speech must never come back as a verified judgement: this is the "invents nothing" property.
  ['a clip with no speech is not accepted as a judgement', noise.http === 200 && noise.status !== 'audio'],
  [
    'the same speech buried in noise is not trusted like the clean clip (refused, flagged weaker, or scored lower)',
    degraded.http === 200
      && judged(clean)
      && (!judged(degraded)
        || degraded.audioQuality !== 'good'
        || degraded.confidence !== 'high'
        || degraded.intelligibilityScore < clean.intelligibilityScore),
  ],
];

const row = (label, r) =>
  `${label.padEnd(18)} http=${r.http} status=${r.status ?? r.error ?? '?'}` +
  `${r.audioQuality ? ` quality=${r.audioQuality}` : ''}` +
  `${r.reason ? ` reason=${r.reason}` : ''}` +
  ` similarity=${r.openingSimilarity ?? '-'} intelligibility=${r.intelligibilityScore ?? '-'}` +
  ` observations=${r.observations?.length ?? '-'} confidence=${r.confidence ?? '-'}` +
  ` model=${r.model ?? '-'} audioMs=${r.durationMs ?? '-'} latencyMs=${r.latencyMs ?? '-'}`;

const all = [['clean', clean], ['noise-only', noise], ['degraded', degraded], ...accented.map((r) => [r.file, r])];
for (const [label, r] of all) {
  console.log(row(label, r));
  console.log(`${' '.repeat(18)} heard: ${JSON.stringify(r.heardOpening ?? null)}${r.message ? ` | error: ${r.message}` : ''}`);
}

console.log('');
let failed = 0;
for (const [name, ok] of checks) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failed += 1;
}

writeFileSync(
  'audio-probe-result.json',
  JSON.stringify({ phrase, clean, noise, degraded, accented, checks: checks.map(([name, ok]) => ({ name, ok })) }, null, 2),
);
if (process.env.GITHUB_STEP_SUMMARY) {
  const lines = [
    `### Speaking audio judge probe: ${failed === 0 ? 'PASS' : `FAIL (${failed} of ${checks.length})`}`,
    '',
    '| Clip | HTTP | Status | Similarity | Intelligibility | Observations | Model |',
    '|---|---|---|---|---|---|---|',
    ...all.map(
      ([label, r]) =>
        `| ${label} | ${r.http} | ${r.status ?? r.error ?? '?'}${r.reason ? ` (${r.reason})` : ''} | ${r.openingSimilarity ?? '-'} | ${r.intelligibilityScore ?? '-'} | ${r.observations?.length ?? '-'} | ${r.model ?? '-'} |`,
    ),
    '',
    ...checks.map(([name, ok]) => `- ${ok ? 'PASS' : 'FAIL'}: ${name}`),
  ];
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${lines.join('\n')}\n`);
}

process.exit(failed === 0 ? 0 : 1);
