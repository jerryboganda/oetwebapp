#!/usr/bin/env node
// speaking:import — load the transcribed OET Speaking role-play corpus into the
// admin API, verbatim.
//
//   node scripts/import-speaking-cards.mjs                 # dry run + report
//   node scripts/import-speaking-cards.mjs --apply         # actually writes
//   node scripts/import-speaking-cards.mjs --self-check    # unit-check the mapping
//
// Env / flags:
//   OET_API_URL        --api        default http://localhost:5199
//   OET_ADMIN_EMAIL    --email
//   OET_ADMIN_PASSWORD --password
//   OET_ADMIN_TOKEN    --token      skips sign-in when supplied
//                      --transcripts <dir>
//                      --limit <n>  first N cards only (smoke runs)
//                      --out <path> report path
//
// FIDELITY CONTRACT — the corpus is transcribed verbatim from printed cards and
// adversarially verified against page scans. This script must not paraphrase,
// truncate, reflow, re-order or drop any transcribed string:
//   * every candidate/roleplayer bullet goes in, however many there are (the
//     five legacy Task1..Task5 columns are NEVER used — cards with 6-9 bullets
//     exist and would be silently clipped);
//   * `footerNote` (the rights-holder notice on the printed page) is preserved
//     into the ADMIN-ONLY `sourceAttribution` column. It is never shown to a
//     learner, and it is never stripped — deleting a third-party rights notice
//     while redistributing the material is not a cleanup, it is a liability;
//   * metadata the source does not print (patient emotion, communication goal,
//     clinical topic, criteria focus) is left at neutral defaults and counted in
//     the report as "needs admin enrichment". It is not guessed.
import { readFileSync, readdirSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { homedir } from "node:os";

// ── CLI ───────────────────────────────────────────────────────────────────
const argv = process.argv.slice(2);
const flag = (name) => argv.includes(`--${name}`);
const opt = (name, fallback) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : fallback;
};

const APPLY = flag("apply");
const SELF_CHECK = flag("self-check");
const API = (opt("api", process.env.OET_API_URL) || "http://localhost:5199").replace(/\/+$/, "");
const EMAIL = opt("email", process.env.OET_ADMIN_EMAIL);
const PASSWORD = opt("password", process.env.OET_ADMIN_PASSWORD);
const TOKEN = opt("token", process.env.OET_ADMIN_TOKEN);
const LIMIT = Number(opt("limit", "0")) || 0;
const TRANSCRIPTS = opt(
  "transcripts",
  join(homedir(), "Desktop", "Speaking-STAGING", "transcripts"),
);
const OUT = resolve(opt("out", "artifacts/speaking-import/report.json"));

// ── Constants mirrored from the backend ───────────────────────────────────
// AdminService.SpeakingCalibration.cs:26 — the canonical nine. Anything else
// is rejected with ROLE_PLAY_CARD_CRITERIA_FOCUS_INVALID.
const CRITERION_CODES = new Set([
  "intelligibility", "fluency", "appropriateness", "grammarExpression",
  "relationshipBuilding", "patientPerspective", "structure",
  "informationGathering", "informationGiving",
]);
// lib/auth/enrollment.ts:38 — PROFESSION_CATALOG ids.
const PROFESSIONS = new Map([
  ["MEDICINE", "medicine"],
  ["NURSING", "nursing"],
  ["PHARMACY", "pharmacy"],
  ["DENTISTRY", "dentistry"],
  ["PHYSIOTHERAPY", "physiotherapy"],
  ["RADIOGRAPHY", "radiography"],
]);
// AdminService.SpeakingRolePlayCards.cs — EvaluatePublishGate.
const MIN_PUBLISHABLE_TASKS = 3;
const MAX_SOURCE_ATTRIBUTION = 400;

// ── Pure mapping (exercised by --self-check) ──────────────────────────────

