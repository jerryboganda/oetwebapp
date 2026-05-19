/**
 * generate-pronunciation.mjs
 *
 * Bulk-authors PronunciationDrill entries across all 12 professions and
 * activates them via the admin CMS endpoints. Uses the grounded admin
 * AI-draft endpoint when available, then POSTs to create + PUTs to activate
 * (the PUT runs the publish gate inside AdminService).
 *
 *   POST /v1/admin/pronunciation/drills/ai-draft    (grounded; platform-only)
 *   POST /v1/admin/pronunciation/drills              (create — Status="draft")
 *   PUT  /v1/admin/pronunciation/drills/{drillId}    (Status="active" → publish gate)
 *
 * Publish gate (AdminService.ContentAdmin.cs:1815): requires phoneme + label
 * + tips + ≥3 example words + ≥1 sentence. Failures are logged with full
 * payload + error so they can be retried.
 *
 * Usage:
 *   node scripts/admin/generate-pronunciation.mjs [flags]
 *
 *   --dry-run                          Print what would be done, no admin writes.
 *   --profession <slug>                Limit to one profession (default: all 12).
 *   --count-per-profession <n>         Drills per profession (default 5).
 *   --resume                           Skip professions whose target count was
 *                                      already met in a previous run (uses
 *                                      output/admin-bulk/pronunciation-progress.json).
 *   --no-ai-draft                      Skip the admin /ai-draft endpoint and build
 *                                      the draft locally via aiChatJson.
 *   --healthcheck                      Run lib healthcheck and exit.
 *
 *   --chat-model <id>                  Override CONFIG.ai.chatModel.
 *   --api-base <url>                   Override CONFIG.apiBase.
 */

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, aiChatJson, logFailure,
  healthcheck, progress, sleep, makeProvenance,
} from './_lib.mjs';

const flags = parseFlags();

// ── Catalog of 12 OET professions ───────────────────────────────────────────

const ALL_PROFESSIONS = [
  'medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy', 'veterinary',
  'optometry', 'radiography', 'occupationaltherapy', 'speechpathology',
  'podiatry', 'dietetics',
];

// ── Phoneme pool — challenging targets for non-native OET candidates ────────
// Each entry: { phoneme (IPA), focus, hint (varies the draft prompt) }
const PHONEME_POOL = [
  { phoneme: 'θ', focus: 'phoneme', hint: 'voiceless dental fricative — "th" as in "think", "throat", "thorough"' },
  { phoneme: 'ð', focus: 'phoneme', hint: 'voiced dental fricative — "th" as in "this", "breathe", "soothe"' },
  { phoneme: 'ʃ', focus: 'phoneme', hint: '"sh" sound — as in "shoulder", "tissue", "incision"' },
  { phoneme: 'ʒ', focus: 'phoneme', hint: 'voiced post-alveolar — as in "vision", "occasion", "lesion"' },
  { phoneme: 'tʃ', focus: 'phoneme', hint: '"ch" as in "chest", "fracture", "natural"' },
  { phoneme: 'dʒ', focus: 'phoneme', hint: '"j" or "ge" — as in "injection", "discharge", "manage"' },
  { phoneme: 'ŋ', focus: 'phoneme', hint: '"ng" as in "swallowing", "breathing", "tongue"' },
  { phoneme: 'r', focus: 'phoneme', hint: 'post-alveolar approximant — as in "respiratory", "referral", "rehabilitation"' },
  { phoneme: 'l', focus: 'phoneme', hint: 'lateral approximant (clear vs dark) — as in "scalpel", "label", "tablet"' },
  { phoneme: 'v', focus: 'phoneme', hint: 'voiced labiodental — as in "vein", "vital", "valve"' },
  { phoneme: 'w', focus: 'phoneme', hint: 'voiced labial-velar — distinguishing from /v/, as in "wound", "wheeze"' },
  { phoneme: 'æ', focus: 'phoneme', hint: 'short open front vowel — as in "abdomen", "anaphylaxis", "rash"' },
  { phoneme: 'ɪ', focus: 'phoneme', hint: 'short close-front vowel — as in "vitals", "hip", "stitches"' },
  { phoneme: 'iː', focus: 'phoneme', hint: 'long close-front vowel — as in "fever", "wheeze", "needle"' },
  { phoneme: 'ʌ', focus: 'phoneme', hint: 'open-mid back vowel — as in "blood", "tongue", "lung"' },
  { phoneme: 'ɜː', focus: 'phoneme', hint: 'long mid-central vowel — as in "nurse", "burn", "surgery"' },
  { phoneme: 'ə', focus: 'phoneme', hint: 'schwa (unstressed) — as in "patient", "doctor", "medication"' },
  { phoneme: 'aɪ', focus: 'phoneme', hint: 'diphthong — as in "iodine", "diabetes", "minor"' },
  { phoneme: 'eɪ', focus: 'phoneme', hint: 'diphthong — as in "patient", "pain", "vaccinate"' },
  { phoneme: 'əʊ', focus: 'phoneme', hint: 'diphthong — as in "throat", "dose", "post-operative"' },
  { phoneme: 'stress', focus: 'stress', hint: 'multi-syllable medical word stress — placement on correct syllable in words like "diagnosis", "anaesthesia", "physiotherapy"' },
  { phoneme: 'intonation', focus: 'intonation', hint: 'rising vs falling intonation in clinical questions and reassurance' },
];

