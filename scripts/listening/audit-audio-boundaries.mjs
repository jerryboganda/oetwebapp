#!/usr/bin/env node
/**
 * Listening — per-sub-section audio boundary audit for Atlas + Nova papers.
 *
 * READ-ONLY. For every Listening paper in the Atlas Practice Series and Nova
 * Practice Series (same matchers as lib/listening-exam-categories.ts), pull the
 * primary per-part audio assets (A1/A2/B/C1/C2) and the authored extract
 * timers, then classify the split state and — where the paper can be matched
 * to a source master in scripts/materials/test_audio_boundaries_computed.json
 * — whether the Extract Two introduction + preparation window is sitting at
 * the tail of A1 / C1 instead of the head of A2 / C2.
 *
 * Assets are read from GET /v1/admin/papers/{id} (ProjectPaper.assets), which
 * — unlike the authoring /structure endpoint — does not filter on media status
 * and mirrors what the learner audio resolver actually uses.
 *
 * Master matching (either is sufficient, both is best):
 *   1. Title stem match against the computed-bounds source filenames.
 *   2. Duration-signature match: A1+A2 ≈ bounds.B.start and C1+C2 ≈
 *      total − bounds.C1.start (± tolerance) — the bounds are contiguous, so
 *      the pair sums identify the source test regardless of naming.
 *
 * Usage:
 *   OET_ADMIN_EMAIL=... OET_ADMIN_PASSWORD=... node scripts/listening/audit-audio-boundaries.mjs
 *   node scripts/listening/audit-audio-boundaries.mjs --paper <paperId>   # single-paper debug
 *
 * Env:
 *   OET_API_BASE               default https://api.oetwithdrhesham.co.uk
 *   OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD   preferred (auto-refresh)
 *   OET_ADMIN_REFRESH_TOKEN    fallback
 *   OET_ADMIN_TOKEN            raw token (15-min lifetime; smoke tests only)
 *
 * Output: scripts/listening/state/audit-<timestamp>.json (+ console table).
 * Credentials are read from the environment only and are never printed.
 */

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const BOUNDS_JSON = join(__dirname, '..', 'materials', 'test_audio_boundaries_computed.json');
const STATE_DIR = join(__dirname, 'state');

const SINGLE_PAPER = (() => {
  const i = process.argv.indexOf('--paper');
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : null;
})();

// Mirror lib/listening-exam-categories.ts — do not drift.
const SERIES = [
  { id: 'atlas', matchers: ['atlas-practice-series', 'atlas practice series', 'atlas-practice'] },
  { id: 'nova', matchers: ['nova-practice-series', 'nova practice series', 'nova-practice'] },
];
const SECTIONS = ['A1', 'A2', 'B', 'C1', 'C2'];

// A mis-split shifts roughly one preparation window (~30s Part A / ~90s Part C)
// plus the spoken cue from the destination file onto the source file. 10s
// tolerance absorbs re-encode rounding while staying far below any window.
const TOL = 10;

// ── Auth (same contract as scripts/materials/ingest-materials.mjs) ──────────
let accessToken = process.env.OET_ADMIN_TOKEN || '';
let refreshToken = process.env.OET_ADMIN_REFRESH_TOKEN || '';
let accessTokenExpiresAt = accessToken ? Date.now() + 15 * 60_000 : 0;

