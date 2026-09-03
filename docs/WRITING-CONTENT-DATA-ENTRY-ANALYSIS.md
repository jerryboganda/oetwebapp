# Writing Module — Full Data-Presentation & Data-Entry Analysis

**Date:** 2026-09-01
**Purpose:** Pre-flight analysis before real production Writing content (from `OET Materials & Videos Data/Materials/Writing/**` and `OET Materials & Videos Data/Writing/**`) is uploaded to production. Every claim below was produced by a 39-agent forensic sweep (36 parallel file-level readers + 3 cross-layer reconciliation passes) that read the actual source files in full — not inferred from naming or memory — plus a handful of facts I re-verified myself directly against source after the sweep flagged them as decision-critical. Citations (`file:line`) are preserved in the raw per-area reports under the session scratchpad if a claim needs re-checking; the highest-stakes claims are cited inline below.
**Verdict up front:** this module is **not safe for a naive bulk-JSON-import** of your content. Three independent, non-interoperable admin surfaces write into overlapping parts of the same database table, at least one bug is a **100%-reproducible crash** on the app's own canonical vocabulary, and one gap is a **confirmed, silent content-presentation failure** (practice mode literally cannot display task-prompt text — only a PDF). Section 8 is the concrete playbook that avoids all of this.

---

## 0. Executive summary — the 10 things that matter most

1. **Practice mode cannot render task-prompt text at all — only a PDF.** Verified myself directly against `WritingV2Contracts.cs:323-337` and `WritingV2ResponseMapper.cs:72-90`: the DTO served by `GET /v1/writing/scenarios/{id}` (what `/writing/practice/session/*` calls) has exactly 13 fields — it has **no** `taskPromptMarkdown`, `fixedInstructions`, `wordGuideMin/Max`, `writerRole`, `todayDate`, or `answerSheetPdf*`. If a scenario has no `stimulusPdfMediaAssetId`, the practice screen's stimulus panel renders **nothing** (`components/domain/writing/WritingStimulus.tsx`'s PDF-or-text fallback reads fields that are `undefined` on the wire). **Every task must have its case-notes PDF attached, no exceptions**, for practice mode to work — text-only import is not enough there (it is enough for paper/mock mode, which fetches a richer DTO — see §1.2).
2. **`WritingScenario.LetterType` is `varchar(8)`, but the app's own canonical letter-type vocabulary is 15–33 characters long.** (`routine_referral`=16, `update_referral_specialist_to_gp`=33, etc.) Importing with a canonical value throws an unhandled DB exception (500) on save. No code path today produces a value that is both DB-legal and canonical — see §4.1.
3. **There are three independent admin write paths into the same `WritingScenarios` table** (System A "Papers"/ContentPaper, System B "Tasks v2", legacy Scenario CRUD), with disjoint field coverage, disjoint vocabularies, and no cross-validation. Pick one per §3 — do not mix.
4. **`PUT` to update an existing task is a destructive full-replace for 9+ fields**, not a patch: omitting `taskPromptMarkdown`, `fixedInstructions`, `sourceProvenance`, or either PDF asset id on an update **wipes them to null/empty**, even though the admin UI has no controls for most of them. See §4.4.
5. **No case-note-drill content can be authored through any admin UI or API** — that content type has zero write endpoints. Direct DB/migration insert only. See §4.5.
6. **No mock exam paper (`WritingMock`) can be created through any HTTP endpoint** — it is a thin wrapper with zero admin callers anywhere in the codebase. Must be hand-inserted after the underlying `WritingScenario` exists. See §4.6.
7. **The database has no CHECK constraints and no foreign keys on any Writing table** — confirmed directly in the purge migration's own comment and the schema migrations. Every "enum" (profession, letter type, status, severity…) is a free `varchar` enforced, if at all, only in application code that in most places (per §4.9) doesn't actually run. **Content must go through the real service/API layer, never raw SQL**, or invalid values land silently.
8. **None of the 5 existing content-tooling scripts write to the database.** They produce intermediate JSON/rulebook files only, and the one PDF-extraction tool that targets exactly your folder shape is hardcoded to Medicine only and to exactly 6 fixed "Writing 1–6" folders. See §6.
9. **Your real source folders already match the shape the team explicitly designed for** (`docs/CONTENT-UPLOAD-PLAN.md` §1.4, written by reading `Project Real Content/Writing_/**`): one case-notes PDF + one answer-sheet PDF per task, grouped by profession and letter type. See §7.
10. **A migration/purge already ran in production** (`20260713090000_PurgeSeededWritingContent`) that deletes any row whose `AuthorId` matches a seed sentinel. Real content is safe from this **only if it's authored with a real admin user id** — never `system:seed*` or `system:writing-seed`. See §5.

---

## 1. How Writing data is presented to students today

### 1.1 Full student route inventory

| Route | Reads | Notes |
|---|---|---|
| `/writing` | nothing dynamic | static hub, 2 nav cards only (`practice`, `submissions`) |
| `/writing/welcome` | `GET /v1/writing/v2/profile` | onboarding gate; auto-skips if already onboarded |
| `/writing/profile-setup/{profession,goals,focus,confirm}` | — | 4-step wizard → `POST /v1/writing/profile` + `/onboarding/complete` |
| `/writing/today` | V2 `/v1/writing/v2/today`, falls back to legacy `/v1/writing-pathway/plan/today` | routes out via server-supplied `actionHref`, renders no content itself |
| `/writing/pathway` | legacy-only `GET /v1/writing-pathway/pathway` | 10-week roadmap; **does not use the V2 pathway API at all** |
| `/writing/skill-tree` | V2-only `GET /v1/writing/v2/lessons` + `/v1/writing/stats/skills` | W1–W8 mastery cards |
| `/writing/practice/library` → `/writing/practice/session/[scenarioId]` | `GET /v1/writing/scenarios[/{id}]` (`WritingScenarioDto`) | **13-field DTO — see §0.1, no task-prompt text unless a PDF is attached** |
| `/writing/paper/session/[id]` | tries `GET /v1/admin/writing/tasks/{id}` (`WritingTaskDto`, full 20+-field DTO) **first**, falls back to the scenario endpoint | the only student page that can render text-only (no-PDF) content |
| `/writing/mocks` → `/writing/mocks/session/[id]` | `GET /v1/writing/mocks`, `/v1/writing/mocks/sessions/{id}` | wraps a `WritingScenario` via `WritingMock.ScenarioId` |
| `/writing/lessons`, `/writing/lessons/[slug]` | **two different backends** — catalogue reads V2 `WritingLessonV2`, but the detail page reads the *legacy slug-keyed* lesson table | a lesson written only into V2 is browsable but 404s on open unless a same-slug legacy row also exists |
| `/writing/drills`, `/drills/[type]`, `/drills/practice/[id]` | static JSON drill bank (Zod-validated) + a separate legacy "pathway" drill queue | the V2 `WritingDrill` SQL table has a component but **it's orphaned — no route imports it** |
| `/writing/case-notes-drills` | V2 `WritingCaseNoteDrill` table | read-only from the app's side — no admin write path exists (§4.5) |
| `/writing/canon`, `/writing/canon/[ruleId]` | **two different backends** — main browse page reads the 172-rule locked Rulebook JSON; the rule-detail/dispute page reads the free-editable SQL `WritingCanonRule` table | content in the SQL table is invisible on the main browse page |
| `/writing/model` | `ContentItem.ModelAnswerJson` | separate from everything else in this document |
| `/writing/showcase` | `WritingShowcasePost` | **not admin-authorable** — auto-derived from opted-in A-grade learner submissions |
| `/writing/common-mistakes[/mine]` | `WritingCommonMistake` | admin-CRUD, mostly clean |
| `/writing/result*`, `/writing/submissions/[id]/*`, `/writing/feedback` | grading/assessment results | downstream of content, not a content-entry concern |

