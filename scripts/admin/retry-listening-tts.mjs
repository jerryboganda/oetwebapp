/**
 * retry-listening-tts.mjs
 *
 * For every Listening ContentPaper in Status=Draft (0):
 *   1. GET /v1/admin/papers/{id} → enumerate assets
 *   2. If AudioScript text asset is present, read its bytes from the API
 *      container's local filesystem via `docker exec`, parse the
 *      `[PART <code>] ... ---- <transcript>` blocks emitted by
 *      generate-listening.mjs::renderAudioScript().
 *   3. For each canonical part (A1, A2, B, C1, C2) that does NOT already have
 *      an attached Audio asset:
 *        a. Call aiTts(transcript, { retries: 20 }) — _lib's chunker handles
 *           the 480-char cap; the rate-limiter throttles us under DO's quota.
 *        b. Sniff the container (RIFF/WAVE → wav, OggS → ogg, fLaC → flac,
 *           else MP3) and upload as a MediaAsset via the chunked uploader.
 *        c. POST /v1/admin/papers/{id}/assets with role=Audio.
 *   4. POST /v1/admin/papers/{id}/listening/backfill {}
 *   5. SQL fallback to fill DifficultyRating/DifficultyLevel = 3 where NULL.
 *   6. POST /v1/admin/papers/{id}/publish {}
 *
 * The script is idempotent — re-running it skips parts already attached and
 * skips papers that have no AudioScript text asset (those are skeletons that
 * the orch died before adding text — they need to be regenerated, not retried).
 *
 * Designed to run on the VPS (alongside generate-listening.mjs --skip-tts)
 * because it shells out to `docker exec` for both file reads and the
 * difficulty-fallback SQL.
 *
 * Flags:
 *   --poll-seconds N   loop forever, sleeping N seconds between sweeps
 *                      (default: single pass).
 *   --paper-sleep N    seconds to sleep between papers (default 30).
 *   --part-sleep N     seconds to sleep between parts within one paper
 *                      (default 5).
 *   --tts-retries N    aiTts retries per chunk (default 20).
 *   --max-papers N     stop after processing N papers in one sweep (default
 *                      unlimited).
 *   --dry-run          print plan only — no TTS/upload/publish calls.
 *   --paper-id ID      process only one paper id (debug).
 */

import { execFileSync } from 'node:child_process';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, aiTts,
  uploadMediaAsset, logFailure, sleep,
} from './_lib.mjs';

const flags = parseFlags();

const POLL_SECONDS = parseInt(flags['poll-seconds'], 10) || 0;
const PAPER_SLEEP_S = parseInt(flags['paper-sleep'], 10) || 30;
const PART_SLEEP_S = parseInt(flags['part-sleep'], 10) || 5;
const TTS_RETRIES = parseInt(flags['tts-retries'], 10) || 20;
const MAX_PAPERS = parseInt(flags['max-papers'], 10) || 0;
const DRY_RUN = !!flags['dry-run'];
const ONLY_PAPER = typeof flags['paper-id'] === 'string' ? flags['paper-id'] : null;

const PAPER_AUDIO_ROLE = 'Audio';
const PAPER_AUDIOSCRIPT_ROLE = 'AudioScript';
const PART_ORDER = ['A1', 'A2', 'B', 'C1', 'C2'];
const STORAGE_ROOT_IN_CONTAINER = '/var/opt/oet-learner/storage';
const API_CONTAINER = process.env.API_CONTAINER || 'oet-api-green';
const PG_CONTAINER = process.env.PG_CONTAINER || 'oet-postgres';
const PG_USER = process.env.PG_USER || 'oet_learner';
const PG_DB = process.env.PG_DB || 'oet_learner';

// -----------------------------------------------------------------------------
// helpers
// -----------------------------------------------------------------------------

function psqlExec(sql) {
  const out = execFileSync(
    'docker',
    ['exec', PG_CONTAINER, 'psql', '-U', PG_USER, '-d', PG_DB, '-P', 'pager=off', '-tA', '-c', sql],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }
  );
  return out.trim();
}

function readStorageFile(storagePath) {
  // storagePath is a relative key like "published/ab/cd/abcd...txt".
  // The API container mounts the storage root at STORAGE_ROOT_IN_CONTAINER.
  if (!storagePath || storagePath.includes('..')) throw new Error(`invalid storagePath: ${storagePath}`);
  const full = `${STORAGE_ROOT_IN_CONTAINER}/${storagePath}`;
  return execFileSync(
    'docker',
    ['exec', API_CONTAINER, 'cat', full],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 16 * 1024 * 1024 }
  );
}

