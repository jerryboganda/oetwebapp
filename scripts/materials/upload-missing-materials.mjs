#!/usr/bin/env node
// ─────────────────────────────────────────────────────────────────────────────
// Hash-safe Materials uploader — one command, cannot duplicate, cannot split.
//
// Uploads ONLY the on-disk files whose exact bytes (SHA256) are not already in
// production, into the folders they belong in. It is impossible for this script
// to create a duplicate of content that already exists, because every file is
// gated THREE ways before a MaterialFile row is created:
//
//   1. PRE-GATE (content):  the file's SHA256 must be absent from the prod
//      manifest (scripts/materials/prod-manifest.txt, or --manifest). Files
//      already in prod are skipped WITHOUT re-uploading their bytes.
//   2. SERVER DEDUP:        the upload /complete step returns the canonical
//      MediaAsset for the uploaded bytes; identical content reuses one asset.
//   3. LIVE ROW CHECK:      before attaching, the target folder's existing files
//      are listed live; if one already references that asset, no row is created.
//      This catches a stale manifest — staleness can waste an upload, never
//      duplicate a row.
//
// Folders are resolved from the LIVE prod tree (GET), never blindly re-created,
// so it cannot fork the hierarchy. Files upload WHOLE: the 8 MB parts are a
// transport detail the server reassembles into ONE asset — nothing is split or
// re-encoded.
//
// DRY-RUN BY DEFAULT. It prints exactly which files it will upload; pass --apply
// to write. If the plan shows more than you expect, STOP — your manifest is stale
// (regenerate it, see verify-materials-parity.mjs header) or the disk changed.
//
// ── Auth (env only, never hard-coded — same as ingest-materials.mjs) ──────────
//   OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD    preferred: sign-in + auto-refresh
//   OET_ADMIN_REFRESH_TOKEN                 MFA admins: refresh token from a
//                                           signed-in browser session
//   OET_ADMIN_TOKEN                         raw 15-min access token (short jobs)
//   (needs the AdminContentWrite permission)
//
// ── Env ──────────────────────────────────────────────────────────────────────
//   OET_API_BASE               default https://api.oetwithdrhesham.co.uk
//   MATERIALS_SRC_ROOT         default "<repo>/OET Materials & Videos Data/Materials"
//   MATERIALS_WRITES_PER_MIN   default 30
//   MATERIALS_PUBLISH          default 1 (Draft→Published); 0 leaves it invisible
//
// ── Usage ────────────────────────────────────────────────────────────────────
//   node scripts/materials/upload-missing-materials.mjs                 # dry-run plan
//   OET_ADMIN_EMAIL=… OET_ADMIN_PASSWORD=… \
//     node scripts/materials/upload-missing-materials.mjs --apply       # upload
//   node scripts/materials/upload-missing-materials.mjs --manifest /tmp/live.txt
//
// After a run, prove parity: node scripts/materials/verify-materials-parity.mjs
// ─────────────────────────────────────────────────────────────────────────────

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import fsp from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');

const argv = process.argv.slice(2);
const APPLY = argv.includes('--apply');
const DRY_RUN = !APPLY;
const manifestArg = (() => {
  const i = argv.indexOf('--manifest');
  return i >= 0 && argv[i + 1] ? argv[i + 1] : null;
})();

const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/$/, '');
const SRC_ROOT = process.env.MATERIALS_SRC_ROOT
  || path.join(REPO_ROOT, 'OET Materials & Videos Data', 'Materials');
const MANIFEST = path.resolve(manifestArg || path.join(HERE, 'prod-manifest.txt'));
const WRITES_PER_MIN = Number(process.env.MATERIALS_WRITES_PER_MIN || 30);
const MAX_ATTEMPTS = Number(process.env.MATERIALS_MAX_ATTEMPTS || 5);
const PUBLISH = process.env.MATERIALS_PUBLISH !== '0';
const DEFAULT_SUBTEST = (process.env.MATERIALS_DEFAULT_SUBTEST || 'reading').toLowerCase();

const SUBTESTS = new Set(['reading', 'listening', 'speaking', 'writing']);
const FORMAT_MIME = {
  pdf: 'application/pdf', doc: 'application/msword',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  txt: 'text/plain', csv: 'text/csv', rtf: 'application/rtf',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  ppt: 'application/vnd.ms-powerpoint',
  pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
  mp3: 'audio/mpeg', m4a: 'audio/mp4', wav: 'audio/wav', ogg: 'audio/ogg',
  mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
};
const AUDIO_EXTS = new Set(['mp3', 'm4a', 'wav', 'ogg']);
const MAX_AUDIO_BYTES = 350 * 1024 * 1024;
const MAX_OTHER_BYTES = 250 * 1024 * 1024;
const MAX_FOLDER_LEVEL = 7;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const mb = (b) => (b / 1024 ** 2).toFixed(1);
const extOf = (name) => path.extname(name).replace(/^\./, '').toLowerCase();

