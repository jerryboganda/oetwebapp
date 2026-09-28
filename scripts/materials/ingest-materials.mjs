#!/usr/bin/env node
// Ingest the on-disk OET Materials tree into the app's Materials library, mirroring
// the folder hierarchy into MaterialFolder rows and uploading every file through the
// admin chunked-upload pipeline.
//
// - DRY-RUN BY DEFAULT. Nothing is written without --apply. The owner runs this
//   against PRODUCTION, so the safe mode is the default mode.
// - Resumable: per-folder and per-file state persisted to state.json; re-running
//   skips completed work and resumes a half-finished upload on the SAME uploadId
//   (no orphaned sessions, no duplicate MaterialFiles).
// - Rate limited to MATERIALS_WRITES_PER_MIN writes/minute (default 30) across a
//   rolling 60s window. Every mutating request counts, including each upload part.
// - Emits manifest.json (one row per file incl. subtest/discipline/folder id/media
//   asset id) so the ingest can be audited after the fact.
//
// ── How content ends up gated ────────────────────────────────────────────────
// The tree is subtest-first: <subtest>/<discipline>/... — e.g. Writing/Medicine/...
// Reading and Listening carry no discipline level, which matches
// SubtestReference.SupportsProfessionSpecificContent (true for writing/speaking only).
//   * Subtest — set as SubtestCode on the LEVEL-1 folder only. Descendants inherit it
//     via MaterialAccessService.ResolveEffectiveSubtest, which walks up to the nearest
//     tagged ancestor. Files carry their own SubtestCode (the API requires one).
//   * Discipline — matched by folder NAME, not a column. MaterialFolder has no
//     profession field; MaterialAccessService.IsDisciplineVisible compares each folder
//     name (and its ancestors') against Professions.Id ∪ Professions.Label. The on-disk
//     names (Medicine, Nursing, Pharmacy, Physiotherapy, Dentistry, Radiography) already
//     match the seeded ProfessionReference.Label verbatim, so mirroring the tree
//     UNCHANGED is what makes the gate work. Do not "tidy" these folder names.
//
// ── Auth (choose ONE; env only, never hard-coded) ────────────────────────────
//   OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD   sign-in, then auto-refresh. Preferred.
//   OET_ADMIN_REFRESH_TOKEN                for MFA-protected admins: copy the refresh
//                                          token from a signed-in browser session.
//                                          Auto-refreshes the same way.
//   OET_ADMIN_TOKEN                        a raw access token. NO auto-refresh — prod
//                                          access tokens live 15 MINUTES and this run
//                                          takes hours, so this is for smoke tests only.
// The admin needs the AdminContentWrite permission. There is no step-up/MFA header on
// these endpoints (AdminRouteBuilderExtensions.WithAdminWrite = rate limit + permission).
//
// ── Env vars ─────────────────────────────────────────────────────────────────
//   OET_API_BASE               default https://api.oetwithdrhesham.co.uk
//   MATERIALS_SRC_ROOT         default "<repo>/OET Materials & Videos Data/Materials"
//   MATERIALS_STATE_DIR        default "<cwd>/scripts/materials/state"
//   MATERIALS_WRITES_PER_MIN   default 30
//   MATERIALS_DEFAULT_SUBTEST  default "reading" — fallback for files that sit above any
//                              subtest folder (the API rejects a file with no subtest)
//   MATERIALS_PUBLISH          default 1; set 0 to leave everything Draft (invisible)
//   MATERIALS_LIMIT            default 0 (all); cap the file count for a canary run
//   MATERIALS_MAX_ATTEMPTS     default 5
//
// ── Usage ────────────────────────────────────────────────────────────────────
//   node scripts/materials/ingest-materials.mjs                  # dry-run: plan only
//   MATERIALS_LIMIT=3 node scripts/materials/ingest-materials.mjs --apply   # canary
//   node scripts/materials/ingest-materials.mjs --apply          # full ingest
//
// ── Resuming ─────────────────────────────────────────────────────────────────
// Just re-run the SAME command. state.json records every folder id, upload session,
// media asset id and material file id keyed by relative path; completed work is skipped,
// a partially uploaded file resumes at the first part the server has not confirmed, and
// a part the server already holds (409 upload_part_duplicate) is treated as received.
// To start over from scratch, delete the state dir — but note that re-uploading dedupes
// server-side on SHA-256, so you would create DUPLICATE MaterialFile rows pointing at the
// same MediaAsset. Prefer resuming.

