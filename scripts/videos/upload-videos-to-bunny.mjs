#!/usr/bin/env node
// Bulk-upload OET lecture videos to Bunny Stream, mirroring the on-disk folder
// hierarchy as flat, path-named collections (Bunny Stream collections cannot nest).
//
// - Videos only (.mp4 / .mkv). Skips the Materials and "Basic English Course" trees.
// - Resumable: per-file state persisted to state.json; re-running skips completed files
//   and re-PUTs any half-finished uploads to the SAME Bunny video guid (no orphans).
// - Emits manifest.json (one row per video incl. parsed module/profession/language/
//   content-type + Bunny video/collection guids) for the downstream app-wiring step.
//
// Secrets are read from env only (never hard-coded / committed):
//   BUNNY_ACCOUNT_KEY   Bunny account (master) API key -> used to fetch the library key
//   BUNNY_STREAM_KEY    (optional) the OET Video Library key directly (skips the fetch)
//
// Usage:
//   node scripts/videos/upload-videos-to-bunny.mjs --dry-run     # plan only, no network writes
//   node scripts/videos/upload-videos-to-bunny.mjs               # real upload
//   node scripts/videos/upload-videos-to-bunny.mjs --plan-only   # alias for --dry-run

import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import https from 'node:https';

// ── Config ───────────────────────────────────────────────────────────────────
const LIBRARY_ID = process.env.BUNNY_LIBRARY_ID || '696416';
const ACCOUNT_KEY = process.env.BUNNY_ACCOUNT_KEY || '';
let STREAM_KEY = process.env.BUNNY_STREAM_KEY || '';

const SRC_ROOT =
  process.env.VIDEO_SRC_ROOT ||
  'D:\\Projects\\NEW OET WEB APP\\OET Materials & Videos Data';

const STATE_DIR = process.env.VIDEO_STATE_DIR || path.join(process.cwd(), 'scripts', 'videos', 'state');
const STATE_PATH = path.join(STATE_DIR, 'state.json');
const MANIFEST_PATH = path.join(STATE_DIR, 'manifest.json');

const CONCURRENCY = Number(process.env.VIDEO_CONCURRENCY || 3);
const MAX_ATTEMPTS = Number(process.env.VIDEO_MAX_ATTEMPTS || 5);
const IDLE_TIMEOUT_MS = Number(process.env.VIDEO_IDLE_TIMEOUT_MS || 120_000);

const VIDEO_EXTS = new Set(['.mp4', '.mkv']);
const EXCLUDE_RE = /(\\|\/)Materials(\\|\/)|Basic English Course/i;

const DRY_RUN = process.argv.includes('--dry-run') || process.argv.includes('--plan-only');

const API = 'https://video.bunnycdn.com';
const ACCOUNT_API = 'https://api.bunny.net';

// Classification vocab (case-insensitive, trailing _/space tolerant)
const PROFESSIONS = ['Medicine', 'Nursing', 'Pharmacy', 'Radiography', 'Dentistry'];
const LANGUAGES = ['Arabic', 'English'];
const CONTENT_TYPES = ['Sessions', 'Workshops'];

// ── Small utils ───────────────────────────────────────────────────────────────
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const cleanSeg = (s) => s.replace(/[\s_]+$/g, '').replace(/\s{2,}/g, ' ').trim();
const norm = (s) => cleanSeg(s).toLowerCase();

function classify(segments) {
  const cleaned = segments.map(cleanSeg);
  const findIn = (vocab) => {
    for (const seg of cleaned) {
      const hit = vocab.find((v) => v.toLowerCase() === seg.toLowerCase());
      if (hit) return hit;
    }
    return null;
  };
  return {
    module: cleaned[0] || null,
    profession: findIn(PROFESSIONS),
    language: findIn(LANGUAGES),
    contentType: findIn(CONTENT_TYPES),
    pathSegments: cleaned,
    collectionName: cleaned.join(' / '),
  };
}

async function walk(dir, out = []) {
  const entries = await fsp.readdir(dir, { withFileTypes: true });
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (EXCLUDE_RE.test(full + path.sep)) continue;
      await walk(full, out);
    } else if (e.isFile()) {
      if (EXCLUDE_RE.test(full)) continue;
      if (VIDEO_EXTS.has(path.extname(e.name).toLowerCase())) out.push(full);
    }
  }
  return out;
}

// ── State ─────────────────────────────────────────────────────────────────────
let state = { collections: {}, videos: {} };
let saveQueued = false;
async function loadState() {
  try {
    state = JSON.parse(await fsp.readFile(STATE_PATH, 'utf8'));
    state.collections ||= {};
    state.videos ||= {};
  } catch {
    /* fresh */
  }
}
async function saveState() {
  if (saveQueued) return;
  saveQueued = true;
  await sleep(0);
  saveQueued = false;
  await fsp.mkdir(STATE_DIR, { recursive: true });
  await fsp.writeFile(STATE_PATH, JSON.stringify(state, null, 2));
}

