// Pure decisions of the Writing production QA harness (no network, no browser): script validation, inputs,
// discovery plan, credit / timer / provider verdicts, preflight + guard decisions, the lane mutex and pacing,
// report checks and the QA-2 evidence table. lib.test.ts covered every function here until it was deleted 2026-10-08.
import { createHash } from 'node:crypto';
import {
  CATEGORIES, CATEGORY_LETTER_TYPE, CONTRACT_GROUPS, CORRECTIONS_PREVIEW, CREDITS_PER_LETTER, EMPTY_MODEL, FALLBACK_LETTER_TYPES,
  GRADING_STEP_MODEL_ANSWER, HANDOFF_PROFESSIONS, LEDGER, PROFESSION_ALIASES, PROVIDERS, RESULT_SECTION_ORDER,
  RESULT_SECTION_PRIORITIES, RESULT_SECTIONS_REQUIRED,
} from './contract.mjs';

export const STATUSES = ['PASS', 'FAIL', 'PARTIAL', 'NOT_RUN', 'BLOCKED', 'VOID_DEPLOY', 'NOT_ENABLED'];

// ---- Professions, letter types, scripts -----------------------------------------------------------------------

// Lower case, '_' and spaces to '-', aliases folded ('veterinary_science' -> 'veterinary').
export function normalizeProfessionId(value) {
  const id = String(value ?? '').trim().toLowerCase().replace(/[\s_]+/g, '-').replace(/-+/g, '-');
  return PROFESSION_ALIASES[id] ?? id;
}

// Mirror of WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType: any token to an LT-* code, unknown -> LT-OT.
const LETTER_TYPE_TOKENS = {
  'LT-RR': ['LT-RR', 'ROUTINE_REFERRAL', 'REFERRAL', 'ROUTINE'],
  'LT-UR': ['LT-UR', 'URGENT_REFERRAL', 'URGENT'],
  'LT-DG': ['LT-DG', 'DISCHARGE', 'UPDATE_DISCHARGE', 'UPDATE-DISCHARGE'],
  'LT-TR': ['LT-TR', 'TRANSFER', 'TRANSFER_LETTER', 'TRANSFER-LETTER'],
  'LT-NM': ['LT-NM', 'NON_MEDICAL_REFERRAL', 'NON-MEDICAL', 'NON_MEDICAL'],
};
export function letterTypeCode(value) {
  const token = String(value ?? '').trim().toUpperCase();
  return Object.keys(LETTER_TYPE_TOKENS).find((code) => LETTER_TYPE_TOKENS[code].includes(token)) ?? 'LT-OT';
}

// Characters that fire a Tiptap StarterKit input rule (heading, quote, lists, code, bold/italic/strike) or that
// a keyboard cannot type as-is. The scripts are plain prose, so any of these is a transcription error.
const INPUT_RULE_CHARS = /[#>*_`~|\\]/;
const LEADING_RULE = /^\s*([-+]|\d+[.)])\s/;
const PRINTABLE_ASCII = /^[\x20-\x7E]+$/;

export function validateScripts(doc) {
  const problems = [];
  const scripts = Array.isArray(doc?.scripts) ? doc.scripts : [];
  if (scripts.length !== HANDOFF_PROFESSIONS.length * CATEGORIES.length) {
    problems.push(`expected ${HANDOFF_PROFESSIONS.length * CATEGORIES.length} scripts, found ${scripts.length}`);
  }
  const seen = new Set();
  scripts.forEach((s, i) => {
    const at = `script ${i + 1} (${s?.profession}/${s?.category})`;
    if (!HANDOFF_PROFESSIONS.includes(s?.profession)) problems.push(`${at}: unknown profession id`);
    if (normalizeProfessionId(s?.profession) !== s?.profession) problems.push(`${at}: profession id is not normalised`);
    if (!CATEGORIES.includes(s?.category)) problems.push(`${at}: unknown category`);
    const key = `${s?.profession}/${s?.category}`;
    if (seen.has(key)) problems.push(`${at}: duplicate profession/category`);
    seen.add(key);
    for (const field of ['label', 'title', 'focus']) if (!String(s?.[field] ?? '').trim()) problems.push(`${at}: ${field} is empty`);
    const text = String(s?.text ?? '');
    if (!text.startsWith('Dear ')) problems.push(`${at}: does not start with "Dear "`);
    if (/[\r\n]/.test(text)) problems.push(`${at}: more than one paragraph`);
    if (!PRINTABLE_ASCII.test(text)) problems.push(`${at}: not printable ASCII`);
    if (INPUT_RULE_CHARS.test(text) || LEADING_RULE.test(text)) problems.push(`${at}: contains an editor input-rule trigger`);
    if (text !== text.trim() || /\s{2}/.test(text)) problems.push(`${at}: stray whitespace`);
  });
  for (const profession of HANDOFF_PROFESSIONS) {
    for (const category of CATEGORIES) if (!seen.has(`${profession}/${category}`)) problems.push(`missing script ${profession}/${category}`);
  }
  return { ok: problems.length === 0, problems };
}

export const scriptFor = (scripts, profession, category) => scripts.find((s) => s.profession === profession && s.category === category) ?? null;

// ---- Realistic letters (suite=letters) -----------------------------------------------------------------------------

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const PRINTABLE_ASCII_TEXT = /^[\x20-\x7E\n]+$/;
export const LETTER_WORDS = { min: 200, max: 300 };

/** Words as the editor counts them (whitespace-separated). */
export const wordCount = (text) => String(text ?? '').trim().split(/\s+/).filter(Boolean).length;

/**
 * The fixture letters are typed like a candidate: printable ASCII, no editor input-rule trigger, paragraphs split by
 * a blank line, hard line breaks by one newline, each pinned to ONE production scenario (a letter graded against
 * another task's case notes would make every fact look invented) and mixing major and minor defects.
 */
export function validateLetters(doc) {
  const problems = [];
  const letters = Array.isArray(doc?.letters) ? doc.letters : [];
  if (!letters.length) problems.push('no letters');
  const ids = new Set();
  const scenarios = new Set();
  letters.forEach((l, i) => {
    const at = `letter ${i + 1} (${l?.id})`;
    if (!String(l?.id ?? '').trim()) problems.push(`${at}: no id`);
    if (ids.has(l?.id)) problems.push(`${at}: duplicate id`);
    ids.add(l?.id);
    if (!HANDOFF_PROFESSIONS.includes(l?.profession)) problems.push(`${at}: unknown profession id`);
    if (!GUID.test(String(l?.scenarioId ?? ''))) problems.push(`${at}: scenarioId is not a GUID`);
    if (scenarios.has(String(l?.scenarioId).toLowerCase())) problems.push(`${at}: scenario used by another letter (needs its own learner)`);
    scenarios.add(String(l?.scenarioId).toLowerCase());
    const d = l?.plantedDefects ?? {};
    if (!((d.critical ?? 0) + (d.major ?? 0) > 0 && (d.minor ?? 0) > 0)) problems.push(`${at}: must mix major/critical and minor defects`);
    const text = String(l?.text ?? '');
    const words = wordCount(text);
    if (words < LETTER_WORDS.min || words > LETTER_WORDS.max) problems.push(`${at}: ${words} words, expected ${LETTER_WORDS.min}-${LETTER_WORDS.max}`);
    if (!PRINTABLE_ASCII_TEXT.test(text)) problems.push(`${at}: not printable ASCII`);
    if (/\n\n\n|^\s|\s$|[ \t]\n|\n[ \t]/.test(text)) problems.push(`${at}: stray whitespace`);
    if (INPUT_RULE_CHARS.test(text) || text.split('\n').some((line) => LEADING_RULE.test(line))) problems.push(`${at}: contains an editor input-rule trigger`);
  });
  return { ok: problems.length === 0, problems };
}

