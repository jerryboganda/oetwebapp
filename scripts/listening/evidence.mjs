#!/usr/bin/env node
/**
 * Listening fleet evidence collector. RUNS ON GITHUB ACTIONS (repo compute policy); `plan` is GET-only.
 *
 *   node scripts/listening/evidence.mjs plan [--shards 12] [--out plan.json]
 *   node scripts/listening/evidence.mjs run  --plan plan.json --shard <i> [--out dir] [--model small.en] [--limit n] [--dry]
 *
 * plan: every published Atlas/Nova paper -> primary Audio asset per section (A1/A2/B/C1/C2, exact key ->
 *       legacy B1..B6 -> parent A/C, the same resolution the learner audio uses), de-duplicated by media asset
 *       (a shared parent file is analysed once). Contains ids, titles and durations only.
 * run:  for this shard's assets: download, ffprobe, ffmpeg silence map, edge levels, then speech-to-text via
 *       asr_windows.py (whole file for A1/A2/C1/C2, head+tail for B). Writes evidence-<shard>.json.
 *       Logs never contain transcript text.
 *
 * Env: OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD (never printed), OET_API_BASE.
 */
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];
const SERIES = [
  { id: 'atlas', matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'] },
  { id: 'nova', matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'] },
];
const HEAD_SEC = 150, TAIL_SEC = 120;

const arg = (n, d = null) => { const i = process.argv.indexOf(n); return i >= 0 ? process.argv[i + 1] : d; };
const has = (n) => process.argv.includes(n);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const readJson = (f) => JSON.parse(readFileSync(f, 'utf-8'));
const writeJson = (f, d) => { mkdirSync(dirname(f), { recursive: true }); writeFileSync(f, JSON.stringify(d, null, 1)); };
const sh = (cmd, args, opts = {}) => spawnSync(cmd, args, { encoding: 'utf-8', maxBuffer: 1 << 27, ...opts });

// ── API ──────────────────────────────────────────────────────────────────────
// A new sign-in invalidates every earlier token of the account (single active session). So the plan job signs in
// ONCE and shares the token (sealed) with the shards; shards only sign in again if that token was revoked.
let token = '', tokenAt = 0;
async function signIn() {
  const res = await fetch(`${API}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD, rememberMe: true }) });
  if (!res.ok) throw new Error(`sign-in failed: HTTP ${res.status}`);
  token = (await res.json()).accessToken; tokenAt = Date.now();
}
async function authed(path, retried = 0) {
  if (!token || Date.now() - tokenAt > 12 * 60_000) await signIn();
  const res = await fetch(`${API}${path}`, { headers: { Authorization: `Bearer ${token}`, Accept: 'application/json' } });
  if (res.status === 401 && retried < 4) { token = ''; await sleep(1000 + Math.random() * 4000); return authed(path, retried + 1); }
  return res;
}
async function getJson(path) {
  const res = await authed(path);
  if (!res.ok) throw new Error(`GET ${path} -> HTTP ${res.status}`);
  return res.json();
}

// ── plan ─────────────────────────────────────────────────────────────────────
function seriesOf(p) {
  const hay = [p.tagsCsv, p.slug, p.title].filter((v) => v && String(v).trim()).join(' ').toLowerCase().replace(/_+/g, '-');
  return SERIES.find((s) => s.matchers.some((m) => hay.includes(m)))?.id ?? 'other';
}
function resolver(assets) {
  const byKey = new Map();
  for (const a of assets) {
    if (a.role !== 'Audio' || !a.isPrimary) continue;
    const key = String(a.part ?? '').trim().toUpperCase();
    if (key && !byKey.has(key)) byKey.set(key, { assetId: a.mediaAssetId, dbDur: a.media?.durationSeconds ?? null, file: a.media?.originalFilename ?? null, key });
  }
  return (code) => {
    if (byKey.has(code)) return byKey.get(code);
    if (code === 'B') for (let i = 1; i <= 6; i++) if (byKey.has(`B${i}`)) return byKey.get(`B${i}`);
    return byKey.get(code[0]) ?? null;
  };
}

async function plan() {
  let shards = Number(arg('--shards', 12));
  const extra = (arg('--extra', '') || '').split(',').map((s) => s.trim()).filter(Boolean); // candidate media assets (uploaded, not yet attached)
  const onlyExtra = has('--only-extra');
  if (arg('--token-out')) await signIn(); // one session for the whole run, shared (sealed) with the shards
  const papers = [];
  for (let page = 1; !onlyExtra; page++) {
    const batch = await getJson(`/v1/admin/papers?subtest=listening&page=${page}&pageSize=100`);
    if (!Array.isArray(batch) || !batch.length) break;
    papers.push(...batch);
    if (batch.length < 100) break;
  }
  const inScope = papers.filter((p) => p.status !== 'Archived' && seriesOf(p) !== 'other');
  const assets = new Map();
  const out = [];
  for (const p of inScope) {
    const full = await getJson(`/v1/admin/papers/${p.id}`);
    const audioOf = resolver(full.assets ?? []);
    const sections = {};
    for (const code of SECTIONS) {
      const a = audioOf(code);
      sections[code] = a ? { assetId: a.assetId, dbDur: a.dbDur, file: a.file, viaKey: a.key } : null;
      if (!a) continue;
      if (!assets.has(a.assetId)) assets.set(a.assetId, { assetId: a.assetId, file: a.file, dbDur: a.dbDur, uses: [], mode: 'headtail' });
      const rec = assets.get(a.assetId);
      rec.uses.push({ paperId: p.id, title: p.title, section: code });
      if (code !== 'B') rec.mode = 'full';
    }
    out.push({ paperId: p.id, title: p.title, series: seriesOf(p), status: full.status, candidateVisible: full.candidateVisible ?? null, sections });
    await sleep(100);
  }
  for (const id of extra) {
    if (!assets.has(id)) assets.set(id, { assetId: id, file: '(candidate)', dbDur: null, uses: [{ paperId: null, title: 'candidate', section: '?' }], mode: 'full' });
  }
  const list = [...assets.values()].sort((a, b) => cost(b) - cost(a));
  shards = Math.max(1, Math.min(shards, list.length));
  const load = Array(shards).fill(0);
  for (const a of list) { const s = load.indexOf(Math.min(...load)); a.shard = s; load[s] += cost(a); }
  const plan = { generatedAt: new Date().toISOString(), apiBase: API, shards, papers: out, assets: list };
  writeJson(arg('--out', 'plan.json'), plan);
  if (arg('--token-out') && token) writeJson(arg('--token-out'), { token, at: tokenAt }); // sealed by the workflow before upload
  console.log(`papers ${out.length} | assets ${list.length} | full ${list.filter((a) => a.mode === 'full').length} | shard load (min) ${load.map((l) => Math.round(l / 60)).join(',')}`);
}
const cost = (a) => (a.mode === 'full' ? (a.dbDur ?? 600) : HEAD_SEC + TAIL_SEC);

// ── run ──────────────────────────────────────────────────────────────────────
function probeDuration(f) { return parseFloat(sh('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', f]).stdout); }
function silenceMap(f, dur) {
  const r = sh('ffmpeg', ['-hide_banner', '-nostats', '-i', f, '-af', 'silencedetect=noise=-35dB:d=1.0', '-f', 'null', '-']);
  const out = [];
  for (const line of r.stderr.split('\n')) {
    let m;
    if ((m = line.match(/silence_start:\s*(-?[\d.]+)/))) out.push({ start: Math.max(0, +m[1]), end: null });
    else if ((m = line.match(/silence_end:\s*([\d.]+)/)) && out.length) out[out.length - 1].end = +m[1];
  }
  for (const s of out) if (s.end == null) s.end = dur;
  return out.map((s) => ({ start: +s.start.toFixed(2), end: +s.end.toFixed(2) }));
}
function edgeDb(f, tail) {
  const r = sh('ffmpeg', ['-hide_banner', '-nostats', ...(tail ? ['-sseof', '-0.4'] : ['-t', '0.4']), '-i', f, '-af', 'volumedetect', '-f', 'null', '-']);
  const m = r.stderr.match(/max_volume:\s*(-?[\d.]+|-inf) dB/);
  return m ? (m[1] === '-inf' ? -99 : +m[1]) : null;
}

async function run() {
  const plan = readJson(arg('--plan', 'plan.json'));
  const shard = Number(arg('--shard', 0));
  const outDir = arg('--out', 'evidence-out');
  const model = arg('--model', 'small.en');
  const limit = Number(arg('--limit', 0)) || Infinity;
  const work = join(outDir, 'work');
  mkdirSync(work, { recursive: true });
  if (arg('--token-file')) { const t = readJson(arg('--token-file')); token = t.token; tokenAt = t.at; } // shared session from the plan job
  const mine = plan.assets.filter((a) => a.shard === shard).slice(0, limit);
  console.log(`shard ${shard}: ${mine.length} assets`);

  const evidence = {};
  const manifest = [];
  for (const a of mine) {
    const rec = { file: a.file, dbDur: a.dbDur, mode: a.mode, uses: a.uses };
    evidence[a.assetId] = rec;
    try {
      const f = join(work, `${a.assetId}.mp3`);
      let ok = false;
      for (let attempt = 0; attempt < 3 && !ok; attempt++) {
        const res = await authed(`/v1/media/${a.assetId}/content`);
        if (!res.ok) { await sleep(2000 * (attempt + 1)); continue; }
        writeFileSync(f, Buffer.from(await res.arrayBuffer()));
        ok = true;
      }
      if (!ok) throw new Error('download failed');
      rec.bytes = statSync(f).size;
      rec.realDur = probeDuration(f);
      rec.silences = silenceMap(f, rec.realDur);
      rec.headMaxDb = edgeDb(f, false);
      rec.tailMaxDb = edgeDb(f, true);
      if (a.mode === 'full' || rec.realDur <= HEAD_SEC + TAIL_SEC + 30) {
        manifest.push({ key: `${a.assetId}|full`, file: f, offset: 0 });
      } else {
        const head = join(work, `${a.assetId}.head.mp3`), tail = join(work, `${a.assetId}.tail.mp3`);
        const tStart = Math.max(0, rec.realDur - TAIL_SEC);
        for (const [file, ss, t] of [[head, 0, HEAD_SEC], [tail, tStart, TAIL_SEC]]) {
          const c = sh('ffmpeg', ['-y', '-v', 'error', '-ss', String(ss), '-t', String(t), '-i', f, '-ac', '1', '-c:a', 'libmp3lame', '-q:a', '5', file]);
          if (c.status !== 0) throw new Error(`window cut failed: ${c.stderr.slice(-120)}`);
        }
        manifest.push({ key: `${a.assetId}|head`, file: head, offset: 0 }, { key: `${a.assetId}|tail`, file: tail, offset: tStart });
      }
      console.log(`prepared ${a.assetId} ${rec.realDur.toFixed(1)}s silences=${rec.silences.length}`);
    } catch (e) {
      rec.error = String(e.message || e);
      console.log(`ERROR ${a.assetId}: ${rec.error}`);
    }
  }

  if (!has('--dry') && manifest.length) {
    const mf = join(outDir, `manifest-${shard}.json`), rf = join(outDir, `asr-${shard}.json`);
    writeJson(mf, manifest);
    const r = spawnSync('python3', [join(__dirname, 'asr_windows.py'), mf, rf, model], { stdio: 'inherit' });
    if (r.status !== 0) { console.error('ASR step failed'); process.exitCode = 2; }
    else {
      const asr = readJson(rf);
      for (const [key, val] of Object.entries(asr)) {
        const [assetId, label] = key.split('|');
        (evidence[assetId].windows ??= {})[label] = val;
      }
    }
  }
  writeJson(join(outDir, `evidence-${shard}.json`), { generatedAt: new Date().toISOString(), shard, model, assets: evidence });
  if (Object.values(evidence).some((e) => e.error)) process.exitCode = process.exitCode || 2;
}

const cmd = process.argv[2];
({ plan, run }[cmd] ?? (() => { console.error('usage: evidence.mjs <plan|run>'); process.exit(1); }))().catch((e) => { console.error(e.message || e); process.exit(1); });