// ── Per-profession vocabulary hints (steer the AI toward field-appropriate words) ──

const PROFESSION_HINT = {
  medicine: 'general internal medicine + emergency: vocabulary like diagnosis, prescription, symptoms, examination',
  nursing: 'bedside nursing care: vocabulary like wound dressing, vitals, medication round, handover',
  dentistry: 'dental care: vocabulary like cavity, molar, anaesthesia, extraction, occlusion',
  pharmacy: 'community/hospital pharmacy: vocabulary like dispensing, interactions, dosage, counselling',
  physiotherapy: 'musculoskeletal/rehab: vocabulary like mobilisation, gait, range-of-motion, exercise prescription',
  veterinary: 'small animal practice: vocabulary like vaccination, neutering, anaesthetic, owner counselling',
  optometry: 'eye care: vocabulary like refraction, intraocular pressure, retina, prescription lenses',
  radiography: 'medical imaging: vocabulary like X-ray, contrast, positioning, exposure, MRI safety',
  occupationaltherapy: 'OT rehab: vocabulary like activities of daily living, adaptive equipment, splinting',
  speechpathology: 'speech & swallowing therapy: vocabulary like dysphagia, articulation, aphasia, voice therapy',
  podiatry: 'foot care: vocabulary like callus, plantar, orthotic, ingrown toenail, diabetic foot',
  dietetics: 'nutrition counselling: vocabulary like macronutrients, enteral feeding, BMI, dietary plan',
};

// ── Resume state ────────────────────────────────────────────────────────────

const STATE_PATH = resolve(CONFIG.outputDir, 'pronunciation-progress.json');

function loadState() {
  if (!existsSync(STATE_PATH)) return { perProfession: {} };
  try { return JSON.parse(readFileSync(STATE_PATH, 'utf8')); } catch { return { perProfession: {} }; }
}

function saveState(state) {
  try { writeFileSync(STATE_PATH, JSON.stringify(state, null, 2), 'utf8'); } catch {}
}

// ── Endpoint discovery: probe whether /ai-draft is reachable for this admin ──

async function probeAiDraftEndpoint() {
  const r = await adminFetch('/v1/admin/pronunciation/drills/ai-draft', {
    method: 'POST',
    body: { Phoneme: 'θ', Focus: 'phoneme', Profession: 'medicine', Difficulty: 'medium' },
    retries: 0,
  });
  // 200 → works.  4xx (other than 404/405) likely means validation/policy issue
  // but the route exists. 404/405 → fall back to local AI generation.
  if (r.ok) return { available: true, sample: r.data };
  if (r.status === 404 || r.status === 405) return { available: false, sample: null };
  // Any other status — assume the route exists; we may still use it for real calls.
  return { available: true, sample: null, probeStatus: r.status };
}