/**
 * Each letter goes to the task of its own scenario, on a learner of its own profession. A letter whose profession
 * is not enabled, or whose scenario a learner cannot open, is BLOCKED with the reason: never given another task.
 */
export function matchLetters(plan, letters) {
  const runs = [];
  const blocked = [];
  for (const letter of letters) {
    const prof = plan.find((p) => p.profession === letter.profession);
    if (!prof?.enabled) { blocked.push({ letter, reason: `${letter.profession} is not enabled for learners` }); continue; }
    const task = (prof.eligibleTasks ?? []).find((t) => t.scenarioId.toLowerCase() === letter.scenarioId.toLowerCase());
    if (!task) { blocked.push({ letter, reason: `scenario ${letter.scenarioId} is not an eligible task for a ${letter.profession} learner` }); continue; }
    let run = runs.find((r) => r.profession.profession === prof.profession);
    if (!run) runs.push((run = { profession: prof, letters: [] }));
    run.letters.push({ letter, task });
  }
  return { runs, blocked };
}

// The script whose category best matches a task's letter type (free-sample task, closest-category rule).
export function categoryForLetterType(letterType) {
  const code = letterTypeCode(letterType);
  return CATEGORIES.find((c) => CATEGORY_LETTER_TYPE[c] === code)
    ?? CATEGORIES.find((c) => FALLBACK_LETTER_TYPES[c][0] === code)
    ?? 'routine';
}

// ---- Workflow inputs --------------------------------------------------------------------------------------------

export const SUITES = ['discover', 'matrix', 'acceptance', 'ui', 'letters', 'all'];
const list = (raw) => String(raw ?? '').split(',').map((v) => v.trim()).filter(Boolean);
const bool = (raw, name, problems) => {
  const v = String(raw ?? '').trim().toLowerCase();
  if (v === 'true') return true;
  if (v === 'false' || v === '') return false;
  problems.push(`${name} must be true or false`);
  return false;
};

// Validates the dispatch inputs (env names = upper-cased input names). Throws one error listing every problem.
export function parseInputs(env) {
  const problems = [];
  const pick = (name, allowed, fallback) => {
    const v = String(env[name] ?? '').trim() || fallback;
    if (!allowed.includes(v)) problems.push(`${name}=${v} is not one of ${allowed.join(', ')}`);
    return v;
  };
  const int = (name, fallback, min, max) => {
    const raw = String(env[name] ?? '').trim() || String(fallback);
    const v = Number(raw);
    if (!Number.isInteger(v) || v < min || v > max) problems.push(`${name}=${raw} must be a whole number from ${min} to ${max}`);
    return v;
  };
  const professions = list(env.PROFESSIONS).map(normalizeProfessionId);
  for (const p of professions) if (!HANDOFF_PROFESSIONS.includes(p)) problems.push(`PROFESSIONS: unknown profession ${p}`);
  const categories = list(env.CATEGORIES);
  for (const c of categories) if (!CATEGORIES.includes(c)) problems.push(`CATEGORIES: unknown category ${c}`);
  const browsers = list(env.BROWSERS || 'chromium');
  for (const b of browsers) if (!['chromium', 'webkit'].includes(b)) problems.push(`BROWSERS: unknown browser ${b}`);
  const inputs = {
    suite: pick('SUITE', SUITES, 'discover'),
    professions: professions.length ? professions : [...HANDOFF_PROFESSIONS],
    categories: categories.length ? categories : [...CATEGORIES],
    readingWindow: pick('READING_WINDOW', ['real', 'seed'], 'real'),
    concurrency: int('CONCURRENCY', 3, 1, 3),
    paceSeconds: int('PACE_SECONDS', 35, 35, 600),
    faultMode: pick('FAULT_MODE', ['none', 'flag', 'client'], 'flag'),
    verifyCredits: bool(env.VERIFY_CREDITS ?? 'true', 'VERIFY_CREDITS', problems),
    requireL2Disabled: bool(env.REQUIRE_L2_DISABLED, 'REQUIRE_L2_DISABLED', problems),
    preflightRepair: bool(env.PREFLIGHT_REPAIR, 'PREFLIGHT_REPAIR', problems),
    browsers,
    cleanup: pick('CLEANUP', ['on_success', 'always', 'never'], 'on_success'),
  };
  if (problems.length) throw new Error(`Invalid workflow inputs: ${problems.join('; ')}`);
  return inputs;
}

export const suiteRuns = (suite, name) => suite === 'all' || suite === name;

// ---- Discovery plan -----------------------------------------------------------------------------------------------

