/**
 * publish-vocab.mjs
 *
 * Activates draft VocabularyTerm rows in the backend by promoting status
 * "draft" → "active" via the admin CRUD endpoint. When required fields are
 * missing/sparse, calls the DO Serverless chat model (Opus 4.7 by default,
 * via aiChatJson) to backfill IPA, definition, example sentence, etc., then
 * PUTs the merged payload. The backend's publish gate
 * (EnforceVocabularyPublishGate) does the final validation.
 *
 * Backend contract (verified against VocabularyEndpoints.cs / AdminEndpoints.cs
 * @ "AdminContentRead" / "AdminContentWrite" + AdminVocabularyItemUpdateRequestV2):
 *
 *   GET  /v1/admin/vocabulary/items?status=draft&page=N&pageSize=100
 *        → { total, page, pageSize, items: [{ id, term, definition,
 *            professionId, category, difficulty, exampleSentence,
 *            americanSpelling, status }] }
 *
 *   GET  /v1/admin/vocabulary/items/{itemId}
 *        → full term detail (all fields incl. ipaPronunciation, audioUrl,
 *          sourceProvenance, *Json arrays …)
 *
 *   PUT  /v1/admin/vocabulary/items/{itemId}  AdminVocabularyItemUpdateRequestV2
 *        → publish gate fires when status="active"; required: term, definition,
 *          exampleSentence, category, sourceProvenance, and for medical
 *          categories either ipaPronunciation OR audioUrl.
 *
 * Usage:
 *   node scripts/admin/publish-vocab.mjs                 # publish all drafts
 *   node scripts/admin/publish-vocab.mjs --dry-run       # plan only, no writes
 *   node scripts/admin/publish-vocab.mjs --limit 50      # cap items processed
 *   node scripts/admin/publish-vocab.mjs --profession medicine
 *   node scripts/admin/publish-vocab.mjs --only VOC-abc123def456
 *   node scripts/admin/publish-vocab.mjs --resume        # skip ids already in resume log
 *   node scripts/admin/publish-vocab.mjs --healthcheck
 *
 * Notes:
 *   - Uses ONLY the foundation lib exports (adminFetch / aiChatJson / startRun
 *     / endRun / logFailure / progress / makeProvenance / abortIfFailureCascade).
 *   - No direct fetch(), no hard-coded endpoint base, no node-fetch.
 *   - Auto-retries 429/5xx via adminFetch; auto-refreshes JWT on 401.
 *   - Resume log: output/admin-bulk/publish-vocab.resume.jsonl (one id per line).
 */

import { readFileSync, appendFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, aiChatJson,
  logFailure, progress, healthcheck, makeProvenance,
  abortIfFailureCascade, sleep,
} from './_lib.mjs';

const flags = parseFlags();

const LIST_PATH   = '/v1/admin/vocabulary/items';
const DETAIL_PATH = (id) => `/v1/admin/vocabulary/items/${encodeURIComponent(id)}`;
const UPDATE_PATH = (id) => `/v1/admin/vocabulary/items/${encodeURIComponent(id)}`;

const RESUME_LOG = resolve(CONFIG.outputDir, 'publish-vocab.resume.jsonl');

// Medical categories — backend requires ipaPronunciation OR audioUrl on these
// before allowing status="active". Mirror AdminService.ContentAdmin.cs.
const MEDICAL_CATEGORIES = new Set([
  'medical', 'anatomy', 'pharmacology', 'procedures',
  'symptoms', 'conditions', 'diagnostics',
]);

const PAGE_SIZE = 100;

// --------------------------------------------------------------------------
// Resume support
// --------------------------------------------------------------------------

function loadResumeSet() {
  if (!flags.resume) return new Set();
  if (!existsSync(RESUME_LOG)) return new Set();
  const done = new Set();
  try {
    const text = readFileSync(RESUME_LOG, 'utf8');
    for (const line of text.split('\n')) {
      const t = line.trim();
      if (!t) continue;
      try {
        const row = JSON.parse(t);
        if (row.id) done.add(row.id);
      } catch { /* tolerate junk lines */ }
    }
  } catch { /* ignore */ }
  return done;
}

function markResumed(id, outcome) {
  try {
    appendFileSync(
      RESUME_LOG,
      JSON.stringify({ ts: new Date().toISOString(), id, outcome }) + '\n',
      'utf8'
    );
  } catch { /* non-fatal */ }
}

// --------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------

