#!/usr/bin/env node
// ─────────────────────────────────────────────────────────────────────────────
// Writing scenario stimulus-PDF swap — raster → vector replacement (owner patch
// 7 Oct 2026, "every Writing sample sharp": source-quality fix only, content
// verbatim). Uploads a new PDF as a MediaAsset (or takes an existing asset id)
// and points one Writing task's StimulusPdfMediaAssetId at it.
//
// The authoring PUT is a FULL-REPLACE upsert: every mirrored field below must
// carry the task's current value or it is wiped. This script therefore GETs the
// task first and mirrors every field, changing ONLY the stimulus pointer, then
// re-GETs to prove the pointer moved and runs the validate gate for evidence.
//
// Dry-run by default. Auth: OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD (or
// OET_ADMIN_REFRESH_TOKEN / OET_ADMIN_TOKEN), needs AdminContentWrite.
// Rate limited to 30 writes/min across upload parts + PUTs.
//
// Usage:
//   node scripts/materials/swap-stimulus-pdf.mjs --task <scenarioId> --upload <file.pdf>   # plan
//   node scripts/materials/swap-stimulus-pdf.mjs --task <scenarioId> --upload <file.pdf> --apply
//   node scripts/materials/swap-stimulus-pdf.mjs --task <scenarioId> --asset <mediaAssetId> --apply
//   node scripts/materials/swap-stimulus-pdf.mjs --from ledger.json --apply   # [{task,asset}|{task,upload}]
//   OET_ADMIN_TOKEN=… node scripts/materials/swap-stimulus-pdf.mjs …         # short runs
// ─────────────────────────────────────────────────────────────────────────────
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
const APPLY = argv.includes('--apply');
const API_BASE = (process.env.OET_API_BASE || 'https://api.oetwithdrhesham.co.uk').replace(/\/$/, '');
const WRITES_PER_MIN = Number(process.env.MATERIALS_WRITES_PER_MIN || 30);
const MAX_ATTEMPTS = Number(process.env.MATERIALS_MAX_ATTEMPTS || 5);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const writeTimes = [];
async function throttleWrite() {
  for (;;) {
    const now = Date.now();
    while (writeTimes.length && now - writeTimes[0] >= 60_000) writeTimes.shift();
    if (writeTimes.length < WRITES_PER_MIN) { writeTimes.push(now); return; }
    await sleep(60_000 - (now - writeTimes[0]) + 50);
  }
}

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
  if (email && password) { applySession(await authPost('/v1/auth/sign-in', { email, password, rememberMe: true })); return; }
  if (refreshToken) { applySession(await authPost('/v1/auth/refresh', { refreshToken })); return; }
  if (accessToken) return;
  throw new Error('No admin credentials (OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD).');
}
async function ensureFreshToken() {
  if (!accessTokenExpiresAt || Date.now() < accessTokenExpiresAt - 60_000) return;
  if (!refreshToken) {
    if (process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD) return signIn();
    return;
  }
  applySession(await authPost('/v1/auth/refresh', { refreshToken }));
}

