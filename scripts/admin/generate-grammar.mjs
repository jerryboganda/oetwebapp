/**
 * generate-grammar.mjs
 *
 * Bulk-generates new GrammarLesson entries via the grounded admin AI-draft
 * pipeline, then publishes each one. Default plan: 5 topics × 12 professions
 * = 60 new lessons, added on top of whatever already exists.
 *
 * Flow (per (profession, topic) pair):
 *   1. POST /v1/admin/grammar/ai-draft  { prompt, profession, topicSlug, level, targetExerciseCount }
 *        → server creates the lesson server-side via the grounded
 *          AiGatewayService (Kind=Grammar) and returns:
 *            { lessonId, title, contentBlockCount, exerciseCount,
 *              rulebookVersion, appliedRuleIds }
 *        → any warning/unusable draft is treated as a hard failure; scripts
 *          must never publish deterministic starter-template lessons.
 *   2. POST /v1/admin/grammar/lessons/{lessonId}/publish
 *
 * Each generated draft is dumped (full response JSON) to
 *   output/admin-bulk/grammar-drafts-<runId>.jsonl
 * for after-the-fact review. State (completed pairs) is persisted to
 *   output/admin-bulk/grammar-state.json
 * so --resume can skip already-done pairs.
 *
 * Usage:
 *   node scripts/admin/generate-grammar.mjs [flags]
 *
 *   --dry-run                   Print plan, do not call the backend.
 *   --profession <slug>         Only generate for one profession.
 *   --count-per-profession N    Override default (5).
 *   --resume                    Skip (profession, topic) pairs already in state file.
 *   --no-publish                Generate drafts but do not publish.
 *   --healthcheck               Run lib healthcheck and exit.
 *
 * Notes:
 *   - The AI-draft endpoint is platform-grounded; BYOK is refused server-side.
 *     This script only triggers it; model selection lives in backend config.
 *   - Admin accounts are exempt from the free-tier 3-per-week cap
 *     (GrammarEntitlementService). A 429 with errorCode=ai_quota_denied
 *     would still be logged and counted as a failure for that pair.
 */

import { readFileSync, writeFileSync, appendFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, logFailure,
  healthcheck, progress, sleep, slugify, abortIfFailureCascade,
} from './_lib.mjs';

const flags = parseFlags();

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const STATE_PATH = resolve(REPO_ROOT, 'output', 'admin-bulk', 'grammar-state.json');

// ── Profession catalog (matches rulebooks/writing/* slugs; covers the 12
//    OET clinical professions). The grammar rulebook loader falls back to
//    rulebooks/grammar/common for professions without a dedicated grammar
//    rulebook — handled entirely by the backend GrammarDraftService.
const PROFESSIONS = [
  'medicine',
  'nursing',
  'dentistry',
  'dietetics',
  'occupational-therapy',
  'optometry',
  'pharmacy',
  'physiotherapy',
  'podiatry',
  'radiography',
  'speech-pathology',
  'veterinary',
];

// ── Topic catalog (5 per profession). Each topic is cycled into a level
//    + per-profession scenario in the prompt below. Topics are chosen to
//    exercise rulebook categories: conditionals, modals, reported speech,
//    passives, tense consistency.
const TOPICS = [
  {
    slug: 'conditionals-patient-communication',
    title: 'Conditionals in patient communication',
    level: 'intermediate',
    focus: 'first, second, and zero conditionals used when counselling patients about treatment outcomes, side-effects, and lifestyle changes',
  },
  {
    slug: 'modal-verbs-advice-obligation',
    title: 'Modal verbs for clinical advice and obligation',
    level: 'intermediate',
    focus: 'must / should / may / could / might / had better — distinguishing strong recommendation, possibility, and obligation in clinical advice',
  },
  {
    slug: 'reported-speech-clinical-handover',
    title: 'Reported speech in clinical handover',
    level: 'intermediate',
    focus: 'backshift of tense, pronoun changes, and reporting verbs used when relaying patient history, prior clinician decisions, and SBAR-style handover',
  },
  {
    slug: 'passive-voice-medical-documentation',
    title: 'Passive voice in medical documentation',
    level: 'intermediate',
    focus: 'when to prefer passive constructions in case notes, referral letters, and incident reports vs. active voice in patient-facing communication',
  },
  {
    slug: 'tense-consistency-case-notes',
    title: 'Tense consistency in case notes',
    level: 'intermediate',
    focus: 'keeping past simple for completed events, present perfect for ongoing relevance, and avoiding tense drift across paragraphs in a referral letter or discharge summary',
  },
];