/**
 * Split AudioScript text emitted by renderAudioScript() into part→transcript.
 * Format:
 *   AUDIO SCRIPT — {title}
 *   ============================================================
 *
 *
 *   [PART A1] {extractTitle} (accent: en-GB)
 *   ------------------------------------------------------------
 *   <transcript paragraphs ...>
 *
 *   [PART A2] ...
 */
function parseAudioScript(text) {
  const out = {};
  const re = /\n\[PART (A1|A2|B|C1|C2)\][^\n]*\n-+\n([\s\S]*?)(?=\n\[PART (?:A1|A2|B|C1|C2)\]|\n*$)/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    const part = m[1];
    const transcript = (m[2] || '').trim();
    if (transcript) out[part] = transcript;
  }
  return out;
}

function sniffAudio(buf) {
  if (buf.length >= 12 && buf.slice(0, 4).toString() === 'RIFF' && buf.slice(8, 12).toString() === 'WAVE') {
    return { ext: 'wav', mime: 'audio/wav' };
  }
  if (buf.length >= 4 && buf.slice(0, 4).toString() === 'OggS') {
    return { ext: 'ogg', mime: 'audio/ogg' };
  }
  if (buf.length >= 4 && buf.slice(0, 4).toString() === 'fLaC') {
    return { ext: 'flac', mime: 'audio/flac' };
  }
  return { ext: 'mp3', mime: 'audio/mpeg' };
}

async function listDraftListeningPapers() {
  const all = [];
  let page = 1;
  while (true) {
    const r = await adminFetch('/v1/admin/papers', {
      query: { subtest: 'Listening', status: 'Draft', page, pageSize: 50 },
    });
    if (!r.ok) throw new Error(`list drafts failed: ${r.status} ${JSON.stringify(r.data).slice(0, 300)}`);
    const items = Array.isArray(r.data?.items) ? r.data.items : Array.isArray(r.data) ? r.data : [];
    if (items.length === 0) break;
    for (const it of items) all.push(it);
    if (items.length < 50) break;
    page++;
    if (page > 20) break; // safety
  }
  return all;
}

function audioScriptStoragePathForPaper(paperId) {
  // Pull the AudioScript asset's StoragePath in one SQL call.
  // role=2 is AudioScript per PaperAssetRole enum.
  const sql = `SELECT ma."StoragePath" FROM "ContentPaperAssets" pa JOIN "MediaAssets" ma ON ma."Id"=pa."MediaAssetId" WHERE pa."PaperId"='${paperId}' AND pa."Role"=2 LIMIT 1;`;
  const out = psqlExec(sql);
  return out || null;
}

async function getPaperDetail(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}`);
  if (!r.ok) throw new Error(`GET paper ${paperId} failed: ${r.status}`);
  return r.data;
}

function extractAttachedParts(paper) {
  // paper.assets[] entries with role==='Audio' and a part code.
  const set = new Set();
  const assets = Array.isArray(paper?.assets) ? paper.assets : [];
  for (const a of assets) {
    if ((a.role === PAPER_AUDIO_ROLE || a.role === 0) && a.part) {
      set.add(String(a.part).toUpperCase());
    }
  }
  return set;
}

async function attachAudio(paperId, mediaAssetId, partCode, displayOrder) {
  const body = {
    role: PAPER_AUDIO_ROLE,
    mediaAssetId,
    part: partCode,
    title: `Listening Part ${partCode}`,
    displayOrder,
    makePrimary: true,
  };
  const r = await adminFetch(`/v1/admin/papers/${paperId}/assets`, { method: 'POST', body });
  if (!r.ok) throw new Error(`attach ${partCode} failed: ${r.status} ${JSON.stringify(r.data).slice(0, 300)}`);
  return r.data;
}

async function backfillRelational(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/listening/backfill`, { method: 'POST', body: {} });
  if (!r.ok) console.log(`  ⚠ backfill failed (${r.status}): ${JSON.stringify(r.data).slice(0, 200)}`);
  return r.ok;
}

