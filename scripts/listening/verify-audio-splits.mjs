#!/usr/bin/env node
/**
 * Verify semantic splits for Listening papers.
 *
 * TWO MODES
 *
 * 1. Legacy manifest mode (unchanged inputs, honest verdicts):
 *   node scripts/listening/verify-audio-splits.mjs --manifestDir ./out --transcriptDir ./transcripts
 *   node scripts/listening/verify-audio-splits.mjs --manifest ./out/test-05/split-manifest.json --transcript ./transcripts/LISTENING\ TEST\ 5.json
 *   Structural checks only (existence, duration, mapping, overlap/gap). A
 *   structural pass is reported as "Needs manual review (structural-only)" —
 *   it must NEVER be reported as fully verified, because the semantic head/tail
 *   content check has not run (Issue 3, audio-split-audit-report gap).
 *
 * 2. LIVE content mode (the real verifier):
 *   OET_ADMIN_EMAIL=... OET_ADMIN_PASSWORD=... node scripts/listening/verify-audio-splits.mjs --live [--paper <id>]
 *     [--state-dir <dir>] [--max-papers N] [--no-jev] [--api-stt]
 *
 *   For every in-scope Atlas/Nova paper (same matchers as the audit script):
 *   resolve each section's primary audio (A1/A2/B/C1/C2), download, cut
 *   head/tail windows, transcribe (local faster-whisper by default;
 *   --api-stt forces the QA STT endpoint), then run content-level checks:
 *
 *   - Destination head check (A2/C2): the Extract Two introduction cue must
 *     open the section (≤20s in), followed by a preparation window before the
 *     clinical dialogue. A2 starting straight into dialogue (or into the
 *     previous section's speech) FAILS.
 *   - Source tail leak check (A1/C1): the Extract Two cue must NOT sit at the
 *     source tail — that is the mis-split signature and FAILS.
 *   - Sibling duplicate guard (Issue 4): A1↔A2 and C1↔C2 must not be the same
 *     recording. Same media asset id → automatic FAIL. Different ids with
 *     near-identical first-speech windows (re-encodes, re-uploads) → FAIL via
 *     deterministic similarity, with Jev (TypeSafe System One, model
 *     jev-1.13.0) as the semantic judge in the ambiguous band.
 *   - Boundary clipping: Jev judges whether the head/tail speech is cut
 *     mid-sentence.
 *   - B: opening-speech-present + not a duplicate of its neighbours (review
 *     only — the 15 Sep brief scoped repairs to the A and C pairs).
 *
 *   Verdicts per section (Issue 3 vocabulary):
 *     "Verified (content)"      — every content check passed (STT + Jev)
 *     "Failed (<reasons>)"      — a content DEFECT was detected (repair needed)
 *     "Needs manual review (<reason>)" — check could not be performed/decided
 *     "Skipped (<reason>)"      — not applicable (e.g. C2 absent)
 *   A paper is "fully verified" only when every present section is Verified.
 *   Shared parent audio assets are only acceptable when the cue windows are
 *   verified distinct (i.e. a completed split); otherwise the duplicate guard
 *   flags them for repair — intentional shared assets are not an error by
 *   themselves.
 *
 *   Jev ("use JEV 100%"): every content-level verdict is judged by Jev over
 *   the window transcripts; deterministic checks (timings, cue regex,
 *   similarity) feed Jev's state as evidence and stay the hard gate where the
 *   deployed repair pipeline gates on them. If Jev is unavailable or unsure,
 *   the section is "Needs manual review" — never silently Verified.
 *
 * Exit codes: 0 = every paper fully verified · 2 = ≥1 content defect ·
 *             3 = no defect but manual review pending · 1 = usage/environment.
 *
 * Requires: ffmpeg/ffprobe. Credentials from env only, never printed.
 */

import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { jevJudge, jevConfigured, noulVerdict, confidentChoice, JevError, MODEL as JEV_MODEL } from './jev-client.mjs';
import { transcribeLocal } from './local-stt.mjs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));

// ── shared helpers ───────────────────────────────────────────────────────────

function hasFfprobe() {
  const r = spawnSync('ffprobe', ['-version'], { encoding: 'utf-8' });
  return r.status === 0;
}

function probeDurationSec(path) {
  const r = spawnSync('ffprobe', ['-v','error','-show_entries','format=duration','-of','default=noprint_wrappers=1:nokey=1', path], { encoding: 'utf-8' });
  if (r.status !== 0) return null;
  const v = parseFloat(String(r.stdout).trim());
  return Number.isFinite(v) ? v : null;
}

function loadManifest(path) {
  if (!existsSync(path)) throw new Error(`Manifest not found: ${path}`);
  return JSON.parse(readFileSync(path, 'utf-8'));
}

// ── legacy manifest mode (structural layer; honest verdict wording) ─────────