// ── State (resume support) ─────────────────────────────

function loadState() {
  if (!existsSync(STATE_PATH)) return { completed: [] };
  try {
    const raw = readFileSync(STATE_PATH, 'utf8');
    const j = JSON.parse(raw);
    if (!Array.isArray(j.completed)) return { completed: [] };
    return j;
  } catch {
    return { completed: [] };
  }
}

function saveState(state) {
  try {
    writeFileSync(STATE_PATH, JSON.stringify(state, null, 2), 'utf8');
  } catch (e) {
    console.log(`  ⚠ failed to persist state file: ${e.message}`);
  }
}

function stateKey(profession, topicSlug) {
  return `${profession}::${topicSlug}`;
}

// ── Prompt builder ─────────────────────────────────────
// The backend builds its own grounded prompt via AiGatewayService.BuildGroundedPrompt
// (Kind=Grammar, Task=GenerateGrammarLesson). Our only job is to send a
// clear admin "Prompt" describing what to author. Keep it concrete and
// rule-citing so the model's appliedRuleIds stay tight.

function buildAdminPrompt({ profession, topic }) {
  const profPretty = profession.replace(/-/g, ' ');
  return [
    `Author a focused OET Grammar Lesson for ${profPretty} candidates.`,
    ``,
    `Topic: ${topic.title}`,
    `Level: ${topic.level}`,
    `Focus: ${topic.focus}`,
    ``,
    `Requirements:`,
    `- Write a concise explanation suitable for an intermediate medical English learner (B2).`,
    `- Use authentic ${profPretty} scenarios in every example (patients, colleagues, settings, terminology).`,
    `- Include ${flags['target-exercises'] || 6} practice exercises drilling the target structures.`,
    `- Cite the grammar rulebook rule ids your explanation and exercises rely on, via the standard appliedRuleIds field.`,
    `- Do NOT invent rule ids; only use rule ids that exist in the loaded grammar rulebook for ${profession} (or the common fallback).`,
    `- Keep tone professional, clinically accurate, and aligned with OET expectations.`,
  ].join('\n');
}

// ── Core: generate + publish one lesson ─────────────────