// Which handoff professions can be tested live, and with which tasks. ENABLED = present AND active in the
// profession catalogue AND >= 3 eligible tasks as the learner library shows them (WritingScenarioService
// compares Profession.ToLower() with the account's ActiveProfessionId exactly, so only an exact spelling
// counts). Eligible = publishReady && candidateVisible (catalogue-compatibility) && loadOk (load-integrity).
// Every other profession is NOT_ENABLED with evidence, never dropped.
//   input { catalog, compatibility, integrity, professions?, categories? }
export function planDiscovery(input) {
  const { catalog, compatibility, integrity, professions = HANDOFF_PROFESSIONS, categories = CATEGORIES } = input;
  const loadOk = new Map((integrity?.rows ?? []).map((r) => [String(r.scenarioId).toLowerCase(), r.loadOk === true]));
  const tasks = (compatibility?.rows ?? []).map((r) => ({
    scenarioId: String(r.scenarioId),
    title: String(r.taskTitle ?? r.title ?? ''),
    profession: String(r.profession ?? ''),
    letterType: letterTypeCode(r.letterType),
    eligible: r.publishReady === true && r.candidateVisible === true && loadOk.get(String(r.scenarioId).toLowerCase()) === true,
  })).sort((a, b) => a.title.localeCompare(b.title) || a.scenarioId.localeCompare(b.scenarioId));
  const entries = catalog?.professions ?? [];
  return HANDOFF_PROFESSIONS.filter((p) => professions.includes(p)).map((profession) => {
    const entry = entries.find((e) => normalizeProfessionId(e.id) === profession) ?? null;
    const vocabulary = tasks.filter((t) => normalizeProfessionId(t.profession) === profession);
    const exact = entry ? vocabulary.filter((t) => t.profession.toLowerCase() === String(entry.id).trim().toLowerCase()) : [];
    const eligible = exact.filter((t) => t.eligible);
    const variants = [...new Set(vocabulary.filter((t) => !exact.includes(t)).map((t) => t.profession))];
    const enabled = Boolean(entry?.isActive) && eligible.length >= 3;
    const notes = [];
    if (variants.length) notes.push(`${vocabulary.length - exact.length} task(s) stored as ${variants.map((v) => JSON.stringify(v)).join(', ')}: the learner library compares the profession exactly, so a learner never sees them`);
    const evidence = {
      catalogue: entry ? `${entry.id} (${entry.isActive ? 'active' : 'inactive'})` : 'absent',
      vocabularyTasks: vocabulary.length,
      eligibleVocabularyTasks: vocabulary.filter((t) => t.eligible).length,
      exactMatchTasks: exact.length,
      eligibleExactTasks: eligible.length,
    };
    const picks = enabled ? pickTasks(eligible, categories, notes) : [];
    if (!enabled) {
      notes.unshift(!entry ? 'not an account profession in /v1/professions/catalog'
        : !entry.isActive ? 'inactive in /v1/professions/catalog'
          : `only ${eligible.length} eligible task(s) visible to a ${entry.id} learner (3 needed)`);
    }
    return { profession, catalogId: entry?.id ?? null, enabled, evidence, picks, notes, eligibleTasks: eligible };
  });
}

// One task per category: its own letter type first; then, rank by rank, each open category's fallback letter
// types not used yet (so 3 distinct categories when they exist); then any remaining eligible task (noted).
function pickTasks(eligible, categories, notes) {
  const used = new Set();
  const take = (letterType) => eligible.find((t) => !used.has(t.scenarioId) && (!letterType || t.letterType === letterType));
  const picks = categories.map((category) => ({ category, task: null, fallback: null }));
  const assign = (pick, task, fallback) => {
    used.add(task.scenarioId);
    pick.task = task;
    pick.fallback = fallback;
    if (fallback) notes.push(fallback);
  };
  const why = (pick, task, extra) => `no eligible ${CATEGORY_LETTER_TYPE[pick.category]} task; ${pick.category} script typed into ${task.letterType}${extra}`;
  for (const pick of picks) {
    const own = take(CATEGORY_LETTER_TYPE[pick.category]);
    if (own) assign(pick, own, null);
  }
  for (let rank = 0; rank < 3; rank += 1) {
    for (const pick of picks.filter((p) => !p.task)) {
      const letterType = FALLBACK_LETTER_TYPES[pick.category][rank];
      const task = picks.some((p) => p.task?.letterType === letterType) ? null : take(letterType);
      if (task) assign(pick, task, why(pick, task, ''));
    }
  }
  for (const pick of picks.filter((p) => !p.task)) {
    const task = take(null);
    if (task) assign(pick, task, why(pick, task, ' (category repeated)'));
  }
  return picks.filter((p) => p.task).map((p) => ({
    category: p.category, scenarioId: p.task.scenarioId, title: p.task.title, letterType: p.task.letterType, fallback: p.fallback,
  }));
}

/**
 * QA-2 rows of a plan before anything runs: planned letters NOT_RUN, every other profession NOT_ENABLED.
 * @param {any[]} plan
 * @param {any} [labels] profession id -> display label
 */
export function planRows(plan, labels = {}) {
  return plan.flatMap((p) => {
    const profession = labels[p.profession] ?? p.profession;
    if (!p.enabled) {
      return [{ profession, task: '-', category: '-', saved: '-', provider: '-', fallback: '-', status: 'NOT_ENABLED', notes: [...p.notes, `evidence ${JSON.stringify(p.evidence)}`].join('; ') }];
    }
    return p.picks.map((pick) => ({
      profession, task: `${pick.letterType} ${pick.scenarioId}`, category: pick.category,
      saved: '-', provider: '-', fallback: '-', status: 'NOT_RUN', notes: pick.fallback ?? 'planned',
    }));
  });
}

/** Splits a script into `parts` word-aligned chunks that join back to exactly the same text. */
export function splitText(text, parts) {
  const words = String(text).match(/\S+\s*/g) ?? [];
  const size = Math.ceil(words.length / parts);
  return Array.from({ length: parts }, (_, i) => words.slice(i * size, (i + 1) * size).join('')).filter(Boolean);
}

// ---- Credits ------------------------------------------------------------------------------------------------------

const POOLS = ['sharedCredits', 'flexibleCredits', 'writingOnlyCredits', 'speakingOnlyCredits'];
const DELTAS = ['sharedCreditsDelta', 'flexibleCreditsDelta', 'writingOnlyCreditsDelta', 'speakingOnlyCreditsDelta'];
const sum = (record, keys) => keys.reduce((total, key) => total + (Number(record?.[key]) || 0), 0);
export const creditPools = (snapshot) => sum(snapshot, POOLS);
export const creditRowDelta = (row) => sum(row, DELTAS);
const rowKey = (t) => t.id ?? `${t.referenceId}|${t.createdAt}|${t.reason}`;
const hex = (id) => String(id ?? '').replaceAll('-', '').toLowerCase();

// Why credit assertions would prove nothing for this learner (empty = go). kind 'paid': a finite balance that
// funds `letters` letters; 'free': a zero-credit free-sample learner. entitlement = GET /v1/writing/entitlement.
export function creditPreflight(snapshot, entitlement, { letters, kind = 'paid', now = Date.now() }) {
  if (!snapshot) return ['the credit balance could not be read'];
  const reasons = [];
  if (snapshot.writingUnlimited) reasons.push('the learner has unlimited Writing, so nothing would be charged');
  if (kind === 'free') {
    if (creditPools(snapshot) > 0) reasons.push('the free-sample learner holds credits, so a paid path could fund the letter');
    return reasons;
  }
  if (!entitlement) reasons.push('the Writing entitlement could not be read');
  else {
    if (entitlement.allowed !== true) reasons.push(`the Writing entitlement refuses (${entitlement.reason ?? 'no reason'})`);
    if (entitlement.allowed === true && (entitlement.remaining === null || entitlement.remaining === undefined)) reasons.push('the entitlement is unlimited, so nothing would be charged');
    if (entitlement.tier === 'free') reasons.push('the free tier would grant the letter without a debit');
  }
  const positiveGrant = (snapshot.transactions ?? []).some((t) => t.reason === 'Purchase' || creditRowDelta(t) > 0);
  if (creditPools(snapshot) === 0 && !positiveGrant) reasons.push('a never-funded account takes the legacy grading bypass, so nothing would be charged');
  const fundable = Math.floor(creditPools(snapshot) / CREDITS_PER_LETTER);
  if (fundable < letters) reasons.push(`only ${fundable} letter(s) can be funded and ${letters} are planned`);
  const expires = Date.parse(snapshot.expiresAt ?? '');
  if (Number.isFinite(expires) && expires < now + 86_400_000) reasons.push('the credits expire within a day');
  return reasons;
}

