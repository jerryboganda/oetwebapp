#!/usr/bin/env node
// Register the already-uploaded Bunny videos into the app's Video Library so
// learners see them, grouped into breadcrumb-named shelves (VideoCategory) that
// mirror the folder hierarchy, gated by profession (+ the global VideoLibrary
// package entitlement). Drives the admin API — needs an OWNER admin bearer token.
//
// Prereqs:
//   1. The Bunny upload finished (scripts/videos/state/state.json all 'done').
//   2. `node scripts/videos/enrich-manifest.mjs` produced registration-plan.json.
//   3. Bunny has finished ENCODING the videos (publish is gated on encode=Ready).
//
// Env:
//   ADMIN_TOKEN   owner admin bearer token (obtain via login; never hard-code)
//   API_BASE      default https://api.oetwithdrhesham.co.uk
//
// Usage:
//   node scripts/videos/register-videos-in-app.mjs                 # categories + import + patch (leaves DRAFT)
//   node scripts/videos/register-videos-in-app.mjs --publish       # also publish encode-ready videos
//   node scripts/videos/register-videos-in-app.mjs --dry-run       # print what it WOULD do, no writes

import fsp from 'node:fs/promises';
import path from 'node:path';

const STATE_DIR = process.env.VIDEO_STATE_DIR || path.join(process.cwd(), 'scripts', 'videos', 'state');
const PLAN_PATH = path.join(STATE_DIR, 'registration-plan.json');
const REG_STATE_PATH = path.join(STATE_DIR, 'registration-state.json');

const API_BASE = (process.env.API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/$/, '');
const TOKEN = process.env.ADMIN_TOKEN || '';
const BASE = `${API_BASE}/v1/admin/video-library`;

const DRY = process.argv.includes('--dry-run');
const DO_PUBLISH = process.argv.includes('--publish');
const THROTTLE_MS = Number(process.env.REG_THROTTLE_MS || 600); // pace writes; token is short-lived

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let reg = { categories: {}, videos: {} }; // title->catId ; bunnyVideoId->{videoId, step}
async function loadReg() { try { reg = JSON.parse(await fsp.readFile(REG_STATE_PATH, 'utf8')); reg.categories ||= {}; reg.videos ||= {}; } catch {} }
async function saveReg() { await fsp.writeFile(REG_STATE_PATH, JSON.stringify(reg, null, 2)); }