import fsp from 'node:fs/promises';
import path from 'node:path';

// ── Config ───────────────────────────────────────────────────────────────────
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/+$/, '');

const SRC_ROOT =
  process.env.MATERIALS_SRC_ROOT ||
  'D:\\Projects\\NEW OET WEB APP\\OET Materials & Videos Data\\Materials';

const STATE_DIR = process.env.MATERIALS_STATE_DIR || path.join(process.cwd(), 'scripts', 'materials', 'state');
const STATE_PATH = path.join(STATE_DIR, 'state.json');
const MANIFEST_PATH = path.join(STATE_DIR, 'manifest.json');

const WRITES_PER_MIN = Number(process.env.MATERIALS_WRITES_PER_MIN || 30);
const MAX_ATTEMPTS = Number(process.env.MATERIALS_MAX_ATTEMPTS || 5);
const LIMIT = Number(process.env.MATERIALS_LIMIT || 0);
const PUBLISH = process.env.MATERIALS_PUBLISH !== '0';
const DEFAULT_SUBTEST = (process.env.MATERIALS_DEFAULT_SUBTEST || 'reading').toLowerCase();

const APPLY = process.argv.includes('--apply');
const DRY_RUN = !APPLY;

// Subtest folders — the level-1 names. Anything else at level 1 is untagged content
// and falls back to DEFAULT_SUBTEST (reported explicitly in the plan).
const SUBTESTS = new Set(['reading', 'listening', 'speaking', 'writing']);

// Discipline folder names, verbatim from the seeded ProfessionReference.Label
// (SeedData.cs SeedReferenceData). Informational only — the server matches by name;
// the script just reports the mapping so the owner can eyeball it before applying.
const DISCIPLINE_LABELS = new Set([
  'Nursing', 'Medicine', 'Dentistry', 'Pharmacy', 'Physiotherapy', 'Radiography',
  'Other Allied health profession', 'Academic / General English',
]);

// Accepted formats — MUST mirror MaterialsAdminEndpoints.FormatKinds. An extension
// missing here is rejected by POST /materials/files with material_file_type_invalid
// AFTER the bytes are already uploaded, so the plan rejects it up front instead.
const FORMAT_MIME = {
  pdf: 'application/pdf',
  doc: 'application/msword',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  txt: 'text/plain',
  csv: 'text/csv',
  rtf: 'application/rtf',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  ppt: 'application/vnd.ms-powerpoint',
  pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
  mp3: 'audio/mpeg', m4a: 'audio/mp4', wav: 'audio/wav', ogg: 'audio/ogg',
  mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
};
const AUDIO_EXTS = new Set(['mp3', 'm4a', 'wav', 'ogg']);

// Server-side caps (ChunkedUploadService.ResolveSizeLimitForRole → ContentUploadOptions).
// The role picks the size cap and nothing else — MediaAsset.MediaKind is derived from the
// mime/extension independently. "Audio" buys 350 MB, everything else 250 MB.
const ROLE_AUDIO = 'Audio';
const ROLE_DEFAULT = 'Supplementary';
const MAX_AUDIO_BYTES = 350 * 1024 * 1024;
const MAX_OTHER_BYTES = 250 * 1024 * 1024;

// MaterialsAdminEndpoints.MaxFolderDepth is 8, but it is compared against
// GetFolderDepthAsync(parent), which returns parent_level + 1 — so a create is rejected
// once the PARENT sits at level 7, capping a real folder at level 7. Level 1 = top level.
const MAX_FOLDER_LEVEL = 7;

// ── Small utils ──────────────────────────────────────────────────────────────
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const mb = (bytes) => (bytes / 1024 ** 2).toFixed(1);
const extOf = (name) => path.extname(name).replace(/^\./, '').toLowerCase();