function verifyOne(manifestPath, transcriptPath, opts) {
  const manifest = loadManifest(manifestPath);
  console.log(`\n=== ${manifestPath} ===`);
  const flat = transcriptPath && existsSync(transcriptPath) ? JSON.parse(readFileSync(transcriptPath,'utf-8')) : null;
  let structuralOk = true;
  for (const entry of manifest.manifest ?? manifest.bounds ?? []) {
    const file = entry.file ?? join(join(manifestPath,'..'), `${entry.code}.mp3`);
    const exists = existsSync(file);
    const dur = exists ? probeDurationSec(file) : null;
    const startOk = entry.start != null && entry.start >= 0;
    const endOk = entry.end == null || entry.end > entry.start;
    const mappingOk = !!entry.code && ['A1','A2','B','C1','C2'].includes(entry.code);
    const pass = exists && startOk && endOk && mappingOk && dur != null && dur > 2;
    console.log(`  ${entry.code}: ${exists ? 'exists' : 'MISSING'} dur=${dur != null ? dur.toFixed(1)+'s' : '?'} mapping=${mappingOk?'ok':'FAIL'} ${pass?'structural-PASS':'FAIL'}`);
    if (!pass) structuralOk = false;
  }
  const bounds = manifest.bounds ?? [];
  for (let i = 1; i < bounds.length; i++) {
    if (bounds[i].start < bounds[i-1].end - 0.05) {
      console.log(`  OVERLAP ${bounds[i-1].code} → ${bounds[i].code} ${bounds[i].start.toFixed(2)} < ${bounds[i-1].end.toFixed(2)} FAIL`);
      structuralOk = false;
    }
    if (Math.abs(bounds[i].start - bounds[i-1].end) > 0.25) {
      console.log(`  GAP ${bounds[i-1].code}→${bounds[i].code} ${(bounds[i].start - bounds[i-1].end).toFixed(2)}s (check lead-in)`);
    }
  }
  // Issue 3: a structural pass is NOT a content verification. Never label it
  // "Verified" — the semantic head/tail check has not been performed here.
  const verdict = structuralOk
    ? '⚠ Needs manual review (structural-only pass — content-level head/tail check not performed; run with --live)'
    : '❌ Failed structural checks';
  console.log(`  ${verdict}`);
  return { structuralOk };
}

// ── live content mode ────────────────────────────────────────────────────────

const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
// Mirror lib/listening-exam-categories.ts / audit-audio-boundaries.mjs.
const SERIES = [
  { id: 'atlas', matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'] },
  { id: 'nova', matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'] },
];
const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];
const HEAD_SEC = 75;   // captures cue + prep + dialogue start
const TAIL_SEC = 60;   // captures any leaked cue + enough speech context
const CUE_MAX_HEAD_SEC = 20;   // same gate as the repair pipeline's post-cut verify
const DUP_NEAR_THRESHOLD = 0.90;  // deterministic near-duplicate
const DUP_JEV_BAND = 0.45;        // below → distinct; band → Jev decides
const DUP_MIN_WORDS = 30;         // first-speech window size for similarity

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
async function api(method, urlPath, retryOn401 = true) {
  await ensureFreshToken();
  const res = await fetch(`${API_BASE}${urlPath}`, {
    method,
    headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` },
  });
  if (res.status === 401 && retryOn401 && (refreshToken || process.env.OET_ADMIN_EMAIL)) {
    accessToken = ''; accessTokenExpiresAt = 0;
    await signIn();
    return api(method, urlPath, false);
  }
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  return text ? JSON.parse(text) : null;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function seriesOf(paper) {
  const hay = [paper.tagsCsv, paper.slug, paper.title].filter(Boolean).join(' ').toLowerCase().replace(/_+/g, '-');
  for (const s of SERIES) if (s.matchers.some((m) => hay.includes(m))) return s.id;
  return 'other';
}

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
  return { byKey, audio: Object.fromEntries(SECTIONS.map((c) => [c, resolve(c)])) };
}

// Reuse the boundary-repair pipeline's downloads when the media id matches.
function reuseFixStateDownload(fixDir, pairCode, code, mediaId) {
  const candidate = join(fixDir, `${pairCode}-${code}-${mediaId}.mp3`);
  return existsSync(candidate) && statSync(candidate).size > 1000 ? candidate : null;
}

async function downloadMedia(mediaAssetId, destPath) {
  const res = await fetch(`${API_BASE}/v1/media/${mediaAssetId}/content`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!res.ok || !res.body) throw new Error(`download media ${mediaAssetId} -> HTTP ${res.status}`);
  const { createWriteStream } = await import('node:fs');
  const { Readable } = await import('node:stream');
  const { pipeline } = await import('node:stream/promises');
  await pipeline(Readable.fromWeb(res.body), createWriteStream(destPath));
  return destPath;
}

function ffmpegWin(srcFile, outPath, { offset = 0, duration = null } = {}) {
  if (existsSync(outPath) && statSync(outPath).size > 1000) return true;
  const args = [];
  if (offset > 0) args.push('-ss', offset.toFixed(3));
  args.push('-i', srcFile);
  if (duration != null) args.push('-t', duration.toFixed(3));
  args.push('-ac', '1', '-ar', '16000', '-c:a', 'libmp3lame', '-b:a', '64k', outPath);
  const r = spawnSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], { encoding: 'utf-8', timeout: 300_000 });
  return r.status === 0 && existsSync(outPath) && statSync(outPath).size > 1000;
}

async function sttWindow(windowPath, useLocalStt) {
  if (useLocalStt) return transcribeLocal(windowPath);
  const bytes = readFileSync(windowPath);
  const form = new FormData();
  form.append('audio', new Blob([bytes], { type: 'audio/mpeg' }), windowPath.split(/[\\/]/).pop());
  form.append('language', 'en');
  await ensureFreshToken();
  const res = await fetch(`${API_BASE}/v1/admin/listening/qa/transcribe`, {
    method: 'POST',
    headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` },
    body: form,
  });
  if (res.status === 401) {
    accessToken = ''; accessTokenExpiresAt = 0;
    await signIn();
    return sttWindow(windowPath, useLocalStt);
  }
  const text = await res.text();
  if (!res.ok) throw new Error(`qa/transcribe -> HTTP ${res.status}: ${text.slice(0, 160)}`);
  return JSON.parse(text);
}