### 1.2 The practice/paper DTO split (the single most important fact in this document)

Two different DTOs represent "a writing task," and which one a student's browser receives depends entirely on **which page** they're on:

- **Practice mode** (`/writing/practice/session/[scenarioId]`) → `getWritingScenario()` → `GET /v1/writing/scenarios/{id}` → `WritingScenarioResponse`. I verified this DTO directly (`WritingV2Contracts.cs:323-337`): `Id, Title, LetterType, Profession, SubDiscipline, Topics, Difficulty, CaseNotesStructured, IsDiagnostic, Status, CreatedAt, UpdatedAt, StimulusPdfMediaAssetId, StimulusPdfDownloadPath`. That's it. No prompt text, no instructions, no word-count guide, no answer-sheet PDF.
- **Paper/mock mode** (`/writing/paper/session/[id]`) → tries `getWritingTask()` → `GET /v1/admin/writing/tasks/{id}` → `WritingTaskDto` **first** (falls back to the scenario DTO only on 404). This DTO has everything: `taskPromptMarkdown, writerRole, todayDate, expectedPurpose, expectedAction, fixedInstructions, wordGuideMin/Max, readingTimeSeconds, writingTimeSeconds, simulationModes, markingMode, answerSheetPdfMediaAssetId`, plus the stimulus PDF fields.
- **`WritingStimulus.tsx`** (the component that actually renders the case-notes stimulus) picks a PDF viewer if `stimulusPdfDownloadPath` is present, else falls back to plain text built from `taskPromptMarkdown` + `fixedInstructions` — **and never reads `caseNotesStructured` at all**, even though that field exists on the wire and is populated by the legacy Scenario-CRUD write path.
- **Net effect:** a task authored with rich prompt text but no PDF renders correctly in paper/mock mode and renders **blank** in practice mode. A task with only a PDF and no prompt text renders identically in both. The only configuration that is safe everywhere is **prompt text + fixed instructions + word guide AND a stimulus PDF**, all populated — which conveniently is also what a well-formed exam task should have anyway.
- One more oddity worth flagging, not fixing: the paper-mode student session page reaches an `/v1/admin/...`-prefixed endpoint directly. Auth/role gating for a learner hitting that route isn't visible in the frontend files; it presumably works today (the flow ships), but a schema/permission change to that admin endpoint could silently break student paper-mode without anyone touching a student-facing file.

### 1.3 What's rendered around the stimulus

- **Highlights** (yellow marks on the case-notes PDF): per-(user, scenario) autosaved JSON, `GET/PUT /v1/writing/highlights/{scenarioId}`, 800ms debounce. Malformed marks are silently dropped, not rejected.
- **Drafts**: autosaved every 5s, `GET/PUT /v1/writing/drafts/{scenarioId}/{mode}`.
- **Timers**: reading window defaults to 5 min (`readingTimeSeconds`, overridable per-task in the DTO, but the paper-mode *direct-launch* path and the practice-mode word counter both **hardcode** their own constants and never actually read the per-task override — a pre-existing drift, not something a content migration causes or can fix from content alone).
- **Word counter**: practice mode hardcodes a 180–220 target and ignores `wordGuideMin/Max` entirely; only the paper-mode printed booklet reads the real per-task word guide (default 180/200 there). A migrated custom word-count range is therefore invisible in practice mode.
- **Submission**: `POST /v1/writing/submissions` with `{scenarioId, mode, letterContent, wordCount, timeSpentSeconds, inputSource, caseNoteHighlightsJson}`. No client-side length/required validation before POST.
- **Eligibility/credit gate runs before any content is fetched**: both session pages call `GET /v1/writing/scenarios/{id}/eligibility` first and will show a "buy credits" modal instead of the task if the learner has none — a perfectly-formed migrated task is invisible to a zero-credit learner, which matters for QA'ing that migrated content actually renders (test with a funded account).

### 1.4 Secondary content types students see

- **Lessons** (`WritingLessonV2`): clean single-system CRUD, *but* the lesson-detail page reads a different, legacy slug-keyed table than the catalogue — a lesson needs a matching row in both to be fully navigable.
- **Drills**: three unrelated systems (static JSON bank, V2 SQL table [orphaned from the UI], legacy pathway free-text drills). The static JSON bank is the only one actually wired to `/writing/drills*` for most content.
- **Case-note drills**: DB schema + child-sentence table exist and the *read* side is wired to `/writing/case-notes-drills`, but there is no write path (§4.5).
- **Canon rules**: two systems, only one of which (the 172-rule locked Rulebook JSON) is on the main browse page; the free-editable SQL table only shows on rule-detail/dispute links.
- **Common mistakes, showcase, model answers**: each is its own small system; showcase is derived from student work, not authored.

---

## 2. The data model — `WritingScenario`, the core content table

This is the table every "writing task" (practice item, paper item, and the item a `WritingMock` wraps) ultimately lives in. Full field-by-field table, DB ↔ backend DTO ↔ frontend TS/zod ↔ admin form ↔ import JSON, is reproduced below from the cross-verified reconciliation pass (re-checked directly against `WritingScenarioEntities.cs`, `WritingTaskAuthoringService.cs`, `WritingV2Contracts.cs`, `lib/writing/types.ts`, `builder-state.ts`, `WritingTaskBuilder.tsx`).

| Field | DB column | Required to **create** | Required to **publish** | Settable via Task Builder UI | Settable via `/tasks/import` JSON | Risk if omitted on an update |
|---|---|---|---|---|---|---|
| `Title` | `varchar(200)` | **Yes** (only server-enforced check) | Yes | ✅ | `taskTitle` (falls back if blank, never errors) | safe (not overwritten to blank) |
| `Profession` | `varchar(64)` | No | Yes | ✅ dropdown | `profession` (plain string, no enum check) | safe |
| `LetterType` | **`varchar(8)`** | No | Yes | ✅ dropdown (`LT-*` codes) | `taskType` (see §4.1 — **crash risk**) | safe |
| `Difficulty` | `int` | No | No | ✅ 1–5 | not present | **unclamped if omitted on create (defaults to 0, below valid range)** |
| `TaskPromptMarkdown` | `text` | No | No (not gated!) | ❌ no control rendered | `writingTask.instruction` | **wiped to null if omitted** |
| `WriterRole` / `TodayDate` | `varchar(256)` / `varchar(64)` | No | No | ❌ | `caseNotes.candidateRole` / `.todayDate` | **wiped if omitted** |
| `FixedInstructionsJson` | `jsonb`, default `[]` | No | No | ❌ | `writingTask.fixedInstructions` | **wiped to `[]` if omitted** |
| `WordGuideMin/Max` | `int`, default 180/200 | No | **Yes** (min>0, max≥min) | ❌ (import/direct-API only) | `writingTask.wordGuide.{min,max}` | safe — preserved if omitted on update |
| `StimulusPdfMediaAssetId` | `varchar(64)` | No | No (not gated!) | ✅ chunked upload | **not present — must PUT separately** | **detached (set null) if omitted on an update PUT** |
| `AnswerSheetPdfMediaAssetId` | `varchar(64)` | No | No | ✅ chunked upload | **not present — must PUT separately** | same detach risk |
| `SourceProvenance` | `varchar(512)` | No | No | ❌ | not present | **wiped if omitted; no UI or import path sets it at all today** |
| `IntegrityAcknowledged(At/ById)` | derived from `DateTimeOffset?` | No | No | ❌ (hardcoded `false` on every new task) | not present | effectively broken end-to-end — see §4 |
| `SimulationModes` / `MarkingMode` | `varchar(16)`, defaults `both`/`tutor` | No | No | ❌ | not present | stuck at default unless set via a raw API call |
| `CaseNotesStructured` (child table) | separate table, no column | No | No | ❌ **no field on the Tasks-v2 DTO at all** | not present | **only the legacy Scenario-CRUD surface can write this — Tasks-v2 import can never populate it** |