async function generateOne({ profession, topic, runId, dryRun, doPublish }) {
  const body = {
    prompt: buildAdminPrompt({ profession, topic }),
    profession,
    topicSlug: topic.slug,
    level: topic.level,
    targetExerciseCount: Number(flags['target-exercises']) || 6,
    // ExamTypeCode left undefined → backend normalizes to the platform default.
  };

  if (dryRun) {
    console.log(`  (dry-run) would POST /v1/admin/grammar/ai-draft  profession=${profession}  topic=${topic.slug}`);
    if (doPublish) console.log(`  (dry-run) would POST /v1/admin/grammar/lessons/{id}/publish`);
    return { dryRun: true };
  }

  const draft = await adminFetch('/v1/admin/grammar/ai-draft', {
    method: 'POST',
    body,
    timeoutMs: 180_000,
  });

  if (!draft.ok) {
    const msg = typeof draft.data === 'string'
      ? draft.data.slice(0, 400)
      : JSON.stringify(draft.data).slice(0, 400);
    throw new Error(`ai-draft ${draft.status}: ${msg}`);
  }

  const lessonId = draft.data?.lessonId;
  if (!lessonId) {
    throw new Error(`ai-draft returned no lessonId: ${JSON.stringify(draft.data).slice(0, 400)}`);
  }

  // Persist full draft envelope (for review).
  try {
    appendFileSync(
      `${CONFIG.outputDir}/grammar-drafts-${runId}.jsonl`,
      JSON.stringify({
        ts: new Date().toISOString(),
        profession,
        topic: topic.slug,
        topicTitle: topic.title,
        request: body,
        response: draft.data,
      }) + '\n',
      'utf8',
    );
  } catch {}

  if (draft.data?.warning) {
    throw new Error(`ai-draft returned warning; refusing to publish fallback/starter content: ${String(draft.data.warning).slice(0, 300)}`);
  }

  const applied = Array.isArray(draft.data?.appliedRuleIds) ? draft.data.appliedRuleIds.length : 0;
  console.log(`  ✓ drafted lesson ${lessonId} "${draft.data?.title || '(untitled)'}"  blocks=${draft.data?.contentBlockCount}  exercises=${draft.data?.exerciseCount}  appliedRules=${applied}  rulebook=${draft.data?.rulebookVersion ?? '?'}`);

  let published = false;
  if (doPublish) {
    const pub = await adminFetch(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish`, {
      method: 'POST',
      body: {},
    });
    if (!pub.ok) {
      // 422 means publish-gate rejected — keep the lesson as draft, log and move on.
      const msg = typeof pub.data === 'string' ? pub.data.slice(0, 400) : JSON.stringify(pub.data).slice(0, 400);
      throw new Error(`publish ${pub.status} for ${lessonId}: ${msg}`);
    }
    published = pub.data?.published === true;
    console.log(`  ✓ published ${lessonId}  status=${pub.data?.status ?? 'unknown'}`);
  }

  return { lessonId, published, draft: draft.data };
}

// ── Main ───────────────────────────────────────────────

async function main() {
  if (flags.healthcheck) {
    startRun('generate-grammar-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const runId = startRun('generate-grammar');
  const dryRun = !!flags['dry-run'];
  const doPublish = !flags['no-publish'];
  const resume = !!flags.resume;
  const onlyProfession = flags.profession ? slugify(String(flags.profession)) : null;
  const countPerProfession = Math.max(1, Math.min(TOPICS.length, Number(flags['count-per-profession']) || 5));

  const professions = onlyProfession
    ? PROFESSIONS.filter(p => p === onlyProfession)
    : PROFESSIONS;

  if (onlyProfession && professions.length === 0) {
    console.error(`Unknown --profession "${flags.profession}". Known: ${PROFESSIONS.join(', ')}`);
    endRun({ planned: 0 });
    process.exit(2);
  }

  // Build (profession, topic) plan.
  const plan = [];
  for (const profession of professions) {
    for (let i = 0; i < countPerProfession; i++) {
      plan.push({ profession, topic: TOPICS[i] });
    }
  }

  const state = loadState();
  const completedSet = new Set(state.completed);
  const targets = resume
    ? plan.filter(p => !completedSet.has(stateKey(p.profession, p.topic.slug)))
    : plan;

  console.log(`Plan: ${plan.length} lessons across ${professions.length} professions (${countPerProfession}/profession).`);
  console.log(`Targets after resume filter: ${targets.length}.`);
  console.log(`Publish after draft: ${doPublish ? 'yes' : 'no'}.`);
  console.log(`Dry-run: ${dryRun ? 'yes' : 'no'}.`);

  let drafted = 0;
  let published = 0;
  let failed = 0;
  let skipped = plan.length - targets.length;
  let consecutiveFailures = 0;

  for (let i = 0; i < targets.length; i++) {
    const t = targets[i];
    const label = `${t.profession} • ${t.topic.slug}`;
    console.log(progress(i + 1, targets.length, label));

    try {
      const r = await generateOne({ profession: t.profession, topic: t.topic, runId, dryRun, doPublish });
      if (r.dryRun) {
        skipped++;
      } else {
        drafted++;
        if (r.published) published++;
        completedSet.add(stateKey(t.profession, t.topic.slug));
        state.completed = Array.from(completedSet);
        saveState(state);
        consecutiveFailures = 0;
      }
    } catch (e) {
      failed++;
      consecutiveFailures++;
      logFailure('grammar-generate', { profession: t.profession, topic: t.topic.slug }, e);
      if (abortIfFailureCascade(consecutiveFailures, 'grammar-generate')) break;
    }

    if ((i + 1) % 5 === 0) {
      console.log(`  — checkpoint: drafted=${drafted} published=${published} failed=${failed} skipped=${skipped}`);
    }

    // Gentle pacing between AI-heavy calls (admin bucket already throttles to 4 rps).
    await sleep(150);
  }

  endRun({
    planned: plan.length,
    targets: targets.length,
    drafted,
    published,
    failed,
    skipped,
    stateFile: STATE_PATH,
  });

  process.exit(failed > 0 && drafted === 0 ? 1 : 0);
}

await main();