/** Uppercase banner profession → canonical catalog id. Unknown → null. */
export function mapProfession(raw) {
  return PROFESSIONS.get(String(raw ?? "").trim().toUpperCase()) ?? null;
}

/**
 * Keep every transcribed bullet, trimmed only of surrounding whitespace.
 *
 * A bullet that contains newlines is a printed bullet with indented sub-items;
 * it stays ONE task with its newlines intact. Splitting it would inflate the
 * count and let a 2-bullet card sneak past the >=3 publish gate; dropping the
 * sub-items would lose printed content. Neither is acceptable.
 */
export function normaliseTasks(list) {
  return (Array.isArray(list) ? list : [])
    .map((t) => String(t ?? "").replace(/[ \t]+$/gm, "").trim())
    .filter(Boolean);
}

/** First sentence(s) of the background, clipped — the printed cards carry no title. */
export function deriveScenarioTitle(card) {
  const background = String(card.candidateBackground ?? "").trim().replace(/\s+/g, " ");
  // Take whole sentences until the title is distinguishing. Many cards open on
  // a generic line ("You are a general practitioner.") that is identical across
  // dozens of scenarios; one sentence alone makes them indistinguishable in the
  // admin list.
  let title = "";
  for (const sentence of background.match(/[^.!?]+[.!?]+["')\]]*\s*|[^.!?]+$/g) ?? []) {
    if (title.trim().length >= 60) break;
    title += sentence;
  }
  title = (title.trim() || background).slice(0, 140).trim();
  if (title) return title;
  const stem = String(card.setting || card.profession || "OET Speaking").trim();
  return `${stem} — card ${card.printedCardNumber ?? "?"}`;
}

/** The rights notice, verbatim, plus a stable provenance token. */
export function deriveSourceAttribution(card, chunkId) {
  const note = String(card.footerNote ?? "").trim();
  const joined = [note, provenanceToken(card, chunkId)].filter(Boolean).join(" ");
  return joined ? joined.slice(0, MAX_SOURCE_ATTRIBUTION) : null;
}

/**
 * `[chunkId pN,M]` — globally unique per source card face, and the importer's
 * idempotency key. Keying on the title instead would be wrong: 80 title
 * collisions exist in the corpus (official cards reprinted across sets, and
 * scenarios that open on the same generic sentence), so a second run would
 * skip real cards as "already imported".
 */
export function provenanceToken(card, chunkId) {
  if (!chunkId) return "";
  return `[${chunkId} p${(card.sourcePages ?? []).join(",")}]`;
}

/**
 * The roleplayer's first cue doubles as the script's opening response — that is
 * literally what "When asked, say ..." bullet 1 is. It is copied, not moved:
 * the full patientTasks list is still sent in its entirety.
 */
export function deriveOpeningResponse(patientTasks, patientBackground) {
  return (
    patientTasks[0]
    || String(patientBackground ?? "").trim()
    || "Respond in role as printed on the roleplayer card."
  );
}

/** Build the create + upsert payloads for one transcribed card. */
// Column widths from `Domain/RolePlayCardEntities.cs`. An over-length value is
// not a soft problem: Postgres rejects the whole INSERT with "value too long
// for type character varying(N)", so the card never lands. The dry run has to
// catch it here rather than let `--apply` discover it one row at a time.
//
// Task bullets are deliberately absent: `TasksJson` is unbounded text, and the
// legacy varchar(500) mirror columns clamp their own copy
// (RolePlayCardTasks.MirrorToLegacyColumns).
const COLUMN_LIMITS = {
  scenarioTitle: 200,
  setting: 160,
  candidateRole: 256,
  interlocutorRole: 256,
  background: 4000,
  sourceAttribution: 400,
};
const SCRIPT_COLUMN_LIMITS = {
  openingResponse: 500,
  patientBackground: 4000,
};

function findOverLength(values, limits) {
  return Object.entries(limits).flatMap(([field, max]) => {
    const length = String(values[field] ?? "").length;
    return length > max ? [`${field} is ${length} chars, column holds ${max}`] : [];
  });
}

export function buildPayloads(card, chunkId) {
  const professionId = mapProfession(card.profession);
  const tasks = normaliseTasks(card.candidateTasks);
  const patientTasks = normaliseTasks(card.patientTasks);
  const printedNumber = Number.parseInt(String(card.printedCardNumber ?? ""), 10);

  const create = {
    professionId,
    scenarioTitle: deriveScenarioTitle(card),
    setting: String(card.setting ?? "").trim() || "Clinical setting",
    candidateRole: String(card.candidateRoleLabel ?? "").trim() || "Candidate",
    interlocutorRole: String(card.interlocutorRoleLabel ?? "").trim() || "Patient",
    background: String(card.candidateBackground ?? "").trim(),
    tasks,
    // Un-printed metadata: neutral defaults, never guessed. See FIDELITY CONTRACT.
    patientEmotion: "neutral",
    communicationGoal: "Inform",
    clinicalTopic: "general",
    difficulty: "core",
    criteriaFocus: [],
    displayCardNumber: Number.isFinite(printedNumber) ? printedNumber : null,
    sourceAttribution: deriveSourceAttribution(card, chunkId),
  };

  const script = {
    openingResponse: deriveOpeningResponse(patientTasks, card.patientBackground),
    patientBackground: String(card.patientBackground ?? "").trim() || null,
    patientTasks,
  };

  const blockers = [];
  if (!professionId) blockers.push(`unmapped profession "${card.profession}"`);
  if (!create.background) blockers.push("no candidate background");
  if (tasks.length < MIN_PUBLISHABLE_TASKS) {
    blockers.push(`${tasks.length} task bullet(s), publish gate needs ${MIN_PUBLISHABLE_TASKS}`);
  }
  if (card.missingFace && card.missingFace !== "none") {
    blockers.push(`source is missing the ${card.missingFace} face`);
  }

  return {
    create,
    script,
    blockers,
    overLength: [
      ...findOverLength(create, COLUMN_LIMITS),
      ...findOverLength(script, SCRIPT_COLUMN_LIMITS),
    ],
    canPublish: blockers.length === 0,
  };
}

// ── HTTP ──────────────────────────────────────────────────────────────────
let bearer = TOKEN ?? null;

async function api(method, path, body) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: {
      "content-type": "application/json",
      ...(bearer ? { authorization: `Bearer ${bearer}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const detail = json?.detail ?? json?.title ?? json?.message ?? text.slice(0, 300);
    throw new Error(`${method} ${path} → ${res.status} ${detail}`);
  }
  return json;
}

async function signIn() {
  if (bearer) return;
  if (!EMAIL || !PASSWORD) {
    throw new Error("--apply needs OET_ADMIN_EMAIL + OET_ADMIN_PASSWORD (or OET_ADMIN_TOKEN).");
  }
  const res = await api("POST", "/v1/auth/sign-in", {
    email: EMAIL, password: PASSWORD, rememberMe: false,
  });
  if (!res?.accessToken) throw new Error("sign-in returned no accessToken (MFA challenge?).");
  bearer = res.accessToken;
}

// ── Corpus ────────────────────────────────────────────────────────────────
/** `Nursing__Cards_p004-006` → `Nursing__Cards` — the source PDF a chunk came from. */
function sourceDocument(chunkId) {
  return String(chunkId ?? "").replace(/_p\d+(-\d+)?$/, "");
}

/**
 * Rejoin card faces that the chunker split across its own boundary.
 *
 * Some source PDFs print the HEALTH PROFESSIONAL face on the last page of one
 * chunk and its matching PATIENT face on the first page of the next. Each half
 * arrives as a separate record flagged `missingFace`. Imported as-is that is 16
 * unusable half-cards instead of 8 complete ones — the candidate halves have no
 * roleplayer to talk to and the roleplayer halves have no tasks at all, so every
 * one of them fails the publish gate.
 *
 * Stitching is deliberately strict: same source PDF, complementary missing
 * faces, and physically consecutive pages. Anything short of that is left
 * alone as a Draft rather than guessed at — a wrongly-paired card is worse
 * than an obviously incomplete one.
 */
export function stitchSplitFaces(entries) {
  const out = [];
  let stitched = 0;
  for (let i = 0; i < entries.length; i += 1) {
    const head = entries[i];
    const next = entries[i + 1];
    const a = head.card;
    const b = next?.card;
    const lastPage = (a?.sourcePages ?? []).at(-1);
    const firstPage = (b?.sourcePages ?? [])[0];

    const pairable = b
      && a.missingFace === "roleplayer"
      && b.missingFace === "candidate"
      && sourceDocument(head.chunkId) === sourceDocument(next.chunkId)
      && Number.isFinite(lastPage) && Number.isFinite(firstPage)
      && firstPage === lastPage + 1;

    if (!pairable) {
      out.push(head);
      continue;
    }

    out.push({
      ...head,
      stitchedWith: next.chunkId,
      card: {
        ...a,
        sourcePages: [...(a.sourcePages ?? []), ...(b.sourcePages ?? [])],
        interlocutorRoleLabel: b.interlocutorRoleLabel || a.interlocutorRoleLabel,
        patientBackground: b.patientBackground,
        patientTasks: b.patientTasks,
        footerNote: a.footerNote || b.footerNote,
        missingFace: "none",
        uncertain: [...(a.uncertain ?? []), ...(b.uncertain ?? [])],
      },
    });
    stitched += 1;
    i += 1; // the roleplayer half is now part of the card above
  }
  return { entries: out, stitched };
}

function loadCorpus() {
  const files = readdirSync(TRANSCRIPTS).filter((f) => /^chunk_\d+\.json$/.test(f)).sort();
  if (files.length === 0) throw new Error(`no chunk_*.json under ${TRANSCRIPTS}`);
  const out = [];
  for (const file of files) {
    // Explicit utf8 — the transcripts contain typographic quotes and the ©
    // sign, and a default/ANSI read mojibakes them into the database.
    const chunk = JSON.parse(readFileSync(join(TRANSCRIPTS, file), "utf8"));
    for (const card of chunk.cards ?? []) {
      out.push({ file, chunkId: chunk.chunkId ?? file, notes: chunk.notes ?? "", card });
    }
  }
  return out;
}

// ── Self-check ────────────────────────────────────────────────────────────
function selfCheck() {
  const assert = (cond, msg) => {
    if (!cond) throw new Error(`self-check failed: ${msg}`);
  };

  assert(mapProfession("  medicine ") === "medicine", "profession is case/space insensitive");
  assert(mapProfession("OPTOMETRY") === null, "unknown profession is rejected, not defaulted");

  // A nine-bullet card must survive intact — this is the whole reason the
  // unbounded task columns exist.
  const nine = Array.from({ length: 9 }, (_, i) => `bullet ${i + 1}`);
  assert(normaliseTasks(nine).length === 9, "nine bullets survive");

  // Sub-bullets stay welded to their parent bullet.
  const withSub = ["parent\n  - sub one\n  - sub two", "  ", "second"];
  const kept = normaliseTasks(withSub);
  assert(kept.length === 2, "blank bullets dropped, sub-bullets not split");
  assert(kept[0].includes("\n  - sub two"), "sub-bullet text preserved verbatim");

  // Rights notice preserved, not stripped.
  const attr = deriveSourceAttribution(
    { footerNote: "\u00a9 Cambridge Boxhill Language Assessment / SAMPLE TEST", sourcePages: [7] },
    "Dentistry_p007",
  );
  assert(attr.includes("Cambridge Boxhill"), "rights notice preserved");
  assert(attr.includes("Dentistry_p007 p7"), "provenance appended");
  assert(
    deriveSourceAttribution({ footerNote: "x".repeat(600), sourcePages: [] }, "c").length
      === MAX_SOURCE_ATTRIBUTION,
    "attribution clamped to column width",
  );
  assert(deriveSourceAttribution({ footerNote: "  " }, "") === null, "blank notice → null");

  // Title derivation never invents and never returns empty.
  assert(
    deriveScenarioTitle({ candidateBackground: "You see a parent. They want advice." })
      === "You see a parent. They want advice.",
    "title is background prose, verbatim",
  );
  assert(
    deriveScenarioTitle({ candidateBackground: "A".repeat(300) }).length === 140,
    "over-long title clipped to the column-friendly width",
  );
  assert(
    deriveScenarioTitle({ candidateBackground: "", setting: "Ward", printedCardNumber: "4" })
      === "Ward — card 4",
    "title falls back to setting + printed number",
  );

  // Full payload: 9 bullets in, 9 bullets out, nothing routed to Task1..Task5.
  const built = buildPayloads({
    profession: "NURSING",
    setting: "Hospital Ward",
    candidateRoleLabel: "NURSE",
    interlocutorRoleLabel: "PATIENT",
    candidateBackground: "You are seeing a patient. They are anxious.",
    candidateTasks: nine,
    patientBackground: "You are the patient.",
    patientTasks: ["When asked, say you are worried.", "Ask about side effects."],
    footerNote: "\u00a9 Cambridge Boxhill Language Assessment",
    printedCardNumber: "12",
    missingFace: "none",
  }, "chunk_001");
  assert(built.create.tasks.length === 9, "all nine bullets reach the payload");
  assert(!("task1" in built.create), "legacy five-slot fields are never sent");
  assert(built.create.displayCardNumber === 12, "printed card number parsed");
  assert(built.script.openingResponse === "When asked, say you are worried.",
    "opening response is roleplayer bullet 1");
  assert(built.script.patientTasks.length === 2, "roleplayer bullet 1 is copied, not moved");
  assert(built.canPublish, "a complete card clears the gate");
  assert(built.overLength.length === 0, "a normal card breaches no column width");

  // An over-length value must be caught in the dry run, not by Postgres
  // mid-apply. A 602-char task bullet must NOT trip it — TasksJson is
  // unbounded and the legacy mirror clamps its own copy.
  const wide = buildPayloads({
    profession: "MEDICINE", setting: "s".repeat(161),
    candidateBackground: "bg", candidateTasks: ["x".repeat(602)], patientTasks: [],
  }, "c");
  assert(wide.overLength.length === 1, "the one over-wide column is reported");
  assert(wide.overLength[0].startsWith("setting is 161"), "the offending field is named");
  assert(wide.create.tasks[0].length === 602, "a long bullet is never clipped by the importer");

  // A short card is imported as a Draft, never skipped.
  const thin = buildPayloads({
    profession: "MEDICINE", candidateBackground: "You see a patient.",
    candidateTasks: ["only one"], patientTasks: [], missingFace: "candidate",
  }, "c");
  assert(!thin.canPublish, "thin card is blocked from publish");
  assert(thin.blockers.length === 2, "both blockers reported");
  assert(thin.create.background === "You see a patient.", "thin card still carries its content");

  // Faces split across a chunk boundary are rejoined into one complete card.
  const split = [
    { chunkId: "Nursing__Cards_p004-006", card: {
      profession: "NURSING", candidateBackground: "Candidate half.",
      candidateTasks: ["a", "b", "c"], patientTasks: [], sourcePages: [6],
      missingFace: "roleplayer", footerNote: "\u00a9 CBLA", uncertain: ["x"] } },
    { chunkId: "Nursing__Cards_p007-009", card: {
      profession: "NURSING", candidateBackground: "", candidateTasks: [],
      patientBackground: "Roleplayer half.", patientTasks: ["p1", "p2"],
      sourcePages: [7], missingFace: "candidate", interlocutorRoleLabel: "PATIENT",
      uncertain: ["y"] } },
  ];
  const joined = stitchSplitFaces(split);
  assert(joined.stitched === 1 && joined.entries.length === 1, "the two halves become one card");
  assert(joined.entries[0].card.missingFace === "none", "stitched card is complete");
  assert(joined.entries[0].card.patientTasks.length === 2, "roleplayer content carried over");
  assert(joined.entries[0].card.candidateTasks.length === 3, "candidate content kept");
  assert(joined.entries[0].card.uncertain.length === 2, "both halves' caveats retained");
  assert(buildPayloads(joined.entries[0].card, "c").canPublish, "stitched card clears the gate");

  // Non-consecutive pages must NOT be stitched — a wrong pairing is worse than
  // an obviously incomplete card.
  const farApart = structuredClone(split);
  farApart[1].card.sourcePages = [40];
  assert(stitchSplitFaces(farApart).stitched === 0, "non-adjacent pages are left alone");
  const otherDoc = structuredClone(split);
  otherDoc[1].chunkId = "Pharmacy__Other_p007-009";
  assert(stitchSplitFaces(otherDoc).stitched === 0, "halves from different PDFs are left alone");

  // Idempotency key is provenance, not title: two cards can share a title.
  const t1 = provenanceToken({ sourcePages: [4, 5] }, "Nursing__Cards_p004-006");
  const t2 = provenanceToken({ sourcePages: [6] }, "Nursing__Cards_p004-006");
  assert(t1 !== t2 && t1 && t2, "same chunk, different pages → different keys");
  assert(
    deriveSourceAttribution({ footerNote: "\u00a9 CBLA", sourcePages: [4, 5] }, "c").endsWith(t1.replace("Nursing__Cards_p004-006", "c")),
    "the token is recoverable from the stored attribution",
  );

  // Generic openers must not collapse into one indistinguishable title.
  const generic = (rest) => deriveScenarioTitle({
    candidateBackground: `You are a general practitioner. ${rest}`,
  });
  assert(generic("A 40-year-old presents with chest pain.")
    !== generic("A 12-year-old presents with a rash."),
    "titles extend past a shared generic opener");

  console.log("self-check: all assertions passed");
}

// ── Main ──────────────────────────────────────────────────────────────────
async function main() {
  if (SELF_CHECK) return selfCheck();

  const raw = loadCorpus();
  const { entries: corpus, stitched } = stitchSplitFaces(raw);
  const slice = LIMIT ? corpus.slice(0, LIMIT) : corpus;
  console.log(`[import] ${raw.length} card records in ${TRANSCRIPTS}`);
  if (stitched > 0) {
    console.log(`[import] ${stitched} card(s) rejoined from faces split across chunk boundaries → ${corpus.length} cards`);
  }
  if (LIMIT) console.log(`[import] limited to the first ${LIMIT}`);
  console.log(`[import] mode: ${APPLY ? `APPLY → ${API}` : "DRY RUN (no writes)"}`);

  /** Provenance token → existing card. See provenanceToken() for why not the title. */
  const existing = new Map();
  if (APPLY) {
    await signIn();
    const res = await api("GET", "/v1/admin/speaking/role-play-cards");
    const rows = res?.rolePlayCards ?? [];
    for (const row of rows) {
      const token = String(row.sourceAttribution ?? "").match(/\[[^\]]+\]$/)?.[0];
      if (token) existing.set(token, { cardId: row.cardId, status: row.status });
    }
    console.log(`[import] ${rows.length} card(s) already present, ${existing.size} with an import token — those are skipped, not duplicated`);
  }

  const report = [];
  const counts = {
    total: slice.length, stitched, created: 0, published: 0, draft: 0, skipped: 0, failed: 0,
  };
  let bulletsIn = 0;
  let bulletsOut = 0;

  for (const { file, chunkId, card, stitchedWith } of slice) {
    const { create, script, blockers, overLength, canPublish } = buildPayloads(card, chunkId);
    bulletsIn += (card.candidateTasks ?? []).filter((t) => String(t ?? "").trim()).length
      + (card.patientTasks ?? []).filter((t) => String(t ?? "").trim()).length;
    bulletsOut += create.tasks.length + script.patientTasks.length;

    const entry = {
      file,
      chunkId,
      stitchedWith: stitchedWith ?? null,
      printedCardNumber: card.printedCardNumber ?? null,
      profession: create.professionId,
      scenarioTitle: create.scenarioTitle,
      taskCount: create.tasks.length,
      patientTaskCount: script.patientTasks.length,
      sourceAttribution: create.sourceAttribution,
      uncertain: card.uncertain ?? [],
      blockers,
      overLength,
      outcome: "dry-run",
      cardId: null,
    };

    if (!create.professionId || overLength.length > 0) {
      entry.outcome = "failed";
      counts.failed += 1;
      report.push(entry);
      continue;
    }

    if (!APPLY) {
      entry.outcome = canPublish ? "would-publish" : "would-create-draft";
      counts[canPublish ? "published" : "draft"] += 1;
      report.push(entry);
      continue;
    }

    const key = provenanceToken(card, chunkId);
    if (existing.has(key)) {
      entry.outcome = "skipped-exists";
      entry.cardId = existing.get(key).cardId;
      counts.skipped += 1;
      report.push(entry);
      continue;
    }

    try {
      const created = await api("POST", "/v1/admin/speaking/role-play-cards", create);
      entry.cardId = created.cardId;
      counts.created += 1;
      existing.set(key, { cardId: created.cardId, status: created.status });

      await api("PUT", `/v1/admin/speaking/role-play-cards/${created.cardId}/interlocutor-script`, script);

      if (canPublish) {
        await api("POST", `/v1/admin/speaking/role-play-cards/${created.cardId}/publish`);
        entry.outcome = "published";
        counts.published += 1;
      } else {
        entry.outcome = "draft";
        counts.draft += 1;
      }
    } catch (err) {
      entry.outcome = "failed";
      entry.error = err.message;
      counts.failed += 1;
      // A card that got created but failed mid-way stays a Draft — visible and
      // fixable in the admin UI rather than silently half-written.
      console.error(`[import] ${chunkId} card ${card.printedCardNumber}: ${err.message}`);
    }
    report.push(entry);
  }

  mkdirSync(dirname(OUT), { recursive: true });
  writeFileSync(OUT, JSON.stringify({ generatedAt: new Date().toISOString(), api: API, apply: APPLY, counts, bulletsIn, bulletsOut, cards: report }, null, 2), "utf8");

  console.log("");
  console.log(`  cards            ${counts.total}`);
  console.log(`  publishable      ${counts.published}`);
  console.log(`  draft-only       ${counts.draft}`);
  if (APPLY) console.log(`  created          ${counts.created}`);
  if (APPLY) console.log(`  skipped (exists) ${counts.skipped}`);
  console.log(`  failed           ${counts.failed}`);
  console.log(`  task bullets     ${bulletsIn} read → ${bulletsOut} mapped`);
  console.log(`  report           ${OUT}`);

  const wide = report.filter((e) => e.overLength?.length);
  if (wide.length > 0) {
    console.error(`[import] ${wide.length} card(s) exceed a column width and would be rejected:`);
    for (const e of wide) console.error(`  ${e.chunkId}: ${e.overLength.join("; ")}`);
    process.exitCode = 1;
  }

  if (bulletsIn !== bulletsOut) {
    console.error(`[import] FIDELITY BREACH: ${bulletsIn - bulletsOut} bullet(s) lost in mapping.`);
    process.exitCode = 1;
  }
  if (counts.failed > 0) process.exitCode = 1;
}

main().catch((err) => {
  console.error(err.message);
  process.exit(1);
});