**Practical minimum for a genuinely complete, safely-published, both-modes-visible task**: `title`, `profession`, `letterType` (short internal code, not canonical — see §4.1), `wordGuideMin/Max`, `taskPromptMarkdown`, `fixedInstructions`, `stimulusPdfMediaAssetId` (uploaded PDF), and ideally `answerSheetPdfMediaAssetId`. None of these except `title` and the word-guide pair are actually *enforced* — the rest is a content-quality bar you must self-police, because the API won't (§4.9).

Other content-model notes:
- **`WritingMock`** (the catalogue/publish wrapper around a scenario) is exhaustively 6 columns (`Id, ScenarioId, Title, Difficulty, Status, CreatedAt`) with **zero HTTP creation path anywhere** — `Difficulty` is captured but never serialized to the student response either. Must be inserted directly (via migration or DB tool) once the underlying scenario is published; `Status` must be the exact case-sensitive string `"published"` or it never appears in `GET /v1/writing/mocks`.
- **`WritingCaseNoteDrill`** + its child `Sentence` table have zero admin write endpoints (interface only exposes `List/Get/SubmitAttempt`). A zero-sentence drill saves fine via direct insert but is permanently unattemptable — sentences must be inserted atomically with the parent.
- **Canon rules, System B** (`WritingCanonRule`, freely admin-editable): `Id` DB column is `varchar(16)` but its own DTO validation attribute claims 64 — a 17+ char id passes the (unenforced) DTO check and then throws a raw Postgres error. Same DTO-wider-than-DB pattern on `Category` (64 vs DB 32) and `Severity` (16 vs DB 8, and this table's `high/medium/low` vocabulary doesn't match the System-A Rulebook's `critical/major/minor/info`). The admin form's "detection config" field is sent under the wrong JSON key and is silently dropped every time — any regex/structural rule authored here is **permanently inert** for detection, with no error anywhere including the page's own "test rule" panel.
- **No DB CHECK constraints and no foreign keys exist on any Writing table** — confirmed at the migration level and explicitly stated in the purge migration's own comment ("ordering is for cleanliness, not correctness"). The one exception is the new-in-August V11 assessment-report schema, which does use real cascading FKs.

---

## 3. Three parallel authoring systems — which one to actually use

| | **System A — "Papers"** (`ContentPaper`, `/admin/content/writing`) | **System B — "Tasks v2"** (`WritingTaskDto`, `/admin/writing/tasks`) | **Legacy Scenario CRUD** (`/v1/admin/writing/scenarios`) |
|---|---|---|---|
| Designed intent | The **officially designed** pipeline (`docs/CONTENT-UPLOAD-PLAN.md`) — multi-asset papers, chunked upload, versioning, matches your real folder structure exactly | A parallel, later-built "unified task" system | Oldest surface, minimal fields |
| Admin UI | Full list/editor/review workflow, 5-state lifecycle (`Draft→InReview→Published→Archived/Rejected`) | `WritingTaskBuilder` form — but only renders **4 of 13 upsert fields** in JSX (title/profession/letterType/difficulty) plus the two PDF slots; everything else is silently defaulted | No dedicated admin UI found in the reviewed surface |
| Reaches students how | **Never directly** — must be "projected" into `WritingScenario` via `WritingTaskProjectionService`, self-labeled a temporary "WS-B2 bridge," triggered only on `approve-publish`, and it swallows its own exceptions (a publish "succeeds" even if the projection into the student-visible table fails) | Directly writes `WritingScenario` via its own upsert DTO — this is what paper-mode fetches first | Directly writes `WritingScenario`, but its response mapper is the incomplete 13-field one |
| Bulk import | ZIP/manifest import is **designed but only partially built** per the plan doc; convention parser matches your exact filenames (`*Case Notes*.pdf`→CaseNotes, `*Answer Sheet*.pdf`→ModelAnswer, folder `Writing N (Urgent Referral)`→`LetterType=urgent_referral`) | `POST /v1/admin/writing/tasks/import` — real, working, JSON-only (no PDFs inline; upload those in a follow-up PUT) | none found |
| Can set structured case-note sentences | No | **No — not on the DTO at all** | **Yes — the only surface that can** |
| Known correctness issues | Projection never wires the uploaded PDF asset into the scenario; hardcodes `Status="published"` on every projection regardless of completeness; letter-type has no length check on the projection path (crash risk, §4.1) | Import-mapper's `taskType`→`LetterType` mapping also risks the crash (§4.1); most upsert fields have no UI control at all | `UpdateAsync` silently discards any `Status` sent, no known UI wired to it in the reviewed surface |

**Recommendation:** author real content through **System B (Tasks-v2 admin form / `/tasks/import` + a follow-up PUT for PDFs)**, because it's the one path that (a) has a working import endpoint, (b) is what the paper/mock session screen fetches first with the full field set, and (c) doesn't route through the self-described-temporary projection bridge. Use it for every field **except** structured case-note sentences (needed only if you want the case-note-drill exercise type to work from the same content) — those require going through the legacy Scenario-CRUD API as a **second, additional** write against the same row, not a replacement for the Tasks-v2 write. Do **not** use System A/ContentPaper for real content today unless the team first fixes the projection bridge (PDF wiring, letter-type length, non-swallowed errors) — it looks like the "proper" long-term design but is not the live path that actually reaches students correctly yet.

---

## 4. Confirmed bugs & landmines, ranked by risk to "zero data loss, zero bypassed validation"

1. **`LetterType` DB-overflow crash (CRITICAL, reproducible).** `WritingScenarios.LetterType` is `character varying(8)`. Every one of the app's own 6 canonical letter-type strings (`routine_referral`…`update_referral_specialist_to_gp`) is 15–33 characters. The import mapper passes a canonical value through unchanged, so `POST /v1/admin/writing/tasks/import` with `"taskType": "update_referral_specialist_to_gp"` throws an unhandled 500 at `SaveChangesAsync`. The Task Builder's own dropdown avoids this by using short `LT-RR`-style codes with no canonicalization step — **that's the vocabulary to actually use for `letterType`/`taskType`, never the app's own "canonical" long-form strings**, until someone widens the column.
2. **Practice mode's text fallback is unreachable** — see §0.1/§1.2. Every task needs an attached stimulus PDF.
3. **Three silently-dead admin-form fields**, verified in code: Drill `inputVariant` (selector is fully inert, response always says `"text"`), Canon `detectionConfig` (wrong JSON key sent, always discarded), Scenario `caseNotesMarkdown` (form field doesn't map to the real `CaseNotesStructured` shape at all — this field was actually *dropped from the DB* in a 2026-06-29 migration, so the legacy admin form is editing a column that no longer exists).
4. **`ApplyUpsert` (the method both Create and Update funnel through) unconditionally nulls-out 9 fields if omitted on a PUT**: `taskPromptMarkdown`, `writerRole`, `todayDate`, `expectedPurpose`, `expectedAction`, `fixedInstructions` (→`[]`), `sourceProvenance`, and both PDF asset ids. **Any update script must always resend the full current value of these fields, never a partial patch**, or it will silently delete existing content.
5. **No admin/API write path exists for Case-Note Drills** at all — direct DB/migration insert only, and it must be atomic with at least one child sentence row or the drill is permanently unattemptable.
6. **No HTTP path creates a `WritingMock` row** — confirmed via a full-codebase grep (zero admin-endpoint references, zero frontend form). Must hand-insert after the scenario exists, with `Status` exactly `"published"` (case-sensitive).
7. **DTO string-length ceilings wider than the real DB columns**, confirmed in 5 places: Canon `Id` (DTO 64 vs DB 16), Canon `Category` (64 vs 32), Canon `Severity` (16 vs 8), Drill `DrillType` (64 vs 32), plus DTOs with *no* length cap at all where the DB has one (Canon `RuleText`, Mistake `ExampleWrong`/`ExampleRight`). Since DataAnnotations aren't enforced anyway (#9), these become raw Postgres truncation-error crashes instead of clean validation messages — **pre-validate every string length against the DB limits above in your own tooling, don't trust either DTO's stated limit.**
8. **At least four incompatible letter-type vocabularies coexist** with no shared enum: the app's 6-value canonical set (`routine_referral`…); the Task Builder's `LT-*` short codes; the static JSON drill bank's `referral|urgent_referral|discharge|transfer|advice|update|non_medical_referral`; and the AI-classification/preflight services' own `discharge|transfer|referral_to_gp|gp_referral` set. Similarly, **profession** has at least 3 different casings/sets (`occupational-therapy` vs `occupational_therapy` vs `occupationaltherapy`) across systems. **Decide the target system first, then use exactly its vocabulary — never copy a value from one subsystem's dropdown into another's field.**
9. **DataAnnotations validation is decorative, not enforced**, on every content-upsert route reviewed except the Writing Options endpoint. A blank "required" field or an over-length string is not rejected with a clean 400 — it either silently persists or crashes the DB save. **Any bulk-upload tool must independently validate every constraint listed in this document; the API will not catch what its own attributes claim to guard.**
10. **`UpdatedAt` on the student/admin API response always equals `CreatedAt`** (a real bug in the response mapper — verified directly, `WritingV2ResponseMapper.cs:87`), and **`WritingScenarioStructuredSentence.Notes` is stored but never reaches any API response** (dropped in the same mapper). Neither blocks content entry, but don't rely on "last updated" timestamps from the API, and don't bother authoring per-sentence `Notes` — they're write-only.
11. Mock submissions really do call the AI grading pipeline despite a doc-comment and a response-DTO comment both stating mock Writing is "never AI-graded" — not a content-entry concern, flagged in case it affects your QA expectations when test-submitting migrated mock content.
12. **Export→Import round-trips are lossy.** The `/tasks/export` → `/tasks/import` JSON envelope drops `difficulty`, `simulationModes`, `markingMode`, `sourceProvenance`, `integrityAcknowledged`, and both PDF asset ids. Never use export-then-reimport as a way to "clone" a fully-configured task; those fields must be re-set via a direct PUT afterward.

---

## 5. Migration & production-state facts

- **`20260713090000_PurgeSeededWritingContent`** already ran (irreversible, `Down()` is a documented no-op). It deletes every Writing row (scenarios, submissions, grades, appeals, mocks, structured sentences, embeddings, and related `ContentItem`/`ContentPaper` rows) whose `AuthorId` matches `system:seed%` or equals `system:writing-seed`. **Any content you enter must be authored under a real admin user id** (which `WritingTaskAuthoringService.CreateAsync` does automatically for you when you go through a real authenticated admin session) — never insert rows with a `system:seed*`-style `AuthorId`, or a future re-run of an equivalent cleanup could delete it.
- The purge migration's own comment states explicitly there are **no FK constraints between any Writing tables** — referential integrity is an application-layer contract only; a bulk loader must replicate the app's own ordering (parent scenario before child sentences, etc.) since the DB won't catch orphans.
- **2026-06-29 (`RemoveWritingTaskFieldGroups`)** dropped `CaseNotesMarkdown`, `CaseNotesStructuredJson`, `RecipientJson`, `ModelAnswerExemplarId`, the whole `WritingExemplar*` table family, and `WritingContentChecklistItems` from the schema — data loss was explicit and intentional ("Down() does not restore data"). **Case notes now live primarily in the uploaded stimulus PDF, not as DB text**, for the tables that matter to student presentation.
- The August V11 assessment-report schema (`AddWritingAssessmentV11`) is the first and only Writing migration to use real cascading foreign keys — it's a downstream grading/reporting concern, not part of content entry.
- Two migration files in the reviewed history are missing the inline `[Migration]`/`[DbContext]` attributes that this repo's own `CLAUDE.md` house style requires for hand-authored migrations — worth a sanity check with the team, not something to fix as part of a content upload.

---

## 6. Existing content tooling — what's actually usable

None of the following writes to the database. All are file-to-file tools:

- **`scripts/extract-writing-pdfs/Program.cs`** — targets exactly `Project Real Content/Writing_/Writing 1..6*` folders, hardcodes `profession: "medicine"` for every entry, silently skips (prints, doesn't fail) any folder missing a case-notes or answer PDF, extracts text via PdfPig (no OCR fallback — a scanned PDF yields silent garbage), and writes a seed JSON meant to feed a `WritingSampleSeeder` hosted service that **no longer exists in the codebase** (removed as part of the same fix that shipped the purge migration). **This tool is not usable as-is for your real content** — it's Medicine-only, fixed to 6 items, and its output has nowhere to load into anymore.
- **`scripts/generate-writing-rulebooks.cjs`** / **`scripts/conformance/tag-writing-rulebooks.mjs`** — rulebook JSON generation/patching only, unrelated to task content, not a content-loading path.
- **`scripts/writing-v2-smoke.sh`** — anonymous HTTP smoke test (status-code only), useful for confirming an endpoint is alive post-deploy, reveals no DTO shapes.
- **`scripts/audit-contentitem-seed.py`** — read-only regex auditor for one specific missing-field defect in an unrelated `ContentItem` seed file; not applicable to Writing tasks.
- **Precedent worth copying**: `scripts/admin/import-reading-manifests-local.mjs` (Reading's importer) shows the right shape for a *real* bulk importer — chunked upload with SHA-256 dedup, a hard pre-publish `isPublishReady` validation gate that throws before publish, `--dry-run` offline validation, `--replace-existing` update semantics. **No Writing equivalent of this script exists today.** Building one (or a smaller version scoped to what §8 needs) is the safest way to load your real content rather than hand-clicking dozens of tasks through the admin UI.
- **Precedent worth heeding**: `scripts/listening/audit-listening-questions.sql` is a *post-hoc* SQL audit built specifically because pre-publish validation historically wasn't enough to keep placeholder/artifact text ("See PDF", "Option A/B/C", PDF-extraction page-footer junk) out of production for Listening. **Plan for an equivalent post-import audit query against `WritingScenarios`/`WritingScenarioStructuredSentences`** once real content is loaded, not just a pre-publish gate.

---

## 7. Your real source material vs. what the system expects

`OET Materials & Videos Data/Materials/Writing/{Medicine,Nursing,Dentistry,...}` already matches the shape the team designed for (`docs/CONTENT-UPLOAD-PLAN.md` §1.4, written from reading the canonical `Project Real Content/Writing_/` folder): each task is a **case-notes PDF + an answer-sheet/model-answer PDF pair**, grouped by profession, with an explicit letter-type in the folder name (e.g. "Case Notes - 2026 Recalls/4- Urgent Referral (Mrs Karen Smith)"). Nursing's "New Writing Tasks" folder alone has 50+ per-patient PDFs. There are also grammar-rule PDFs and a "Writing Criteria" PDF per profession that are reference/rulebook material, not per-task content.

**Gap**: none of this raw material carries the structured metadata the system needs per task — `title`, `profession`, `letterType` (in the short `LT-*` form, §4.1), `wordGuideMin/Max`, `writerRole`, `todayDate`, `fixedInstructions`. That has to be authored once per task during ingestion (a spreadsheet/JSON manifest, one row per folder, is the natural shape) — the PDFs themselves only become the `stimulusPdfMediaAssetId`/`answerSheetPdfMediaAssetId` uploads.

---

## 8. Recommended ingestion procedure

For each real writing task (one per source folder):

1. **Derive the manifest row** by hand or script: `title`, `profession` (use System B's 13-value hyphenated set), `letterType` (short `LT-*` code — map from the folder's stated letter type, do **not** use the app's long canonical string, §4.1), `difficulty` (1–5, your judgment), `taskPromptMarkdown` (the instruction/brief text — write this even if you also have a PDF, so paper-mode text-fallback and any future practice-mode fix work), `writerRole`, `todayDate` if applicable, `fixedInstructions` (standard OET boilerplate — 4 lines used elsewhere as defaults), `wordGuideMin/Max` (180/200 unless the task specifies otherwise).
2. **Upload the case-notes PDF** via the chunked-upload flow (`POST /v1/admin/uploads` → parts → `/complete`) to get a `mediaAssetId`. Repeat for the answer-sheet PDF.
3. **Create the task** via `POST /v1/admin/writing/tasks/import` with the manifest JSON (§4's core-field table has the exact envelope shape), **then immediately follow with `PUT /v1/admin/writing/tasks/{id}`** carrying the full `WritingTaskUpsertDto` — including `stimulusPdfMediaAssetId`, `answerSheetPdfMediaAssetId`, `sourceProvenance`, `integrityAcknowledged: true`, `simulationModes`, `markingMode` — since the import envelope alone cannot set any of those (§4.4, §4.12). Always send the **full** field set on this PUT, never a partial one (§4.4).
4. **Call `GET /v1/admin/writing/tasks/{id}/validate`** and confirm `isPublishReady: true` before publishing — this is the one real server-side gate that exists (title/profession/letterType non-blank + word-guide sanity). It will **not** catch a missing PDF or missing prompt text (§0's #2 and #4), so self-check those two specifically before every publish.
5. **`POST /v1/admin/writing/tasks/{id}/publish`.**
6. If the task should also support the case-note-drill relevance exercise, separately call the legacy `/v1/admin/writing/scenarios/{id}` endpoint to write `CaseNotesStructured` sentences against the **same scenario id** (§3) — this is additive, not a replacement for steps 1–5.
7. If the task should appear as a mock exam paper, hand-insert (or write a small one-off EF migration for) a `WritingMock` row pointing at the scenario's id, with `Status = "published"` exactly.
8. **QA every migrated task in both practice mode and paper mode**, logged in as a funded test account (credit gate, §1.3) — because the two modes render from different DTOs (§1.2), a task that looks right in the admin preview is not proof it renders right for a student.
9. Author every row under a real admin session (never a `system:seed*` `AuthorId`, §5).
10. After a batch is loaded, run a manual audit query in the spirit of `audit-listening-questions.sql` (§6) against `WritingScenarios`/`WritingScenarioStructuredSentences` for placeholder text, PDF-extraction artifacts, and empty prompts before treating the batch as production-ready.

Repo-wide operating rules that apply once real implementation work starts (from this repo's `CLAUDE.md`): EF migrations here are **hand-authored**, never `dotnet ef migrations add` output; all file I/O for user/content data goes through `IFileStorage`, never raw `File.*`; a push to `main` triggers the real blue/green production deploy automatically — the VPS is never touched directly; heavy `dotnet build/test` runs belong on CI, not this machine.

---

## 9. Decisions confirmed / resolved after this analysis

1. **System choice: confirmed — System B (Tasks-v2) is primary.** Use the legacy Scenario-CRUD endpoint only as a supplementary second write against the same scenario id if/when case-note-drill structured sentences matter.
2. **`LetterType varchar(8)`**: keeping the short `LT-*` codes (no schema change requested) — validated directly against production (see §10, the one real row uses `LetterType='LT-RR'`, 5 chars). Do not use the app's long canonical strings.
3. **Bulk-import tooling: build it, two-stage.** See §11 — the real source material (§7 revised) is far messier than a filename-convention parser can handle alone; the plan below reflects that.
4. **Production content state: confirmed by direct read-only query** (§10) — production is effectively a blank slate for real exam-task content. No collision risk.

---

## 10. Production database state (confirmed 2026-09-01, read-only query via documented VPS access)

| Table | Row count | What this means |
|---|---|---|
| `WritingScenarios` | **1** | A single placeholder/test row: *"Writing 1 - Routine Referral"*, medicine, `LetterType='LT-RR'` (confirms the 5-char short code is what's actually live — validates §4.1), `Status='published'`, `AuthorId='auth_admin_local_001'` (a real admin id — safe from the purge), has both a stimulus PDF and an answer-sheet PDF attached, but **`TaskPromptMarkdown` is empty (0 chars)** — direct live confirmation that prompt text is not required to publish (§0.1/§2). This single row should be treated as a placeholder to review/replace, not real content. |
| `WritingMocks` | **0** | No mock exam papers exist at all — matches §4.6 (no HTTP path creates one). |
| `WritingScenarioStructuredSentences` | **0** | No structured case-notes exist — matches §2 (Tasks-v2 import can't populate this). |
| `WritingLessonsV2` | 48 | Starter/stub lesson content already seeded (per `docs/WRITING-MODULE-IMPLEMENTATION-LOOP.md`'s explicit note that code shipped with editable starter content, not final Dr Ahmed-approved material). Not in scope of "the writing folder" unless you want it reviewed separately. |
| `WritingDrills` | 90 | Same — starter/stub drill content. |
| `WritingCaseNoteDrills` | 36 | Exists despite having no admin write path (§4.5) — must have been loaded via a migration/seed script directly. |
| `WritingCanonRules` (SQL, System B) | 197 | Substantial existing canon-rule content (separate from the 172-rule locked Rulebook JSON, §2). |
| `WritingCommonMistakes` | 50 | Existing content. |
| `WritingSubmissions` | 41 | Real/test student attempts already recorded — do not touch or purge this table as a side effect of any Scenario work. |
| `WritingShowcasePosts` | 0 | Expected — derived from opted-in student work, not authored. |

**Bottom line: the actual exam-task content (`WritingScenarios`/`WritingMocks` — what your PDFs will become) is essentially empty in production.** There is nothing to collide with; the one existing scenario row is a throwaway placeholder. Lessons/drills/canon/mistakes already have starter content that is a separate concern from the writing-folder upload.

---

## 11. Your real source material — actual PDF-level analysis (not just filenames)

I opened a representative sample of the real files at `C:\Users\Dr Faisal Maqsood PC\Desktop\Writing\` (the folder you pointed me to — distinct from, and larger than, the sample set already in the repo). The folder covers 7 professions (Medicine, Nursing, Pharmacy, Dentistry, Physiotherapy, Radiography, and a Dentistry samples file) with roughly 150–200+ individual PDF files, some of which are themselves multi-task compilations — so the real task count is likely 200-300+ once compilations are split. **This is meaningfully messier than the two-file-per-task (case-notes.pdf + answer-sheet.pdf) shape `docs/CONTENT-UPLOAD-PLAN.md` assumed** — I found three distinct real shapes:

- **Shape A — self-contained single task, WITH model answer** (e.g. `Nursing/Nursing Official Samples/*.pdf`, `Pharmacy/Official Samples/*.pdf`): 5 pages — cover (`NURSAMPLE05`/`PHASAMPLE01`-style code) → case notes (pp.2-3, ends with the "Writing Task:" instructions) → blank answer-booklet page → "SAMPLE RESPONSE" model answer (p.5). **The model answer is embedded in the same PDF as the stimulus** — this file cannot be uploaded as-is as a stimulus PDF, or a student would see the answer before writing. It must be split into two PDFs (pp.1-4 → stimulus, p.5 → answer sheet) before upload.
- **Shape B — single task, case-notes only, no answer** (e.g. `Medicine/EXTRA OET CASE NOTES 1/*.pdf`, `Nursing/Nursing Case Notes/Nursing_Test_*.pdf`): 2 pages, case notes + "WRITING TASK" instructions, no model answer anywhere. These can be uploaded as the stimulus PDF directly; `answerSheetPdfMediaAssetId` is simply left unset (it's optional, §2).
- **Shape C — multi-task compilation, official CBLA exam-booklet format** (e.g. `Dentistry/OET Writing Dentistry Samples 1 to 5.pdf` — confirmed 5 separate tasks, 3 pages each, cover-coded `DENSAMPLE01`…`05`; likely also `Radiography/Radiography Writing Case Notes...pdf`, `Medicine/VIP Writing Tasks.pdf`, `Medicine/Writing Recalls 2019/2020.pdf`, not yet individually opened but same filename pattern suggests the same shape). Each task inside must be split out into its own PDF by page range.

**Consistent, reliable facts across every sample opened** (useful defaults for the manifest): reading time 5 minutes, writing time 40 minutes, target word count "approximately 180–200 words" in the body, letter format required, always "Expand the relevant notes into complete sentences / Do not use note form / Use letter format" as the fixed instructions. `letterType` is never given as a machine-readable code — it must be inferred per task from the "Writing Task:" paragraph (e.g. "write a letter of referral to the Emergency Registrar" → urgent/routine referral depending on urgency language; "write a letter of discharge to..." → discharge; "write a letter of referral to the endodontist/orthodontist" → referral to specialist). Patient name (for a task title) is always in the case notes' "Patient/Name:" field.

---

## 12. Recommended two-stage import pipeline (supersedes a naive filename-convention parser)

**Stage 1 — PDF processing + manifest generation (semi-automated, needs your review before Stage 2 runs):**
1. Walk the folder tree; for each PDF, detect its shape (A/B/C above) — Shape C (multi-task) is detectable by repeated `WRITING SUB-TEST` cover-page headers within one file; Shape A (has an answer) is detectable by a `SAMPLE RESPONSE` page.
2. For each individual task found: extract patient name, letter-type classification (from the task-instruction paragraph, against a keyword→`LT-*` mapping table I'll build from the samples), profession (from the folder), and split the source PDF into a clean **stimulus-only PDF** (case notes + task instructions, answer/model-response pages removed) and, where one exists, a separate **answer-sheet PDF** (the model response only).
3. Write one manifest row per task (title, profession, `LT-*` letterType, `taskPromptMarkdown` transcribed from the instructions, `fixedInstructions`, `wordGuideMin/Max`, and the two split PDF file paths) into a review file (CSV/JSON) — **you or I spot-check this before anything touches the admin API**, since letter-type classification and any OCR/extraction on scanned pages needs a human sanity check (per the Listening-module precedent in §6, pre-publish gates alone haven't historically been enough to keep bad content out).
4. Flag anything that doesn't cleanly match Shape A/B/C (a handful of oddly-named files like `VIP Writing Tasks.pdf` may need individual inspection) rather than silently guessing.

**Stage 2 — mechanical upload (fully scriptable once the manifest is approved):** loop the manifest, running exactly the steps in §8 (upload PDFs → chunked-upload → import → PUT full field set → validate → publish), mirroring the Reading module's `import-reading-manifests-local.mjs` precedent (§6) including its `--dry-run` and pre-publish validation-gate pattern.

I have not yet built either stage — this section is the plan, ready to implement once you confirm the approach (or want changes to it) and are ready to hand off the full folder for real ingestion.

---

## 13. Stage 1 executed — full corpus extracted, classified, and split (2026-09-01)

Ran the two-stage pipeline from §12 against the complete real folder at `C:\Users\Dr Faisal Maqsood PC\Desktop\Writing\` (156 source PDFs, 7 professions). Key discovery that changed the plan mid-run: **many source PDFs have no embedded text layer at all** (PyMuPDF text extraction returns empty) despite being crisp, readable documents — they were flattened during a prior "protect PDF" or scan step. A first mechanical pass correctly handled the ~130 files that do carry real text, but silently would have fabricated wrong data for the other ~25 (some genuine multi-task compilations up to 43 pages) had it not been caught — the pipeline was built to flag these as `IMAGE_SCAN_NO_TEXT_LAYER` and hand off rather than guess. Confirmed my Read tool's vision rendering reads these files correctly regardless of the missing text layer (verified directly on the largest one, a 43-page file that turned out to hold 21 separate tasks), and used that path — one focused read pass per file, several run in parallel — to extract the remainder faithfully.

**Final corpus: 218 real writing tasks recovered from the 156 source files** (many files bundle 2–21 tasks each). All results, including full reasoning per task, are on disk at the session scratchpad's `writing-import/manifest_FINAL.json` (and a spreadsheet-friendly `manifest_FINAL_review.csv`), with 216 of the 218 already split into individual ready-to-upload stimulus PDFs (`writing-import/stimulus/`) and every task carrying a model answer split into `writing-import/answer/` — most source tasks have no model answer at all (consistent with §11), only a handful of "Official Samples"/"Case Notes 2/3" files did.

| Profession | Tasks extracted |
|---|---|
| Nursing | 101 |
| Medicine | 55 |
| Pharmacy | 40 |
| Physiotherapy | 12 |
| Dentistry | 5 |
| Radiography | 5 |

**Letter-type classification** (into the system's real `LT-RR/UR/DG/TR/NM/RP` short codes, per §4.1 — the long "canonical" strings were never used): 151/218 (69%) confidently classified — `LT-RR` 94, `LT-DG` 24, `LT-UR` 19, `LT-NM` 8, `LT-TR` 5, `LT-RP` 1. The remaining 67 are `null` with an explicit reason, split into two real categories, not one:
- **15 are a genuine taxonomy gap, not a classification failure.** Pharmacy in particular repeatedly addresses letters to the patient themselves, a family member, a regulatory body (Pharmacy Board, Adverse Drug Reactions Data Bank), or the general public ("Dear Parent" school letters) — none of the system's 6 letter-type codes represent "advice to a non-clinician." This is a real product decision to make (§14) before these 15 tasks can be entered with a meaningful `letterType` value at all.
- **52 are genuinely ambiguous and flagged for a human/product-owner call**, mostly the recurring Nursing question of whether a hospital-to-facility handoff is a discharge (`LT-DG`) or a transfer of care (`LT-TR`) — both readings are defensible and the source text itself doesn't disambiguate.

**79 of the 218 tasks are fully clean** (patient name found, letter type confidently classified, explicit task instruction transcribed, no flags at all) — a natural pilot batch.

Two known small gaps, not yet fixed: 2 tasks (`Pharmacy Official Samples 3 & 4`) have a page-range edge case in the automated splitter and are missing their stimulus PDF split; trivial to fix by hand before upload.

**This entire stage was local file reading, classification, and PDF splitting — zero writes, zero reads against the production API or database.** Nothing here has touched production. §8's ingestion procedure (upload → import → PUT full field set → validate → publish) still applies per task once a human/product-owner review pass resolves the 67 flagged classifications and the pharmacy taxonomy-gap decision.

---

## 14. New open decision from Stage 1: what to do with non-clinician-addressed content

15 confirmed real tasks (mostly Pharmacy) are well-formed, authentic OET writing tasks that simply don't have a doctor/clinician as the letter's recipient. Three options, not mutually exclusive:
1. Exclude them from the initial upload batch and revisit later.
2. Extend the product's letter-type vocabulary (a real schema/UI change, not just a content decision) to represent "advice letter to patient/family/public/regulatory body" as its own category.
3. Force-fit them to the nearest existing code (e.g. `LT-RP`) with a note, accepting the label is approximate.
Recommend deferring this to you rather than guessing — it changes what students will see in the letter-type filter on `/writing/practice/library`.

---

## 15. Stage 2 executed — first production batch live (2026-09-01)

Uploaded the 79 flag-free tasks from Stage 1 through the live Tasks-v2 admin API (`https://api.oetwithdrhesham.co.uk`), using the **direct create endpoint** (`POST /v1/admin/writing/tasks`) rather than `/import`, specifically to avoid the letter-type-mangling bug documented in §4.1/§2 — confirmed on the pilot task that `LetterType` landed as the exact 5-char code (`LT-RR`), not a mangled/canonical string. Full per-task pipeline: chunked-upload the stimulus PDF (and answer PDF where one exists) → create with the full field set (title, profession, letterType, difficulty, prompt, fixed instructions, word guide, simulation/marking mode, source provenance, `integrityAcknowledged: true`, both PDF asset ids) → `GET .../validate` → `POST .../publish`. Internal codes assigned sequentially `WR-0001`…`WR-0079`.

**Result: 79/79 succeeded on the first pass, zero API failures.** Every row verified directly against the database afterward (not just trusted from API responses): correct `LetterType` values/lengths, real admin `AuthorId` (not a seed sentinel), `Status='published'`, `IntegrityAcknowledgedAt` set, stimulus PDF FK present and resolving to a real `MediaAssets` row for all 72 (see below).

**Caught before it mattered: source-material duplication.** A rigorous post-upload audit (mirroring the Listening-module precedent from §6/§12) — cross-checked by normalized prompt text *and* normalized patient name, since OCR/transcription noise on a couple of source scans hid a few near-duplicates from a naive exact-text match — found **7 of the 79 were genuine duplicates**: the same underlying task existed as copies across overlapping source folders (e.g. "Ms Alexia Rollinson" existed identically in `Pharmacy/Extra Case Notes/`, `Pharmacy/Official Samples/`, and inside the big compiled `Pharmacy Case notes 1.pdf`). All 5 duplicate groups were individually verified by direct text comparison before acting. Archived the 7 extra copies (kept the earliest-created copy of each group), re-verified zero duplicate titles remain among published rows.

**Final live state:**

| | Count |
|---|---|
| Published (unique, live to students) | **72** |
| Archived as confirmed duplicates | 7 |
| Failed | 0 |

Letter-type/profession spread across the 72: Medicine 29 (LT-RR 16, LT-UR 6, LT-NM 3, LT-DG 2, LT-TR 1, LT-RP 1), Nursing 22 (LT-RR 11, LT-DG 9, LT-NM 1, LT-TR 1), Pharmacy 14 (LT-RR 9, LT-DG 2, LT-NM 2, LT-UR 1), Physiotherapy 4 (LT-RR), Radiography 3 (LT-UR 2, LT-RR 1). **Dentistry has zero tasks in this batch** — its only real source (a 5-task official compilation) didn't make the flag-free cut in Stage 1 and is still pending in the remaining 139.

**Remaining work (not yet touched):** 139 of the 218 extracted tasks — the 52 flagged for classification-ambiguity review, the 15 non-clinician-recipient tasks pending the §14 product decision, and the rest that simply weren't in the first "clean" batch (including all of Dentistry). Same pipeline, same verification discipline, once you're ready.

---

## 16. Stage 3 executed — remaining batch processed and live (2026-09-01)

Processed all 139 tasks left after Stage 2. Re-verified the exact letter-type taxonomy against the codebase first (`components/domain/writing/admin/builder-state.ts`) rather than relying on inference: `LT-RR` Routine referral, `LT-UR` Urgent referral, `LT-DG` Discharge, `LT-TR` Transfer, `LT-NM` Non-medical referral, `LT-RP` Reply/response. Built an explicit disambiguation order (doctor recipient → RR/UR by explicit urgency wording only; non-physician recipient + institutional destination → TR; non-physician recipient + community/home destination → NM; writer's own episode explicitly ending → DG) and validated it against the nursing discharge/transfer/non-medical cases that were ambiguous in Stage 1.

**Split the 139 into two tracks:**
- **94 tasks** already had extracted prompt text (from Stage 1's mechanical pass or the vision agents) but no confident `letterType` — dispatched 9 parallel classification agents (batched by profession, ~12 tasks each), each applying the taxonomy above directly to the existing text, deriving missing patient names, and fixing generic/duplicated titles.
- **29 tasks** had no usable text at all (image-only PDFs the mechanical pass couldn't read, plus 2 "Pharmacy Official Samples" files where the original page-range splitter had picked the wrong pages entirely) — dispatched 4 parallel vision-extraction agents to re-read the source PDFs directly and produce full records from scratch, including determining true page ranges for the 2 broken pharmacy files.

**Full-corpus duplicate re-audit.** Before uploading, ran the same two-signal duplicate check from Stage 2 (normalized prompt-text prefix + normalized patient name) but this time across **all 218 tasks**, not just the new batch — deliberately re-checking the already-published 72 as well, since Stage 1's original folder-overlap problem (the same case reused across `Nursing Case Notes/`, `Extra Case Notes/`, and `Nursing Official Samples/`) was known to span both batches. This caught:
- **11 duplicates within the new batch** (kept the cleaner/more complete copy of each pair, e.g. preferring a copy with a recovered full patient name or a captured `todayDate` over one without).
- **4 more non-standard-recipient cases** (letters addressed to a patient's daughter, a patient's husband, and — via a filename/content mismatch that turned out to be a real "you are Nurse X, the patient is Y" role-play convention, not an extraction error — a public health charity), on top of the 15 already known from Stage 1.
- **Two errors already live from Stage 2**, only visible once the wider corpus was cross-checked: `WR-0042` ("Pharmacy – Mrs Alice Ramsey") had been classified `LT-DG`, but an exact-duplicate copy of the same letter surfaced in the new batch and proved it's addressed to the patient's daughter, not a clinician — a genuine misclassification, not a judgment call. `WR-0031` ("Nursing – Ms Sheila Cartwright", `Nursing_Test_23.pdf`) was a true duplicate of the already-published `WR-0027` (same title, `Nursing_Test_07.pdf`) — identical letter, word for word. Both archived, `WR-0027` kept as the canonical copy.
- **One duplicate that slipped through the exclusion list into the live batch anyway**: `WR-0215` ("Nursing – Mr Gerald Baker", `Nursing_Test_04.pdf`) was identified as a duplicate of `WR-0081` (same title, `Nursing_Test_25.pdf`) during analysis but wasn't actually added to the exclusion set before the upload ran. Caught by a second post-upload audit pass (this time restricted to the live catalog) and archived immediately, keeping `WR-0081`.

**Uploaded 108 vetted tasks** via a second script (`upload_stage3.mjs`, same direct-create → validate → publish pipeline as Stage 2). The first batch run published 77/107 before the auth token expired mid-run (30 tasks failed with `401`, all safely tracked, none corrupted); re-running batch mode picked the 30 back up automatically and all succeeded on retry.

**Final live state, verified directly against the database:**

| | Count |
|---|---|
| Published (unique, live to students) | **178** |
| Archived (Stage 2 dedup + Stage 3 dedup + 2 corrected misclassifications) | 10 |
| Failed | 0 |
| Total `WritingScenarios` rows (incl. 1 pre-existing placeholder) | 188 |

Letter-type spread of the 178 live: Medicine 55 (RR 33, UR 10, DG 6, NM 4, TR 1, RP 1), Nursing 85 (NM 44, RR 18, TR 13, DG 9, UR 1), Pharmacy 16 (RR 11, DG 2, NM 2, UR 1), Physiotherapy 12 (RR 6, DG 1, NM 1, RP 2, TR 1, UR 1), Radiography 5 (RR 3, UR 2), **Dentistry 5 (RR 4, NM 1)** — Dentistry's gap from Stage 2 is now closed. Verified: every `LetterType` is exactly 5 characters (a valid `LT-*` code, no mangled values), zero orphaned `StimulusPdfMediaAssetId` references, zero rows missing `AuthorId`/`IntegrityAcknowledgedAt`/stimulus PDF, and the only remaining same-title groups among published rows (`Elizabeth Carmel` ×4, `Derek Shepherd` ×4, `Kylie Weiss` ×3, `Anita Ramamurthy` ×2) were individually confirmed as genuinely distinct tasks — same underlying case notes reused with different recipients/purposes (a real, common pattern in this source material), not duplicate content.

**Remaining work:** 31 of the 218 extracted tasks are intentionally not uploaded — 12 are duplicates that were correctly excluded before consuming a slot, and **19 are non-clinician-recipient tasks** (patient, family member, regulatory body, or general-public addressee) still pending the §14 product decision, since none of the 6 `LT-*` codes represent that category and force-fitting one would be a data-integrity bypass, not a fix.

---

## 17. Stage 4 — "Other Letters" category shipped, §14 gap closed (2026-09-01)

Owner instruction: upload everything, organize by profession, and for any task whose recipient doesn't cleanly fit `LT-RR/UR/DG/TR/NM/RP`, place it under a new **"Other Letters"** category per profession rather than blocking on classification. This resolves §14 directly — instead of excluding non-clinician-recipient content indefinitely pending a decision, it now has a real, first-class home.

**Code change shipped** (`7d647e56b`, deployed via `Build & Deploy (web + API)`): added `LT-OT` as a 7th `WritingLetterType` value.
- Backend needed **no change** — confirmed `WritingTaskAuthoringService.Validate` only checks `LetterType` is non-empty, no server-side enum, so any short code was already accepted (consistent with the `varchar(8)` column finding in §4.1).
- Frontend: `lib/writing/types.ts` (type union), `lib/writing/zod.ts` (runtime validator), `components/domain/writing/admin/builder-state.ts` (admin dropdown + label "Other Letters"), `app/admin/writing/analytics/page.tsx` (analytics breakdown label), `app/writing/practice/library/page.tsx` + `messages/en+ar/writing.json` (student-facing filter option, EN/AR). Typecheck run before shipping: 82 pre-existing errors found, all in unrelated uncommitted work already sitting in the tree (the `tests/e2e-tiers/`, `tests/unit/m1-*` files from the git-hazard CLAUDE.md warns about) — zero touched any of the 7 files changed here.

**Uploaded the 19 previously-excluded non-standard-recipient tasks as `LT-OT`**, plus 1 more (`0144`, "Pharmacy – Mrs Alice Ramsey", the daughter-addressed letter behind the `WR-0042` misclassification found in Stage 3) — 20 candidates. **Re-running the duplicate audit specifically on this set** (it had never been through Stage 3's dedup loop, since non-standard tasks were held out of that loop entirely) caught 4 more genuine duplicates that would otherwise have slipped through:
- `9083` (Henry Styles) — exact duplicate of `9017`, same letter, two source folders.
- `9011` (Pharmacy Board incident letter) — duplicate of `9043` (same recipient "Anne Seaborn, Director, Pharmacy Board, Newtown", same incident), `9043` kept for its more complete captured data.
- `9005` ("Mr Davidson") — duplicate of `0147` ("Mr James Davidson", same address net of an OCR digit-drop, same "addressing his specific concerns" framing).
- `9009` (head-lice letter to Riverside Primary School) — duplicate of `9044`, same school, same scenario, `9044` kept (has the date and full word-count instructions the other lacks).

**16 unique tasks published as `LT-OT`** (13 Pharmacy, 3 Nursing) — zero force-fit classifications, zero left out. One operational bug caught and fixed in the process: the upload script's sequence counter (`Object.keys(results).length + 1`) had drifted from the true highest `InternalCode` in the database (an artifact of the local tracking file not exactly mirroring live archive/correction actions taken directly via the API), producing one real collision — two live rows both labeled `WR-0188`. Caught immediately after the pilot task, the newer row renamed to the correct next-free code (`WR-0218`) via the admin update endpoint, and the script fixed to compute its next code from the actual max `InternalCode` in use rather than local key count.

**Final live state, verified directly against the database:**

| | Count |
|---|---|
| Published (unique, live to students) | **194** |
| Archived | 10 |
| Failed | 0 |
| `LT-OT` ("Other Letters") live | 16 (Pharmacy 13, Nursing 3) |

Verified: zero duplicate `InternalCode` values across all 194+10 rows, all 6 professions present, `LT-OT` selectable in both the admin Task Builder and the student practice-library filter.

**What's intentionally still not uploaded — 15 of the 218 extracted tasks, all pure duplicates of already-live content** (16 total duplicate copies found across Stages 3–4, minus the 1 that became a legitimate `LT-OT` upload once its erroneous live twin was archived — see above). Uploading the literal second/third copy of an identical letter wasn't treated as "content left out," since the underlying case is already live and correctly classified under its first copy; flagging this explicitly rather than deciding it silently — say the word and I'll upload the exact duplicate copies too.

---

## Appendix A — Grading/moderation/visibility surfaces (supplementary, not blocking for content entry)

These don't affect how you enter Writing content, but matter once real tasks start producing real student submissions:

- `WritingResultVisibilityDto` (admin-edited, `/admin/writing/result-visibility`) is the single gate controlling what a learner's results screen reveals (AI estimate, tutor score, full criteria, model answer, etc.) — worth setting deliberately once real tasks are live, rather than leaving at whatever default currently applies.
- **`WritingCommonMistake` delete is a hard delete**, not archival — same "no soft-delete" pattern as elsewhere in this module.
- Same DTO-wider-than-DB / no-length-cap pattern recurs here too: `WritingMistakeUpsertRequest.ExampleWrong/ExampleRight` have `[Required]` but no length cap, while the DB column is `MaxLength(1000)` — an over-length example crashes the save instead of failing cleanly (consistent with §4.7).
- A likely governance bug (not content-entry related): the v1.1 candidate-numeric-score release gate's `approve` endpoint checks a condition it is itself supposed to satisfy, so a freshly created release gate can seemingly never be approved through the exposed API — flag to the team if/when v1.1 numeric scores need to go live for students.
- The mock-writing player (`app/mocks/writing/[sectionAttemptId]/page.tsx`) appears to only send a 400-character preview of the learner's letter to the completion endpoint, with autosave staying local-only ("autosave is local until the backend autosave endpoint is enabled" per its own UI copy) — worth the team confirming where (if anywhere) the full mock letter text is actually persisted before mock papers go live with real students.

---

*Full per-area forensic reports (with exact `file:line` citations for every claim above) are preserved for this session under the scratchpad workflow output and can be re-consulted if any claim needs deeper verification before it's acted on.*
