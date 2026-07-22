#!/usr/bin/env node
// ─────────────────────────────────────────────────────────────────────────────
// Re-tag the live Video Library so it matches the "Course Videos" content chart
// (owner, 2026-07-18). Sets each video's `language` (en/ar) and `targetProfessionIds`
// so the app resolves exactly the availability matrix in the chart — WITHOUT any
// new content (English is one canonical set; Arabic Medicine Writing/Speaking
// aliases Physiotherapy only; Dentistry/Radiography are limited to Listening/Reading).
//
// The rules (derived deterministically from scripts/videos/state/manifest.json):
//
//   English (any module)                 → language en, professions []  (all 6)
//   Arabic Listening / Reading (shared)  → language ar, professions []  (all 6)
//   Arabic Speaking/Writing — Medicine   → language ar, [medicine, physiotherapy]
//   Arabic Speaking/Writing — Nursing    → language ar, [nursing]
//   Arabic Speaking/Writing — Pharmacy   → language ar, [pharmacy]
//
// Dentistry & Radiography receive Listening/Reading only.
//
// SAFETY:
//   * DRY-RUN by default — prints the full plan + summary and touches no network.
//     Pass --apply to actually PATCH production.
//   * Before every PATCH it GETs the video and asserts the returned bunnyVideoId
//     matches the manifest guid, so a stale local id can never mutate the wrong video.
//   * Idempotent + resumable — completed video ids are recorded in
//     scripts/videos/state/retag-state.json and skipped on re-run.
//   * Rate-limit friendly (default 2200ms between calls; admin PerUser limit ~30/min).
//
// USAGE (prod):
//   $env:OET_ADMIN_TOKEN="<prod admin JWT>"; node scripts/videos/retag-language-profession.mjs           # dry-run
//   $env:OET_ADMIN_TOKEN="<prod admin JWT>"; node scripts/videos/retag-language-profession.mjs --apply    # execute
//   Optional: OET_API_BASE (default https://api.oetwithdrhesham.co.uk), --limit N, --delay-ms N
//
// The token is a short-lived admin JWT (DevTools → Copy as cURL). Read from env only;
// never hard-coded, never logged.
// ─────────────────────────────────────────────────────────────────────────────

import fsp from 'node:fs/promises';
import fs from 'node:fs';
import path from 'node:path';

const STATE_DIR = process.env.VIDEO_STATE_DIR || path.join(process.cwd(), 'scripts', 'videos', 'state');
const MANIFEST_PATH = path.join(STATE_DIR, 'manifest.json');
const REG_STATE_PATH = path.join(STATE_DIR, 'registration-state.json');
const RETAG_STATE_PATH = path.join(STATE_DIR, 'retag-state.json');

const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/$/, '');
const TOKEN = process.env.OET_ADMIN_TOKEN || '';
const APPLY = process.argv.includes('--apply');
const LIMIT = numArg('--limit');
const DELAY_MS = numArg('--delay-ms') ?? 2200;

const RULE_VERSION = 'profession-first-flowchart-v2';
const MEDICINE_SET = ['medicine', 'physiotherapy'];

function numArg(flag) {
  const i = process.argv.indexOf(flag);
  if (i === -1 || i + 1 >= process.argv.length) return undefined;
  const n = Number(process.argv[i + 1]);
  return Number.isFinite(n) ? n : undefined;
}

/** Deterministic {language, targetProfessionIds} for one manifest video. */
function computeTarget(v) {
  const module = String(v.module || '').trim().toLowerCase();
  const langRaw = String(v.language || '').trim().toLowerCase();
  const profRaw = String(v.profession || '').trim().toLowerCase();
  const rel = String(v.relPath || '').toLowerCase();

  // English → en; Arabic → ar; untagged → ar (the only untagged rows are the
  // profession-specific Nursing/Pharmacy Arabic Speaking/Writing sets).
  const language = langRaw.startsWith('eng') ? 'en' : 'ar';

  let targetProfessionIds;
  if (language === 'en') {
    targetProfessionIds = []; // shared English → visible to every profession
  } else if (module === 'listening' || module === 'reading') {
    targetProfessionIds = []; // shared Arabic Listening/Reading → every profession
  } else {
    let prof = profRaw;
    if (!prof) {
      // Untagged Arabic Speaking/Writing = the "New Medicine Crash Course" folder.
      if (rel.includes('medicine')) prof = 'medicine';
      else if (rel.includes('nursing')) prof = 'nursing';
      else if (rel.includes('pharmacy')) prof = 'pharmacy';
    }
    if (prof === 'medicine') targetProfessionIds = [...MEDICINE_SET];
    else if (prof === 'nursing') targetProfessionIds = ['nursing'];
    else if (prof === 'pharmacy') targetProfessionIds = ['pharmacy'];
    else {
      // Fail CLOSED: an Arabic Speaking/Writing video we cannot confidently place is
      // surfaced for manual review, never blanket-exposed to the medicine set.
      return { language, targetProfessionIds: null, reason: `unresolved Arabic ${module} profession='${v.profession ?? ''}'` };
    }
  }
  return { language, targetProfessionIds };
}

function sameSet(a, b) {
  if (a.length !== b.length) return false;
  const bs = new Set(b.map((x) => String(x).toLowerCase()));
  return a.every((x) => bs.has(String(x).toLowerCase()));
}