// Transcript JSON → tokens with second timings (segment tokens spread evenly
// when the provider supplies no word timestamps — same approach as the repair
// pipeline).
function windowTokens(transcript, offsetSec = 0) {
  const segments = transcript?.segments ?? [];
  const tokens = [];
  for (const seg of segments) {
    const segStart = typeof seg.startMs === 'number' ? seg.startMs / 1000 : 0;
    const segEnd = typeof seg.endMs === 'number' ? seg.endMs / 1000 : segStart;
    const words = Array.isArray(seg.words) && seg.words.length
      ? seg.words.map((w) => ({ t: String(w.text ?? w.word ?? '').trim(), s: (typeof w.startMs === 'number' ? w.startMs / 1000 : w.start) ?? segStart, e: (typeof w.endMs === 'number' ? w.endMs / 1000 : w.end) ?? segStart }))
      : (() => {
          const toks = String(seg.text ?? '').trim().split(/\s+/).filter(Boolean);
          const span = Math.max(0.2, segEnd - segStart);
          const step = toks.length ? span / toks.length : span;
          return toks.map((t, i) => ({ t, s: segStart + i * step, e: segStart + (i + 1) * step }));
        })();
    for (const w of words) tokens.push({ ...w, s: offsetSec + w.s, e: offsetSec + w.e });
  }
  return tokens;
}
const normTok = (w) => String(w).toLowerCase().replace(/[^a-z0-9]/g, '');
const windowText = (transcript) => {
  const segs = transcript?.segments ?? [];
  return segs.map((s) => String(s.text ?? '').trim()).filter(Boolean).join(' ');
};

// Extract Two cue: any "extract" + two/2/to/tu token pair. Returns hits with
// start/end seconds (relative to the window).
function cueHits(tokens) {
  const hits = [];
  for (let i = 0; i < tokens.length; i++) {
    if (normTok(tokens[i].t) !== 'extract') continue;
    const next = normTok(tokens[i + 1]?.t ?? '');
    if (['two', '2', 'to', 'tu', 'too'].includes(next)) {
      hits.push({ start: tokens[i].s, end: tokens[i + 1].e ?? tokens[i].e });
    }
  }
  return hits;
}

function firstSpeechText(transcript, maxWords = DUP_MIN_WORDS) {
  const toks = windowTokens(transcript).filter((t) => normTok(t.t));
  return toks.slice(0, maxWords).map((t) => normTok(t.t)).join(' ');
}

function bigramDice(a, b) {
  const gram = (s) => {
    const t = s.split(' ').filter(Boolean);
    const out = new Map();
    for (let i = 0; i < t.length - 1; i++) {
      const g = `${t[i]} ${t[i + 1]}`;
      out.set(g, (out.get(g) ?? 0) + 1);
    }
    return out;
  };
  const ga = gram(a), gb = gram(b);
  if (ga.size === 0 || gb.size === 0) return 0;
  let inter = 0;
  for (const [g, c] of ga) inter += Math.min(c, gb.get(g) ?? 0);
  return (2 * inter) / (Math.max(0, [...ga.values()].reduce((x, y) => x + y, 0) - 0) + Math.max(0, [...gb.values()].reduce((x, y) => x + y, 0)));
}

// The preparation window is the LONGEST silent gap between the Extract Two
// cue and the clinical dialogue onset — announcers may speak several intro
// sentences after the cue ("Questions 13 to 24, you hear…") before the pause.
function prepWindowAfterCue(tokens, cue, windowDurationSec = null) {
  if (!cue) return null;
  const after = tokens.filter((t) => t.s >= cue.end - 0.05).sort((a, b) => a.s - b.s);
  if (after.length === 0) return { prepSec: null, note: 'no speech after cue inside window' };
  let bestGap = Math.max(0, after[0].s - cue.end);
  let dialogueStartsAt = after[0].s;
  for (let i = 1; i < after.length; i++) {
    const gap = after[i].s - after[i - 1].e;
    if (gap > bestGap) { bestGap = gap; dialogueStartsAt = after[i].s; }
  }
  // Trailing silence counts: a 90s preparation extends past the head window,
  // and the silence after the last transcribed word is still preparation.
  if (windowDurationSec != null) {
    const trailing = windowDurationSec - after[after.length - 1].e;
    if (trailing > bestGap) { bestGap = trailing; dialogueStartsAt = null; }
  }
  const out = { prepSec: Math.round(bestGap * 10) / 10 };
  if (dialogueStartsAt != null) out.dialogueStartsAt = Math.round(dialogueStartsAt * 10) / 10;
  if (bestGap <= 2) out.note = 'no significant pause after cue (dialogue follows immediately)';
  return out;
}