/**
 * The settled credit rule for ONE letter, judged on the ledger rows created during the test.
 * paid: exactly one 2-credit GradingDeduct at task open under writing-v2:{userId}:{scenarioId}:{n}; grading
 * adopts it (no second debit under writing-grade:...); no release/refund row; Retry costs 0.
 * "Practice this again" is just another paid letter (a new task-open debit under its own reference).
 * free_sample: no credit moves at all.
 * @param {any} input { kind, userId, scenarioId, submissionId, before, after, steps? }
 *   before/after = admin credit snapshots taken before the task was opened and at the end of the test;
 *   steps = [{ name, snapshot, delta }] intermediate balances that must have moved by delta from before.
 */
export function creditVerdict(input) {
  const { kind = 'paid', userId, scenarioId, submissionId, before, after, steps = [] } = input;
  if (!before || !after) return { ok: false, problems: ['the balance before or after the test could not be read'], rows: [] };
  const known = new Set((before.transactions ?? []).map(rowKey));
  const fresh = (after.transactions ?? []).filter((t) => !known.has(rowKey(t)));
  const moving = fresh.filter((t) => creditRowDelta(t) !== 0);
  const problems = [];
  const startRef = new RegExp(`^${LEDGER.startPrefix}${escapeRe(userId)}:${escapeRe(scenarioId)}:\\d+$`, 'i');
  const gradeRef = `${LEDGER.gradePrefix}${hex(submissionId)}`;
  const expected = kind === 'free_sample' ? 0 : -CREDITS_PER_LETTER;
  if (kind === 'paid') {
    const starts = moving.filter((t) => startRef.test(String(t.referenceId ?? '')));
    if (starts.length !== 1) problems.push(`${starts.length} task-open debit row(s), expected exactly 1 under writing-v2:${userId}:${scenarioId}:n`);
    if (starts.some((t) => t.reason !== LEDGER.debit || creditRowDelta(t) !== expected)) problems.push('the task-open debit is not one 2-credit GradingDeduct');
  }
  if (kind === 'paid' && moving.some((t) => String(t.referenceId ?? '').toLowerCase().startsWith(LEDGER.gradePrefix))) problems.push('a second debit was taken at grading (writing-grade:... reference)');
  if (fresh.some((t) => /:release$/i.test(String(t.referenceId ?? '')) || t.reason === LEDGER.refund)) problems.push('a release/refund row exists (a failed grade must hold the credit)');
  const unexplained = moving.filter((t) => !startRef.test(String(t.referenceId ?? '')) && hex(t.referenceId) !== hex(gradeRef) && !/:release$/i.test(String(t.referenceId ?? '')));
  if (unexplained.length) problems.push(`unexpected credit movement under ${unexplained.map((t) => t.referenceId ?? '(no reference)').join(', ')}`);
  const delta = creditPools(after) - creditPools(before);
  if (delta !== expected) problems.push(`the balance moved by ${delta}, expected ${expected}`);
  for (const step of steps) {
    if (!step.snapshot) { problems.push(`${step.name}: the balance could not be read`); continue; }
    const moved = creditPools(step.snapshot) - creditPools(before);
    if (moved !== step.delta) problems.push(`${step.name}: the balance moved by ${moved}, expected ${step.delta}`);
  }
  return { ok: problems.length === 0, problems, delta, rows: fresh.map((t) => ({ referenceId: t.referenceId, reason: t.reason, delta: creditRowDelta(t) })) };
}
const escapeRe = (s) => String(s ?? '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// ---- Timer --------------------------------------------------------------------------------------------------------

/**
 * Exam-clock verdict around an interruption (pause-while-away timer). Seconds remaining in the phase window.
 * mode 'paused' (page closed/reloaded): no reset (after <= window-30), pause proven (after >= before-8, the
 * load tolerance), no gain (after <= before+6, the autosave lag). mode 'running' (page stayed mounted for
 * `elapsed` seconds, e.g. offline): the clock kept running: before-elapsed-8 <= after <= before-elapsed+6.
 */
export function timerVerdict({ before, after, window, mode = 'paused', elapsed = 0 }) {
  if (![before, after, window].every(Number.isFinite)) return { ok: false, problems: ['a timer reading is missing'] };
  const problems = [];
  if (after > window - 30) problems.push(`the timer reset (${after}s left of a ${window}s window)`);
  const expected = mode === 'paused' ? before : before - elapsed;
  if (after < expected - 8) problems.push(mode === 'paused' ? `the timer ran while away (${before}s -> ${after}s)` : `the timer lost time (${before}s -> ${after}s over ${elapsed}s)`);
  if (after > expected + 6) problems.push(`the timer gained time (${before}s -> ${after}s)`);
  return { ok: problems.length === 0, problems };
}

// ---- Provider evidence --------------------------------------------------------------------------------------------

/**
 * Who graded a letter, from the admin usage rows (featureCode=writing.grade, the learner's userId) created
 * during the test. Owner rule: the Claude Max subscription is never skipped, so the EARLIEST row must be
 * writing-claude-sub (failure code max_not_first). A run whose fault flag deliberately fails L1 and L2
 * synthetically (those hops write no row) passes expectedFirst = writing-codex-sub instead.
 * fallback = any non-Success row, more than one provider, a failoverTrace, or a final provider other than
 * the primary. paidApiSpend = any anthropic row (an incident: zero expected).
 */
export function providerEvidence({ usage, modelUsed, expectedFirst = PROVIDERS.claude, primary = PROVIDERS.claude, graded = false }) {
  const rows = [...(usage ?? [])].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt));
  // The platform reuses an existing grade for identical letter text (the handoff scripts are the same for every
  // run): graded, a real model on the grade, and no AI call at all. Nothing to judge for Max-first then.
  if (!rows.length && graded && modelUsed && modelUsed !== EMPTY_MODEL) {
    return {
      firstProvider: null, finalProvider: null, finalModel: modelUsed, fallback: false, fallbackReasons: [],
      paidApiCalls: 0, calls: 0, problems: [], reused: true,
      note: `grade reused for identical text (no AI call; grade model ${modelUsed})`,
    };
  }
  const final = rows.filter((r) => r.outcome === 'Success').at(-1) ?? null;
  const fallbackReasons = [];
  if (rows.some((r) => r.outcome !== 'Success')) fallbackReasons.push('a non-success grading call');
  if (new Set(rows.map((r) => r.providerId)).size > 1) fallbackReasons.push('more than one provider');
  if (rows.some((r) => r.failoverTrace)) fallbackReasons.push('failoverTrace recorded');
  if (final && final.providerId !== primary) fallbackReasons.push(`served by ${final.providerId}`);
  const paidApiCalls = rows.filter((r) => r.providerId === PROVIDERS.api).length;
  const problems = [];
  if (!rows.length) problems.push('no writing.grade usage row was recorded for this learner');
  else if (rows[0].providerId !== expectedFirst) problems.push(`max_not_first: the first grading hop was ${rows[0].providerId}, expected ${expectedFirst}`);
  if (!final) problems.push('no successful grading call');
  if (paidApiCalls) problems.push(`INCIDENT: ${paidApiCalls} paid Anthropic API (L2) call(s) during QA`);
  if (modelUsed === EMPTY_MODEL) problems.push('graded by the empty-letter path (deterministic-empty-v1)');
  return {
    firstProvider: rows[0]?.providerId ?? null,
    finalProvider: final?.providerId ?? null,
    finalModel: final?.model ?? null,
    fallback: fallbackReasons.length > 0,
    fallbackReasons,
    paidApiCalls,
    calls: rows.length,
    problems,
  };
}