class HttpError extends Error {
  constructor(status, code, message) { super(message); this.status = status; this.code = code; }
}
async function api(method, urlPath, { json, body, isWrite = true, retryOn401 = true } = {}) {
  await ensureFreshToken();
  if (isWrite) await throttleWrite();
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(`${API_BASE}${urlPath}`, {
      method,
      headers: {
        Accept: 'application/json',
        ...(json !== undefined || (body && !(body instanceof Buffer) && !(body instanceof Uint8Array))
          ? { 'Content-Type': 'application/json' } : {}),
        ...(body instanceof Buffer || body instanceof Uint8Array ? { 'Content-Type': 'application/octet-stream' } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: json !== undefined ? JSON.stringify(json) : body,
    });
    if (res.status === 401 && retryOn401 && attempt === 1) {
      if (refreshToken) applySession(await authPost('/v1/auth/refresh', { refreshToken }));
      else if (process.env.OET_ADMIN_EMAIL && process.env.OET_ADMIN_PASSWORD) await signIn();
      else retryOn401 = false;
      continue;
    }
    const text = await res.text();
    if (res.ok) {
      try { return JSON.parse(text); } catch { return text; }
    }
    let code = `http_${res.status}`;
    let detail = text.slice(0, 300);
    try {
      const parsed = JSON.parse(text);
      code = parsed?.errorCode ?? parsed?.code ?? parsed?.error?.code ?? code;
      detail = parsed?.detail ?? parsed?.message ?? detail;
    } catch { /* plain text error body */ }
    // 500/502/503/429: transient — retry with backoff (mirrors the uploader).
    if ([429, 500, 502, 503, 504].includes(res.status) && attempt < MAX_ATTEMPTS) {
      await sleep(Math.min(30_000, 2 ** attempt * 1_000));
      continue;
    }
    throw new HttpError(res.status, code, `${method} ${urlPath} -> HTTP ${res.status} (${code}): ${detail}`);
  }
}

// ── Upload one whole file (mirrors upload-missing-materials.mjs uploadWhole) ──
async function uploadPdf(filePath) {
  const buf = fs.readFileSync(filePath);
  const size = buf.length;
  if (size < 1000) throw new Error(`${filePath}: ${size} bytes is suspiciously small for a stimulus PDF`);
  const started = await api('POST', '/v1/admin/uploads', {
    json: {
      originalFilename: path.basename(filePath),
      declaredMimeType: 'application/pdf',
      declaredSizeBytes: size,
      intendedRole: 'writing-stimulus',
    },
  });
  const chunk = started.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(size / chunk));
  for (let part = 1; part <= totalParts; part++) {
    const offset = (part - 1) * chunk;
    await api('PUT', `/v1/admin/uploads/${started.uploadId}/parts/${part}`, { body: buf.subarray(offset, offset + Math.min(chunk, size - offset)) });
  }
  const done = await api('POST', `/v1/admin/uploads/${started.uploadId}/complete`, { json: {} });
  return done; // { mediaAssetId, sha256, deduplicated }
}

// Every mirrored field of WritingTaskUpsertDto (full-replace protection).
// The API speaks camelCase on BOTH sides and binds the PUT body with its
// camelCase policy, so mirror case-insensitively off the GET response and
// emit camelCase keys.
const UPSERT_FIELDS = [
  'internalCode', 'title', 'profession', 'letterType', 'difficulty', 'writerRole', 'todayDate',
  'taskPromptMarkdown', 'expectedPurpose', 'expectedAction', 'fixedInstructions', 'wordGuideMin',
  'wordGuideMax', 'readingTimeSeconds', 'writingTimeSeconds', 'simulationModes', 'markingMode',
  'sourceProvenance', 'integrityAcknowledged', 'stimulusPdfMediaAssetId', 'answerSheetPdfMediaAssetId',
  'recipientRawText', 'recipientNormalizedJson', 'confirmedPurposeText',
];

function pickCaseInsensitive(obj, field) {
  if (obj[field] !== undefined) return obj[field];
  const lower = field.toLowerCase();
  const key = Object.keys(obj ?? {}).find((k) => k.toLowerCase() === lower);
  return key === undefined ? undefined : obj[key];
}

function buildUpsertPayload(task, newStimulusAssetId) {
  const payload = {};
  for (const field of UPSERT_FIELDS) {
    const value = pickCaseInsensitive(task, field);
    if (value !== undefined) payload[field] = value;
  }
  payload.stimulusPdfMediaAssetId = newStimulusAssetId;
  return payload;
}

