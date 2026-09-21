#!/usr/bin/env node
/**
 * Listening safety floor (owner-approved stop-gap, 2026-09-22).
 *
 * Raises any Listening section timer that is shorter than its audio so the 00:00
 * auto-advance can never cut a recording (or auto-submit before the last audio),
 * and hides / unhides a paper from candidates. Temporary protective floor only:
 * final timers are re-derived from source verification later.
 *
 *   node scripts/listening/safety-floor.mjs selftest               # offline logic check
 *   node scripts/listening/safety-floor.mjs plan [--skip id,id]    # GET-only: snapshot + plan
 *   node scripts/listening/safety-floor.mjs apply [--plan file]    # PATCH timers from the newest plan
 *   node scripts/listening/safety-floor.mjs verify [--plan file]   # re-GET, confirm floors still hold
 *   node scripts/listening/safety-floor.mjs visibility <paperId> <true|false>
 *   node scripts/listening/safety-floor.mjs set <paperId> <extractCode> <seconds>   # single timer, never lowers
 *
 * Rule: effective timer < ceil(audio) -> set to ceil(audio) + 15 s. Never lowers a timer.
 * Part B's countdown is driven by B1, so every B extract below the audio is raised.
 * Every write is If-Match guarded, throttled and logged to state/floor/<ts>/ledger.json;
 * a full snapshot of each paper is stored before any write. Nothing is deleted.
 *
 * Env: OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD (never printed), OET_API_BASE.
 */
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const STATE_DIR = join(__dirname, 'state', 'floor');
const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];
const FLOOR_BUFFER_SEC = 15;
const DEFAULT_TIMER_SEC = 90; // LISTENING_EXAM_DEFAULT_TIME_LIMIT_SECONDS in lib/listening-exam-sections.ts
const WRITE_GAP_MS = 2500;    // stays far below the PerUserWrite limiter; each PATCH also triggers a relational resync
const SERIES = [
  { id: 'atlas', matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'] },
  { id: 'nova', matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'] },
];

const arg = (name) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : null; };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const writeJson = (file, data) => { mkdirSync(dirname(file), { recursive: true }); writeFileSync(file, JSON.stringify(data, null, 2)); };
const readJson = (file) => JSON.parse(readFileSync(file, 'utf-8'));

// ── pure logic (covered by selftest) ─────────────────────────────────────────
const sectionOf = (partCode) => {
  const c = String(partCode ?? '').trim().toUpperCase();
  return c === 'B' || /^B[1-6]$/.test(c) ? 'B' : c;
};
const timerOf = (extract) => (extract?.timeLimitSeconds > 0 ? extract.timeLimitSeconds : DEFAULT_TIMER_SEC);

/** Primary Audio per section: exact key -> legacy B1..B6 -> parent A/C (mirrors the learner resolver). */
function resolveAudio(assets) {
  const byKey = new Map();
  for (const a of assets) {
    if (a.role !== 'Audio' || !a.isPrimary) continue;
    const key = String(a.part ?? '').trim().toUpperCase();
    if (key && !byKey.has(key)) byKey.set(key, { mediaAssetId: a.mediaAssetId, dur: a.media?.durationSeconds ?? null });
  }
  return (code) => {
    if (byKey.has(code)) return byKey.get(code);
    if (code === 'B') for (let i = 1; i <= 6; i++) if (byKey.has(`B${i}`)) return byKey.get(`B${i}`);
    return byKey.get(code[0]) ?? null;
  };
}

function planTimers(audioOf, extracts) {
  const actions = [];
  const notes = [];
  for (const code of SECTIONS) {
    const audio = audioOf(code);
    if (!audio?.dur) continue;
    const need = Math.ceil(audio.dur);
    const group = extracts.filter((e) => sectionOf(e.partCode) === code).sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
    if (!group.length) { notes.push(`${code}: audio ${need}s but no extract row (default ${DEFAULT_TIMER_SEC}s applies)`); continue; }
    const rep = timerOf(group[0]);
    if (rep >= need && rep < need + 5) notes.push(`${code}: tight timer ${rep}s vs audio ${need}s (not changed)`);
    for (const e of group) {
      if (timerOf(e) >= need) continue;
      actions.push({ code: String(e.partCode).toUpperCase(), section: code, audioSec: need, from: e.timeLimitSeconds ?? null, effectiveFrom: timerOf(e), to: need + FLOOR_BUFFER_SEC, mediaAssetId: audio.mediaAssetId });
    }
  }
  return { actions, notes };
}

function seriesOf(paper) {
  const hay = [paper.tagsCsv, paper.slug, paper.title].filter((v) => v && String(v).trim()).join(' ').toLowerCase().replace(/_+/g, '-');
  return SERIES.find((s) => s.matchers.some((m) => hay.includes(m)))?.id ?? 'other';
}

function selftest() {
  const asset = (part, dur) => ({ role: 'Audio', isPrimary: true, part, mediaAssetId: `m-${part}`, media: { durationSeconds: dur } });
  const ex = (partCode, t, displayOrder = 0) => ({ partCode, timeLimitSeconds: t, displayOrder });
  // audio longer than timer -> raised to audio+15; timer already >= audio -> untouched
  let r = planTimers(resolveAudio([asset('A1', 300), asset('A2', 330)]), [ex('A1', 346), ex('A2', 307)]);
  assert.deepEqual(r.actions.map((a) => [a.code, a.to]), [['A2', 345]]);
  // shared parent asset resolves for both siblings (ST8 shape)
  r = planTimers(resolveAudio([asset('A', 645)]), [ex('A1', 408), ex('A2', 253)]);
  assert.deepEqual(r.actions.map((a) => [a.code, a.to]), [['A1', 660], ['A2', 660]]);
  // Part B: every B extract below the audio is raised; B timers never lowered
  r = planTimers(resolveAudio([asset('B', 477)]), [ex('B1', 100, 1), ex('B2', 600, 2), ex('B3', 0, 3)]);
  assert.deepEqual(r.actions.map((a) => [a.code, a.to]), [['B1', 492], ['B3', 492]]);
  // missing / zero timer means the 90 s player default, which is also too short
  r = planTimers(resolveAudio([asset('C2', 400)]), [ex('C2', null)]);
  assert.deepEqual(r.actions.map((a) => a.to), [415]);
  // tight-but-sufficient timers are only noted
  r = planTimers(resolveAudio([asset('C1', 400)]), [ex('C1', 402)]);
  assert.equal(r.actions.length, 0);
  assert.equal(r.notes.length, 1);
  console.log('selftest ok');
}

// ── API ──────────────────────────────────────────────────────────────────────
class HttpError extends Error { constructor(status, message) { super(message); this.status = status; } }
let token = '';
let tokenExpiresAt = 0;
async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL, password = process.env.OET_ADMIN_PASSWORD;
  if (!email || !password) throw new Error('Set OET_ADMIN_EMAIL and OET_ADMIN_PASSWORD.');
  const res = await fetch(`${API_BASE}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password, rememberMe: true }) });
  const text = await res.text();
  if (!res.ok) throw new HttpError(res.status, `sign-in failed: HTTP ${res.status}`);
  const j = JSON.parse(text);
  token = j.accessToken;
  tokenExpiresAt = j.accessTokenExpiresAt ? new Date(j.accessTokenExpiresAt).getTime() : Date.now() + 15 * 60_000;
}
async function api(method, path, { json, headers = {}, retried = false } = {}) {
  if (!token || Date.now() > tokenExpiresAt - 60_000) await signIn();
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers: { Accept: 'application/json', Authorization: `Bearer ${token}`, ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}), ...headers },
    body: json !== undefined ? JSON.stringify(json) : undefined,
  });
  if (res.status === 401 && !retried) { token = ''; return api(method, path, { json, headers, retried: true }); }
  const text = await res.text();
  if (!res.ok) throw new HttpError(res.status, `${method} ${path} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  // The reverse proxy gzips and rewrites the strong ETag "12" to W/"12"; the API's If-Match check only parses "12".
  return { status: res.status, json: text ? JSON.parse(text) : null, etag: res.headers.get('etag')?.replace(/^W\//, '') ?? null };
}

async function listInScope() {
  const papers = [];
  for (let page = 1; ; page++) {
    const batch = (await api('GET', `/v1/admin/papers?subtest=listening&page=${page}&pageSize=100`)).json;
    if (!Array.isArray(batch) || !batch.length) break;
    papers.push(...batch);
    if (batch.length < 100) break;
  }
  return papers.filter((p) => p.status !== 'Archived' && seriesOf(p) !== 'other');
}

const newestRun = () => { const d = readdirSync(STATE_DIR).filter((n) => /^\d{8}T/.test(n)).sort().pop(); if (!d) throw new Error('no plan yet'); return join(STATE_DIR, d); };
const planFile = () => arg('--plan') ?? join(newestRun(), 'plan.json');

// ── commands ─────────────────────────────────────────────────────────────────
async function plan() {
  const skip = new Set((arg('--skip') ?? '').split(',').filter(Boolean));
  const dir = join(STATE_DIR, new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z'));
  const papers = [];
  for (const p of await listInScope()) {
    const full = (await api('GET', `/v1/admin/papers/${p.id}`)).json;
    const ex = await api('GET', `/v1/admin/papers/${p.id}/listening/extracts`);
    writeJson(join(dir, 'snapshot', `${p.id}.json`), { paper: full, extracts: ex.json, etag: ex.etag });
    const { actions, notes } = planTimers(resolveAudio(full.assets ?? []), ex.json?.extracts ?? []);
    papers.push({ paperId: p.id, title: p.title, status: full.status, candidateVisible: full.candidateVisible ?? null, skipped: skip.has(p.id), actions, notes });
    await sleep(120);
  }
  writeJson(join(dir, 'plan.json'), { generatedAt: new Date().toISOString(), floorBufferSec: FLOOR_BUFFER_SEC, papers });
  const rows = papers.flatMap((p) => p.actions.map((a) => `${p.skipped ? '(skip) ' : ''}${p.title.replace(/ Practice Series — (Listening )?/, ' ')} | ${a.code} | audio ${a.audioSec}s | timer ${a.effectiveFrom}s -> ${a.to}s`));
  console.log(rows.join('\n') || 'nothing to change');
  const n = papers.filter((p) => !p.skipped).reduce((s, p) => s + p.actions.length, 0);
  console.log(`\n${papers.length} papers snapshotted; ${n} timer patches planned on ${papers.filter((p) => !p.skipped && p.actions.length).length} papers. Plan: ${join(dir, 'plan.json')}`);
}

async function apply() {
  const file = planFile();
  const { papers } = readJson(file);
  const ledgerFile = join(dirname(file), 'ledger.json');
  const ledger = [];
  for (const p of papers) {
    if (p.skipped) continue;
    for (const a of p.actions) {
      const rec = { paperId: p.paperId, title: p.title, ...a };
      let last = null;
      for (let attempt = 0; attempt < 3; attempt++) {
        try {
          const g = await api('GET', `/v1/admin/papers/${p.paperId}/listening/extracts`);
          const cur = timerOf((g.json?.extracts ?? []).find((e) => String(e.partCode).toUpperCase() === a.code));
          if (cur >= a.audioSec) { Object.assign(rec, { result: 'skipped-already-ok', current: cur }); break; }
          await sleep(WRITE_GAP_MS);
          const r = await api('PATCH', `/v1/admin/papers/${p.paperId}/listening/extracts/${a.code}`, { json: { timeLimitSeconds: a.to }, headers: g.etag ? { 'If-Match': g.etag } : {} });
          Object.assign(rec, { result: 'patched', httpStatus: r.status, at: new Date().toISOString() });
          break;
        } catch (e) {
          last = e;
          // Papers with learner attempts refuse every authoring edit (refused before any mutation); needs a revision or a backend change.
          if (/listening_relational_resync_blocked/.test(e.message)) { Object.assign(rec, { result: 'blocked-attempts' }); break; }
          if (![409, 412, 429].includes(e.status)) break;
          await sleep(3000 * (attempt + 1));
        }
      }
      if (!rec.result) Object.assign(rec, { result: 'FAILED', error: String(last?.message ?? last) });
      ledger.push(rec);
      writeJson(ledgerFile, ledger);
      console.log(`${rec.result.padEnd(18)} ${p.title.replace(/ Practice Series — (Listening )?/, ' ')} ${a.code} ${a.effectiveFrom} -> ${a.to}${rec.error ? `  ${rec.error}` : ''}`);
    }
  }
  const c = (k) => ledger.filter((r) => r.result === k).length;
  console.log(`\npatched ${c('patched')} | already ok ${c('skipped-already-ok')} | blocked by learner attempts ${c('blocked-attempts')} | FAILED ${c('FAILED')}. Ledger: ${ledgerFile}`);
  if (c('FAILED')) process.exitCode = 2;
}

async function verify() {
  const { papers } = readJson(planFile());
  let held = 0, drift = 0;
  for (const p of papers) {
    if (p.skipped || !p.actions.length) continue;
    const ex = (await api('GET', `/v1/admin/papers/${p.paperId}/listening/extracts`)).json?.extracts ?? [];
    for (const a of p.actions) {
      const now = timerOf(ex.find((e) => String(e.partCode).toUpperCase() === a.code));
      if (now >= a.audioSec) held++; else { drift++; console.log(`DRIFT ${p.title} ${a.code}: now ${now}s < audio ${a.audioSec}s`); }
    }
    await sleep(120);
  }
  console.log(`floors held ${held} | drifted ${drift}`);
  if (drift) process.exitCode = 3;
}

async function visibility() {
  const [paperId, flag] = process.argv.slice(3);
  if (!paperId || !['true', 'false'].includes(flag)) throw new Error('usage: visibility <paperId> <true|false>');
  const before = (await api('GET', `/v1/admin/papers/${paperId}`)).json;
  await api('POST', `/v1/admin/papers/${paperId}/candidate-visible?visible=${flag}`);
  const after = (await api('GET', `/v1/admin/papers/${paperId}`)).json;
  const rec = { at: new Date().toISOString(), paperId, title: before.title, requested: flag, before: { status: before.status, candidateVisible: before.candidateVisible }, after: { status: after.status, candidateVisible: after.candidateVisible } };
  writeJson(join(STATE_DIR, `visibility-${Date.now()}.json`), rec);
  console.log(JSON.stringify(rec));
  if (String(after.candidateVisible) !== flag) process.exitCode = 4;
}

/** One targeted timer change (used ahead of a verified audio repair): set <paperId> <extractCode> <seconds>. Never lowers. */
async function set() {
  const [paperId, code, sec] = process.argv.slice(3);
  const to = Number(sec);
  if (!paperId || !/^(A1|A2|B[1-6]|C1|C2)$/i.test(code ?? '') || !(to > 0)) throw new Error('usage: set <paperId> <A1|A2|B1..B6|C1|C2> <seconds>');
  const c = code.toUpperCase();
  const rec = { at: new Date().toISOString(), paperId, code: c, to };
  for (let attempt = 0; attempt < 3 && !rec.result; attempt++) {
    try {
      const g = await api('GET', `/v1/admin/papers/${paperId}/listening/extracts`);
      const cur = timerOf((g.json?.extracts ?? []).find((e) => String(e.partCode).toUpperCase() === c));
      rec.from = cur;
      if (cur >= to) { rec.result = 'skipped-already-ok'; break; }
      await sleep(WRITE_GAP_MS);
      rec.httpStatus = (await api('PATCH', `/v1/admin/papers/${paperId}/listening/extracts/${c}`, { json: { timeLimitSeconds: to }, headers: g.etag ? { 'If-Match': g.etag } : {} })).status;
      rec.result = 'patched';
    } catch (e) {
      if (/listening_relational_resync_blocked/.test(e.message)) { rec.result = 'blocked-attempts'; break; }
      if (![409, 412, 429].includes(e.status)) { rec.result = 'FAILED'; rec.error = e.message; break; }
      await sleep(3000 * (attempt + 1));
    }
  }
  rec.result ??= 'FAILED';
  writeJson(join(STATE_DIR, `set-${Date.now()}.json`), rec);
  console.log(JSON.stringify(rec));
  if (rec.result === 'FAILED') process.exitCode = 2;
}

const commands = { selftest, plan, apply, verify, visibility, set };
const cmd = process.argv[2];
if (!commands[cmd]) { console.error('usage: safety-floor.mjs <selftest|plan|apply|verify|visibility>'); process.exit(1); }
Promise.resolve(commands[cmd]()).catch((e) => { console.error(e.message || e); process.exit(1); });