async function liveMode(opts) {
  if (!process.env.OET_ADMIN_TOKEN && !process.env.OET_ADMIN_REFRESH_TOKEN && !(process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD)) {
    console.error('live mode needs admin credentials in the environment (never stored in files).');
    process.exit(1);
  }
  const useJev = !opts.noJev;
  // Local faster-whisper is the default STT source (deterministic, free, and
  // the production whisper-asr credential is currently rejected upstream);
  // pass --api-stt to force the QA transcribe endpoint.
  const useLocalStt = !opts.apiStt;
  if (useJev && !jevConfigured()) {
    console.error('TYPESAFE_API_KEY not configured — Jev content judgments unavailable. Re-run with --no-jev for deterministic-only (all semantic verdicts become "Needs manual review").');
    process.exit(1);
  }
  await signIn();

  const stateDir = opts.stateDir;
  mkdirSync(stateDir, { recursive: true });

  const papers = [];
  const pageSize = 100;
  for (let page = 1; ; page++) {
    const batch = await api('GET', `/v1/admin/papers?subtest=listening&page=${page}&pageSize=${pageSize}`);
    if (!Array.isArray(batch) || batch.length === 0) break;
    papers.push(...batch);
    if (batch.length < pageSize) break;
    await sleep(200);
  }
  let inScope = papers.filter((p) => p.status !== 'Archived' && seriesOf(p) !== 'other');
  if (opts.paper) inScope = inScope.filter((p) => p.id === opts.paper || p.slug === opts.paper);
  if (opts.maxPapers) inScope = inScope.slice(0, opts.maxPapers);
  console.log(`Listening papers: ${papers.length} total, ${inScope.length} in scope for content verification (Jev: ${useJev ? 'ON' : 'OFF'}, STT: ${useLocalStt ? 'local faster-whisper' : 'QA endpoint'}).`);

  const rows = [];
  let jevCalls = 0;
  for (const paper of inScope) {
    try {
      rows.push(await verifyPaperLive(paper, { stateDir, useJev, useLocalStt, fixRoot: join(SCRIPT_DIR, 'state', 'fix'), countJev: () => jevCalls++ }));
    } catch (err) {
      rows.push({ paperId: paper.id, title: paper.title, series: seriesOf(paper), error: String(err.message || err), sections: {}, fullyVerified: false, anyFail: false, needsReview: true });
      console.error(`[ERROR] ${paper.title}: ${err.message}`);
    }
    await sleep(150);
  }

  const summary = {
    generatedAt: new Date().toISOString(),
    mode: 'live-content',
    jev: useJev ? `typesafe ${JEV_MODEL} (direct API)` : 'disabled (--no-jev)',
    apiBase: API_BASE,
    papers: rows.length,
    fullyVerified: rows.filter((r) => r.fullyVerified).length,
    withDefects: rows.filter((r) => r.anyFail).length,
    needsReview: rows.filter((r) => r.needsReview).length,
    jevCalls,
    rows,
  };
  const out = join(stateDir, `content-verify-${Date.now()}.json`);
  writeFileSync(out, JSON.stringify(summary, null, 2));

  console.log('');
  for (const r of rows) {
    if (r.error) { console.log(`✗ ${r.title}: ERROR ${r.error}`); continue; }
    console.log(`${r.fullyVerified ? '✅' : r.anyFail ? '❌' : '⚠'} ${r.title}`);
    for (const code of SECTIONS) {
      const s = r.sections[code];
      if (!s) continue;
      console.log(`    ${code}: ${s.verdict}${s.reasons?.length ? ` — ${s.reasons.join('; ')}` : ''}`);
    }
  }
  console.log(`\nFully verified ${summary.fullyVerified}/${summary.papers} | defects ${summary.withDefects} | needs review ${summary.needsReview} | Jev calls ${jevCalls}`);
  console.log(`Report: ${out}`);
  const exit = summary.withDefects > 0 ? 2 : summary.needsReview > 0 || summary.fullyVerified < summary.papers ? 3 : 0;
  process.exitCode = exit;
}