function difficultyFallback(paperId) {
  try {
    const sql = `UPDATE "ListeningExtracts" SET "DifficultyRating"=3 WHERE "DifficultyRating" IS NULL AND "ListeningPartId" IN (SELECT "Id" FROM "ListeningParts" WHERE "PaperId"='${paperId}'); UPDATE "ListeningQuestions" SET "DifficultyLevel"=3 WHERE "DifficultyLevel" IS NULL AND "PaperId"='${paperId}';`;
    execFileSync('docker', ['exec', PG_CONTAINER, 'psql', '-U', PG_USER, '-d', PG_DB, '-P', 'pager=off', '-c', sql], { stdio: 'pipe' });
    console.log(`  ✓ difficulty fallback applied`);
  } catch (e) {
    console.log(`  ⚠ difficulty fallback failed: ${String(e?.message || e).slice(0, 200)}`);
  }
}

async function publishPaper(paperId) {
  const r = await adminFetch(`/v1/admin/papers/${paperId}/publish`, { method: 'POST', body: {} });
  if (!r.ok) {
    console.log(`  ⚠ publish failed (${r.status}): ${JSON.stringify(r.data).slice(0, 300)}`);
    return false;
  }
  console.log(`  ✓ published`);
  return true;
}

// -----------------------------------------------------------------------------
// per-paper
// -----------------------------------------------------------------------------

async function processPaper(paperListItem) {
  const paperId = paperListItem.id || paperListItem.Id;
  const title = paperListItem.title || paperListItem.Title || '(no title)';
  console.log(`\n──────── ${title}  [${paperId}] ────────`);

  let detail;
  try {
    detail = await getPaperDetail(paperId);
  } catch (e) {
    console.log(`  ⚠ GET detail failed: ${String(e?.message || e).slice(0, 200)}`);
    return { ok: false, reason: 'get-detail-failed' };
  }

  // Identify missing parts.
  const attached = extractAttachedParts(detail);
  const missing = PART_ORDER.filter((p) => !attached.has(p));
  if (missing.length === 0) {
    console.log(`  ✓ all 5 parts already attached — running publish path only`);
    if (!DRY_RUN) {
      await backfillRelational(paperId);
      difficultyFallback(paperId);
      await publishPaper(paperId);
    }
    return { ok: true, reason: 'already-complete' };
  }
  console.log(`  parts attached: [${[...attached].sort().join(',') || 'none'}]`);
  console.log(`  parts missing : [${missing.join(',')}]`);

  // Load AudioScript text.
  let storagePath;
  try {
    storagePath = audioScriptStoragePathForPaper(paperId);
  } catch (e) {
    console.log(`  ⚠ SQL lookup failed: ${String(e?.message || e).slice(0, 200)}`);
    return { ok: false, reason: 'sql-lookup-failed' };
  }
  if (!storagePath) {
    console.log(`  ⊘ skip: no AudioScript asset on this paper`);
    return { ok: false, reason: 'no-audioscript' };
  }

  let scriptText;
  try {
    scriptText = readStorageFile(storagePath);
  } catch (e) {
    console.log(`  ⚠ read storage failed: ${String(e?.message || e).slice(0, 200)}`);
    return { ok: false, reason: 'read-failed' };
  }

  const partTranscripts = parseAudioScript(scriptText);
  for (const p of missing) {
    if (!partTranscripts[p] || partTranscripts[p].length < 80) {
      console.log(`  ⚠ part ${p} has no usable transcript in AudioScript (len=${(partTranscripts[p] || '').length}) — skipping`);
    }
  }

  if (DRY_RUN) {
    console.log(`  (dry-run) would TTS+attach: ${missing.filter((p) => (partTranscripts[p] || '').length >= 80).join(',')}`);
    return { ok: true, reason: 'dry-run' };
  }

  // TTS + attach for each missing part.
  let attachedNow = 0;
  for (const p of missing) {
    const transcript = (partTranscripts[p] || '').trim();
    if (transcript.length < 80) continue;
    const displayOrder = PART_ORDER.indexOf(p) + 1;
    console.log(`  → TTS ${p} (${transcript.length} chars, displayOrder=${displayOrder})`);
    let buf;
    try {
      buf = await aiTts(transcript, { retries: TTS_RETRIES, format: 'mp3' });
    } catch (e) {
      logFailure('retry-tts', { paperId, part: p }, e);
      console.log(`  ⚠ TTS ${p} failed: ${String(e?.message || e).slice(0, 200)}`);
      continue;
    }
    if (!buf || buf.length < 500) {
      console.log(`  ⚠ TTS ${p} returned empty/tiny buffer (${buf?.length || 0} bytes) — skipping`);
      continue;
    }
    const sniff = sniffAudio(buf);
    let mediaAssetId;
    try {
      mediaAssetId = await uploadMediaAsset(buf, {
        filename: `listening-${paperId}-${p}.${sniff.ext}`,
        mimeType: sniff.mime,
        kind: 'audio',
        intendedRole: 'Audio',
      });
    } catch (e) {
      logFailure('retry-upload', { paperId, part: p }, e);
      console.log(`  ⚠ upload ${p} failed: ${String(e?.message || e).slice(0, 200)}`);
      continue;
    }
    try {
      await attachAudio(paperId, mediaAssetId, p, displayOrder);
      console.log(`  ✓ attached ${p} (${buf.length} bytes, ${sniff.mime})`);
      attachedNow++;
    } catch (e) {
      logFailure('retry-attach', { paperId, part: p }, e);
      console.log(`  ⚠ attach ${p} failed: ${String(e?.message || e).slice(0, 200)}`);
      continue;
    }
    if (PART_SLEEP_S > 0) await sleep(PART_SLEEP_S * 1000);
  }

  // Re-fetch to confirm all parts present.
  let after;
  try {
    after = await getPaperDetail(paperId);
  } catch (e) {
    console.log(`  ⚠ re-GET failed: ${String(e?.message || e).slice(0, 200)}`);
    return { ok: false, reason: 'reget-failed', attachedNow };
  }
  const stillMissing = PART_ORDER.filter((p) => !extractAttachedParts(after).has(p));
  if (stillMissing.length > 0) {
    console.log(`  ⊘ still missing parts: [${stillMissing.join(',')}] — will retry on next sweep`);
    return { ok: false, reason: 'still-missing', stillMissing, attachedNow };
  }

  await backfillRelational(paperId);
  difficultyFallback(paperId);
  const published = await publishPaper(paperId);
  return { ok: published, reason: published ? 'published' : 'publish-failed', attachedNow };
}