async function swapOne({ task: scenarioId, asset, upload }) {
  const result = { task: scenarioId, uploadedFrom: upload ?? null, dryRun: !APPLY };
  const before = await api('GET', `/v1/admin/writing/tasks/${scenarioId}`, { isWrite: false });
  if (!before?.id) throw new Error(`task ${scenarioId} not found (HTTP response had no id)`);
  result.title = pickCaseInsensitive(before, 'title');
  result.profession = pickCaseInsensitive(before, 'profession');
  result.before = pickCaseInsensitive(before, 'stimulusPdfMediaAssetId');

  if (APPLY) {
    let assetId = asset ?? null;
    if (upload) {
      const done = await uploadPdf(upload);
      assetId = done.mediaAssetId;
      result.upload = { mediaAssetId: done.mediaAssetId, sha256: done.sha256, deduplicated: done.deduplicated ?? false, bytes: done.sizeBytes ?? null };
    }
    if (!assetId) throw new Error('no asset id resolved (--asset or --upload required)');
    result.after = assetId;
    if (String(result.before ?? '') === String(assetId)) {
      result.skipped = 'pointer already set';
      return result;
    }
    const payload = buildUpsertPayload(before, assetId);
    if (!payload.title) throw new Error('mirrored payload would drop title — refusing to PUT');
    const updated = await api('PUT', `/v1/admin/writing/tasks/${scenarioId}`, { json: payload });
    const updatedPointer = pickCaseInsensitive(updated ?? {}, 'stimulusPdfMediaAssetId');
    if (String(updatedPointer ?? '') !== String(assetId)) {
      throw new Error(`PUT did not move the pointer (got ${updatedPointer})`);
    }
    // Read-back + validate gate for the evidence ledger.
    const reread = await api('GET', `/v1/admin/writing/tasks/${scenarioId}`, { isWrite: false });
    result.readBack = pickCaseInsensitive(reread ?? {}, 'stimulusPdfMediaAssetId');
    result.titleStillIntact = pickCaseInsensitive(reread ?? {}, 'title') === result.title;
    if (result.readBack !== assetId) throw new Error('read-back mismatch after PUT');
    try {
      const validation = await api('GET', `/v1/admin/writing/tasks/${scenarioId}/validate`, { isWrite: false });
      result.validate = { publishReady: validation?.isPublishReady ?? null, errorCount: (validation?.issues ?? []).filter((i) => i.severity === 'error').length };
    } catch (e) { result.validate = { error: e.message.slice(0, 200) }; }
  } else {
    result.after = upload ? '(upload on apply)' : asset;
    const probe = buildUpsertPayload(before, 'dry-run-probe');
    result.payloadMirror = { fields: Object.keys(probe).length, titlePresent: Boolean(probe.title) };
    if (!probe.title) throw new Error('mirrored payload would drop title — refusing to apply');
  }
  return result;
}

function parseArgs() {
  const jobs = [];
  const fromIndex = argv.indexOf('--from');
  if (fromIndex >= 0) {
    const ledger = JSON.parse(fs.readFileSync(argv[fromIndex + 1], 'utf8'));
    for (const row of ledger) jobs.push(row);
    return jobs;
  }
  const taskIndex = argv.indexOf('--task');
  const assetIndex = argv.indexOf('--asset');
  const uploadIndex = argv.indexOf('--upload');
  if (taskIndex < 0) throw new Error('--task <scenarioId> (or --from ledger.json) is required');
  jobs.push({
    task: argv[taskIndex + 1],
    ...(assetIndex >= 0 ? { asset: argv[assetIndex + 1] } : {}),
    ...(uploadIndex >= 0 ? { upload: path.resolve(argv[uploadIndex + 1]) } : {}),
  });
  return jobs;
}

async function main() {
  const jobs = parseArgs();
  if (!jobs.length) throw new Error('nothing to do');
  await signIn();
  const results = [];
  let failed = 0;
  for (const job of jobs) {
    try {
      const row = await swapOne(job);
      results.push(row);
      console.log(JSON.stringify(row));
    } catch (error) {
      failed++;
      const row = { task: job.task, error: error.message.slice(0, 300), dryRun: !APPLY };
      results.push(row);
      console.error(JSON.stringify(row));
    }
  }
  const out = process.env.SWAP_OUT || path.join(HERE, 'swap-result.json');
  fs.writeFileSync(out, `${JSON.stringify({ apply: APPLY, at: new Date().toISOString(), results }, null, 2)}\n`);
  console.error(`\n${APPLY ? 'APPLIED' : 'DRY-RUN'}: ${results.length - failed} ok, ${failed} failed; ledger at ${out}`);
  if (failed) process.exit(1);
}

main().catch((e) => { console.error(e); process.exit(1); });