// ── Rate limiter ─────────────────────────────────────────────────────────────
// Rolling 60s window over every mutating request. Production's PerUserWrite limiter is
// currently far higher (Program.cs), but this stays conservative by default: a throttled
// run is recoverable, a hammered production API is not. Raise via MATERIALS_WRITES_PER_MIN.
const writeTimes = [];
let writeCount = 0;
async function throttleWrite() {
  for (;;) {
    const now = Date.now();
    while (writeTimes.length && now - writeTimes[0] >= 60_000) writeTimes.shift();
    if (writeTimes.length < WRITES_PER_MIN) {
      writeTimes.push(now);
      writeCount++;
      return;
    }
    const waitMs = 60_000 - (now - writeTimes[0]) + 50;
    await sleep(waitMs);
  }
}

// ── Auth ─────────────────────────────────────────────────────────────────────
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
  if (refreshToken) {
    applySession(await authPost('/v1/auth/refresh', { refreshToken }));
    return;
  }
  if (accessToken) {
    console.warn(
      '! Using OET_ADMIN_TOKEN with no refresh token. Production access tokens expire after\n' +
      '  15 minutes and a full ingest takes hours — expect 401s. Supply OET_ADMIN_EMAIL +\n' +
      '  OET_ADMIN_PASSWORD (or OET_ADMIN_REFRESH_TOKEN) for a run that survives.'
    );
    return;
  }
  throw new Error(
    'No admin credentials. Set OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD, or OET_ADMIN_REFRESH_TOKEN, ' +
    'or OET_ADMIN_TOKEN in the environment.'
  );
}

// Refresh 60s before expiry so a long part upload cannot straddle the boundary.
async function ensureFreshToken() {
  if (!accessTokenExpiresAt || Date.now() < accessTokenExpiresAt - 60_000) return;
  if (!refreshToken) {
    if (process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD) return signIn();
    return; // OET_ADMIN_TOKEN only — nothing we can do; the 401 handler will report it.
  }
  applySession(await authPost('/v1/auth/refresh', { refreshToken }));
}

// ── API ──────────────────────────────────────────────────────────────────────
class HttpError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

async function api(method, urlPath, { json, body, isWrite = true, retryOn401 = true } = {}) {
  await ensureFreshToken();
  if (isWrite) await throttleWrite();

  const headers = { Accept: 'application/json', Authorization: `Bearer ${accessToken}` };
  if (json !== undefined) headers['Content-Type'] = 'application/json';
  else if (body !== undefined) headers['Content-Type'] = 'application/octet-stream';

  const res = await fetch(`${API_BASE}${urlPath}`, {
    method,
    headers,
    body: json !== undefined ? JSON.stringify(json) : body,
  });
  const text = await res.text();

  if (res.status === 401 && retryOn401 && (refreshToken || process.env.OET_ADMIN_EMAIL)) {
    accessTokenExpiresAt = 0;
    await ensureFreshToken();
    return api(method, urlPath, { json, body, isWrite, retryOn401: false });
  }
  if (!res.ok) {
    let code;
    try { code = JSON.parse(text)?.code; } catch { /* non-JSON error body */ }
    throw new HttpError(res.status, code, `${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 300)}`);
  }
  return text ? JSON.parse(text) : {};
}

async function withRetry(label, fn) {
  let lastErr;
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    try {
      return await fn();
    } catch (err) {
      // A 4xx that is not a rate-limit is a contract problem — retrying just burns
      // the write budget and hammers production with a request that cannot succeed.
      if (err instanceof HttpError && err.status >= 400 && err.status < 500 && err.status !== 429) throw err;
      lastErr = err;
      const backoff = Math.min(30_000, 1000 * 2 ** (attempt - 1));
      console.warn(`  ! ${label} attempt ${attempt}/${MAX_ATTEMPTS} failed: ${err.message}. Retrying in ${backoff}ms`);
      await sleep(backoff);
    }
  }
  throw lastErr;
}