async function api(method, urlPath, body, { write = true } = {}) {
  if (DRY && write) { console.log(`  [dry] ${method} ${urlPath} ${body ? JSON.stringify(body).slice(0, 120) : ''}`); return { __dry: true }; }
  let res, text;
  for (let attempt = 1; attempt <= 5; attempt++) {
    res = await fetch(`${BASE}${urlPath}`, {
      method,
      headers: { Authorization: `Bearer ${TOKEN}`, Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
    text = await res.text();
    // Back off on rate-limit / transient upstream errors and retry.
    if (res.status === 429 || res.status === 502 || res.status === 503 || res.status === 504) {
      const wait = Math.min(15000, 1000 * 2 ** (attempt - 1));
      console.warn(`  ~ ${res.status} on ${method} ${urlPath}; retry ${attempt}/5 in ${wait}ms`);
      await sleep(wait);
      continue;
    }
    break;
  }
  if (write) await sleep(THROTTLE_MS);
  let json = null; try { json = text ? JSON.parse(text) : null; } catch {}
  return { ok: res.ok, status: res.status, json, text };
}

async function ensureCategories(plan) {
  console.log(`\n== Categories (${plan.categories.length}) ==`);
  const existing = await api('GET', '/categories', null, { write: false });
  const byTitle = {};
  if (existing.ok && Array.isArray(existing.json)) for (const c of existing.json) byTitle[c.title] = c.id;
  for (const cat of plan.categories) {
    if (reg.categories[cat.title]) continue;
    if (byTitle[cat.title]) { reg.categories[cat.title] = byTitle[cat.title]; await saveReg(); continue; }
    const r = await api('POST', '/categories', { title: cat.title, description: null });
    if (r.__dry) { reg.categories[cat.title] = `dry_${cat.sortIndex}`; continue; }
    if (r.ok && r.json?.id) { reg.categories[cat.title] = r.json.id; await saveReg(); console.log(`  + ${cat.title}`); }
    else console.error(`  ✗ category "${cat.title}": ${r.status} ${r.text?.slice(0, 160)}`);
  }
}

async function importAndPatch(plan) {
  const ready = plan.videos.filter((v) => v.bunnyVideoId && v.uploadStatus === 'done');
  const skipped = plan.videos.length - ready.length;
  console.log(`\n== Import + patch (${ready.length} videos${skipped ? `, ${skipped} skipped: no guid / not done` : ''}) ==`);
  let done = 0;
  for (const v of ready) {
    const st = reg.videos[v.bunnyVideoId] || {};
    let videoId = st.videoId;
    try {
      if (!videoId) {
        const imp = await api('POST', `/collections/videos/${v.bunnyVideoId}/import`, { title: v.title, collectionId: v.collectionGuid });
        if (imp.__dry) { videoId = `dry_${done}`; }
        else if (imp.ok && imp.json?.videoId) { videoId = imp.json.videoId; }
        else if (imp.status === 409) { console.warn(`  ! ${v.title}: already imported (409) — resolve manually`); reg.videos[v.bunnyVideoId] = { ...st, step: 'already_imported_409' }; await saveReg(); continue; }
        else { console.error(`  ✗ import ${v.title}: ${imp.status} ${imp.text?.slice(0, 160)}`); continue; }
        reg.videos[v.bunnyVideoId] = { videoId, step: 'imported' }; await saveReg();
      }
      const catId = reg.categories[v.categoryTitle];
      const patch = {
        subtestCode: v.subtestCode,
        accessTier: v.accessTier,
        targetProfessionIds: v.targetProfessionIds,
        categoryIds: catId ? [catId] : [],
        tagsCsv: v.tagsCsv,
      };
      const pr = await api('PATCH', `/videos/${videoId}`, patch);
      if (pr.__dry || pr.ok) { reg.videos[v.bunnyVideoId] = { videoId, step: 'patched' }; await saveReg(); done++; if (!DRY) console.log(`  ✓ ${v.title}  (${done}/${ready.length})`); }
      else console.error(`  ✗ patch ${v.title}: ${pr.status} ${pr.text?.slice(0, 160)}`);
    } catch (e) { console.error(`  ✗ ${v.title}: ${e.message}`); }
  }
  return ready;
}

async function publishAll() {
  const ids = Object.values(reg.videos).filter((v) => v.step === 'patched' && v.videoId && !String(v.videoId).startsWith('dry_')).map((v) => v.videoId);
  if (!ids.length) { console.log('\n(no patched videos to publish)'); return; }
  console.log(`\n== Publish (${ids.length}) via bulk-lifecycle ==`);
  for (let i = 0; i < ids.length; i += 25) {
    const chunk = ids.slice(i, i + 25);
    const r = await api('POST', '/videos/bulk-lifecycle', { action: 'publish', videoIds: chunk });
    if (r.__dry) continue;
    if (r.ok) { console.log(`  batch ${i / 25 + 1}: ${JSON.stringify(r.json)}`); for (const id of chunk) { const e = Object.values(reg.videos).find((v) => v.videoId === id); if (e) e.step = 'publish_attempted'; } await saveReg(); }
    else console.error(`  ✗ publish batch: ${r.status} ${r.text?.slice(0, 200)}`);
  }
  console.log('  NOTE: videos whose Bunny encode is not Ready are skipped by the publish gate — re-run --publish after encoding completes.');
}

async function main() {
  if (!TOKEN && !DRY) { console.error('Set ADMIN_TOKEN (owner admin bearer token). Use --dry-run to preview without it.'); process.exit(1); }
  const plan = JSON.parse(await fsp.readFile(PLAN_PATH, 'utf8'));
  await loadReg();
  console.log(`API: ${API_BASE}  Mode: ${DRY ? 'DRY-RUN' : 'LIVE'}  Publish: ${DO_PUBLISH}`);
  await ensureCategories(plan);
  await importAndPatch(plan);
  if (DO_PUBLISH) await publishAll();
  console.log(`\nDone. Registration state: ${REG_STATE_PATH}`);
  console.log('Re-run safely (idempotent via registration-state.json). Add --publish once Bunny encoding is complete.');
}

main().catch((e) => { console.error('FATAL', e.message); process.exit(1); });