// ── Rate limiter (rolling 60s window over every mutating request) ─────────────
const writeTimes = [];
async function throttleWrite() {
  for (;;) {
    const now = Date.now();
    while (writeTimes.length && now - writeTimes[0] >= 60_000) writeTimes.shift();
    if (writeTimes.length < WRITES_PER_MIN) { writeTimes.push(now); return; }
    await sleep(60_000 - (now - writeTimes[0]) + 50);
  }
}

// ── Auth (mirror of ingest-materials.mjs) ─────────────────────────────────────
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
  if (!res.ok) throw new Error(`POST ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 300)}`);
  return JSON.parse(text);
}
async function signIn() {
  const email = process.env.OET_ADMIN_EMAIL || '';
  const password = process.env.OET_ADMIN_PASSWORD || '';
  if (email && password) {
    applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true }));
    return;
  }
  if (refreshToken) { applySession(await authPost('/v1/auth/refresh', { refreshToken })); return; }
  if (accessToken) return;
  throw new Error('No admin credentials. Set OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD, or OET_ADMIN_REFRESH_TOKEN, or OET_ADMIN_TOKEN.');
}
async function ensureFreshToken() {
  if (!accessTokenExpiresAt || Date.now() < accessTokenExpiresAt - 60_000) return;
  if (!refreshToken) {
    if (process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD) return signIn();
    return;
  }
  applySession(await authPost('/v1/auth/refresh', { refreshToken }));
}

// ── API ───────────────────────────────────────────────────────────────────────
class HttpError extends Error {
  constructor(status, code, message) { super(message); this.status = status; this.code = code; }
}
async function api(method, urlPath, { json, body, isWrite = true, retryOn401 = true } = {}) {
  await ensureFreshToken();
  if (isWrite) await throttleWrite();
  const headers = { Accept: 'application/json', Authorization: `Bearer ${accessToken}` };
  if (json !== undefined) headers['Content-Type'] = 'application/json';
  else if (body !== undefined) headers['Content-Type'] = 'application/octet-stream';
  const res = await fetch(`${API_BASE}${urlPath}`, {
    method, headers, body: json !== undefined ? JSON.stringify(json) : body,
  });
  const text = await res.text();
  if (res.status === 401 && retryOn401 && (refreshToken || process.env.OET_ADMIN_EMAIL)) {
    accessTokenExpiresAt = 0;
    await ensureFreshToken();
    return api(method, urlPath, { json, body, isWrite, retryOn401: false });
  }
  if (!res.ok) {
    let code;
    try { code = JSON.parse(text)?.code; } catch { /* non-JSON */ }
    throw new HttpError(res.status, code, `${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 300)}`);
  }
  return text ? JSON.parse(text) : {};
}
async function withRetry(label, fn) {
  let lastErr;
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    try { return await fn(); }
    catch (err) {
      if (err instanceof HttpError && err.status >= 400 && err.status < 500 && err.status !== 429) throw err;
      lastErr = err;
      const backoff = Math.min(30_000, 1000 * 2 ** (attempt - 1));
      console.warn(`  ! ${label} attempt ${attempt}/${MAX_ATTEMPTS} failed: ${err.message}. Retry in ${backoff}ms`);
      await sleep(backoff);
    }
  }
  throw lastErr;
}