function applySession(json) {
  accessToken = json.accessToken;
  if (json.refreshToken) refreshToken = json.refreshToken;
  accessTokenExpiresAt = json.accessTokenExpiresAt
    ? new Date(json.accessTokenExpiresAt).getTime()
    : Date.now() + 15 * 60_000;
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
  if (email && password) {
    applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true }));
    return;
  }
  if (refreshToken) {
    applySession(await authPost('/v1/auth/refresh', { refreshToken }));
    return;
  }
  if (accessToken) {
    console.warn('! OET_ADMIN_TOKEN has no refresh path; production tokens expire in 15 minutes.');
    return;
  }
  throw new Error('No admin credentials in environment (OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD or OET_ADMIN_REFRESH_TOKEN).');
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
    accessToken = '';
    accessTokenExpiresAt = 0;
    await signIn();
    return api(method, urlPath, false);
  }
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 200)}`);
  return text ? JSON.parse(text) : null;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ── Source-master bounds ─────────────────────────────────────────────────────
function loadBounds() {
  const raw = JSON.parse(readFileSync(BOUNDS_JSON, 'utf-8'));
  const entries = [];
  for (const entry of raw) {
    const stem = normStem(entry.filename);
    if (!stem) continue;
    const b = entry.bounds ?? {};
    entries.push({
      stem,
      filename: entry.filename,
      total: entry.duration ?? null,
      A1: b.A1?.dur ?? null,
      A2: b.A2?.dur ?? null,
      B: b.B?.dur ?? null,
      C1: b.C1?.dur ?? null,
      C2: b.C2?.dur ?? null,
      bStart: b.A1 && b.A2 ? round(b.A1.dur + b.A2.dur) : null,   // A pair ends where B starts
      cStart: b.C1 && b.C2 ? round(b.C1.dur) : null,               // C pair length = C1.dur + C2.dur
    });
  }
  return entries;
}

function normStem(name) {
  return String(name || '')
    .replace(/\.[a-z0-9]+$/i, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

const round = (x) => Math.round(x * 10) / 10;

function matchMasterByTitle(paperTitle, bounds) {
  const t = normStem(paperTitle);
  if (!t) return null;
  let best = null;
  for (const e of bounds) {
    if (e.stem.length < 6) continue;
    if (t.includes(e.stem) || e.stem.includes(t)) {
      if (!best || e.stem.length > best.stem.length) best = e;
    }
  }
  return best ? { entry: e0(bounds, best), via: 'title' } : null;
}
const e0 = (bounds, e) => e;

function matchMasterByDurations(parts, bounds) {
  const aSum = parts.A1 && parts.A2 ? round(parts.A1 + parts.A2) : null;
  const cSum = parts.C1 && parts.C2 ? round(parts.C1 + parts.C2) : null;
  if (aSum == null && cSum == null) return null;
  const hits = [];
  for (const e of bounds) {
    let aOk = aSum != null && e.bStart != null && Math.abs(aSum - e.bStart) <= 20;
    let cOk = cSum != null && e.cStart != null && Math.abs(cSum - e.cStart) <= 20;
    // Both available: require both. Only one available: that one decides.
    if (aSum != null && cSum != null) {
      if (aOk && cOk) hits.push(e);
    } else if (aOk || cOk) {
      hits.push(e);
    }
  }
  if (hits.length !== 1) return { entry: null, via: 'duration', ambiguous: hits.map((h) => h.filename) };
  return { entry: hits[0], via: 'duration' };
}

// ── Series classification (mirrors resolveListeningExamCategoryId) ──────────
function seriesOf(paper) {
  const haystack = [paper.tagsCsv, paper.slug, paper.title]
    .filter((v) => v && String(v).trim())
    .join(' ')
    .toLowerCase()
    .replace(/_+/g, '-');
  for (const s of SERIES) {
    if (s.matchers.some((m) => haystack.includes(m))) return s.id;
  }
  return 'other';
}

// ── Audit one paper ──────────────────────────────────────────────────────────
async function auditPaper(paper, bounds) {
  const full = await api('GET', `/v1/admin/papers/${paper.id}`);
  const assets = full.assets ?? [];

  // Primary Audio per learner-facing section. Resolution mirrors
  // ResolveUploadedAudioForSection: exact section key → legacy B1..B6 →
  // parent A/C. First (IsPrimary, DisplayOrder) wins per key.
  const byKey = new Map();
  for (const a of assets) {
    if (a.role !== 'Audio' || !a.isPrimary) continue;
    const key = String(a.part ?? '').trim().toUpperCase();
    if (!key) continue;
    const dur = a.media?.durationSeconds ?? null;
    if (!byKey.has(key)) byKey.set(key, { mediaAssetId: a.mediaAssetId, file: a.media?.originalFilename ?? null, dur });
  }
  const resolve = (code) => {
    if (byKey.has(code)) return byKey.get(code);
    if (code === 'B') {
      for (let i = 1; i <= 6; i++) if (byKey.has(`B${i}`)) return byKey.get(`B${i}`);
    }
    const parent = code[0];
    if (byKey.has(parent)) return byKey.get(parent);
    return null;
  };
  const audio = Object.fromEntries(SECTIONS.map((c) => [c, resolve(c)]));

  const extractsDoc = await api('GET', `/v1/admin/papers/${paper.id}/listening/extracts`);
  const timers = {};
  for (const e of extractsDoc?.extracts ?? []) {
    const code = String(e.partCode ?? '').trim().toUpperCase();
    if (SECTIONS.includes(code) && e.timeLimitSeconds != null) timers[code] = e.timeLimitSeconds;
  }

  const uploaded = SECTIONS.filter((c) => audio[c]);
  const parentOnly = ['A', 'C'].filter((p) => byKey.has(p) && !byKey.has(`${p}1`));
  let splitState;
  if (uploaded.length > 0) splitState = 'split';
  else if (parentOnly.length > 0) splitState = 'parent-only';
  else if (Object.keys(timers).length > 0) splitState = 'no-uploaded-audio';
  else splitState = 'none';

  const row = {
    paperId: paper.id,
    title: paper.title,
    status: full.status,
    series: seriesOf(paper),
    splitState,
    parts: uploaded,
    audio: Object.fromEntries(SECTIONS.map((c) => [c, audio[c] ? { file: audio[c].file, dur: audio[c].dur, mediaAssetId: audio[c].mediaAssetId } : null])),
    timers,
    flags: [],
    verdict: {},
  };

  // Missing C2 → skip only the C1→C2 step; never an error.
  if (audio.C1 && !audio.C2) row.flags.push('c2-missing: C1→C2 split/transition skipped (not applicable)');

  // Timer shorter than its section audio would cut the recording at 00:00
  // now that expiry auto-advances.
  for (const code of SECTIONS) {
    const dur = audio[code]?.dur;
    const limit = timers[code];
    if (dur && limit != null && limit + 5 < Math.ceil(dur)) {
      row.flags.push(`timer-short: ${code} timer ${limit}s < audio ${Math.ceil(dur)}s`);
    }
  }

  // Master match: title first, duration signature as fallback/cross-check.
  const byTitle = matchMasterByTitle(paper.title, bounds);
  const byDur = matchMasterByDurations(
    Object.fromEntries(SECTIONS.map((c) => [c, audio[c]?.dur ?? null])),
    bounds,
  );
  const master = byTitle ?? (byDur?.entry ? byDur : null);
  if (master?.entry) {
    row.master = { source: master.entry.filename, via: master.via };
    if (byDur?.entry && byTitle && byDur.entry.filename !== byTitle.entry.filename) {
      row.flags.push(`master-conflict: title→${byTitle.entry.filename} vs duration→${byDur.entry.filename}`);
    }
    for (const [src, dst, prep] of [['A1', 'A2', 30], ['C1', 'C2', 90]]) {
      const srcDur = audio[src]?.dur;
      const dstDur = audio[dst]?.dur;
      const expSrc = master.entry[src];
      const expDst = master.entry[dst];
      if (!srcDur || !dstDur || !expSrc || !expDst) continue;
      const srcDelta = Math.round(srcDur - expSrc);
      const dstDelta = Math.round(dstDur - expDst);
      if (srcDelta > TOL && dstDelta < -TOL && Math.abs(srcDelta + dstDelta) <= 25) {
        row.verdict[src] = `MIS-SPLIT: ${src} +${srcDelta}s / ${dst} ${dstDelta}s vs expected (~${prep}s cue+prep on the wrong side)`;
      } else if (Math.abs(srcDelta) > 45 || Math.abs(dstDelta) > 45) {
        row.verdict[src] = `review: ${src} ${srcDelta >= 0 ? '+' : ''}${srcDelta}s / ${dst} ${dstDelta >= 0 ? '+' : ''}${dstDelta}s vs expected bounds`;
      } else {
        row.verdict[src] = 'ok';
      }
    }
  } else {
    row.verdict.boundary = byDur?.ambiguous?.length
      ? `ambiguous duration match: ${byDur.ambiguous.join(', ')}`
      : 'unknown: no source master matched (title or duration signature)';
  }

  return row;
}

// ── Main ─────────────────────────────────────────────────────────────────────
async function main() {
  await signIn();
  const bounds = loadBounds();

  const papers = [];
  if (SINGLE_PAPER) {
    papers.push(await api('GET', `/v1/admin/papers/${SINGLE_PAPER}`));
  } else {
    const pageSize = 100;
    for (let page = 1; ; page++) {
      const batch = await api('GET', `/v1/admin/papers?subtest=listening&page=${page}&pageSize=${pageSize}`);
      if (!Array.isArray(batch) || batch.length === 0) break;
      papers.push(...batch);
      if (batch.length < pageSize) break;
      await sleep(200);
    }
  }

  const inScope = papers.filter((p) => p.status !== 'Archived' && seriesOf(p) !== 'other');
  console.log(`Listening papers: ${papers.length} total, ${inScope.length} in scope (Atlas/Nova), ${papers.length - inScope.length} out of scope.`);

  const rows = [];
  for (const paper of inScope) {
    try {
      const row = await auditPaper(paper, bounds);
      rows.push(row);
      const verdict = row.verdict.A1 ?? row.verdict.C1 ?? row.verdict.boundary ?? 'ok';
      const durStr = SECTIONS.map((c) => (row.audio[c] ? `${c}:${Math.round(row.audio[c].dur)}s` : null)).filter(Boolean).join(' ');
      console.log(
        `[${row.series.toUpperCase()}] ${row.title}\n    ${durStr || '(no per-section audio)'}`
        + `${row.c2Missing ? '  (no C2 — skipped)' : ''}\n    → ${verdict}${row.flags.length ? `\n    flags: ${row.flags.join('; ')}` : ''}`,
      );
    } catch (err) {
      rows.push({ paperId: paper.id, title: paper.title, series: seriesOf(paper), error: String(err.message || err) });
      console.error(`[ERROR] ${paper.title}: ${err.message}`);
    }
    await sleep(150);
  }

  const summary = {
    generatedAt: new Date().toISOString(),
    apiBase: API_BASE,
    total: papers.length,
    inScope: rows.length,
    misSplit: rows.filter((r) => Object.values(r.verdict ?? {}).some((v) => String(v).startsWith('MIS-SPLIT'))).length,
    review: rows.filter((r) => Object.values(r.verdict ?? {}).some((v) => String(v).startsWith('review'))).length,
    unknown: rows.filter((r) => r.verdict?.boundary?.startsWith('unknown') || r.verdict?.boundary?.startsWith('ambiguous')).length,
    c2Missing: rows.filter((r) => r.c2Missing).length,
    errors: rows.filter((r) => r.error).length,
    rows,
  };
  mkdirSync(STATE_DIR, { recursive: true });
  const out = join(STATE_DIR, `audit-${Date.now()}.json`);
  writeFileSync(out, JSON.stringify(summary, null, 2));
  console.log(`\nMIS-SPLIT ${summary.misSplit} | review ${summary.review} | unknown ${summary.unknown} | c2-missing ${summary.c2Missing} | errors ${summary.errors}`);
  console.log(`Report: ${out}`);
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
