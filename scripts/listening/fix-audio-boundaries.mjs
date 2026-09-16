#!/usr/bin/env node
/**
 * Listening — Extract Two cue+preparation boundary repair for Atlas + Nova.
 *
 * The brief (15 Sep 2026): the destination sub-section owns the Extract Two
 * transition cue and ALL of its preparation time. Where the cue+prep window
 * sits at the tail of A1/C1, the pair is re-cut from the production files
 * themselves:
 *
 *   A1' = A1[0 → cue)              A2' = concat(A1[cue → end], A2)
 *   C1' = C1[0 → cue)              C2' = concat(C1[cue → end], C2)
 *
 * (Same rule when A1/A2 share one media asset: split that file at the cue.)
 *
 * The cue is located by word-timestamped transcription (OpenAI whisper-1,
 * verbose_json + word granularity) of the source tail / destination head —
 * never a hard-coded phrase: any "Extract Two / Extract 2" mention qualifies,
 * exactly like scripts/listening/split-audio-semantic.mjs.
 *
 * Transcription runs ON THE PRODUCTION VPS inside the oet-api container,
 * which holds the platform's Whisper credential in its environment. The key
 * is expanded by the container shell into the curl header only — it is never
 * printed, stored, or copied out of the server.
 *
 * Missing C2 → the C pair is logged "skipped / not applicable" and NEVER
 * fails the run. Every other per-paper error is logged and the run continues.
 *
 * Phases (run in order; each resumable):
 *   prepare     download each paper's A1/A2/C1/C2 audio, cut scan windows
 *               (source tail 300s, destination head 150s; shared assets:
 *               whole file), record assets.json + windows
 *   transcribe  ship each bundle through the VPS container, save word JSON
 *   plan        locate cues, classify pairs (recut | split | ok | unknown |
 *               skipped), write plan.json per paper
 *   apply       re-cut, verify the cue opens the new destination file,
 *               upload, attach (MakePrimary), sync timers
 *               (timeLimitSeconds = ceil(new duration) + 5s)
 *
 * Usage:
 *   OET_ADMIN_EMAIL=... OET_ADMIN_PASSWORD=... node scripts/listening/fix-audio-boundaries.mjs prepare
 *   ... node scripts/listening/fix-audio-boundaries.mjs transcribe [--paper <id>]
 *   ... node scripts/listening/fix-audio-boundaries.mjs plan
 *   ... node scripts/listening/fix-audio-boundaries.mjs apply [--paper <id>] [--pair A|C]
 *
 * Env: OET_API_BASE (default https://api.oetwithdrhesham.co.uk),
 * OET_SSH_HOST (default root@185.252.233.186), admin creds (same contract as
 * audit-audio-boundaries.mjs). Never prints credentials.
 */

import { spawn } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, readFileSync, statSync, writeFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const SSH_HOST = process.env.OET_SSH_HOST || 'root@185.252.233.186';
const STATE_DIR = join(__dirname, 'state', 'fix');

const PHASE = (process.argv[2] || '').replace(/^--/, '').toLowerCase();
const ONLY_PAPER = (() => { const i = process.argv.indexOf('--paper'); return i >= 0 ? process.argv[i + 1] : null; })();
const ONLY_PAIR = (() => { const i = process.argv.indexOf('--pair'); return i >= 0 ? (process.argv[i + 1] || '').toUpperCase() : null; })();
const RESCAN = process.argv.includes('--rescan');

const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];
const PAIRS = [
  { src: 'A1', dst: 'A2', id: 'A' },
  { src: 'C1', dst: 'C2', id: 'C' },
];
const TAIL_SCAN_SEC = 300;
const HEAD_SCAN_SEC = 150;
const CUT_PREROLL_SEC = 0.02;
const TIMER_BUFFER_SEC = 5;
const WRITES_PER_MIN = 30;

// ── auth (same contract as audit-audio-boundaries.mjs) ──────────────────────
let accessToken = process.env.OET_ADMIN_TOKEN || '';
let refreshToken = process.env.OET_ADMIN_REFRESH_TOKEN || '';
let accessTokenExpiresAt = accessToken ? Date.now() + 15 * 60_000 : 0;