function isBlank(v) {
  return v === undefined || v === null || (typeof v === 'string' && v.trim() === '');
}

function parseJsonArray(s) {
  if (!s) return [];
  try {
    const v = JSON.parse(s);
    return Array.isArray(v) ? v.filter(x => typeof x === 'string' && x.trim() !== '') : [];
  } catch { return []; }
}

/**
 * Decide whether a draft term needs AI backfill, and what.
 * Returns { needsAi: boolean, missing: string[] }.
 */
function diagnoseSparse(detail) {
  const missing = [];
  if (isBlank(detail.definition))      missing.push('definition');
  if (isBlank(detail.exampleSentence)) missing.push('exampleSentence');
  if (isBlank(detail.ipaPronunciation) && isBlank(detail.audioUrl)) {
    // Always useful to have IPA; required for medical to publish.
    missing.push('ipaPronunciation');
  }
  // ContextNotes / partOfSpeech are not gated but improve the entry; only
  // backfill them when several harder fields were also missing, to keep
  // AI cost proportional.
  return { needsAi: missing.length > 0, missing };
}

/**
 * Call Opus 4.7 (or whatever CONFIG.ai.chatModel resolves to) to backfill
 * sparse fields. Strict JSON shape; on any failure the caller treats this
 * as a non-fatal skip for that term.
 */
async function aiBackfill(detail) {
  const term = String(detail.term || '').trim();
  const profession = detail.professionId || 'all healthcare professionals';
  const category = detail.category || 'general';

  const system =
    'You are an OET (Occupational English Test) vocabulary editor. ' +
    'You write concise, clinically accurate, learner-facing entries. ' +
    'Output STRICT JSON only — no prose, no markdown fences. ' +
    'IPA must use International Phonetic Alphabet symbols inside / / ' +
    '(British English). Definitions are one sentence, plain language, ' +
    'no circular references. Example sentences are one realistic clinical ' +
    'utterance that uses the term naturally.';

  const user =
    `Backfill the following OET vocabulary term for ${profession} ` +
    `(category: ${category}). Preserve any fields that are already provided; ` +
    `only fill what is empty.\n\n` +
    `Term: ${term}\n` +
    `Current definition: ${detail.definition || '(empty)'}\n` +
    `Current IPA: ${detail.ipaPronunciation || '(empty)'}\n` +
    `Current example: ${detail.exampleSentence || '(empty)'}\n\n` +
    `Return JSON with this exact shape (omit any key you cannot confidently fill):\n` +
    `{\n` +
    `  "ipa": "/.../",\n` +
    `  "definition": "one-sentence learner definition",\n` +
    `  "exampleSentence": "one realistic clinical sentence using the term",\n` +
    `  "partOfSpeech": "noun|verb|adjective|adverb",\n` +
    `  "professionRelevance": "1-sentence note on why this matters for ${profession}"\n` +
    `}`;

  const { json } = await aiChatJson({
    system,
    user,
    temperature: 0.3,
    maxTokens: 600,
  });
  return json || {};
}

/**
 * Build the PUT body. Only includes fields we intentionally want to write —
 * AdminVocabularyItemUpdateRequestV2 treats nulls as "leave unchanged" on
 * scalar fields and as "clear" on list fields, so we omit nulls entirely.
 */
function buildUpdateBody(detail, ai, { activate }) {
  const body = {};

  // Definition / example / IPA — only set when current value is blank AND we
  // got something usable from the model.
  if (isBlank(detail.definition) && typeof ai.definition === 'string' && ai.definition.trim() !== '') {
    body.definition = ai.definition.trim();
  }
  if (isBlank(detail.exampleSentence) && typeof ai.exampleSentence === 'string' && ai.exampleSentence.trim() !== '') {
    body.exampleSentence = ai.exampleSentence.trim();
  }
  if (isBlank(detail.ipaPronunciation) && typeof ai.ipa === 'string' && ai.ipa.trim() !== '') {
    body.ipaPronunciation = ai.ipa.trim();
  }

  // Stash partOfSpeech / professionRelevance in contextNotes if there's room
  // and the current value is empty — these are not gated fields but they
  // enrich the entry.
  if (isBlank(detail.contextNotes)) {
    const parts = [];
    if (typeof ai.partOfSpeech === 'string' && ai.partOfSpeech.trim()) {
      parts.push(`Part of speech: ${ai.partOfSpeech.trim()}.`);
    }
    if (typeof ai.professionRelevance === 'string' && ai.professionRelevance.trim()) {
      parts.push(ai.professionRelevance.trim());
    }
    if (parts.length > 0) body.contextNotes = parts.join(' ');
  }

  if (activate) {
    // sourceProvenance is required by the publish gate. If the draft already
    // has one we leave it untouched (omit from body); otherwise we stamp an
    // AI-attestation string via the shared makeProvenance helper.
    if (isBlank(detail.sourceProvenance)) {
      body.sourceProvenance = makeProvenance({
        kind: 'vocabulary',
        profession: detail.professionId,
        model: CONFIG.ai.chatModel,
      });
    }
    body.status = 'active';
  }

  return body;
}