// -----------------------------------------------------------------------------
// sweep
// -----------------------------------------------------------------------------

async function sweep() {
  let drafts;
  if (ONLY_PAPER) {
    drafts = [{ id: ONLY_PAPER, title: '(forced)' }];
  } else {
    drafts = await listDraftListeningPapers();
  }
  console.log(`[sweep] ${drafts.length} Draft listening papers`);
  const stats = { processed: 0, published: 0, skipped: 0, deferred: 0, failed: 0 };

  for (const p of drafts) {
    if (MAX_PAPERS > 0 && stats.processed >= MAX_PAPERS) break;
    stats.processed++;
    let res;
    try {
      res = await processPaper(p);
    } catch (e) {
      logFailure('retry-paper', { paperId: p.id || p.Id }, e);
      console.log(`  ⚠ uncaught: ${String(e?.message || e).slice(0, 200)}`);
      stats.failed++;
      continue;
    }
    if (res.ok && res.reason === 'published') stats.published++;
    else if (res.reason === 'already-complete') stats.published++;
    else if (res.reason === 'no-audioscript') stats.skipped++;
    else if (res.reason === 'still-missing' || res.reason === 'publish-failed') stats.deferred++;
    else stats.failed++;

    if (PAPER_SLEEP_S > 0 && stats.processed < drafts.length) {
      console.log(`  (sleep ${PAPER_SLEEP_S}s)`);
      await sleep(PAPER_SLEEP_S * 1000);
    }
  }

  console.log(`[sweep] DONE  ${JSON.stringify(stats)}`);
  return stats;
}

// -----------------------------------------------------------------------------
// main
// -----------------------------------------------------------------------------

async function main() {
  startRun('retry-listening-tts');
  console.log(`config: poll=${POLL_SECONDS}s paperSleep=${PAPER_SLEEP_S}s partSleep=${PART_SLEEP_S}s ttsRetries=${TTS_RETRIES} dryRun=${DRY_RUN} onlyPaper=${ONLY_PAPER || '(none)'}`);
  if (POLL_SECONDS > 0) {
    while (true) {
      const stats = await sweep();
      if (stats.processed === 0 && !ONLY_PAPER) {
        console.log(`[idle] no drafts — sleeping ${POLL_SECONDS}s`);
      } else {
        console.log(`[loop] sleeping ${POLL_SECONDS}s`);
      }
      await sleep(POLL_SECONDS * 1000);
    }
  } else {
    const stats = await sweep();
    endRun({ ok: stats.failed === 0, stats });
  }
}

main().catch((e) => {
  console.error('FATAL:', e);
  process.exit(1);
});