// ── State ────────────────────────────────────────────────────────────────────
let state = { folders: {}, files: {} };
async function loadState() {
  try {
    state = JSON.parse(await fsp.readFile(STATE_PATH, 'utf8'));
    state.folders ||= {};
    state.files ||= {};
  } catch {
    /* fresh */
  }
}
async function saveState() {
  await fsp.mkdir(STATE_DIR, { recursive: true });
  await fsp.writeFile(STATE_PATH, JSON.stringify(state, null, 2));
}

// ── Plan build ───────────────────────────────────────────────────────────────
async function walk(dir, out = { dirs: [], files: [] }) {
  const entries = await fsp.readdir(dir, { withFileTypes: true });
  for (const e of entries.sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      out.dirs.push(full);
      await walk(full, out);
    } else if (e.isFile()) {
      out.files.push(full);
    }
  }
  return out;
}

const relOf = (full) => full.substring(SRC_ROOT.length).replace(/^[\\/]+/, '').split(path.sep).join('/');

function subtestFor(segments) {
  const top = (segments[0] || '').trim().toLowerCase();
  return SUBTESTS.has(top) ? top : null;
}

function disciplineFor(segments) {
  for (const seg of segments) {
    const name = seg.trim();
    if (DISCIPLINE_LABELS.has(name)) return name;
  }
  return null;
}

async function buildPlan() {
  const { dirs, files } = await walk(SRC_ROOT);

  // Parents before children — the API needs parentFolderId to already exist.
  const folderRows = dirs
    .map((full) => {
      const rel = relOf(full);
      const segments = rel.split('/');
      return {
        relPath: rel,
        name: segments[segments.length - 1],
        parentRel: segments.length > 1 ? segments.slice(0, -1).join('/') : null,
        level: segments.length,
        // SubtestCode goes on the level-1 folder ONLY; ResolveEffectiveSubtest walks up
        // to the nearest tagged ancestor, so tagging every level would be redundant.
        subtestCode: segments.length === 1 ? subtestFor(segments) : null,
        discipline: disciplineFor(segments),
      };
    })
    .sort((a, b) => a.level - b.level || a.relPath.localeCompare(b.relPath));

  const fileRows = [];
  for (const full of files) {
    const rel = relOf(full);
    const segments = rel.split('/');
    const fileName = segments[segments.length - 1];
    const folderSegs = segments.slice(0, -1);
    const stat = await fsp.stat(full);
    const ext = extOf(fileName);
    const isAudio = AUDIO_EXTS.has(ext);
    const inferred = subtestFor(folderSegs);

    const issues = [];
    if (!FORMAT_MIME[ext]) issues.push(`unsupported_format:${ext || '(none)'}`);
    const cap = isAudio ? MAX_AUDIO_BYTES : MAX_OTHER_BYTES;
    if (stat.size > cap) issues.push(`too_large:${mb(stat.size)}MB>${mb(cap)}MB`);
    if (stat.size === 0) issues.push('empty_file');
    if (folderSegs.length > MAX_FOLDER_LEVEL) issues.push(`folder_too_deep:${folderSegs.length}`);

    fileRows.push({
      relPath: rel,
      fullPath: full,
      fileName,
      title: fileName.replace(/\.[^.]+$/, ''),
      folderRel: folderSegs.length ? folderSegs.join('/') : null,
      sizeBytes: stat.size,
      ext,
      mimeType: FORMAT_MIME[ext] || 'application/octet-stream',
      role: isAudio ? ROLE_AUDIO : ROLE_DEFAULT,
      subtestCode: inferred || DEFAULT_SUBTEST,
      subtestInferred: Boolean(inferred),
      discipline: disciplineFor(folderSegs),
      issues,
    });
  }
  fileRows.sort((a, b) => a.relPath.localeCompare(b.relPath));

  return { folderRows, fileRows };
}