// ---- Preflight + guard --------------------------------------------------------------------------------------------

/** Health of the Writing chain from GET writing-provider + GET circuits (only the three writing providers). */
export function writingHealth({ writingProvider, circuits, now = Date.now() }) {
  const writing = new Set(Object.values(PROVIDERS));
  const notClosed = (circuits?.rows ?? []).filter((c) => c.kind === 'provider' && writing.has(c.key) && c.state !== 'closed');
  const live = (c) => !c.openUntil || Date.parse(c.openUntil) > now;
  return {
    marker: writingProvider?.quotaExceededUntil ?? null,
    failoverActive: writingProvider?.failoverActive === true,
    maxCircuit: notClosed.find((c) => c.key === PROVIDERS.claude) ?? null,
    liveOpen: notClosed.filter((c) => c.key !== PROVIDERS.claude && live(c)).map((c) => c.key),
    staleOpen: notClosed.filter((c) => c.key !== PROVIDERS.claude && !live(c)).map((c) => c.key),
  };
}

/**
 * May the live suites start? failures = FAIL rows (the Max route is off or a retired marker is set: these are
 * defects, never repaired by the harness); blockers = BLOCKED rows; repairs = audited admin circuit resets the
 * harness may do when preflight_repair=true (L2/L3 circuits only), after which preflight is run again.
 * @param {any} input { health, providers, requireL2Disabled?, repair?, writingProvider?, plannedLetters? }
 */
export function preflightDecision(input) {
  const { health, providers, requireL2Disabled = false, repair = false, writingProvider = null, plannedLetters = 0 } = input;
  const failures = [];
  const blockers = [];
  const warnings = [];
  if (health.marker !== null) failures.push(`quotaExceededUntil is ${health.marker}: the quota marker is retired and must be null`);
  if (health.maxCircuit) failures.push(`the ${PROVIDERS.claude} circuit is ${health.maxCircuit.state}: Max circuits are exempt and must stay closed`);
  if (health.failoverActive) failures.push('failoverActive is true: new grades would skip the Max subscription');
  const repairable = [...health.liveOpen, ...health.staleOpen];
  if (health.liveOpen.length && !repair) blockers.push(`provider circuit(s) open: ${health.liveOpen.join(', ')} (dispatch with preflight_repair=true to reset)`);
  if (health.staleOpen.length && !repair) warnings.push(`stale open circuit(s) (openUntil passed): ${health.staleOpen.join(', ')}`);
  const l2 = (providers ?? []).find((p) => String(p.code ?? '').toLowerCase() === PROVIDERS.api);
  if (requireL2Disabled && l2?.isActive) blockers.push('require_l2_disabled: the anthropic (L2) provider row is active');
  const quota = writingProvider?.quota;
  if (quota?.weeklyTokenCap > 0 && Number.isFinite(quota.weeklyTokensUsed) && Number.isFinite(writingProvider.failoverPct)) {
    const projected = ((quota.weeklyTokensUsed + plannedLetters * 40_000) / quota.weeklyTokenCap) * 100;
    if (projected >= writingProvider.failoverPct) blockers.push(`projected weekly Max utilisation ${projected.toFixed(1)}% reaches the ${writingProvider.failoverPct}% threshold`);
  }
  return {
    ok: failures.length === 0 && blockers.length === 0 && !(repair && repairable.length),
    failures, blockers, warnings,
    repairs: repair && !failures.length ? repairable : [],
  };
}

/**
 * Guard loop (every 45 s while letters are in flight). halt = stop starting tests; pause = a deploy is running.
 * @param {any} input { deployBusy, health, anthropicRows? }
 */
export function guardDecision(input) {
  const { deployBusy, health, anthropicRows = [] } = input;
  const reasons = [];
  if (health.marker !== null) reasons.push(`quotaExceededUntil was set (${health.marker})`);
  if (health.failoverActive) reasons.push('failoverActive turned true');
  if (health.maxCircuit) reasons.push(`the ${PROVIDERS.claude} circuit is ${health.maxCircuit.state}`);
  if (health.liveOpen.length) reasons.push(`provider circuit(s) open: ${health.liveOpen.join(', ')}`);
  if (anthropicRows.length) reasons.push(`INCIDENT: ${anthropicRows.length} paid Anthropic API grading call(s)`);
  if (reasons.length) return { action: 'halt', reasons };
  if (deployBusy) return { action: 'pause', reasons: ['a production deploy is running'] };
  return { action: 'continue', reasons: [] };
}

/** A synthetic fault must leave no trace on the chain: no marker, no new open circuit, no failover. */
export function faultSideEffects(before, after) {
  const problems = [];
  if (after.marker !== null) problems.push(`the quota marker is set after the fault (${after.marker})`);
  if (after.maxCircuit) problems.push(`the ${PROVIDERS.claude} circuit is ${after.maxCircuit.state} after the fault`);
  const opened = after.liveOpen.filter((k) => !before.liveOpen.includes(k));
  if (opened.length) problems.push(`circuit(s) opened by the fault: ${opened.join(', ')}`);
  if (after.failoverActive && !before.failoverActive) problems.push('failoverActive turned true after the fault');
  return problems;
}

/** Run-level paid-spend rule: zero anthropic usage rows are expected in a normal run. */
export const paidSpendProblems = (count) => (count > 0 ? [`paidApiSpend = ${count} anthropic call(s); zero expected in a normal run`] : []);

// ---- Lane mutex + pacing ------------------------------------------------------------------------------------------

// Only ONE QA submission may be grading at a time (Submit -> graded/failed): run(fn) queues fn behind the
// previous holder, FIFO; a rejected holder never blocks the lane.
export function createLane() {
  let tail = Promise.resolve();
  let active = 0;
  let maxActive = 0;
  return {
    run(fn) {
      const result = tail.then(async () => {
        active += 1;
        maxActive = Math.max(maxActive, active);
        try { return await fn(); } finally { active -= 1; }
      });
      tail = result.catch(() => undefined);
      return result;
    },
    get maxActive() { return maxActive; },
  };
}