// ── Draft generators ────────────────────────────────────────────────────────

async function generateDraftViaAdminEndpoint(profession, slot) {
  const r = await adminFetch('/v1/admin/pronunciation/drills/ai-draft', {
    method: 'POST',
    body: {
      Phoneme: slot.phoneme,
      Focus: slot.focus,
      Profession: profession,
      Difficulty: slot.difficulty || 'medium',
      Prompt: `${slot.hint}\nContext: ${PROFESSION_HINT[profession] || ''}`,
      PrimaryRuleId: null,
    },
    retries: 2,
  });
  if (!r.ok) throw new Error(`ai-draft ${r.status}: ${JSON.stringify(r.data).slice(0, 300)}`);
  // The .NET draft DTO uses PascalCase; ASP.NET serialises as camelCase by default.
  const d = r.data || {};
  return normalizeDraft(d, slot);
}

async function generateDraftViaLocalAi(profession, slot) {
  const system = `You are an OET pronunciation drill author. Produce a single drill targeting the given phoneme/feature for the named profession. Use profession-appropriate medical vocabulary. Reply with strict JSON only (no markdown fences) matching the schema in the user message.`;
  const user = [
    `Target phoneme or feature: /${slot.phoneme}/`,
    `Focus category: ${slot.focus}`,
    `Profession: ${profession} — ${PROFESSION_HINT[profession] || ''}`,
    `Difficulty: ${slot.difficulty || 'medium'}`,
    `Hint: ${slot.hint}`,
    '',
    'Reply with JSON of this exact shape:',
    '{',
    '  "targetPhoneme": "<IPA symbol or feature name>",',
    '  "label": "<short title for the drill, e.g. \\"Voiceless TH — /θ/\\">",',
    '  "difficulty": "easy" | "medium" | "hard",',
    '  "focus": "phoneme" | "stress" | "intonation",',
    '  "exampleWords": ["word1", "word2", "word3", ...]  (at least 6 words, profession-appropriate),',
    '  "minimalPairs": [{"a": "word", "b": "minimal-pair-word"}, ...] (2-4 pairs),',
    '  "sentences": ["full clinical sentence with several target sounds", ...] (at least 2),',
    '  "tipsHtml": "<p>One short HTML paragraph (use <p>, <strong>, <em>, <ul>, <li> only) explaining articulation and a common mistake.</p>",',
    '  "appliedRuleIds": [] (leave empty — we cannot validate without the rulebook here)',
    '}',
  ].join('\n');

  const r = await aiChatJson({
    system,
    user,
    temperature: 0.6,
    maxTokens: 1200,
  });
  return normalizeDraft(r.json, slot);
}

function normalizeDraft(raw, slot) {
  const obj = raw || {};
  const target = obj.targetPhoneme || obj.TargetPhoneme || slot.phoneme;
  const label = obj.label || obj.Label || `Pronunciation drill — /${target}/`;
  const difficulty = obj.difficulty || obj.Difficulty || slot.difficulty || 'medium';
  const focus = obj.focus || obj.Focus || slot.focus || 'phoneme';
  const exampleWords = arr(obj.exampleWords ?? obj.ExampleWords);
  const sentences = arr(obj.sentences ?? obj.Sentences);
  const minimalPairs = arrOfPairs(obj.minimalPairs ?? obj.MinimalPairs);
  const tipsHtml = (obj.tipsHtml ?? obj.TipsHtml ?? '').toString();
  const appliedRuleIds = arr(obj.appliedRuleIds ?? obj.AppliedRuleIds);
  const primaryRuleId = obj.primaryRuleId ?? obj.PrimaryRuleId ?? (appliedRuleIds[0] || null);
  return {
    target, label, difficulty, focus, exampleWords, sentences, minimalPairs,
    tipsHtml, appliedRuleIds, primaryRuleId,
  };
}