async function verifyPaperLive(paper, { stateDir, useJev, useLocalStt, fixRoot, countJev }) {
  const paperDir = join(stateDir, paper.id);
  const winDir = join(paperDir, 'windows');
  const trDir = join(paperDir, 'transcripts');
  mkdirSync(winDir, { recursive: true });
  mkdirSync(trDir, { recursive: true });

  const full = await api('GET', `/v1/admin/papers/${paper.id}`);
  const { byKey, audio } = resolveAudioByPart(full.assets ?? []);
  const row = {
    paperId: paper.id,
    title: paper.title,
    series: seriesOf(paper),
    sections: {},
    dupGuards: {},
    fullyVerified: false,
    anyFail: false,
    needsReview: false,
  };

  // Download each section's audio (reusing repair-pipeline downloads by id).
  for (const code of SECTIONS) {
    const info = audio[code];
    if (!info) continue;
    const pairCode = code[0];
    const local = reuseFixStateDownload(join(fixRoot, paper.id), pairCode, code, info.mediaAssetId)
      ?? join(paperDir, `${code}-${info.mediaAssetId}.mp3`);
    if (!existsSync(local)) await downloadMedia(info.mediaAssetId, local);
    const probed = probeDurationSec(local);
    row.sections[code] = {
      mediaAssetId: info.mediaAssetId,
      file: info.file,
      apiDur: info.dur,
      localDur: probed != null ? Math.round(probeDurationSec(local) * 10) / 10 : null,
      localPath: local,
    };
    if (info.dur && probed && Math.abs(info.dur - probed) > 2) {
      row.sections[code].reasons = [`api duration ${info.dur}s vs file ${Math.round(probed * 10) / 10}s`];
    }
  }

  // Cut + transcribe head/tail windows. Window files (and cached transcripts)
  // are keyed by media id so a re-verification after a repair re-cuts instead
  // of reading stale windows of the replaced asset.
  const wins = {};
  for (const code of SECTIONS) {
    const sec = row.sections[code];
    if (!sec) continue;
    const dur = sec.localDur;
    const stem = `${code}-${String(sec.mediaAssetId).slice(0, 8)}`;
    const headPath = join(winDir, `${stem}-head.mp3`);
    const tailPath = join(winDir, `${stem}-tail.mp3`);
    if (!ffmpegWin(sec.localPath, headPath, { offset: 0, duration: HEAD_SEC })) throw new Error(`head window cut failed for ${code}`);
    if (dur > HEAD_SEC + 20) {
      if (!ffmpegWin(sec.localPath, tailPath, { offset: Math.max(0, dur - TAIL_SEC), duration: TAIL_SEC })) throw new Error(`tail window cut failed for ${code}`);
    }
    wins[code] = {};
    for (const [name, path] of [['head', headPath], ['tail', tailPath]]) {
      if (!existsSync(path)) continue;
      const outJson = join(trDir, `${stem}-${name}.mp3.json`);
      if (existsSync(outJson)) {
        try { wins[code][name] = JSON.parse(readFileSync(outJson, 'utf-8')); continue; } catch { /* re-transcribe */ }
      }
      let result = null;
      for (let attempt = 1; attempt <= 5; attempt++) {
        try { result = await sttWindow(path, useLocalStt); break; }
        catch (err) {
          if (attempt === 5) throw new Error(`STT ${code}-${name}: ${err.message}`);
          await sleep(4000 * attempt);
        }
      }
      writeFileSync(outJson, JSON.stringify(result, null, 2));
      wins[code][name] = result;
      await sleep(300);
    }
  }

  // Deterministic evidence per section.
  const ev = {};
  for (const code of SECTIONS) {
    if (!row.sections[code]) continue;
    const head = wins[code]?.head;
    const tail = wins[code]?.tail;
    const headTokens = head ? windowTokens(head) : [];
    // A cue mention whose end is >=8s before the section end means real audio
    // (the preparation window) follows it on this section — the mis-split
    // signature. A mention within the last 8s is a closing transition
    // sentence ("…Now look at the notes for Extract 2.") and is legitimate.
    const hasTail = Boolean(tail);
    const tailCue = tail ? cueHits(windowTokens(tail)) : [];
    const tailCueLeak = hasTail ? tailCue.filter((h) => (TAIL_SEC - h.end) >= 8) : [];
    ev[code] = {
      headText: head ? windowText(head) : null,
      tailText: tail ? windowText(tail) : null,
      headTokens: headTokens.length,
      headCue: head ? (cueHits(headTokens).find((h) => h.start <= CUE_MAX_HEAD_SEC) ?? null) : null,
      headAnyCue: head ? cueHits(headTokens) : [],
      tailCue,
      tailCueLeak,
      firstSpeech: head ? firstSpeechText(head) : '',
    };
  }

  // Section verdicts (deterministic layer first; Jev judges semantics).
  const reasons = Object.fromEntries(SECTIONS.map((c) => [c, []]));
  const marks = Object.fromEntries(SECTIONS.map((c) => [c, null]));

  // Missing sections / skipped pairs.
  if (audio.C1 && !audio.C2) {
    marks.C1 = 'Skipped (C2 absent — pair not applicable)';
    marks.C2 = 'Skipped (C2 absent — pair not applicable)';
  }

  // Sibling duplicate guard (A1↔A2, C1↔C2).
  for (const [src, dst] of [['A1', 'A2'], ['C1', 'C2']]) {
    if (!row.sections[src] || !row.sections[dst]) continue;
    if (marks[src]?.startsWith('Skipped') || marks[dst]?.startsWith('Skipped')) continue;
    const guard = { pair: `${src}↔${dst}`, srcMedia: row.sections[src].mediaAssetId, dstMedia: row.sections[dst].mediaAssetId };
    if (row.sections[src].mediaAssetId === row.sections[dst].mediaAssetId) {
      guard.result = 'duplicate (same media asset id)';
      guard.similarity = 1;
      reasons[dst].push(`duplicate-content: ${dst} shares media asset ${row.sections[dst].mediaAssetId} with ${src}`);
      marks[dst] = marks[dst] ?? 'fail';
    } else {
      const sim = bigramDice(ev[src].firstSpeech, ev[dst].firstSpeech);
      guard.similarity = Math.round(sim * 1000) / 1000;
      if (sim >= DUP_NEAR_THRESHOLD) {
        guard.result = 'near-duplicate (deterministic transcript similarity)';
        reasons[dst].push(`duplicate-content: ${dst} opening ≈ ${src} opening (similarity ${guard.similarity})`);
        marks[dst] = 'fail';
      } else {
        guard.result = `distinct (similarity ${guard.similarity})`;
      }
    }
    row.dupGuards[`${src}↔${dst}`] = guard;
  }

  // Destination head checks (A2 / C2) — cue present + prep window.
  for (const code of ['A2', 'C2']) {
    if (!row.sections[code] || marks[code]?.startsWith('Skipped')) continue;
    if (ev[code].headText == null) { marks[code] = marks[code] ?? 'review'; reasons[code].push('head transcript unavailable'); continue; }
    const e = ev[code];
    if (e.headTokens === 0) { marks[code] = marks[code] ?? 'review'; reasons[code].push('head window has no transcribed speech (silence or STT empty)'); continue; }
    if (e.headCue) {
      const prep = prepWindowAfterCue(windowTokens(wins[code].head), e.headCue, HEAD_SEC);
      ev[code].prep = prep;
      if (prep?.prepSec != null && prep.prepSec < 8) {
        reasons[code].push(`preparation window too short after cue (${prep.prepSec}s)`);
        marks[code] = 'fail';
      }
    } else if (ev[code].headAnyCue.length === 0) {
      // No cue phrase at the destination head. When the SOURCE tail has no
      // cue mention either, the source material simply does not announce
      // "Extract Two" near this boundary — that is not mis-split evidence
      // and needs a human listen against the source, not an automatic fail.
      const srcCode = code[0] + '1';
      const srcHasCue = ev[srcCode]?.tailCue?.length > 0;
      if (srcHasCue) {
        reasons[code].push(`expected Extract Two introduction cue missing from ${code} head (first ${CUE_MAX_HEAD_SEC}s) while the cue sits at ${srcCode} tail — mis-split`);
        marks[code] = marks[code] ?? 'fail';
      } else {
        reasons[code].push(`no Extract Two cue phrase at ${code} head, and none at ${srcCode} tail either — source convention unverified, listen against the original recording`);
        marks[code] = marks[code] ?? 'review';
      }
    } else {
      reasons[code].push(`Extract Two cue present but later than ${CUE_MAX_HEAD_SEC}s into ${code} head`);
      marks[code] = marks[code] ?? 'review';
    }
  }

  // Source tail leak checks (A1 / C1) — cue at tail = mis-split signature.
  for (const code of ['A1', 'C1']) {
    if (!row.sections[code] || marks[code]?.startsWith('Skipped')) continue;
    if (row.sections[code].mediaAssetId === row.sections[code[0] + '2']?.mediaAssetId) {
      reasons[code].push('shared media asset with destination — split required');
      marks[code] = 'fail';
      continue;
    }
    if (ev[code].tailCueLeak.length > 0) {
      reasons[code].push(`Extract Two cue+preparation sits at ${code} tail (cue followed by ≥8s audio — mis-split, recut required)`);
      marks[code] = 'fail';
    }
  }

  // B: speech present; suspicious duplication → review only (out of repair scope).
  if (row.sections.B) {
    if (ev.B.headTokens === 0) { reasons.B.push('B head has no transcribed speech'); marks.B = 'review'; }
    else if (audio.A2 && row.sections.B.mediaAssetId === row.sections.A2?.mediaAssetId) {
      reasons.B.push('B resolves to the same asset as A2 (parent-asset fallback) — content check not applicable');
      marks.B = 'review';
    }
  }

  // Jev semantic layer ("use JEV 100%"): one batched call per paper over the
  // window texts; deterministic evidence ships in the state.
  if (useJev) {
    const state = {
      task: 'Content-level verification of OET Listening section audio. Deterministic timings and cue regex results are provided as evidence; judge meaning from the transcript texts.',
      paper: paper.title,
      windows: Object.fromEntries(SECTIONS.filter((c) => row.sections[c]).flatMap((c) => {
        const out = [];
        if (ev[c].headText != null) out.push([`${c}_HEAD_first120words`, ev[c].headText.split(/\s+/).slice(0, 120).join(' ')]);
        if (ev[c].tailText != null) out.push([`${c}_TAIL_last80words`, ev[c].tailText.split(/\s+/).slice(-80).join(' ')]);
        return out;
      }).map(([k, v]) => [k, v])),
      evidence: Object.fromEntries(SECTIONS.filter((c) => row.sections[c]).map((c) => [c, {
        headCueWithin20s: Boolean(ev[c].headCue),
        prepSecondsAfterCue: ev[c].prep?.prepSec ?? null,
        cueLeakAtTail: ev[c].tailCueLeak.length > 0,
        closingTransitionOnly: ev[c].tailCue.length > 0 && ev[c].tailCueLeak.length === 0,
      }])),
    };
    const questions = {};
    const want = (id, q) => { questions[id] = q; };
    if (row.sections.A2 && !marks.A2?.startsWith('Skipped')) {
      want('a2_head_cue', { type: 'noul', instructions: 'Does A2_HEAD_first120words begin with (or open with) an announcer transition introducing the second consultation ("Extract Two"), rather than starting directly inside a clinical dialogue?' });
      want('a2_continuation', { type: 'noul', instructions: 'Does A2_HEAD_first120words read as a CONTINUATION of the same consultation heard in A1_TAIL_last80words (same speakers, same conversation), rather than a new consultation?' });
    }
    if (row.sections.A1 && !marks.A1?.startsWith('Skipped')) {
      want('a1_tail_leak', { type: 'noul', instructions: 'Evidence `cueLeakAtTail` means a spoken "Extract 2" cue mention is followed by ≥8s of further audio in A1_TAIL_last80words — i.e. the introduction and preparation sit on this section (mis-split). `closingTransitionOnly` means a bare closing sentence like "Now look at the notes for Extract 2." right at the end (legitimate). Judging A1_TAIL_last80words: does this read as a mis-split (cue followed by preparation/other audio) rather than a closing transition sentence?' });
      want('a1_tail_clip', { type: 'noul', instructions: 'Does A1_TAIL_last80words end mid-sentence or abruptly cut a word, indicating clipped audio at the boundary?' });
    }
    if (row.sections.A2 && !marks.A2?.startsWith('Skipped')) {
      want('a2_head_clip', { type: 'noul', instructions: 'Does A2_HEAD_first120words start mid-sentence or with a fragment of a word, indicating clipped audio at the boundary?' });
    }
    if (row.sections.C2 && !marks.C2?.startsWith('Skipped')) {
      want('c2_head_cue', { type: 'noul', instructions: 'Does C2_HEAD_first120words begin with (or open with) an announcer transition introducing the second extract ("Extract Two"), rather than starting directly inside the interview?' });
      want('c1_tail_leak', { type: 'noul', instructions: 'Evidence `cueLeakAtTail` means a spoken "Extract 2" cue mention is followed by ≥8s of further audio in C1_TAIL_last80words (mis-split). `closingTransitionOnly` means a bare closing sentence at the very end (legitimate). Judging C1_TAIL_last80words: does this read as a mis-split rather than a closing transition sentence?' });
    }
    if (row.sections.A1 && row.sections.A2 && !marks.A2?.startsWith('Skipped')) {
      want('dup_a', { type: 'noul', instructions: 'Comparing A1_HEAD_first120words and A2_HEAD_first120words: are these openings of the SAME spoken recording content (a repeated/relabeled audio), as opposed to two different consultations?' });
    }
    if (row.sections.C1 && row.sections.C2 && !marks.C2?.startsWith('Skipped')) {
      want('dup_c', { type: 'noul', instructions: 'Comparing C1_HEAD_first120words and C2_HEAD_first120words: are these openings of the SAME spoken recording content (a repeated/relabeled audio), as opposed to two different interviews?' });
    }
    if (Object.keys(questions).length > 0) {
      try {
        countJev();
        const answers = await jevJudge(`listening-content:${paper.id}`, state, questions);
        row.jev = {};
        for (const [id, a] of Object.entries(answers)) {
          const v = a.probability != null ? noulVerdict(a) : confidentChoice(a);
          row.jev[id] = a.probability != null ? { p: a.probability, verdict: v } : { choice: v.choice, confident: v.confident };
        }
        const jevFail = (code, id, failureText) => {
          const j = row.jev[id];
          if (!j) return;
          if (j.verdict === 'yes') {
            reasons[code].push(failureText);
            marks[code] = 'fail';
          } else if (j.verdict === 'unsure') {
            if (marks[code] !== 'fail') { marks[code] = 'review'; reasons[code].push(`Jev uncertain on ${id} — manual confirmation required`); }
          }
        };
        // Destination-head semantics.
        if (row.jev.a2_head_cue?.verdict === 'no' && ev.A2.headCue == null && !String(marks.A2 ?? '').startsWith('Skipped')) {
          reasons.A2.push('Jev: A2 head does not read as the Extract Two introduction');
          marks.A2 = marks.A2 === 'fail' ? 'fail' : 'review';
        }
        jevFail('A2', 'a2_continuation', 'Jev: A2 head continues the previous consultation instead of starting Extract Two');
        // Source-tail semantics (confirm the deterministic leak or catch a regex miss).
        if (row.jev.a1_tail_leak?.verdict === 'yes' && marks.A1 !== 'fail' && !String(marks.A1 ?? '').startsWith('Skipped')) {
          reasons.A1.push('Jev: Extract Two cue present at A1 tail (mis-split)');
          marks.A1 = 'fail';
        }
        jevFail('A1', 'a1_tail_clip', 'Jev: A1 tail speech clipped at boundary');
        jevFail('A2', 'a2_head_clip', 'Jev: A2 head speech clipped at boundary');
        if (row.jev.c2_head_cue?.verdict === 'no' && ev.C2.headCue == null && !String(marks.C2 ?? '').startsWith('Skipped')) {
          reasons.C2.push('Jev: C2 head does not read as the Extract Two introduction');
          marks.C2 = marks.C2 === 'fail' ? 'fail' : 'review';
        }
        if (row.jev.c1_tail_leak?.verdict === 'yes' && marks.C1 !== 'fail' && !String(marks.C1 ?? '').startsWith('Skipped')) {
          reasons.C1.push('Jev: Extract Two cue present at C1 tail (mis-split)');
          marks.C1 = 'fail';
        }
        // Duplicate guard: Jev is the decider only inside the ambiguous band;
        // below DUP_JEV_BAND the deterministic similarity already settles
        // distinctness and a hedged Jev answer must not downgrade it.
        for (const [id, src, dst] of [['dup_a', 'A1', 'A2'], ['dup_c', 'C1', 'C2']]) {
          const j = row.jev[id];
          const guard = row.dupGuards[`${src}↔${dst}`];
          if (!j || !guard || guard.result?.startsWith('duplicate') || guard.result?.startsWith('near-duplicate')) continue;
          if (typeof guard.similarity === 'number' && guard.similarity < DUP_JEV_BAND) continue;
          if (j.verdict === 'yes') {
            guard.result = `duplicate (Jev same-content p=${j.p})`;
            reasons[dst].push(`duplicate-content: Jev judges ${dst} opening to be the same recording as ${src}`);
            marks[dst] = 'fail';
          } else if (j.verdict === 'unsure') {
            guard.result = `ambiguous (Jev unsure p=${j.p})`;
            if (marks[dst] !== 'fail') { marks[dst] = 'review'; reasons[dst].push(`duplicate guard ambiguous for ${dst}↔${src} — manual confirmation required`); }
          } else {
            guard.result = `distinct (Jev p=${j.p}, similarity ${guard.similarity})`;
          }
        }
      } catch (err) {
        if (err instanceof JevError) {
          row.jevError = err.message;
          for (const code of SECTIONS) {
            if (row.sections[code] && !marks[code] && !String(marks[code] ?? '').startsWith('Skipped')) {
              marks[code] = 'review';
              reasons[code].push(`Jev unavailable (${err.message}) — semantic confirmation required`);
            }
          }
        } else throw err;
      }
    }
  } else {
    for (const code of SECTIONS) {
      if (row.sections[code] && !marks[code]) {
        marks[code] = 'review';
        reasons[code].push('semantic judgment disabled (--no-jev) — manual confirmation required');
      }
    }
  }

  // Materialize verdicts.
  for (const code of SECTIONS) {
    const sec = row.sections[code];
    if (!sec) continue;
    const mark = marks[code];
    const rs = [...(sec.reasons ?? []), ...reasons[code]];
    if (mark?.startsWith('Skipped')) sec.verdict = mark;
    else if (mark === 'fail') sec.verdict = `Failed (${rs.join('; ')})`;
    else if (mark === 'review') sec.verdict = `Needs manual review (${rs.join('; ') || 'undetermined'})`;
    else sec.verdict = 'Verified (content)';
    sec.evidence = {
      headCue: ev[code]?.headCue ?? null,
      prepAfterCueSec: ev[code]?.prep?.prepSec ?? null,
      tailCueHits: ev[code]?.tailCue.length ?? 0,
    };
    if (sec.verdict.startsWith('Failed')) row.anyFail = true;
    if (sec.verdict.startsWith('Needs manual review')) row.needsReview = true;
  }
  row.fullyVerified = !row.anyFail && !row.needsReview
    && SECTIONS.every((c) => !row.sections[c] || row.sections[c].verdict.startsWith('Verified') || row.sections[c].verdict.startsWith('Skipped'));
  if (!SECTIONS.some((c) => row.sections[c])) {
    row.needsReview = true;
    row.sections.A1 = { verdict: 'Needs manual review (no per-section audio resolved)' };
  }
  return row;
}