/**
 * Returns true if, after merging current detail + planned body, the publish
 * gate will pass. Mirrors EnforceVocabularyPublishGate without re-implementing
 * the server. We only use this to decide whether to attempt activation.
 */
function gateWillPass(detail, body) {
  const merged = { ...detail, ...body };
  if (isBlank(merged.term)) return false;
  if (isBlank(merged.definition)) return false;
  if (isBlank(merged.exampleSentence)) return false;
  if (isBlank(merged.category)) return false;
  if (isBlank(merged.sourceProvenance)) return false;
  if (merged.category && MEDICAL_CATEGORIES.has(String(merged.category).toLowerCase())) {
    if (isBlank(merged.ipaPronunciation) && isBlank(merged.audioUrl)) return false;
  }
  return true;
}

// --------------------------------------------------------------------------
// Main
// --------------------------------------------------------------------------

async function fetchDraftsPage(page, professionFilter) {
  const query = { status: 'draft', page, pageSize: PAGE_SIZE };
  if (professionFilter) query.profession = professionFilter;
  const res = await adminFetch(LIST_PATH, { query });
  if (!res.ok) {
    throw new Error(`list drafts page ${page} failed: ${res.status} ${JSON.stringify(res.data).slice(0, 300)}`);
  }
  return res.data;
}

async function processOne(item, { dryRun }) {
  // Pull full detail (list endpoint omits IPA / sourceProvenance / audioUrl).
  const det = await adminFetch(DETAIL_PATH(item.id));
  if (!det.ok) throw new Error(`detail ${item.id} failed: ${det.status}`);
  const detail = det.data || {};

  const diag = diagnoseSparse(detail);
  let ai = {};
  if (diag.needsAi) {
    try {
      ai = await aiBackfill(detail);
    } catch (e) {
      // AI is best-effort. If it fails, fall through and try to activate
      // with what's already in the row (publish gate will reject if it's
      // truly underspecified, and we'll logFailure on the 4xx).
      logFailure('vocab-ai-backfill', { id: item.id, term: detail.term }, e);
    }
  }

  let body = buildUpdateBody(detail, ai, { activate: true });

  if (!gateWillPass(detail, body)) {
    // Don't waste a PUT that we know will 4xx with VOCAB_PUBLISH_GATE.
    // Still PUT whatever non-status fields we did manage to derive so the
    // term improves for the next run — but drop the status flip.
    delete body.status;
    if (Object.keys(body).length === 0) {
      return { id: item.id, outcome: 'skipped-gate', reason: 'insufficient fields and AI did not return enough to publish' };
    }
    if (dryRun) {
      return { id: item.id, outcome: 'dry-run-improve', body };
    }
    const upd = await adminFetch(UPDATE_PATH(item.id), { method: 'PUT', body });
    if (!upd.ok) throw new Error(`PUT ${item.id} (improve-only) failed: ${upd.status} ${JSON.stringify(upd.data).slice(0, 300)}`);
    return { id: item.id, outcome: 'improved-only' };
  }

  if (dryRun) {
    return { id: item.id, outcome: 'dry-run-activate', body };
  }

  const upd = await adminFetch(UPDATE_PATH(item.id), { method: 'PUT', body });
  if (!upd.ok) {
    throw new Error(`PUT ${item.id} (activate) failed: ${upd.status} ${JSON.stringify(upd.data).slice(0, 300)}`);
  }
  return { id: item.id, outcome: 'activated' };
}