function arr(v) {
  if (!v) return [];
  if (Array.isArray(v)) return v.filter(Boolean).map(String);
  return [];
}
function arrOfPairs(v) {
  if (!Array.isArray(v)) return [];
  return v
    .map(p => {
      if (!p || typeof p !== 'object') return null;
      const a = (p.a ?? p.A ?? '').toString().trim();
      const b = (p.b ?? p.B ?? '').toString().trim();
      return a && b ? { a, b } : null;
    })
    .filter(Boolean);
}

// ── Publish gate enforcement (mirror backend check before we POST) ──────────

function meetsPublishGate(draft) {
  return (
    draft.target && draft.target.trim() &&
    draft.label && draft.label.trim() &&
    draft.tipsHtml && draft.tipsHtml.trim() &&
    Array.isArray(draft.exampleWords) && draft.exampleWords.length >= 3 &&
    Array.isArray(draft.sentences) && draft.sentences.length >= 1
  );
}

// ── Create + activate ───────────────────────────────────────────────────────

async function createAndActivate(profession, draft) {
  const provenance = makeProvenance({
    kind: 'pronunciation-drill',
    profession,
    model: CONFIG.ai.chatModel,
  });
  // Stamp provenance into TipsHtml as a discreet HTML comment so the rendered
  // drill is unchanged but the audit trail survives. SafeHtmlSanitizer strips
  // comments, so also append a small italic credit line.
  const tipsWithProvenance = `${draft.tipsHtml}\n<p><em>Authoring note: ${escapeHtml(provenance)}</em></p>`;

  const createBody = {
    Word: draft.label,
    PhoneticTranscription: draft.target,
    Profession: profession,
    Focus: draft.focus,
    PrimaryRuleId: draft.primaryRuleId || null,
    Difficulty: draft.difficulty,
    ExampleWordsJson: JSON.stringify(draft.exampleWords),
    MinimalPairsJson: JSON.stringify(draft.minimalPairs),
    SentencesJson: JSON.stringify(draft.sentences),
    TipsHtml: tipsWithProvenance,
    Status: 'draft',
  };

  const cr = await adminFetch('/v1/admin/pronunciation/drills', {
    method: 'POST',
    body: createBody,
  });
  if (!cr.ok) throw new Error(`create ${cr.status}: ${JSON.stringify(cr.data).slice(0, 300)}`);
  const id = cr.data?.id;
  if (!id) throw new Error(`create returned no id: ${JSON.stringify(cr.data).slice(0, 200)}`);

  // Activate (triggers publish gate inside AdminService).
  const upd = await adminFetch(`/v1/admin/pronunciation/drills/${id}`, {
    method: 'PUT',
    body: { Status: 'active' },
  });
  if (!upd.ok) {
    // Don't archive — leave the draft for manual fix-up. Re-throw with payload.
    const err = new Error(`activate ${upd.status}: ${JSON.stringify(upd.data).slice(0, 300)}`);
    err.draftPayload = createBody;
    err.drillId = id;
    throw err;
  }
  return { id, status: upd.data?.status || 'active' };
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]));
}

// ── Main ────────────────────────────────────────────────────────────────────