async function api(method, urlPath, body) {
  const res = await fetch(`${API_BASE}${urlPath}`, {
    method,
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON */ }
  if (!res.ok) {
    const msg = json?.message || json?.title || text?.slice(0, 200) || '';
    const err = new Error(`HTTP ${res.status} ${method} ${urlPath} — ${msg}`);
    err.status = res.status;
    throw err;
  }
  return json;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function loadDoneSet() {
  try {
    const s = JSON.parse(await fsp.readFile(RETAG_STATE_PATH, 'utf8'));
    return s.ruleVersion === RULE_VERSION ? new Set(s.done || []) : new Set();
  } catch {
    return new Set();
  }
}

async function saveDone(done) {
  await fsp.writeFile(RETAG_STATE_PATH, JSON.stringify({ ruleVersion: RULE_VERSION, done: [...done] }, null, 2));
}

async function main() {
  const manifest = JSON.parse(await fsp.readFile(MANIFEST_PATH, 'utf8'));
  const videos = Array.isArray(manifest) ? manifest : manifest.videos || [];
  const regState = fs.existsSync(REG_STATE_PATH)
    ? JSON.parse(await fsp.readFile(REG_STATE_PATH, 'utf8'))
    : { videos: {} };
  const regVideos = regState.videos || {};

  // Build the plan.
  const plan = [];
  const unmapped = [];
  for (const v of videos) {
    const bunnyId = v.bunnyVideoId;
    const appVideoId = bunnyId ? regVideos[bunnyId]?.videoId : null;
    const target = computeTarget(v);
    if (!appVideoId || target.targetProfessionIds === null) { unmapped.push({ ...v, target }); continue; }
    plan.push({ bunnyId, appVideoId, title: v.title, relPath: v.relPath, module: v.module, ...target });
  }

  // Summary by (language × professions).
  const summary = {};
  for (const p of plan) {
    const key = `${p.language} | ${p.targetProfessionIds.length ? p.targetProfessionIds.join('+') : '(all)'}`;
    summary[key] = (summary[key] || 0) + 1;
  }

  console.log(`API base:      ${API_BASE}`);
  console.log(`Mode:          ${APPLY ? 'APPLY (writes to production)' : 'DRY-RUN (no network writes)'}`);
  console.log(`Rule version:  ${RULE_VERSION}`);
  console.log(`Manifest:      ${videos.length} videos`);
  console.log(`Mapped:        ${plan.length}   Unmapped: ${unmapped.length}`);
  console.log(`\nPlanned tag distribution:`);
  for (const k of Object.keys(summary).sort()) console.log(`  ${summary[k].toString().padStart(3)}  ${k}`);
  if (unmapped.length) {
    console.log(`\n⚠ Unmapped / unresolved (skipped — needs manual review):`);
    for (const u of unmapped) {
      const reason = u.target?.targetProfessionIds === null
        ? (u.target.reason || 'unresolved profession')
        : 'no app videoId in registration-state';
      console.log(`   [${reason}] ${u.relPath}`);
    }
  }

  if (!APPLY) {
    console.log(`\nDry-run only. Re-run with --apply and OET_ADMIN_TOKEN set to write.`);
    return;
  }
  if (!TOKEN) {
    console.error(`\nFATAL: --apply requires OET_ADMIN_TOKEN (a prod admin JWT).`);
    process.exit(1);
  }

  const done = await loadDoneSet();
  let applied = 0, skipped = 0, mismatched = 0, failed = 0, processed = 0;
  for (const p of plan) {
    if (done.has(p.appVideoId)) { skipped++; continue; }
    if (LIMIT && processed >= LIMIT) break;
    processed++;
    try {
      // 1) Confirm this app video really is the manifest's Bunny video.
      const detail = await api('GET', `/v1/admin/video-library/videos/${encodeURIComponent(p.appVideoId)}`);
      // Fail CLOSED: only PATCH when the fetched row positively IS the manifest's
      // Bunny video. A missing or non-matching bunnyVideoId is treated as a mismatch,
      // so a stale local id can never mutate the wrong production video.
      if (!detail?.bunnyVideoId || (p.bunnyId && detail.bunnyVideoId !== p.bunnyId)) {
        console.warn(`  MISMATCH ${p.appVideoId}: bunny ${detail?.bunnyVideoId ?? '(none)'} != ${p.bunnyId} — skipping ${p.relPath}`);
        mismatched++;
        await sleep(DELAY_MS);
        continue;
      }
      await sleep(DELAY_MS);
      // 2) Apply the language + profession tags.
      const patched = await api('PATCH', `/v1/admin/video-library/videos/${encodeURIComponent(p.appVideoId)}`, {
        language: p.language,
        targetProfessionIds: p.targetProfessionIds,
      });
      // 3) Self-verify the write landed.
      const okLang = patched?.language === p.language;
      const okProf = sameSet(patched?.targetProfessionIds || [], p.targetProfessionIds);
      if (!okLang || !okProf) {
        console.warn(`  VERIFY-FAIL ${p.appVideoId}: got lang=${patched?.language} profs=${JSON.stringify(patched?.targetProfessionIds)} — ${p.relPath}`);
        failed++;
      } else {
        applied++;
        done.add(p.appVideoId);
        if (applied % 10 === 0) await saveDone(done);
        console.log(`  ✓ ${p.language.padEnd(2)} [${p.targetProfessionIds.join(',') || 'all'}]  ${p.title}`);
      }
    } catch (e) {
      failed++;
      console.warn(`  FAIL ${p.appVideoId}: ${e.message}`);
    }
    await sleep(DELAY_MS);
  }
  await saveDone(done);
  console.log(`\nDone. applied=${applied} skipped(already)=${skipped} mismatched=${mismatched} failed=${failed}`);
  if (failed || mismatched) process.exitCode = 1;
}

main().catch((e) => { console.error('FATAL', e.message); process.exit(1); });