async function main() {
  if (flags.healthcheck) {
    startRun('publish-vocab-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  startRun('publish-vocab');

  const dryRun = !!flags['dry-run'];
  const limit = flags.limit ? Number(flags.limit) : Infinity;
  const professionFilter = typeof flags.profession === 'string' ? flags.profession : null;
  const onlyId = typeof flags.only === 'string' ? flags.only : null;
  const resumeSet = loadResumeSet();

  let processed = 0;
  let activated = 0;
  let improved = 0;
  let skipped = 0;
  let failed = 0;
  let consecutiveFailures = 0;

  // --only short-circuit: skip pagination, hit one item.
  if (onlyId) {
    try {
      const r = await processOne({ id: onlyId }, { dryRun });
      console.log(`  ${onlyId}: ${r.outcome}${r.reason ? ' — ' + r.reason : ''}`);
      if (r.outcome === 'activated') activated++;
      else if (r.outcome === 'improved-only') improved++;
      else if (r.outcome.startsWith('dry-run')) skipped++;
      else skipped++;
      markResumed(onlyId, r.outcome);
    } catch (e) {
      logFailure('vocab-publish', { id: onlyId }, e);
      failed++;
    }
    endRun({ processed: 1, activated, improved, skipped, failed, dryRun });
    return;
  }

  // Paginated drain. We always fetch page 1 because activating an item
  // removes it from the status=draft filter — the next "page 1" naturally
  // surfaces the next batch. This is more correct than incrementing `page`
  // (which would skip items as drafts disappear).
  while (processed < limit) {
    let pageData;
    try {
      pageData = await fetchDraftsPage(1, professionFilter);
    } catch (e) {
      logFailure('vocab-list', { page: 1, profession: professionFilter }, e);
      failed++;
      if (abortIfFailureCascade(++consecutiveFailures, 'vocab-list')) break;
      await sleep(2000);
      continue;
    }
    consecutiveFailures = 0;

    const items = Array.isArray(pageData?.items) ? pageData.items : [];
    if (items.length === 0) break;
    const total = pageData.total ?? items.length;

    let batchProgress = 0;
    for (const item of items) {
      if (processed >= limit) break;

      if (resumeSet.has(item.id)) {
        skipped++;
        processed++;
        continue;
      }

      processed++;
      batchProgress++;

      try {
        const r = await processOne(item, { dryRun });
        if (r.outcome === 'activated') { activated++; consecutiveFailures = 0; }
        else if (r.outcome === 'improved-only') { improved++; consecutiveFailures = 0; }
        else if (r.outcome.startsWith('dry-run')) { skipped++; consecutiveFailures = 0; }
        else { skipped++; }
        markResumed(item.id, r.outcome);
      } catch (e) {
        logFailure('vocab-publish', { id: item.id, term: item.term }, e);
        failed++;
        if (abortIfFailureCascade(++consecutiveFailures, 'vocab-publish')) {
          endRun({ processed, activated, improved, skipped, failed, dryRun, abortedOnCascade: true });
          return;
        }
      }

      if (processed % 25 === 0) {
        console.log(progress(processed, Math.min(total, limit),
          `activated=${activated} improved=${improved} skipped=${skipped} failed=${failed}`));
      }
    }

    // Safety: if we processed nothing new this loop (e.g. every item was on
    // the resume list and we're not actually shrinking the draft pool),
    // break to avoid an infinite loop.
    if (dryRun || (batchProgress > 0 && activated === 0 && improved === 0)) {
      // In dry-run mode, draft pool doesn't shrink — paginate normally.
      // Otherwise the natural exit is items.length === 0 above.
      if (dryRun && processed < total && processed < limit) {
        // Walk forward through pages without mutating, until limit hit.
        let p = 2;
        while (processed < Math.min(total, limit)) {
          let pg;
          try { pg = await fetchDraftsPage(p, professionFilter); } catch { break; }
          const more = Array.isArray(pg?.items) ? pg.items : [];
          if (more.length === 0) break;
          for (const item of more) {
            if (processed >= limit) break;
            if (resumeSet.has(item.id)) { processed++; skipped++; continue; }
            processed++;
            try {
              const r = await processOne(item, { dryRun: true });
              skipped++;
              markResumed(item.id, r.outcome);
            } catch (e) {
              logFailure('vocab-publish', { id: item.id, term: item.term }, e);
              failed++;
            }
            if (processed % 25 === 0) {
              console.log(progress(processed, Math.min(total, limit),
                `(dry-run) skipped=${skipped} failed=${failed}`));
            }
          }
          p++;
        }
        break;
      }
      if (batchProgress === 0) break;
    }
  }

  endRun({ processed, activated, improved, skipped, failed, dryRun });
}

await main();