async function main() {
  if (flags.healthcheck) {
    startRun('generate-pronunciation-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const dryRun = !!flags['dry-run'];
  const resume = !!flags.resume;
  const noAiDraft = !!flags['no-ai-draft'];
  const countPerProfession = Math.max(
    1, parseInt(flags['count-per-profession'] || '5', 10) || 5,
  );
  const professions = flags.profession
    ? [String(flags.profession).toLowerCase()]
    : ALL_PROFESSIONS.slice();
  for (const p of professions) {
    if (!ALL_PROFESSIONS.includes(p)) {
      console.error(`Unknown profession: ${p}. Known: ${ALL_PROFESSIONS.join(', ')}`);
      process.exit(2);
    }
  }

  startRun('generate-pronunciation');
  const state = resume ? loadState() : { perProfession: {} };

  // Endpoint discovery (skipped on dry-run / --no-ai-draft).
  let useAdminDraft = false;
  if (!dryRun && !noAiDraft) {
    try {
      const probe = await probeAiDraftEndpoint();
      useAdminDraft = probe.available;
      console.log(`  AI draft endpoint: ${useAdminDraft ? 'available — using /v1/admin/pronunciation/drills/ai-draft' : 'unavailable — falling back to local aiChatJson'}`);
    } catch (e) {
      console.log(`  AI draft probe failed (${e.message}) — falling back to local aiChatJson`);
      useAdminDraft = false;
    }
  } else if (noAiDraft) {
    console.log('  --no-ai-draft set — using local aiChatJson for all drills');
  }

  const totalTarget = professions.length * countPerProfession;
  let totalCreated = 0, totalActivated = 0, totalFailed = 0, totalSkipped = 0;
  let i = 0;

  for (const profession of professions) {
    const alreadyDone = state.perProfession[profession] || 0;
    const needed = Math.max(0, countPerProfession - alreadyDone);
    if (resume && needed === 0) {
      console.log(`▶ ${profession}: already at target (${alreadyDone}/${countPerProfession}) — skipping`);
      totalSkipped += countPerProfession;
      i += countPerProfession;
      continue;
    }

    console.log(`\n▶ Profession: ${profession}  (target ${countPerProfession}, resume offset ${alreadyDone})`);

    let consecutiveFailures = 0;
    for (let n = 0; n < needed; n++) {
      i++;
      // Rotate phoneme pool by an offset that varies per profession so each
      // profession gets a different mix.
      const poolIndex = (ALL_PROFESSIONS.indexOf(profession) * countPerProfession + alreadyDone + n) % PHONEME_POOL.length;
      const slot = {
        ...PHONEME_POOL[poolIndex],
        difficulty: ['easy', 'medium', 'medium', 'hard'][n % 4] || 'medium',
      };
      const label = `${profession} /${slot.phoneme}/ (${slot.difficulty})`;
      console.log(progress(i, totalTarget, label));

      if (dryRun) {
        console.log(`  (dry-run) would generate draft + create + activate`);
        totalSkipped++;
        continue;
      }

      let draft;
      try {
        draft = useAdminDraft
          ? await generateDraftViaAdminEndpoint(profession, slot)
          : await generateDraftViaLocalAi(profession, slot);
      } catch (e) {
        logFailure('pronunciation-draft', { profession, slot }, e);
        totalFailed++;
        consecutiveFailures++;
        if (consecutiveFailures >= 6) {
          console.log(`  ⚠ ${consecutiveFailures} consecutive draft failures for ${profession} — moving on`);
          break;
        }
        continue;
      }

      if (!meetsPublishGate(draft)) {
        logFailure('pronunciation-gate', { profession, slot, draft }, new Error(
          `Draft fails publish gate: need phoneme + label + tips + ≥3 example words + ≥1 sentence; got words=${draft.exampleWords.length}, sentences=${draft.sentences.length}, tips=${(draft.tipsHtml || '').length > 0}`,
        ));
        totalFailed++;
        consecutiveFailures++;
        continue;
      }

      try {
        const result = await createAndActivate(profession, draft);
        totalCreated++;
        totalActivated++;
        consecutiveFailures = 0;
        state.perProfession[profession] = (state.perProfession[profession] || 0) + 1;
        if (totalCreated % 5 === 0) {
          console.log(`  ✓ ${totalCreated}/${totalTarget} drills created so far`);
          saveState(state);
        }
        console.log(`    → ${result.id} status=${result.status}`);
      } catch (e) {
        logFailure('pronunciation-create-or-activate', {
          profession,
          slot,
          draft,
          payload: e.draftPayload,
          drillId: e.drillId,
        }, e);
        totalFailed++;
        consecutiveFailures++;
      }

      // Be polite to admin rate limits (bucket already throttles; this is belt-and-braces).
      await sleep(150);
    }

    saveState(state);
  }

  endRun({
    professions: professions.length,
    targetPerProfession: countPerProfession,
    totalTarget,
    created: totalCreated,
    activated: totalActivated,
    failed: totalFailed,
    skipped: totalSkipped,
  });

  process.exit(totalFailed > 0 && totalCreated === 0 ? 1 : 0);
}

main().catch(e => {
  console.error(e);
  process.exit(2);
});