// The AiScoring limiter is per learner (2/min + queue 3): wait so two scoring calls are >= paceSeconds apart.
export const pacingDelayMs = (lastAt, now, paceSeconds) => (lastAt ? Math.max(0, lastAt + paceSeconds * 1000 - now) : 0);

// ---- Report / UI checks -------------------------------------------------------------------------------------------

export function contractGaps(group, presentIds) {
  const present = new Set(presentIds);
  return (CONTRACT_GROUPS[group] ?? []).filter((id) => !present.has(id));
}

export function gradingStepsProblems(steps) {
  const texts = (steps ?? []).map((s) => String(s).replace(/\s+/g, ' ').trim());
  const problems = [];
  if (!texts.some((t) => t === GRADING_STEP_MODEL_ANSWER || t.startsWith(`${GRADING_STEP_MODEL_ANSWER} `))) problems.push(`no grading step reads "${GRADING_STEP_MODEL_ANSWER}" (saw: ${texts.join(' | ') || 'none'})`);
  if (texts.some((t) => /reference exemplar/i.test(t))) problems.push('a grading step still says "reference exemplar"');
  return problems;
}

export function sectionOrderProblems(sections, { prioritiesRequired = false } = {}) {
  const problems = [];
  const required = prioritiesRequired ? [...RESULT_SECTIONS_REQUIRED, RESULT_SECTION_PRIORITIES] : RESULT_SECTIONS_REQUIRED;
  for (const s of required) if (!sections.includes(s)) problems.push(`report section "${s}" is missing`);
  const dupes = sections.filter((s, i) => sections.indexOf(s) !== i);
  if (dupes.length) problems.push(`report section(s) repeated: ${[...new Set(dupes)].join(', ')}`);
  const known = sections.filter((s) => RESULT_SECTION_ORDER.includes(s));
  const ranks = known.map((s) => RESULT_SECTION_ORDER.indexOf(s));
  if (ranks.some((r, i) => i > 0 && r < ranks[i - 1])) problems.push(`report order is ${known.join(' > ')}, expected ${RESULT_SECTION_ORDER.join(' > ')}`);
  return problems;
}

/**
 * Corrections (WAI-09 contract): more than 5 errors = a 5-item preview + a "View all corrections" control that
 * renders the full list; 5 or fewer = the full list only, no control. Counts are read before/after the click.
 * @param {any} input { preview, full, api, expandable }
 */
export function correctionsProblems(input) {
  const { preview, full, api, expandable } = input;
  const problems = [];
  if (full !== api) problems.push(`the full corrections list shows ${full}, the report has ${api}`);
  if (api > CORRECTIONS_PREVIEW && !expandable) problems.push(`${api} corrections but no "View all corrections" control`);
  if (api > CORRECTIONS_PREVIEW && expandable && preview !== CORRECTIONS_PREVIEW) problems.push(`the preview shows ${preview} corrections, expected ${CORRECTIONS_PREVIEW}`);
  if (api <= CORRECTIONS_PREVIEW && expandable) problems.push(`a "View all corrections" control appears for only ${api} correction(s)`);
  return problems;
}

const BROKEN_TEXT = /\bundefined\b|\bNaN\b|\[object Object\]/;
export function reportTextProblems(text, hrefs = []) {
  const problems = [];
  const broken = String(text ?? '').match(BROKEN_TEXT);
  if (broken) problems.push(`the report shows "${broken[0]}"`);
  if (/appeal/i.test(String(text ?? ''))) problems.push('the report mentions an appeal');
  if (/exemplar/i.test(String(text ?? ''))) problems.push('the report shows the word "Exemplar"');
  if (hrefs.some((h) => /\/appeal(\b|$)/i.test(String(h)))) problems.push('the report links to /appeal');
  if (/revise\s*(?:&|and)\s*resubmit/i.test(String(text ?? ''))) problems.push('the report offers Revise & Resubmit');
  if (hrefs.some((h) => /\/revise(\b|$)/i.test(String(h)))) problems.push('the report links to /revise');
  return problems;
}