function reportPlan(folderRows, fileRows) {
  const ok = fileRows.filter((f) => !f.issues.length);
  const bad = fileRows.filter((f) => f.issues.length);
  const fallback = ok.filter((f) => !f.subtestInferred);
  const totalBytes = ok.reduce((s, f) => s + f.sizeBytes, 0);
  const deepest = folderRows.reduce((m, f) => Math.max(m, f.level), 0);

  const bySubtest = new Map();
  for (const f of ok) {
    const key = `${f.subtestCode}${f.discipline ? ` / ${f.discipline}` : ''}`;
    bySubtest.set(key, (bySubtest.get(key) || 0) + 1);
  }

  console.log(`Folders to mirror : ${folderRows.length} (deepest level ${deepest}, cap ${MAX_FOLDER_LEVEL})`);
  console.log(`Files to ingest   : ${ok.length}${bad.length ? ` (+${bad.length} REJECTED)` : ''}`);
  console.log(`Total size        : ${(totalBytes / 1024 ** 3).toFixed(2)} GB\n`);

  console.log('Files by subtest / discipline:');
  for (const [key, count] of [...bySubtest.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    console.log(`  ${key}  ->  ${count}`);
  }

  if (deepest > MAX_FOLDER_LEVEL) {
    console.log(`\n!! ${folderRows.filter((f) => f.level > MAX_FOLDER_LEVEL).length} folder(s) exceed level ${MAX_FOLDER_LEVEL} and will be REJECTED by the API:`);
    for (const f of folderRows.filter((x) => x.level > MAX_FOLDER_LEVEL)) console.log(`   ${f.relPath}`);
  }

  if (fallback.length) {
    console.log(`\n! ${fallback.length} file(s) sit above any subtest folder and fall back to subtest "${DEFAULT_SUBTEST}"`);
    console.log(`  (the API requires a subtest; override with MATERIALS_DEFAULT_SUBTEST):`);
    for (const f of fallback) console.log(`   ${f.relPath}`);
  }

  if (bad.length) {
    console.log(`\n!! ${bad.length} file(s) REJECTED up front (not uploaded):`);
    for (const f of bad) console.log(`   ${f.relPath}  [${f.issues.join(', ')}]`);
  }

  // Writes = folder create+publish, then per file: upload start + parts + complete
  // + file create + publish. Parts are estimated at the server's default 8 MB chunk.
  const folderWrites = folderRows.length * (PUBLISH ? 2 : 1);
  const estParts = ok.reduce((s, f) => s + Math.max(1, Math.ceil(f.sizeBytes / (8 * 1024 ** 2))), 0);
  const fileWrites = ok.length * (PUBLISH ? 4 : 3) + estParts;
  const totalWrites = folderWrites + fileWrites;
  console.log(`\nEstimated writes  : ${totalWrites} (${folderWrites} folder, ${fileWrites} file incl. ~${estParts} parts)`);
  console.log(`Rate limit        : ${WRITES_PER_MIN}/min  ->  ~${Math.ceil(totalWrites / WRITES_PER_MIN)} min minimum`);
}

// ── Ingest ───────────────────────────────────────────────────────────────────
async function ensureFolder(row) {
  const known = state.folders[row.relPath];
  if (known?.id && (known.published || !PUBLISH)) return known.id;

  let id = known?.id;
  if (!id) {
    const parentId = row.parentRel ? state.folders[row.parentRel]?.id : null;
    if (row.parentRel && !parentId) throw new Error(`parent folder not created yet: ${row.parentRel}`);
    const created = await withRetry(`create folder ${row.relPath}`, () =>
      api('POST', '/v1/admin/materials/folders', {
        json: {
          parentFolderId: parentId,
          // Mirrored VERBATIM — the discipline gate matches on this name.
          name: row.name,
          subtestCode: row.subtestCode,
          sortOrder: 0,
        },
      })
    );
    id = created.id;
    state.folders[row.relPath] = { id, level: row.level, published: false };
    await saveState();
  }

  if (PUBLISH && !state.folders[row.relPath].published) {
    await withRetry(`publish folder ${row.relPath}`, () =>
      api('PUT', `/v1/admin/materials/folders/${id}`, { json: { status: 'Published' } })
    );
    state.folders[row.relPath].published = true;
    await saveState();
  }
  return id;
}

async function uploadFile(row) {
  const st = (state.files[row.relPath] ||= {});

  // 1. Upload the bytes → MediaAsset. Reuse a half-finished session so a resumed run
  //    neither re-sends confirmed parts nor orphans the old session.
  if (!st.mediaAssetId) {
    if (!st.uploadId) {
      const started = await withRetry(`start upload ${row.relPath}`, () =>
        api('POST', '/v1/admin/uploads', {
          json: {
            originalFilename: row.fileName,
            declaredMimeType: row.mimeType,
            declaredSizeBytes: row.sizeBytes,
            intendedRole: row.role,
          },
        })
      );
      st.uploadId = started.uploadId;
      st.chunkSizeBytes = started.chunkSizeBytes;
      st.partsSent = 0;
      await saveState();
    }

    const chunk = st.chunkSizeBytes;
    const totalParts = Math.max(1, Math.ceil(row.sizeBytes / chunk));
    const handle = await fsp.open(row.fullPath, 'r');
    try {
      // Parts MUST go in order: the server sizes each part against the bytes still
      // outstanding (DeclaredSizeBytes - ReceivedBytes), so an out-of-order final
      // short part would be rejected as over-long.
      for (let part = st.partsSent + 1; part <= totalParts; part++) {
        const offset = (part - 1) * chunk;
        const size = Math.min(chunk, row.sizeBytes - offset);
        const buf = Buffer.allocUnsafe(size);
        await handle.read(buf, 0, size, offset);
        try {
          await withRetry(`part ${part}/${totalParts} ${row.relPath}`, () =>
            api('PUT', `/v1/admin/uploads/${st.uploadId}/parts/${part}`, { body: buf })
          );
        } catch (err) {
          // The server already holds this part (a crash between its write and our
          // state flush). Count it and move on rather than failing the whole file.
          if (err instanceof HttpError && err.code === 'upload_part_duplicate') {
            console.warn(`  · part ${part} already on server, skipping`);
          } else {
            throw err;
          }
        }
        st.partsSent = part;
        await saveState();
      }
    } finally {
      await handle.close();
    }

    const done = await withRetry(`complete upload ${row.relPath}`, () =>
      api('POST', `/v1/admin/uploads/${st.uploadId}/complete`, { json: {} })
    );
    st.mediaAssetId = done.mediaAssetId;
    st.sha256 = done.sha256;
    st.deduplicated = done.deduplicated;
    await saveState();
  }

  // 2. Attach the asset as a MaterialFile.
  if (!st.fileId) {
    const created = await withRetry(`create material file ${row.relPath}`, () =>
      api('POST', '/v1/admin/materials/files', {
        json: {
          folderId: row.folderRel ? state.folders[row.folderRel]?.id ?? null : null,
          mediaAssetId: st.mediaAssetId,
          subtestCode: row.subtestCode,
          title: row.title.slice(0, 200),
          sortOrder: 0,
        },
      })
    );
    st.fileId = created.id;
    await saveState();
  }

  // 3. Publish — a Draft file is invisible to every learner, so this is the step that
  //    actually delivers the ingest.
  if (PUBLISH && !st.published) {
    await withRetry(`publish file ${row.relPath}`, () =>
      api('PUT', `/v1/admin/materials/files/${st.fileId}`, { json: { status: 'Published' } })
    );
    st.published = true;
    await saveState();
  }

  st.status = 'done';
  await saveState();
}

// ── Main ─────────────────────────────────────────────────────────────────────
async function main() {
  console.log('\n== OET Materials -> app ingest ==');
  console.log(`Source : ${SRC_ROOT}`);
  console.log(`API    : ${API_BASE}`);
  console.log(`Mode   : ${DRY_RUN ? 'DRY-RUN (no writes — pass --apply to ingest)' : 'LIVE INGEST'}`);
  console.log(`Publish: ${PUBLISH ? 'yes (Draft -> Published)' : 'no (left as Draft, invisible to learners)'}\n`);

  await loadState();
  const { folderRows, fileRows } = await buildPlan();

  let usable = fileRows.filter((f) => !f.issues.length);
  if (LIMIT > 0) {
    usable = usable.slice(0, LIMIT);
    console.log(`(MATERIALS_LIMIT=${LIMIT} -> restricting to the first ${usable.length} file(s))\n`);
  }
  reportPlan(folderRows, LIMIT > 0 ? usable : fileRows);

  if (DRY_RUN) {
    await fsp.mkdir(STATE_DIR, { recursive: true });
    await fsp.writeFile(
      MANIFEST_PATH,
      JSON.stringify({ folders: folderRows, files: usable.map(({ fullPath, ...r }) => r) }, null, 2)
    );
    console.log(`\nDry-run complete. Plan written to ${MANIFEST_PATH}`);
    console.log('Re-run with --apply to ingest.');
    return;
  }

  if (folderRows.some((f) => f.level > MAX_FOLDER_LEVEL)) {
    throw new Error(`Some folders exceed the API's level-${MAX_FOLDER_LEVEL} cap (listed above). Flatten them and re-run.`);
  }

  await signIn();
  console.log('Authenticated. Mirroring folders...\n');

  // Only the folders the selected files actually need (matters under MATERIALS_LIMIT).
  const needed = new Set();
  for (const f of usable) {
    let rel = f.folderRel;
    while (rel) {
      needed.add(rel);
      const idx = rel.lastIndexOf('/');
      rel = idx === -1 ? null : rel.substring(0, idx);
    }
  }

  let foldersCreated = 0;
  let foldersSkipped = 0;
  for (const row of folderRows) {
    if (!needed.has(row.relPath)) continue;
    const before = state.folders[row.relPath]?.id;
    await ensureFolder(row);
    if (before) foldersSkipped++;
    else {
      foldersCreated++;
      console.log(`  + ${row.relPath}`);
    }
  }
  console.log(`\nFolders ready (created=${foldersCreated} existing=${foldersSkipped}).\n`);

  let done = 0;
  let skipped = 0;
  let failed = 0;
  const failures = [];

  for (const [i, row] of usable.entries()) {
    if (state.files[row.relPath]?.status === 'done') {
      skipped++;
      continue;
    }
    try {
      console.log(`[${i + 1}/${usable.length}] ↑ ${row.relPath}  (${mb(row.sizeBytes)} MB)`);
      await uploadFile(row);
      done++;
      console.log(`[${i + 1}/${usable.length}] ✓ ${row.title}`);
    } catch (err) {
      failed++;
      failures.push({ relPath: row.relPath, error: err.message });
      state.files[row.relPath] = {
        ...(state.files[row.relPath] || {}),
        status: 'failed',
        error: String(err.message).slice(0, 300),
      };
      await saveState();
      console.error(`[${i + 1}/${usable.length}] ✗ ${row.relPath}: ${err.message}`);
    }
  }

  const manifest = usable.map((r) => {
    const st = state.files[r.relPath] || {};
    const { fullPath, ...meta } = r;
    return {
      ...meta,
      folderId: r.folderRel ? state.folders[r.folderRel]?.id ?? null : null,
      mediaAssetId: st.mediaAssetId || null,
      materialFileId: st.fileId || null,
      sha256: st.sha256 || null,
      published: Boolean(st.published),
      status: st.status || 'pending',
    };
  });
  await fsp.mkdir(STATE_DIR, { recursive: true });
  await fsp.writeFile(MANIFEST_PATH, JSON.stringify({ folders: state.folders, files: manifest }, null, 2));

  console.log(`\n== Done. created=${done} skipped=${skipped} failed=${failed} total=${usable.length} ==`);
  console.log(`Writes issued: ${writeCount} (limit ${WRITES_PER_MIN}/min)`);
  console.log(`Manifest: ${MANIFEST_PATH}`);
  if (failed) {
    console.log(`\nFailed file(s):`);
    for (const f of failures) console.log(`  ${f.relPath}: ${f.error}`);
    console.log(`\nRe-run the same command to retry only the ${failed} failed file(s).`);
    process.exitCode = 1;
  }
}

main().catch((err) => {
  console.error('\nFATAL:', err.message);
  process.exit(1);
});