// ── Disk + manifest ────────────────────────────────────────────────────────────
function sha256File(file) {
  return new Promise((resolve, reject) => {
    const h = createHash('sha256');
    createReadStream(file).on('error', reject).on('data', (d) => h.update(d)).on('end', () => resolve(h.digest('hex')));
  });
}
async function walk(dir, out = []) {
  for (const e of (await fsp.readdir(dir, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) await walk(full, out);
    else if (e.isFile()) out.push(full);
  }
  return out;
}
const relOf = (full) => full.substring(SRC_ROOT.length).replace(/^[\\/]+/, '').split(path.sep).join('/');
function subtestFor(segments) {
  const top = (segments[0] || '').trim().toLowerCase();
  return SUBTESTS.has(top) ? top : null;
}
async function loadManifestShas() {
  const text = await fsp.readFile(MANIFEST, 'utf8').catch(() => {
    console.warn(`! Manifest not found (${MANIFEST}); relying on live server dedup only.`);
    return '';
  });
  const set = new Set();
  for (const line of text.split('\n')) {
    const sha = line.split('|')[0]?.trim().toLowerCase();
    if (sha && sha.length === 64) set.add(sha);
  }
  return set;
}

// ── Live folder tree → path→id map ────────────────────────────────────────────
function indexFolders(list, map = new Map(), byId = new Map()) {
  for (const f of list) {
    byId.set(f.id, f);
    if (Array.isArray(f.folders) && f.folders.length) indexFolders(f.folders, map, byId);
  }
  return { byId };
}
function fullPathOf(id, byId) {
  const segs = [];
  let cur = byId.get(id);
  const guard = new Set();
  while (cur && !guard.has(cur.id)) {
    guard.add(cur.id);
    segs.unshift(cur.name);
    cur = cur.parentFolderId ? byId.get(cur.parentFolderId) : null;
  }
  return segs.join('/');
}
async function loadLiveFolderMap() {
  const list = await api('GET', '/v1/admin/materials/folders', { isWrite: false });
  const { byId } = indexFolders(list);
  const pathToId = new Map();
  for (const id of byId.keys()) pathToId.set(fullPathOf(id, byId), id);
  return { pathToId, byId };
}
// Ordered create of any missing ancestor folders (only when genuinely absent).
async function resolveFolderId(folderRel, pathToId) {
  if (folderRel == null) return null;
  if (pathToId.has(folderRel)) return pathToId.get(folderRel);
  const segments = folderRel.split('/');
  let parentId = null;
  let acc = '';
  for (let i = 0; i < segments.length; i++) {
    acc = i === 0 ? segments[0] : `${acc}/${segments[i]}`;
    if (pathToId.has(acc)) { parentId = pathToId.get(acc); continue; }
    const created = await withRetry(`create folder ${acc}`, () =>
      api('POST', '/v1/admin/materials/folders', {
        json: {
          parentFolderId: parentId,
          name: segments[i],
          subtestCode: i === 0 ? subtestFor(segments) : null,
          sortOrder: 0,
        },
      }));
    if (PUBLISH) {
      await withRetry(`publish folder ${acc}`, () =>
        api('PUT', `/v1/admin/materials/folders/${created.id}`, { json: { status: 'Published' } }));
    }
    pathToId.set(acc, created.id);
    parentId = created.id;
  }
  return pathToId.get(folderRel);
}

// Live row-dedup: does a file in this folder already reference this asset?
async function folderHasAsset(folderId, mediaAssetId) {
  const page = await api('GET',
    `/v1/admin/materials/files?folderId=${encodeURIComponent(folderId ?? '')}&pageSize=500`,
    { isWrite: false });
  const items = Array.isArray(page?.items) ? page.items : [];
  return items.some((f) => f.mediaAssetId === mediaAssetId);
}

async function uploadWhole(row) {
  const started = await withRetry(`start upload ${row.relPath}`, () =>
    api('POST', '/v1/admin/uploads', {
      json: {
        originalFilename: row.fileName,
        declaredMimeType: row.mimeType,
        declaredSizeBytes: row.sizeBytes,
        intendedRole: row.role,
      },
    }));
  const chunk = started.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(row.sizeBytes / chunk));
  const handle = await fsp.open(row.fullPath, 'r');
  try {
    for (let part = 1; part <= totalParts; part++) {
      const offset = (part - 1) * chunk;
      const size = Math.min(chunk, row.sizeBytes - offset);
      const buf = Buffer.allocUnsafe(size);
      await handle.read(buf, 0, size, offset);
      await withRetry(`part ${part}/${totalParts} ${row.relPath}`, () =>
        api('PUT', `/v1/admin/uploads/${started.uploadId}/parts/${part}`, { body: buf }));
      process.stdout.write(`\r    uploading ${row.relPath} — part ${part}/${totalParts}   `);
    }
  } finally {
    await handle.close();
  }
  process.stdout.write('\n');
  const done = await withRetry(`complete upload ${row.relPath}`, () =>
    api('POST', `/v1/admin/uploads/${started.uploadId}/complete`, { json: {} }));
  return done; // { mediaAssetId, sha256, deduplicated }
}