// Owner review 5 Oct 2026: three different priorities, short criterion cards, no internal labels, no "Exemplar".
// Owner directive 6 Oct 2026: no internal rule/check id, provider tag or debug term reaches the candidate at all.
export const SUMMARY_MAX_CHARS = 240;
export const PRIORITY_MAX_CHARS = 220;
export const CARD_MAX_CHARS = 900;
// Internal id families (rulebook ids, registry check ids, grader pseudo-ids). Case-sensitive; no generic pattern,
// so clinical tokens such as B-12 are never flagged.
const INTERNAL_ID = new RegExp(String.raw`\b(?:BUILTIN\.[A-Za-z0-9_]+|AI[.:][A-Za-z0-9_.-]*[A-Za-z0-9_]|(?:G|DH|OWN|[A-Z]{2,4})-W-\d{1,3}|[A-Z]{2,4}-[A-Z]{1,4}-\d{1,3}|OW-\d{1,3}|OA\d?-\d{1,3}|R\d{1,2}\.\d{1,3})(?![\w-])`);
const CHECK_ID = /\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b/;
const PROVIDER_TAG = /\b(?:writing-claude-sub|claude(?:-[\w.]+)*|gpt-[\w.]+|openai|anthropic|codex|typesafe|jev)\b/i;
const DEBUG_TERM = /\b(?:firewall|validator|rule engine|rulebook|parser|deterministic|Blocked by|release gate)\b/i;
const LEAK_SAMPLE = {
  ruleLabel: /^R\d{1,2}[:.]\s?\S{0,12}/, ruleId: /\([A-Z]{1,4}\d?(?:-[A-Z]{1,3})?-\d[^)]{0,24}/, affects: /This affects[^.]{0,28}/, exemplar: /[Ee]xemplar/,
  internalId: INTERNAL_ID, checkId: CHECK_ID, providerTag: PROVIDER_TAG, debugTerm: DEBUG_TERM,
};
const LEAK_KINDS = {
  ruleLabel: /^R\d{1,2}[:.]/, ruleId: /\([A-Z]{1,4}\d?(?:-[A-Z]{1,3})?-\d/, affects: /This affects/, exemplar: /[Ee]xemplar/,
  internalId: INTERNAL_ID, checkId: CHECK_ID, providerTag: PROVIDER_TAG, debugTerm: DEBUG_TERM,
};
const PRIORITY_LABEL = /^(?:AI(?:[.:][\w.-]*)?|[\w-]*[\d._-][\w.-]*):\s+/;
// Server-written free text of the candidate payloads. The candidate's own wording (candidateWording, quote,
// snippet, letter and model-answer text) is verbatim and is never inspected.
const FREE_TEXT_KEYS = new Set([
  'whyItMatters', 'correction', 'category', 'summary', 'strengthObservation', 'limitationObservation', 'improvementAction',
  'explanation', 'feedback', 'exemplarFix', 'ruleText', 'suggestedFix', 'scoreLabel', 'confidenceLabel',
  'topPriorities', 'topThreePriorities', 'strengths', 'studyPlan', 'whyThisWorks',
]);
function candidateFreeText(node, out = []) {
  if (Array.isArray(node)) node.forEach((n) => candidateFreeText(n, out));
  else if (node && typeof node === 'object') {
    for (const [key, value] of Object.entries(node)) {
      if (!FREE_TEXT_KEYS.has(key)) candidateFreeText(value, out);
      else if (typeof value === 'string') out.push(value);
      else if (Array.isArray(value)) value.forEach((v) => (typeof v === 'string' ? out.push(v) : candidateFreeText(v, out)));
    }
  }
  return out;
}
// Fields the candidate API keeps for one release but must now send blank: any value is a leak.
const filled = (v) => (Array.isArray(v) ? v.length > 0 : v != null && String(v).trim() !== '');
function neutralisedLeakKeys(grade, report) {
  const keys = [
    ...(report?.errors ?? []).flatMap((e) => ['ruleSource', 'provenanceTag', 'candidateBehavior', 'location'].filter((k) => filled(e[k])).map((k) => `error.${k}`)),
    ...['rulePackVersion', 'modelVersion', 'calibrationSetVersion', 'blockingCodes', 'confidenceRange'].filter((k) => filled(report?.[k])).map((k) => `report.${k}`),
    ...['modelUsed', 'canonVersion'].filter((k) => filled(grade?.[k])).map((k) => `grade.${k}`),
    ...['c1', 'c2', 'c3', 'c4', 'c5', 'c6'].filter((k) => filled(grade?.perCriterion?.[k]?.citedRuleIds)).map((k) => `grade.perCriterion.${k}.citedRuleIds`),
    ...(grade?.revisionInvite ? ['grade.revisionInvite'] : []),
    ...(String(grade?.confidenceFlag ?? '') === 'jev_review' ? ['grade.confidenceFlag'] : []),
  ];
  return [...new Set(keys)];
}
const CRITERION_CODES = ['purpose', 'content', 'conciseness_clarity', 'genre_style', 'organisation_layout', 'language'];
const C_KEYS = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6'];

/**
 * What the learner API says about the report's shape: numbers only (no letter, model answer or case-note text).
 * @param {any} grade GET /grade body
 * @param {any} report GET /assessment-v11 body
 */
export function reportShapeFacts(grade, report) {
  const errors = report?.errors ?? [];
  const severities = (name) => errors.filter((e) => String(e.severity).toLowerCase() === name).length;
  // The server now sends label-free priorities, so a raw label is itself a leak; the strip only keeps counts honest.
  const rawPriorities = (report?.topPriorities ?? []).map((p) => String(p));
  const priorities = rawPriorities.map((p) => p.replace(PRIORITY_LABEL, '').trim());
  const summaries = (report?.criteria ?? []).map((c) => String(c.summary ?? ''));
  const feedback = C_KEYS.map((k) => String(grade?.perCriterion?.[k]?.feedback ?? ''));
  const leakTexts = candidateFreeText({ grade, report }).map((t) => String(t ?? ''));
  const summaryOf = (code) => String((report?.criteria ?? []).find((c) => c.criterionCode === code)?.summary ?? '').trim();
  // A Purpose priority repeats the opening of a Purpose correction's explanation (the server sends no ids to match on).
  const purposeReasons = errors.filter((e) => e.primaryCriterionCode === 'purpose').map((e) => String(e.whyItMatters ?? '').toLowerCase().slice(0, 40)).filter(Boolean);
  const neutralised = neutralisedLeakKeys(grade, report);
  return {
    errorsCount: errors.length, critical: severities('critical'), major: severities('major'), minor: severities('minor'),
    advisory: severities('advisory') + severities('info'),
    // Scored = anything that is not coaching only (advisory never takes a priority slot).
    scoredCorrections: errors.length - severities('advisory') - severities('info'),
    rawPriorityLabels: rawPriorities.filter((p) => PRIORITY_LABEL.test(p)).length,
    neutralisedLeaks: neutralised.length, neutralisedLeakKeys: neutralised.slice(0, 8),
    priorityCount: priorities.length, distinctPriorities: new Set(priorities.map((p) => p.toLowerCase())).size,
    maxPriorityChars: Math.max(0, ...priorities.map((p) => p.length)),
    maxSummaryChars: Math.max(0, ...summaries.map((t) => t.length)),
    maxFeedbackChars: Math.max(0, ...feedback.map((t) => t.length)),
    criteriaWithFindings: CRITERION_CODES.filter((code) => errors.some((e) => e.primaryCriterionCode === code)).length,
    criteriaMissingSummary: CRITERION_CODES.filter((code) => errors.some((e) => e.primaryCriterionCode === code) && !summaryOf(code)).length,
    purposePriorities: priorities.filter((p) => purposeReasons.some((reason) => p.toLowerCase().startsWith(reason))).length,
    leakKinds: Object.fromEntries(Object.entries(LEAK_KINDS).map(([kind, re]) => [kind, leakTexts.filter((t) => re.test(t)).length])),
    // The matched fragment only (a rule id / the "This affects ..." note), at most 40 chars: never letter or case-note text.
    leakSamples: Object.fromEntries(Object.entries(LEAK_SAMPLE).map(([kind, re]) => [kind, leakTexts.map((t) => re.exec(t)?.[0]?.slice(0, 40)).filter(Boolean).slice(0, 2)])),
    labelLeaks: leakTexts.filter((t) => Object.values(LEAK_KINDS).some((re) => re.test(t))).length,
  };
}

export function reportShapeProblems(f) {
  const problems = [];
  if (f.priorityCount > 3) problems.push(`${f.priorityCount} top priorities, expected at most 3`);
  if (f.scoredCorrections > 0 && f.priorityCount === 0) problems.push('the report has scored corrections but no top priorities');
  if (f.rawPriorityLabels) problems.push(`${f.rawPriorityLabels} top priorit${f.rawPriorityLabels === 1 ? 'y still carries' : 'ies still carry'} an internal label (the server must send plain text)`);
  if (f.neutralisedLeaks) problems.push(`${f.neutralisedLeaks} internal field(s) still carry a value for candidates (${(f.neutralisedLeakKeys ?? []).join(', ')})`);
  if (f.distinctPriorities !== f.priorityCount) problems.push(`the top priorities repeat (${f.distinctPriorities} distinct of ${f.priorityCount})`);
  if (f.purposePriorities > 1) problems.push(`${f.purposePriorities} top priorities are about Purpose, expected at most 1`);
  if (f.maxPriorityChars > PRIORITY_MAX_CHARS) problems.push(`a top priority is ${f.maxPriorityChars} chars, expected at most ${PRIORITY_MAX_CHARS}`);
  if (f.maxSummaryChars > SUMMARY_MAX_CHARS) problems.push(`a criterion summary is ${f.maxSummaryChars} chars, expected at most ${SUMMARY_MAX_CHARS}`);
  if (f.maxFeedbackChars > SUMMARY_MAX_CHARS) problems.push(`a per-criterion feedback is ${f.maxFeedbackChars} chars, expected at most ${SUMMARY_MAX_CHARS}`);
  if (f.criteriaMissingSummary) problems.push(`${f.criteriaMissingSummary} criterion/criteria have findings but no summary`);
  if (f.labelLeaks) problems.push(`${f.labelLeaks} report text(s) show an internal rule label, id, provider tag or debug term, or "Exemplar"`);
  return problems;
}

/** A realistic letter must really mix severities, or the conciseness check proved nothing. */
export const severityMixPartials = (f) => (f.minor > 0 && f.critical + f.major > 0 ? [] : [`the report did not mix severities (critical ${f.critical}, major ${f.major}, minor ${f.minor})`]);

/**
 * What the learner SEES: each criterion card is short with at most one suggested-fix box; at most three distinct priorities.
 * @param {{ cards: {chars: number, fixBoxes: number}[], priorities: string[] }} dom
 */
export function cardProblems(dom) {
  const problems = [];
  dom.cards.forEach((card, i) => {
    if (card.chars > CARD_MAX_CHARS) problems.push(`criterion card ${i + 1} shows ${card.chars} chars, expected at most ${CARD_MAX_CHARS}`);
    if (card.fixBoxes > 1) problems.push(`criterion card ${i + 1} shows ${card.fixBoxes} suggested fixes, expected at most 1`);
  });
  if (dom.priorities.length > 3) problems.push(`${dom.priorities.length} priorities on the page, expected at most 3`);
  if (new Set(dom.priorities.map((p) => p.toLowerCase())).size !== dom.priorities.length) problems.push('the page repeats a priority');
  return problems;
}

export const scoreLabelProblems = (label) => (/^\d{1,3}\s*\/\s*500$/.test(String(label ?? '').trim()) ? [] : [`the score reads ${JSON.stringify(label ?? null)}, expected N/500`]);

/** Post Submissions (my-work API): the submission is listed exactly once, in the expected state. */
export function myWorkProblems({ items, submissionId, state }) {
  const rows = (items ?? []).filter((i) => hex(i.submissionId) === hex(submissionId));
  if (rows.length !== 1) return [`Post Submissions lists the submission ${rows.length} times, expected once`];
  return state && rows[0].state !== state ? [`Post Submissions shows it as ${rows[0].state}, expected ${state}`] : [];
}

/** Free sample (GET /v1/free-samples/writing): a failed grade burns no use; the successful retry counts once. */
export function freeSampleProblems({ afterFailure, afterRetry }) {
  const first = (offers) => (Array.isArray(offers) ? offers[0] : null);
  const problems = [];
  const failed = first(afterFailure);
  if (!failed) problems.push('no free-sample offer after the failure');
  else if (failed.successfulCount !== 0 || failed.state === 'completed') problems.push(`after the failure the sample reads ${failed.state} with ${failed.successfulCount} use(s)`);
  const retried = first(afterRetry);
  if (!retried) problems.push('no free-sample offer after the retry');
  else if (retried.successfulCount !== 1) problems.push(`after the retry the sample counts ${retried.successfulCount} use(s), expected 1`);
  return problems;
}

// ---- Status + evidence table --------------------------------------------------------------------------------------

/**
 * blocked > problems (FAIL) > partials (not proven live) > PASS.
 * @param {any} input { problems?, partials?, blocked? } lists of reasons
 */
export function verdictOf(input) {
  const { problems = [], partials = [], blocked = [] } = input;
  if (blocked.length) return 'BLOCKED';
  if (problems.length) return 'FAIL';
  return partials.length ? 'PARTIAL' : 'PASS';
}

export const TABLE_COLUMNS = ['Profession', 'Task/ID', 'Category', 'Saved in Post Submissions', 'Provider used', 'Fallback?', 'Final result', 'Notes'];
const cells = (r, runId) => [r.profession, r.task, r.category, r.saved, r.provider, r.fallback, r.status,
  r.status === 'PARTIAL' && runId && !String(r.notes ?? '').includes(String(runId)) ? `${r.notes ?? ''} [proven live in run ${runId}]`.trim() : r.notes]
  .map((v) => String(v ?? '-').replace(/\s+/g, ' ').trim() || '-');

export function buildTable(rows, { runId = '' } = {}) {
  for (const r of rows) if (!STATUSES.includes(r.status)) throw new Error(`unknown status ${r.status}`);
  const md = [
    `| ${TABLE_COLUMNS.join(' | ')} |`,
    `|${TABLE_COLUMNS.map(() => '---').join('|')}|`,
    ...rows.map((r) => `| ${cells(r, runId).map((c) => c.replaceAll('|', '\\|')).join(' | ')} |`),
  ].join('\n');
  const csvCell = (c) => (/[",\n]/.test(c) ? `"${c.replaceAll('"', '""')}"` : c);
  const csv = [TABLE_COLUMNS, ...rows.map((r) => cells(r, runId))].map((line) => line.map(csvCell).join(',')).join('\n');
  return { md, csv };
}

/** "ALL PASS" only when every test row is PASS; NOT_ENABLED professions are scope, listed by name. */
export function overallVerdict(rows) {
  const tests = rows.filter((r) => r.status !== 'NOT_ENABLED');
  const notEnabled = [...new Set(rows.filter((r) => r.status === 'NOT_ENABLED').map((r) => r.profession))];
  const scope = notEnabled.length ? `; NOT_ENABLED: ${notEnabled.join(', ')}` : '';
  if (tests.length && tests.every((r) => r.status === 'PASS')) return `ALL PASS (${tests.length}/${tests.length} tests${scope})`;
  const counts = STATUSES.filter((s) => s !== 'NOT_ENABLED').map((s) => [s, tests.filter((r) => r.status === s).length]).filter(([, n]) => n);
  const listed = counts.map(([s, n]) => n + ' ' + s).join(', ') || 'no tests';
  return `NOT ALL PASS (${listed}${scope})`;
}

// ---- Identities ---------------------------------------------------------------------------------------------------

// Stable per learner and run, so a new browser context presents the SAME (already trusted) device.
export function deriveDeviceId(seed) {
  const h = createHash('sha256').update(String(seed)).digest('hex');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-a${h.slice(17, 20)}-${h.slice(20, 32)}`;
}

export const syntheticEmail = (runKey, key) => `wqa-${String(runKey)}-${String(key)}`.toLowerCase().replace(/[^a-z0-9-]+/g, '-') + '@oetwithdrhesham.co.uk';