// ── Bunny API ────────────────────────────────────────────────────────────────
async function resolveStreamKey() {
  if (STREAM_KEY) return STREAM_KEY;
  if (!ACCOUNT_KEY) throw new Error('Set BUNNY_ACCOUNT_KEY (or BUNNY_STREAM_KEY) in the environment.');
  const res = await fetch(`${ACCOUNT_API}/videolibrary/${LIBRARY_ID}`, {
    headers: { AccessKey: ACCOUNT_KEY, Accept: 'application/json' },
  });
  if (!res.ok) throw new Error(`Failed to fetch library ${LIBRARY_ID}: HTTP ${res.status}`);
  const j = await res.json();
  STREAM_KEY = j.ApiKey;
  if (!STREAM_KEY) throw new Error('Library response had no ApiKey.');
  return STREAM_KEY;
}

async function bunny(method, urlPath, body) {
  const res = await fetch(`${API}${urlPath}`, {
    method,
    headers: {
      AccessKey: STREAM_KEY,
      Accept: 'application/json',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : {};
}

async function loadExistingCollections() {
  const map = {};
  let page = 1;
  for (;;) {
    const j = await bunny('GET', `/library/${LIBRARY_ID}/collections?page=${page}&itemsPerPage=100`);
    for (const it of j.items || []) map[it.name] = it.guid;
    const total = j.totalItems ?? (j.items ? j.items.length : 0);
    if (page * 100 >= total || !(j.items || []).length) break;
    page++;
  }
  return map;
}

async function ensureCollection(name) {
  if (state.collections[name]) return state.collections[name];
  const created = await bunny('POST', `/library/${LIBRARY_ID}/collections`, { name });
  if (!created.guid) throw new Error(`Collection create returned no guid for "${name}"`);
  state.collections[name] = created.guid;
  await saveState();
  return created.guid;
}

// Streamed PUT with explicit Content-Length + idle timeout, using node:https.
function putFile(urlPath, filePath, sizeBytes) {
  return new Promise((resolve, reject) => {
    const u = new URL(`${API}${urlPath}`);
    const req = https.request(
      {
        method: 'PUT',
        hostname: u.hostname,
        path: u.pathname + u.search,
        headers: { AccessKey: STREAM_KEY, 'Content-Type': 'application/octet-stream', 'Content-Length': sizeBytes },
      },
      (res) => {
        let body = '';
        res.on('data', (d) => (body += d));
        res.on('end', () => {
          if (res.statusCode >= 200 && res.statusCode < 300) resolve(body);
          else reject(new Error(`PUT ${urlPath} -> HTTP ${res.statusCode}: ${body.slice(0, 300)}`));
        });
      }
    );
    req.setTimeout(IDLE_TIMEOUT_MS, () => req.destroy(new Error(`idle timeout after ${IDLE_TIMEOUT_MS}ms`)));
    req.on('error', reject);
    const stream = fs.createReadStream(filePath);
    stream.on('error', reject);
    stream.pipe(req);
  });
}

async function withRetry(label, fn) {
  let lastErr;
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    try {
      return await fn();
    } catch (err) {
      lastErr = err;
      const backoff = Math.min(30_000, 1000 * 2 ** (attempt - 1));
      console.warn(`  ! ${label} attempt ${attempt}/${MAX_ATTEMPTS} failed: ${err.message}. Retrying in ${backoff}ms`);
      await sleep(backoff);
    }
  }
  throw lastErr;
}

// ── Plan build ────────────────────────────────────────────────────────────────
async function buildPlan() {
  const files = await walk(SRC_ROOT);
  files.sort((a, b) => a.localeCompare(b));
  const rows = [];
  for (const full of files) {
    const rel = full.substring(SRC_ROOT.length).replace(/^[\\/]+/, '');
    const parts = rel.split(/[\\/]+/);
    const fileName = parts[parts.length - 1];
    const folderSegs = parts.slice(0, -1);
    const meta = classify(folderSegs);
    const stat = await fsp.stat(full);
    rows.push({
      relPath: rel,
      fullPath: full,
      fileName,
      title: fileName.replace(/\.[^.]+$/, ''),
      sizeBytes: stat.size,
      ...meta,
    });
  }
  if (process.env.VIDEO_SORT === 'size') rows.sort((a, b) => a.sizeBytes - b.sizeBytes);
  return rows;
}

function summarize(rows) {
  const byCollection = new Map();
  for (const r of rows) {
    if (!byCollection.has(r.collectionName)) byCollection.set(r.collectionName, { count: 0, bytes: 0 });
    const c = byCollection.get(r.collectionName);
    c.count++;
    c.bytes += r.sizeBytes;
  }
  const totalBytes = rows.reduce((s, r) => s + r.sizeBytes, 0);
  return { byCollection, totalBytes };
}

// ── Main ──────────────────────────────────────────────────────────────────────
async function main() {
  console.log(`\n== OET video -> Bunny Stream uploader ==`);
  console.log(`Source : ${SRC_ROOT}`);
  console.log(`Library: ${LIBRARY_ID}`);
  console.log(`Mode   : ${DRY_RUN ? 'DRY-RUN (no network writes)' : 'LIVE UPLOAD'}\n`);

  await loadState();
  let rows = await buildPlan();
  const LIMIT = Number(process.env.VIDEO_LIMIT || 0);
  if (LIMIT > 0) {
    rows = rows.slice(0, LIMIT);
    console.log(`(VIDEO_LIMIT=${LIMIT} -> restricting to first ${rows.length} file(s))\n`);
  }
  const { byCollection, totalBytes } = summarize(rows);

  console.log(`Planned videos    : ${rows.length}`);
  console.log(`Planned collections: ${byCollection.size}`);
  console.log(`Total size        : ${(totalBytes / 1024 ** 3).toFixed(2)} GB\n`);
  console.log(`Collections (name  ->  #videos, GB):`);
  for (const [name, c] of [...byCollection.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    console.log(`  ${name}  ->  ${c.count}, ${(c.bytes / 1024 ** 3).toFixed(2)} GB`);
  }

  if (DRY_RUN) {
    await fsp.mkdir(STATE_DIR, { recursive: true });
    await fsp.writeFile(MANIFEST_PATH, JSON.stringify(rows.map(({ fullPath, ...r }) => r), null, 2));
    console.log(`\nDry-run complete. Plan written to ${MANIFEST_PATH}`);
    return;
  }

  await resolveStreamKey();
  console.log('Resolved library key. Syncing existing collections...');
  const existing = await loadExistingCollections();
  state.collections = { ...existing, ...state.collections };
  await saveState();

  // Pre-create all collections (cheap, deterministic order).
  for (const name of [...byCollection.keys()].sort()) {
    await withRetry(`ensureCollection ${name}`, () => ensureCollection(name));
  }
  console.log(`Collections ready (${Object.keys(state.collections).length} known).\n`);

  let done = 0;
  let failed = 0;
  const queue = [...rows];
  let idx = 0;

  async function worker(wid) {
    while (idx < queue.length) {
      const r = queue[idx++];
      const st = state.videos[r.relPath];
      if (st && st.status === 'done' && st.bunnyVideoId) {
        done++;
        continue;
      }
      const collectionId = state.collections[r.collectionName];
      const sizeMb = (r.sizeBytes / 1024 ** 2).toFixed(0);
      try {
        // Reuse an already-created (but not-yet-uploaded) video guid to avoid orphans.
        let guid = st?.bunnyVideoId;
        if (!guid) {
          const created = await withRetry(`create "${r.title}"`, () =>
            bunny('POST', `/library/${LIBRARY_ID}/videos`, { title: r.title, collectionId })
          );
          guid = created.guid;
          state.videos[r.relPath] = { ...r, fullPath: undefined, bunnyVideoId: guid, collectionGuid: collectionId, status: 'created', updatedAt: new Date().toISOString() };
          delete state.videos[r.relPath].fullPath;
          await saveState();
        }
        console.log(`[w${wid}] ↑ ${r.relPath}  (${sizeMb} MB)`);
        await withRetry(`upload "${r.title}"`, () => putFile(`/library/${LIBRARY_ID}/videos/${guid}`, r.fullPath, r.sizeBytes));
        state.videos[r.relPath].status = 'done';
        state.videos[r.relPath].updatedAt = new Date().toISOString();
        await saveState();
        done++;
        console.log(`[w${wid}] ✓ ${r.title}  (${done}/${queue.length})`);
      } catch (err) {
        failed++;
        state.videos[r.relPath] = { ...(state.videos[r.relPath] || {}), ...r, fullPath: undefined, status: 'failed', error: String(err.message).slice(0, 300), updatedAt: new Date().toISOString() };
        delete state.videos[r.relPath].fullPath;
        await saveState();
        console.error(`[w${wid}] ✗ ${r.relPath}: ${err.message}`);
      }
    }
  }

  await Promise.all(Array.from({ length: CONCURRENCY }, (_, i) => worker(i + 1)));

  // Write final manifest (all videos with guids) for the app-wiring step.
  const manifest = rows.map((r) => {
    const st = state.videos[r.relPath] || {};
    const { fullPath, ...meta } = r;
    return { ...meta, bunnyVideoId: st.bunnyVideoId || null, collectionGuid: st.collectionGuid || state.collections[r.collectionName] || null, status: st.status || 'pending' };
  });
  await fsp.writeFile(MANIFEST_PATH, JSON.stringify(manifest, null, 2));

  console.log(`\n== Done. uploaded=${done} failed=${failed} total=${queue.length} ==`);
  console.log(`Manifest: ${MANIFEST_PATH}`);
  if (failed) {
    console.log(`Re-run the same command to retry the ${failed} failed file(s).`);
    process.exitCode = 1;
  }
}

main().catch((err) => {
  console.error('\nFATAL:', err.message);
  process.exit(1);
});