async function main() {
  console.log('\n== Hash-safe Materials uploader ==');
  console.log(`Source : ${SRC_ROOT}`);
  console.log(`API    : ${API_BASE}`);
  console.log(`Mode   : ${DRY_RUN ? 'DRY-RUN (no writes — pass --apply)' : 'LIVE UPLOAD'}\n`);

  const prodShas = await loadManifestShas();
  const files = await walk(SRC_ROOT);
  console.log(`Hashing ${files.length} disk files against ${prodShas.size} known prod hashes…`);

  // Hash concurrently (bounded) — the on-disk tree is multi-GB on a slow volume.
  const CONCURRENCY = Math.max(2, Math.min(8, (os.cpus()?.length ?? 4) - 1));
  const shaByFile = new Array(files.length);
  {
    let next = 0;
    const worker = async () => {
      while (next < files.length) {
        const i = next++;
        shaByFile[i] = (await sha256File(files[i])).toLowerCase();
      }
    };
    await Promise.all(Array.from({ length: CONCURRENCY }, worker));
  }

  const plan = [];
  for (let idx = 0; idx < files.length; idx++) {
    const full = files[idx];
    const rel = relOf(full);
    const sha = shaByFile[idx];
    if (prodShas.has(sha)) continue; // content already in prod — never re-upload
    const segments = rel.split('/');
    const fileName = segments[segments.length - 1];
    const folderSegs = segments.slice(0, -1);
    const st = await fsp.stat(full);
    const ext = extOf(fileName);
    const isAudio = AUDIO_EXTS.has(ext);
    const issues = [];
    if (!FORMAT_MIME[ext]) issues.push(`unsupported_format:${ext || '(none)'}`);
    if (st.size > (isAudio ? MAX_AUDIO_BYTES : MAX_OTHER_BYTES)) issues.push(`too_large:${mb(st.size)}MB`);
    if (st.size === 0) issues.push('empty_file');
    if (folderSegs.length > MAX_FOLDER_LEVEL) issues.push(`folder_too_deep:${folderSegs.length}`);
    plan.push({
      relPath: rel, fullPath: full, fileName, sha,
      title: fileName.replace(/\.[^.]+$/, ''),
      folderRel: folderSegs.length ? folderSegs.join('/') : null,
      sizeBytes: st.size, mimeType: FORMAT_MIME[ext] || 'application/octet-stream',
      role: isAudio ? 'Audio' : 'Supplementary',
      subtestCode: subtestFor(folderSegs) || DEFAULT_SUBTEST,
      issues,
    });
  }

  const ok = plan.filter((p) => !p.issues.length);
  const bad = plan.filter((p) => p.issues.length);
  console.log(`\nAlready in prod (skipped) : ${files.length - plan.length}`);
  console.log(`To upload                 : ${ok.length}`);
  if (bad.length) {
    console.log(`REJECTED up front         : ${bad.length}`);
    for (const b of bad) console.log(`   ✖ ${b.relPath}  [${b.issues.join(', ')}]`);
  }
  for (const p of ok) console.log(`   ↑ ${p.relPath}  (${mb(p.sizeBytes)} MB → ${p.folderRel || '(root)'})`);

  if (ok.length === 0) { console.log('\nNothing to upload — prod already holds every disk file. ✓'); return; }
  if (DRY_RUN) {
    console.log('\nDRY-RUN — re-run with --apply to upload the files listed above.');
    console.log('(If that list is larger than you expect, your manifest is stale — regenerate it first.)');
    return;
  }

  await signIn();
  const { pathToId } = await loadLiveFolderMap();
  console.log(`\nResolved ${pathToId.size} live prod folders. Uploading…\n`);

  let uploaded = 0, skipped = 0;
  for (const row of ok) {
    const folderId = await resolveFolderId(row.folderRel, pathToId);
    const done = await uploadWhole(row);
    // Live safety net: if the (possibly stale) manifest missed this content but a
    // row already references the asset here, do not create a second one.
    if (done.deduplicated && (await folderHasAsset(folderId, done.mediaAssetId))) {
      console.log(`  = ${row.relPath} — already attached in prod (asset ${done.mediaAssetId}); no row created.`);
      skipped++;
      continue;
    }
    const created = await withRetry(`create material file ${row.relPath}`, () =>
      api('POST', '/v1/admin/materials/files', {
        json: {
          folderId,
          mediaAssetId: done.mediaAssetId,
          subtestCode: row.subtestCode,
          title: row.title.slice(0, 200),
          sortOrder: 0,
        },
      }));
    if (PUBLISH) {
      await withRetry(`publish file ${row.relPath}`, () =>
        api('PUT', `/v1/admin/materials/files/${created.id}`, { json: { status: 'Published' } }));
    }
    console.log(`  ✓ ${row.relPath} → ${created.id}${done.deduplicated ? ' (asset dedup)' : ''}`);
    uploaded++;
  }

  console.log(`\nDone. Uploaded ${uploaded}, skipped ${skipped} (already attached).`);
  console.log('Verify parity:  node scripts/materials/verify-materials-parity.mjs');
}

main().catch((err) => { console.error('\n✖ uploader failed:', err.message); process.exit(1); });