// ── CLI ──────────────────────────────────────────────────────────────────────

function parseArgs() {
  const a = process.argv.slice(2);
  const get = (k) => { const i = a.indexOf(k); return i >= 0 ? a[i + 1] : null; };
  return {
    live: a.includes('--live'),
    manifest: get('--manifest'),
    manifestDir: get('--manifestDir'),
    transcript: get('--transcript'),
    transcriptDir: get('--transcriptDir'),
    stateDir: get('--state-dir') ?? join(SCRIPT_DIR, 'state', 'verify'),
    paper: get('--paper'),
    maxPapers: Number(get('--max-papers') ?? 0) || 0,
    noJev: a.includes('--no-jev'),
    apiStt: a.includes('--api-stt'),
  };
}

if (!hasFfprobe()) { console.error('ffprobe not found (ffmpeg required)'); process.exit(1); }
const args = parseArgs();
if (args.live) {
  await liveMode(args);
} else if (args.manifest) {
  const { structuralOk } = verifyOne(args.manifest, args.transcript, args);
  process.exit(structuralOk ? 3 : 2); // 3 = structural-only pass ⇒ manual review pending (never "verified")
} else if (args.manifestDir) {
  const entries = readdirSync(args.manifestDir).filter((d) => {
    const p = join(args.manifestDir, d);
    try { return statSync(p).isDirectory(); } catch { return false; }
  });
  let ok = true;
  for (const dir of entries.sort()) {
    const m = join(args.manifestDir, dir, 'split-manifest.json');
    if (!existsSync(m)) { console.log(`Skip ${dir} (no manifest)`); continue; }
    const t = args.transcriptDir ? join(args.transcriptDir, `${dir}.json`) : null;
    let t2 = t;
    try {
      const man = JSON.parse(readFileSync(m, 'utf-8'));
      const srcBase = man.src ? basename(man.src, '.mp3') : null;
      if (srcBase && args.transcriptDir) t2 = join(args.transcriptDir, `${srcBase}.json`);
    } catch {}
    const r = verifyOne(m, t2 && existsSync(t2) ? t2 : null, args);
    ok = r.structuralOk && ok;
  }
  process.exit(ok ? 3 : 2);
} else {
  console.error('Usage: --live [--paper <id>] [--no-jev]   or   --manifest <path> [--transcript <json>]   or   --manifestDir <dir> [--transcriptDir <dir>]');
  process.exit(1);
}