function applySession(json) {
  accessToken = json.accessToken;
  if (json.refreshToken) refreshToken = json.refreshToken;
  accessTokenExpiresAt = json.accessTokenExpiresAt ? new Date(json.accessTokenExpiresAt).getTime() : Date.now() + 15 * 60_000;
  if (!accessToken) throw new Error('Auth response carried no accessToken.');
}
async function authPost(urlPath, body) {
  const res = await fetch(`${API_BASE}${urlPath}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`POST ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  return JSON.parse(text);
}
async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL || '';
  const password = process.env.OET_ADMIN_PASSWORD || '';
  if (email && password) return applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true }));
  if (refreshToken) return applySession(await authPost('/v1/auth/refresh', { refreshToken }));
  throw new Error('No admin credentials in environment.');
}
async function ensureFreshToken() {
  if (!accessTokenExpiresAt || Date.now() < accessTokenExpiresAt - 60_000) return;
  if (refreshToken) return applySession(await authPost('/v1/auth/refresh', { refreshToken }));
  if (process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD) return signIn();
}
class HttpError extends Error {
  constructor(status, code, message) { super(message); this.status = status; this.code = code; }
}
const writeTimes = [];
async function throttleWrite() {
  for (;;) {
    const now = Date.now();
    while (writeTimes.length && now - writeTimes[0] >= 60_000) writeTimes.shift();
    if (writeTimes.length < WRITES_PER_MIN) { writeTimes.push(now); return; }
    await new Promise((r) => setTimeout(r, 60_000 - (now - writeTimes[0]) + 50));
  }
}
async function api(method, urlPath, { json, body, form, isWrite = false, extraHeaders, retryOn401 = true } = {}) {
  await ensureFreshToken();
  if (isWrite) await throttleWrite();
  const headers = { Accept: 'application/json', Authorization: `Bearer ${accessToken}`, ...(extraHeaders ?? {}) };
  if (json !== undefined) headers['Content-Type'] = 'application/json';
  else if (form !== undefined) { /* multipart: fetch supplies the boundary */ }
  else if (body !== undefined) headers['Content-Type'] = 'application/octet-stream';
  const payload = json !== undefined ? JSON.stringify(json) : form !== undefined ? form : body;
  const res = await fetch(`${API_BASE}${urlPath}`, { method, headers, body: payload });
  if (res.status === 401 && retryOn401) {
    accessToken = ''; accessTokenExpiresAt = 0;
    await signIn();
    return api(method, urlPath, { json, body, isWrite, extraHeaders, retryOn401: false });
  }
  const text = await res.text();
  if (!res.ok) {
    let code = null;
    try { code = JSON.parse(text)?.errorCode ?? null; } catch { /* body not json */ }
    throw new HttpError(res.status, code, `${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  }
  return { json: text ? JSON.parse(text) : null, headers: res.headers };
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const round3 = (x) => Math.round(x * 1000) / 1000;

// ── process helpers ──────────────────────────────────────────────────────────
function run(cmd, args, { timeoutMs = 120_000 } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, { windowsHide: true, shell: false });
    let out = '', err = '';
    const t = setTimeout(() => { child.kill(); reject(new Error(`${cmd} timed out`)); }, timeoutMs);
    child.stdout.on('data', (d) => { out += d; });
    child.stderr.on('data', (d) => { err += d; if (err.length > 400_000) err = err.slice(-400_000); });
    child.on('error', (e) => { clearTimeout(t); reject(e); });
    child.on('close', (code) => { clearTimeout(t); resolve({ code, out, err }); });
  });
}

async function ffprobeDurationSec(path) {
  const r = await run('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'default=noprint_wrappers=1:nokey=1', path]);
  const v = parseFloat(String(r.out).trim());
  if (r.code !== 0 || !Number.isFinite(v)) throw new Error(`ffprobe failed for ${path}`);
  return v;
}

function ffmpeg(args, timeoutMs = 600_000) {
  return run('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], { timeoutMs });
}

// Cut one scan window (16k mono 64k mp3 keeps bundles small). offset 0 with
// duration null sends the whole file.
async function cutWindow(srcFile, outPath, { offset = 0, duration = null } = {}) {
  if (existsSync(outPath) && statSync(outPath).size > 1000) return outPath;
  const args = [];
  if (offset > 0) args.push('-ss', offset.toFixed(3));
  args.push('-i', srcFile);
  if (duration != null) args.push('-t', duration.toFixed(3));
  args.push('-ac', '1', '-ar', '16000', '-c:a', 'libmp3lame', '-b:a', '64k', outPath);
  const r = await ffmpeg(args);
  if (r.code !== 0) throw new Error(`ffmpeg window ${outPath} failed: ${r.err.slice(-200)}`);
  return outPath;
}

// ── transcription (platform STT endpoint) ────────────────────────────────────
// POST /v1/admin/listening/qa/transcribe runs the window through the SAME
// ISpeakingTranscriptionProvider the Speaking pipeline uses (whisper-asr
// credential resolves inside the app; mock → 503). Segments carry
// startMs/endMs/text which is what the cue detector needs.
async function transcribeWindowFile(windowPath) {
  const bytes = readFileSync(windowPath);
  const form = new FormData();
  form.append('audio', new Blob([bytes], { type: 'audio/mpeg' }), windowPath.split(/[\\/]/).pop());
  form.append('language', 'en');
  const { json } = await api('POST', '/v1/admin/listening/qa/transcribe', { isWrite: true, form });
  return json;
}

// ── cue detection ────────────────────────────────────────────────────────────
const norm = (w) => String(w).toLowerCase().replace(/[^a-z0-9]/g, '');

// The transition cue is the LAST "Extract Two" mention in the source tail that
// still leaves a preparation window (>= 8s) after it. Any phrasing counts —
// "Now look at Extract Two", "Extract Two. Questions …", "extract 2".
function findCueInWords(words, fileEndSec) {
  const hits = [];
  for (let i = 0; i < words.length; i++) {
    if (norm(words[i].w) !== 'extract') continue;
    const next = norm(words[i + 1]?.w ?? '');
    if (next === 'two' || next === '2' || next === 'to' || next === 'tu') {
      const start = words[i].s;
      const end = words[i + 1].e ?? words[i].e;
      if (fileEndSec - end >= 8) hits.push({ start, end });
    }
  }
  return hits.length ? hits[hits.length - 1] : null;
}

function loadWindowWords(transcriptDir, windowName, offsetSec) {
  const p = join(transcriptDir, `${windowName}.mp3.json`);
  if (!existsSync(p)) return { error: 'transcript json missing' };
  let parsed;
  try { parsed = JSON.parse(readFileSync(p, 'utf-8')); } catch { return { error: 'transcript json unparseable' }; }
  if (parsed.error) return { error: parsed.error };
  const segments = parsed.segments ?? [];
  if (!Array.isArray(segments) || segments.length === 0) return { error: 'transcript has no segments' };
  const words = [];
  for (const seg of segments) {
    const segStartSec = typeof seg.startMs === 'number' ? seg.startMs / 1000 : 0;
    // Prefer genuine word timings when the provider supplied them.
    const segWords = Array.isArray(seg.words) ? seg.words.filter((w) => w && (typeof w.startMs === 'number' || typeof w.start === 'number')) : [];
    if (segWords.length > 0) {
      for (const w of segWords) {
        const s = typeof w.startMs === 'number' ? w.startMs / 1000 : w.start;
        const e = typeof w.endMs === 'number' ? w.endMs / 1000 : (typeof w.end === 'number' ? w.end : s);
        words.push({ w: String(w.text ?? w.word ?? '').trim(), s: round3(offsetSec + s), e: round3(offsetSec + e) });
      }
      continue;
    }
    // Segment-level only: spread the segment's tokens evenly across its span.
    const tokens = String(seg.text ?? '').trim().split(/\s+/).filter(Boolean);
    const span = Math.max(0.2, ((seg.endMs ?? seg.startMs) - seg.startMs) / 1000);
    const step = tokens.length ? span / tokens.length : span;
    tokens.forEach((tok, i) => words.push({ w: tok, s: round3(offsetSec + segStartSec + i * step), e: round3(offsetSec + segStartSec + (i + 1) * step) }));
  }
  if (words.length === 0) return { error: 'transcript has no timings' };
  return { words, text: (parsed.text ?? '') || segments.map((s) => s.text ?? '').join(' ') };
}

// ── paper enumeration + audio resolution ─────────────────────────────────────
function resolveAudioByPart(assets) {
  const byKey = new Map();
  for (const a of assets) {
    if (a.role !== 'Audio' || !a.isPrimary) continue;
    const key = String(a.part ?? '').trim().toUpperCase();
    if (!key) continue;
    if (!byKey.has(key)) byKey.set(key, { mediaAssetId: a.mediaAssetId, file: a.media?.originalFilename ?? null, dur: a.media?.durationSeconds ?? null });
  }
  const resolve = (code) => {
    if (byKey.has(code)) return byKey.get(code);
    if (code === 'B') for (let i = 1; i <= 6; i++) if (byKey.has(`B${i}`)) return byKey.get(`B${i}`);
    const parent = code[0];
    if (byKey.has(parent)) return byKey.get(parent);
    return null;
  };
  return Object.fromEntries(SECTIONS.map((c) => [c, resolve(c)]));
}

async function downloadMedia(mediaAssetId, destPath) {
  if (existsSync(destPath) && statSync(destPath).size > 1000) return destPath;
  const res = await fetch(`${API_BASE}/v1/media/${mediaAssetId}/content`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!res.ok || !res.body) throw new Error(`download media ${mediaAssetId} -> HTTP ${res.status}`);
  await pipeline(Readable.fromWeb(res.body), createWriteStream(destPath));
  return destPath;
}

async function listInScopePapers() {
  const papers = [];
  const pageSize = 100;
  for (let page = 1; ; page++) {
    const batch = (await api('GET', `/v1/admin/papers?subtest=listening&page=${page}&pageSize=${pageSize}`)).json;
    if (!Array.isArray(batch) || batch.length === 0) break;
    papers.push(...batch);
    if (batch.length < pageSize) break;
    await sleep(200);
  }
  const SERIES = [['atlas', ['atlas-practice-series', 'atlas practice series', 'atlas-practice']], ['nova', ['nova-practice-series', 'nova practice series', 'nova-practice']]];
  const seriesOf = (p) => {
    const hay = [p.tagsCsv, p.slug, p.title].filter(Boolean).join(' ').toLowerCase().replace(/_+/g, '-');
    for (const [id, ms] of SERIES) if (ms.some((m) => hay.includes(m))) return id;
    return 'other';
  };
  return papers.filter((p) => p.status !== 'Archived' && seriesOf(p) !== 'other');
}

const stateDirs = () => readdirSync(STATE_DIR)
  .filter((d) => !d.includes('.') && existsSync(join(STATE_DIR, d, 'assets.json')));

// ── phases ───────────────────────────────────────────────────────────────────
async function phasePrepare() {
  await signIn();
  const inScope = await listInScopePapers();
  console.log(`Papers in scope: ${inScope.length}`);
  for (const paper of inScope) {
    if (ONLY_PAPER && paper.id !== ONLY_PAPER && paper.slug !== ONLY_PAPER) continue;
    const dir = join(STATE_DIR, paper.id);
    const winDir = join(dir, 'windows');
    mkdirSync(winDir, { recursive: true });
    try {
      const full = (await api('GET', `/v1/admin/papers/${paper.id}`)).json;
      const audio = resolveAudioByPart(full.assets ?? []);
      const assetsDoc = { title: paper.title, pairs: {}, windows: {} };
      const manifest = [];
      for (const { src, dst, id } of PAIRS) {
        if (ONLY_PAIR && id !== ONLY_PAIR) continue;
        const srcInfo = audio[src];
        const dstInfo = audio[dst];
        if (!srcInfo || !dstInfo) {
          assetsDoc.pairs[id] = { mode: 'skipped', reason: srcInfo ? `${dst} absent` : `${src} absent`, c2Missing: id === 'C' && Boolean(audio.C1) && !audio.C2 };
          continue;
        }
        const sameAsset = srcInfo.mediaAssetId === dstInfo.mediaAssetId;
        const srcFile = join(dir, `${id}-${src}-${srcInfo.mediaAssetId}.mp3`);
        await downloadMedia(srcInfo.mediaAssetId, srcFile);
        const srcDur = await ffprobeDurationSec(srcFile);
        const entry = {
          sharedAsset: sameAsset,
          srcMediaId: srcInfo.mediaAssetId, dstMediaId: dstInfo.mediaAssetId,
          srcFile, srcDur: round3(srcDur),
          dstFile: null, dstDur: null,
        };
        if (sameAsset) {
          // Whole shared file — the cue sits mid-file.
          await cutWindow(srcFile, join(winDir, `${id}-src.mp3`), { offset: 0, duration: null });
          manifest.push([`${id}-src.mp3`, 0]);
        } else {
          const dstFile = join(dir, `${id}-${dst}-${dstInfo.mediaAssetId}.mp3`);
          await downloadMedia(dstInfo.mediaAssetId, dstFile);
          const dstDur = await ffprobeDurationSec(dstFile);
          const tailOff = Math.max(0, srcDur - TAIL_SCAN_SEC);
          await cutWindow(srcFile, join(winDir, `${id}-src.mp3`), { offset: tailOff, duration: srcDur - tailOff });
          manifest.push([`${id}-src.mp3`, tailOff]);
          await cutWindow(dstFile, join(winDir, `${id}-dst.mp3`), { offset: 0, duration: Math.min(HEAD_SCAN_SEC, dstDur) });
          manifest.push([`${id}-dst.mp3`, 0]);
          entry.dstFile = dstFile;
          entry.dstDur = round3(dstDur);
        }
        assetsDoc.pairs[id] = entry;
      }
      writeFileSync(join(winDir, 'manifest.tsv'), manifest.map(([n, o]) => `${n}\t${o}`).join('\n') + '\n');
      writeFileSync(join(dir, 'assets.json'), JSON.stringify(assetsDoc, null, 2));
      const pairSummary = PAIRS.filter(({ id }) => assetsDoc.pairs[id]).map(({ id }) => `${id}:${assetsDoc.pairs[id].mode ?? 'windows'}`).join(' ');
      console.log(`prepared ${paper.title} — ${pairSummary || 'no pairs'}`);
    } catch (err) {
      console.error(`[FAIL] ${paper.title}: ${err.message}`);
    }
    await sleep(150);
  }
}

async function phaseTranscribe() {
  for (const paperId of stateDirs()) {
    if (ONLY_PAPER && paperId !== ONLY_PAPER) continue;
    const dir = join(STATE_DIR, paperId);
    const winDir = join(dir, 'windows');
    const manifestPath = join(winDir, 'manifest.tsv');
    if (!existsSync(manifestPath)) continue;
    const windows = readFileSync(manifestPath, 'utf-8').split(/\r?\n/).filter(Boolean).map((l) => l.split('\t')[0]);
    const outDir = join(dir, 'transcripts');
    mkdirSync(outDir, { recursive: true });
    for (const w of windows) {
      const outJson = join(outDir, `${w}.json`);
      if (existsSync(outJson)) {
        try { JSON.parse(readFileSync(outJson, 'utf-8')); continue; } catch { /* re-transcribe */ }
      }
      const windowPath = join(winDir, w);
      if (!existsSync(windowPath)) { console.error(`  [FAIL] ${paperId}/${w}: window file missing — rerun prepare`); continue; }
      process.stdout.write(`  ${paperId}/${w}...`);
      try {
        const result = await transcribeWindowFile(windowPath);
        writeFileSync(outJson, JSON.stringify(result, null, 2));
        console.log(` ${result.wordCount ?? '?'} words via ${result.provider ?? '?'}`);
      } catch (err) {
        console.log(` FAIL ${err.message}`);
      }
      await sleep(300);
    }
  }
  console.log('transcribe phase complete');
}

async function phasePlan() {
  for (const paperId of stateDirs()) {
    if (ONLY_PAPER && paperId !== ONLY_PAPER) continue;
    const dir = join(STATE_DIR, paperId);
    const assets = JSON.parse(readFileSync(join(dir, 'assets.json'), 'utf-8'));
    const planPath = join(dir, 'plan.json');
    const prev = existsSync(planPath) ? JSON.parse(readFileSync(planPath, 'utf-8')) : {};
    const outDir = join(dir, 'transcripts');
    const plan = { phase: 'planned', plannedAt: new Date().toISOString(), pairs: prev.pairs ?? {} };
    const manifest = existsSync(join(dir, 'windows', 'manifest.tsv'))
      ? Object.fromEntries(readFileSync(join(dir, 'windows', 'manifest.tsv'), 'utf-8').split(/\r?\n/).filter(Boolean).map((l) => l.split('\t')).map(([n, o]) => [n.replace(/\.mp3$/, ''), Number(o)]))
      : {};
    for (const { id } of PAIRS) {
      if (ONLY_PAIR && id !== ONLY_PAIR) continue;
      const pairAssets = assets.pairs[id];
      if (!pairAssets) continue;
      if (pairAssets.mode === 'skipped') { plan.pairs[id] = pairAssets; continue; }
      if (plan.pairs[id]?.mode === 'applied') continue;

      const srcWin = manifest[`${id}-src`];
      if (srcWin == null) { plan.pairs[id] = { mode: 'unknown', reason: 'no source window in manifest' }; continue; }
      const srcWords = loadWindowWords(outDir, `${id}-src`, srcWin);
      if (srcWords.error) { plan.pairs[id] = { mode: 'unknown', reason: `src transcript: ${srcWords.error}` }; continue; }
      const srcEnd = srcWords.words[srcWords.words.length - 1].e;
      const cue = findCueInWords(srcWords.words, srcEnd + 8);
      if (cue) {
        plan.pairs[id] = {
          mode: pairAssets.sharedAsset ? 'split' : 'recut',
          cueSec: cue.start,
          srcCueEnd: cue.end,
          note: `Extract Two cue at ${cue.start}s of source audio`,
        };
        continue;
      }
      if (pairAssets.sharedAsset) {
        plan.pairs[id] = { mode: 'unknown', reason: 'shared asset without cue anywhere in full-file transcript' };
        continue;
      }
      const dstWin = manifest[`${id}-dst`];
      if (dstWin == null) { plan.pairs[id] = { mode: 'unknown', reason: 'no destination window in manifest' }; continue; }
      const dstWords = loadWindowWords(outDir, `${id}-dst`, dstWin);
      if (dstWords.error) { plan.pairs[id] = { mode: 'unknown', reason: `dst transcript: ${dstWords.error}` }; continue; }
      const dstCue = findCueInWords(dstWords.words, Number.MAX_SAFE_INTEGER);
      plan.pairs[id] = dstCue
        ? { mode: 'ok', note: `cue already at ${dst} head (${round3(dstCue.start)}s into window)` }
        : { mode: 'unknown', reason: 'no Extract Two cue found in source tail or destination head — needs manual review' };
    }
    writeFileSync(planPath, JSON.stringify(plan, null, 2));
    console.log(`${assets.title ?? paperId}: ${PAIRS.filter(({ id }) => plan.pairs[id]).map(({ id }) => `${id}=${plan.pairs[id].mode}`).join(' ')}`);
  }
}

// ── apply ────────────────────────────────────────────────────────────────────
async function uploadMedia(filePath, originalName) {
  const size = statSync(filePath).size;
  const started = await api('POST', '/v1/admin/uploads', {
    isWrite: true,
    json: { originalFilename: originalName, declaredMimeType: 'audio/mpeg', declaredSizeBytes: size, intendedRole: 'Audio' },
  });
  const chunk = started.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(size / chunk));
  const handle = await (await import('node:fs/promises')).open(filePath, 'r');
  try {
    for (let part = 1; part <= totalParts; part++) {
      const offset = (part - 1) * chunk;
      const sizePart = Math.min(chunk, size - offset);
      const buf = Buffer.allocUnsafe(sizePart);
      await handle.read(buf, 0, sizePart, offset);
      try {
        await api('PUT', `/v1/admin/uploads/${started.uploadId}/parts/${part}`, { isWrite: true, body: buf });
      } catch (err) {
        if (!(err instanceof HttpError && err.code === 'upload_part_duplicate')) throw err;
      }
    }
  } finally {
    await handle.close();
  }
  const done = await api('POST', `/v1/admin/uploads/${started.uploadId}/complete`, { isWrite: true, json: {} });
  return done.mediaAssetId;
}

async function attachAudio(paperId, part, mediaAssetId, durationSec, title) {
  // Outgoing asset bodies use the NUMERIC role — the deployed request binding
  // refuses the string form (see scripts/admin/retry-listening-tts.mjs).
  return api('POST', `/v1/admin/papers/${paperId}/assets`, {
    isWrite: true,
    json: {
      role: 0,
      mediaAssetId,
      part,
      title: (title ?? `Listening ${part} (boundary repair)`).slice(0, 200),
      displayOrder: 1,
      makePrimary: true,
      durationSeconds: Math.ceil(durationSec),
    },
  });
}

async function patchTimer(paperId, code, timeLimitSeconds) {
  const res = await api('GET', `/v1/admin/papers/${paperId}/listening/extracts`);
  const etag = res.headers.get('etag');
  await api('PATCH', `/v1/admin/papers/${paperId}/listening/extracts/${code}`, {
    isWrite: true,
    json: { timeLimitSeconds },
    extraHeaders: etag ? { 'If-Match': etag } : undefined,
  });
}

async function cutRepairedPair(dir, id, src, dst, pair, cueSec) {
  const srcFile = join(dir, `${id}-${src}-repaired.mp3`);
  const dstFile = join(dir, `${id}-${dst}-repaired.mp3`);
  if (existsSync(srcFile) && existsSync(dstFile)) return { srcFile, dstFile };
  if (!pair.srcFile || !existsSync(pair.srcFile)) throw new Error(`prepared source file missing for pair ${id}`);
  const cutAt = Math.max(0, cueSec - CUT_PREROLL_SEC);
  let r = await ffmpeg(['-i', pair.srcFile, '-t', cutAt.toFixed(3), '-c:a', 'libmp3lame', '-q:a', '2', srcFile]);
  if (r.code !== 0) throw new Error(`source cut failed: ${r.err.slice(-200)}`);
  if (pair.sharedAsset) {
    r = await ffmpeg(['-i', pair.srcFile, '-ss', cutAt.toFixed(3), '-c:a', 'libmp3lame', '-q:a', '2', dstFile]);
    if (r.code !== 0) throw new Error(`shared cut (tail) failed: ${r.err.slice(-200)}`);
  } else {
    if (!pair.dstFile || !existsSync(pair.dstFile)) throw new Error(`prepared destination file missing for pair ${id}`);
    const tailCut = join(dir, `${id}-src-tail.mp3`);
    r = await ffmpeg(['-i', pair.srcFile, '-ss', cutAt.toFixed(3), '-c:a', 'libmp3lame', '-q:a', '2', tailCut]);
    if (r.code !== 0) throw new Error(`source tail cut failed: ${r.err.slice(-200)}`);
    // Re-encode the join so the boundary is sample-exact.
    r = await ffmpeg(['-i', tailCut, '-i', pair.dstFile, '-filter_complex', '[0:a][1:a]concat=n=2:v=0:a=1', '-c:a', 'libmp3lame', '-q:a', '2', dstFile]);
    if (r.code !== 0) throw new Error(`concat failed: ${r.err.slice(-200)}`);
  }
  return { srcFile, dstFile };
}

async function phaseApply() {
  await signIn();
  const inScope = await listInScopePapers();
  const byId = new Map(inScope.map((p) => [p.id, p]));
  const results = [];
  const failures = [];
  for (const paperId of stateDirs()) {
    if (ONLY_PAPER && paperId !== ONLY_PAPER) continue;
    const paper = byId.get(paperId) ?? { id: paperId, title: paperId };
    const dir = join(STATE_DIR, paperId);
    const planPath = join(dir, 'plan.json');
    if (!existsSync(planPath)) continue;
    const plan = JSON.parse(readFileSync(planPath, 'utf-8'));
    const assets = JSON.parse(readFileSync(join(dir, 'assets.json'), 'utf-8'));
    for (const { src, dst, id } of PAIRS) {
      if (ONLY_PAIR && id !== ONLY_PAIR) continue;
      const p = plan.pairs?.[id];
      if (!p) continue;
      if (p.mode === 'applied') { results.push({ paper: paper.title, pair: id, action: 'already applied' }); continue; }
      if (!['recut', 'split'].includes(p.mode)) { results.push({ paper: paper.title, pair: id, action: p.mode }); continue; }
      try {
        const { srcFile, dstFile } = await cutRepairedPair(dir, id, src, dst, assets.pairs[id], p.cueSec);
        const srcNewDur = await ffprobeDurationSec(srcFile);
        const dstNewDur = await ffprobeDurationSec(dstFile);

        // Post-cut verification: the destination file MUST open with the cue.
        const verifyDir = join(dir, 'verify');
        mkdirSync(verifyDir, { recursive: true });
        await cutWindow(dstFile, join(verifyDir, `${id}-verify.mp3`), { offset: 0, duration: 75 });
        const verifyResult = await transcribeWindowFile(join(verifyDir, `${id}-verify.mp3`));
        writeFileSync(join(verifyDir, `${id}-verify.mp3.json`), JSON.stringify(verifyResult, null, 2));
        const verifyWords = loadWindowWords(verifyDir, `${id}-verify`, 0);
        if (verifyWords.error) throw new Error(`post-cut verify transcript: ${verifyWords.error}`);
        const headCue = findCueInWords(verifyWords.words, Number.MAX_SAFE_INTEGER);
        if (!headCue) throw new Error('post-cut verify failed: cue not found at head of new destination audio');
        if (headCue.start > 20) throw new Error(`post-cut verify failed: cue at ${headCue.start}s into new ${dst} (expected ≤ 20s)`);

        const srcAssetId = await uploadMedia(srcFile, `${id}-${src}-repaired.mp3`);
        await attachAudio(paper.id, src, srcAssetId, srcNewDur, `${paper.title} ${src} (boundary repair)`);
        const dstAssetId = await uploadMedia(dstFile, `${id}-${dst}-repaired.mp3`);
        await attachAudio(paper.id, dst, dstAssetId, dstNewDur, `${paper.title} ${dst} (boundary repair)`);
        await patchTimer(paper.id, src, Math.ceil(srcNewDur) + TIMER_BUFFER_SEC);
        await patchTimer(paper.id, dst, Math.ceil(dstNewDur) + TIMER_BUFFER_SEC);

        plan.pairs[id] = {
          ...p,
          mode: 'applied',
          appliedAt: new Date().toISOString(),
          srcNewDur: round3(srcNewDur),
          dstNewDur: round3(dstNewDur),
          assets: { [src]: srcAssetId, [dst]: dstAssetId },
        };
        writeFileSync(planPath, JSON.stringify(plan, null, 2));
        results.push({ paper: paper.title, pair: id, action: `${p.mode} APPLIED`, srcNewDur: Math.round(srcNewDur), dstNewDur: Math.round(dstNewDur), cueAt: p.cueSec });
      } catch (err) {
        failures.push({ paper: paper.title, pair: id, error: String(err.message || err) });
        console.error(`  [FAIL] ${paper.title} ${id}: ${err.message}`);
      }
    }
  }
  const outPath = join(STATE_DIR, `apply-${Date.now()}.json`);
  writeFileSync(outPath, JSON.stringify({ generatedAt: new Date().toISOString(), results, failures }, null, 2));
  console.log(`\nPairs actioned: ${results.length}, failures: ${failures.length}`);
  for (const r of results) console.log(`  ${r.paper} [${r.pair}] ${r.action}${r.dstNewDur ? ` → src ${r.srcNewDur}s / dst ${r.dstNewDur}s` : ''}`);
  if (failures.length) console.log(`Failures:\n${failures.map((f) => `  ${f.paper} [${f.pair ?? '-'}] ${f.error}`).join('\n')}`);
  console.log(`Report: ${outPath}`);
}

async function main() {
  if (!['prepare', 'transcribe', 'plan', 'apply'].includes(PHASE)) {
    console.error('Usage: node fix-audio-boundaries.mjs prepare|transcribe|plan|apply [--paper <id>] [--pair A|C] [--rescan]');
    process.exit(1);
  }
  mkdirSync(STATE_DIR, { recursive: true });
  if (PHASE === 'prepare') return phasePrepare();
  if (PHASE === 'transcribe') return phaseTranscribe();
  if (PHASE === 'plan') return phasePlan();
  if (PHASE === 'apply') return phaseApply();
}

main().catch((err) => { console.error(err.message || err); process.exit(1); });
