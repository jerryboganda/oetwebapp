# OET Rulebook Implementation Plan (A–Z)

> **Source of truth:** Dr. Ahmed Hesham's two rulebooks stored in
> `Project Real Content/`:
> - `Speaking_/Speaking Rulebook ( Medicine Only )/OET_Speaking_Rulebook_v2 (1).pdf` — **55 rules, 7 sections**
> - `Writing_/Writing RuleBook ( Medicine only )/OET_Writing_Rulebook_FINAL.pdf` — **100+ rules, 16 sections, many flagged CRITICAL**
>
> Reference: `Project Real Content/Speaking_/Speaking Assessment Criteria ( … same for all professions ).pdf`
> (linguistic bands 0–6 and clinical communication indicators A–E).
>
> **Scope note:** Both rulebooks are currently **Medicine-only**. The data model
> must be profession-aware from day one so that Nursing, Dentistry, Pharmacy,
> etc., can slot in later without schema churn.

---

## 0. Executive Summary

These rulebooks are **production-grade exam coaching logic**, not generic tips.
They define:

- **What** to include/exclude in Writing by letter type and recipient.
- **How** to structure a Speaking role-play, turn by turn.
- **Which** phrases, tenses, abbreviations, layouts, and safety phrases are
  mandatory — many marked `CRITICAL`, meaning auto-mark-deduction on violation.

The platform must embed every rule as a **machine-checkable constraint**
(lint-style for Writing, rubric-weighted for Speaking) so that:

1. **Candidates** see guidance grounded in these exact rules while they prepare.
2. **AI assistive scoring** applies the same rules the human experts apply.
3. **Expert reviewers** score against the same taxonomy, producing consistent
   feedback across the marketplace.
4. **Analytics** can tell a candidate "you miss R03.4 (smoking/drinking)
   7/10 times" — rule-level granularity drives targeted study plans.

---

## 1. Rule Extraction — Current State

| Rulebook | Sections | Rules | CRITICAL flags | Status |
|---|---|---|---|---|
| Writing  | 16 | ~100 (R01.1 – R16.8) | ~35 | Extracted to `_extracted/OET_Writing_Rulebook_FINAL.txt` |
| Speaking | 7  | 55 (RULE 01 – 55)    | ~10 implicit | Extracted to `_extracted/OET_Speaking_Rulebook_v2_1.txt` |

Both rulebooks are professional-grade and self-consistent. Core framing
points that drive the plan:

- **Writing:** scoring criteria are `Purpose (0–3)`, `Content (0–7)`,
  `Conciseness & Clarity (0–7)`, `Genre & Style (0–7)`,
  `Organisation & Layout (0–7)`, `Language (0–7)`. Content and
  Conciseness carry the most pass/fail weight (R16.3).
- **Speaking:** two 8-minute role-play cards (3 min prep + 5 min discussion)
  assessed holistically against the **Linguistic Criteria (bands 0–6)** and
  the **Clinical Communication Criteria (A–E indicators, each 0–3)** from
  the public OET rubric.
- Both are profession-scoped to Medicine today; the *same rubric*, different
  card content, will apply to other professions.

---

## 2. Target Architecture

A three-layer model, applied identically on TypeScript and .NET:

```
┌────────────────────────────────────────────────────────────────────┐
│  LAYER 1 — Canonical Rulebook (content-as-code, versioned)         │
│    rulebooks/                                                       │
│      writing/medicine/rulebook.v1.json                              │
│      speaking/medicine/rulebook.v1.json                             │
│      speaking/common/assessment-criteria.json                       │
│      speaking/common/warmup-questions.json                          │
│    Each rule: id, section, title, body, critical, examples,        │
│                applies_to, profession, letter_type, card_type       │
└────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────┐
│  LAYER 2 — Rule Engine (pure, unit-testable)                       │
│    lib/rulebook/                                                    │
│      writing-rules.ts (TS)  + backend/...Services/WritingRules.cs   │
│      speaking-rules.ts (TS) + backend/...Services/SpeakingRules.cs  │
│    - loads the JSON rulebook                                        │
│    - evaluates candidate output against rules                       │
│    - returns structured findings: {ruleId, severity, span, message} │
└────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────┐
│  LAYER 3 — Surfaces                                                 │
│    - Candidate UI  (writing editor, speaking player)                │
│    - AI assistive scoring (backend)                                 │
│    - Expert review console (load rubric items with rule pointers)   │
│    - Analytics & recommendations                                    │
│    - Study-plan generator                                           │
│    - Rulebook Library page (docs surface)                           │
└────────────────────────────────────────────────────────────────────┘
```

Why this split:

- **Content-as-code (Layer 1)** lets non-engineers (Dr. Hesham, future
  content authors) edit the source of truth without touching code.
- **Pure rule engine (Layer 2)** mirrors `lib/scoring.ts` — single source
  of truth, 100% unit test coverage, identical behaviour on both runtimes.
- **Surfaces (Layer 3)** call the engine; none of them reimplement rules.

---

## 3. Canonical Rulebook JSON — Shape

```ts
// lib/rulebook/types.ts
export type RuleSeverity = 'critical' | 'major' | 'minor' | 'info';
export type ExamProfession = 'medicine' | 'nursing' | 'dentistry' | 'pharmacy' | ...;
export type LetterType =
  | 'routine_referral' | 'urgent_referral' | 'discharge'
  | 'transfer' | 'non_medical_referral' | 'specialist_to_gp';
export type SpeakingCardType =
  | 'first_visit_routine' | 'first_visit_emergency'
  | 'follow_up' | 'examination' | 'already_known_patient' | 'breaking_bad_news';

export interface WritingRule {
  id: string;                  // "R03.4"
  section: string;             // "Content & Data Selection"
  title: string;               // "Smoking & drinking inclusion"
  body: string;                // full rule text
  severity: RuleSeverity;      // critical rules flagged by rulebook
  profession: ExamProfession[];
  appliesTo: LetterType[] | 'all';
  examples?: { good?: string[]; bad?: string[] };
  // machine-executable check
  check?: RuleCheckId;         // named check in the rule engine
  params?: Record<string, unknown>;
}

export interface SpeakingRule {
  id: string;                  // "RULE_27"
  section: string;
  title: string;
  body: string;
  severity: RuleSeverity;
  profession: ExamProfession[];
  appliesTo: SpeakingCardType[] | 'all';
  turnStage?: SpeakingTurnStage; // greeting, empathy, diagnosis, recap, ...
  exemplarPhrases?: string[];
  forbiddenPatterns?: string[];
}
```

All existing rulebooks (Medicine / Writing / Speaking) become instances of
these interfaces. New professions = new JSON files, same shape.

---

## 4. Writing Implementation

### 4.1 Layer 1 — Writing rulebook JSON

Files:

```
rulebooks/writing/common/assessment-criteria.json
rulebooks/writing/medicine/rulebook.v1.json
rulebooks/writing/medicine/templates/
  routine-referral.md
  urgent-referral.md
  discharge-letter.md
  transfer-letter.md
  non-medical-referral.md
  specialist-to-gp.md
```

Every R-code from the rulebook (R01.1 – R16.8) becomes one JSON entry.
Critical examples that must be machine-checkable (not just textual guidance):

| Rule | Check | Severity |
|---|---|---|
| R02.2 | enforce `read_only_phase_minutes=5` in writing timer | critical |
| R03.4 | content linter: smoking/drinking mention unless recipient is OT | critical |
| R03.6 | content linter: allergy mention for atopic conditions | critical |
| R04.2 | layout linter: no blank line between Salutation and Re: | critical |
| R05.8 | layout linter: no "Date:" prefix before the date | critical |
| R06.1 | salutation linter: `Dear Dr. <Last>` + last name only | critical |
| R06.10 | minors: no title in Re: line, first name in body | critical |
| R06.11 | closing linter: `Yours sincerely` iff named recipient | critical |
| R07.3 | intro must contain a purpose/request clause | critical |
| R07.6 | urgent: intro must include word "urgent" | critical |
| R07.7 | discharge: no patient identity in intro | critical |
| R08.1 | body: ≥ 2 paragraphs | critical |
| R08.3 | no per-visit paragraph for previous visits | critical |
| R08.5 | urgent: body starts with today's visit | critical |
| R08.7 | body: never use "next visit" (use "the following visit") | critical |
| R08.8 | body: never writes today's date literally | critical |
| R08.14 | body: last name + title only, never "the patient" | critical |
| R09.2 | urgent closure contains "at your earliest convenience" | critical |
| R09.4 | follow-up date in case notes → must appear in closure | critical |
| R09.5 | "enclosed results" statement instead of value dump | critical |
| R10.2 | all visits tense = past simple | critical |
| R10.5-7| "since"/"for"/duration tense agreement | critical |
| R10.8 | surgery past simple, never present perfect | critical |
| R10.10| "X ago" forces past simple | critical |
| R11.1 | Latin abbreviations translated (od, bd, tds, prn…) | critical |
| R12.1 | no contractions | critical |
| R12.2 | word "patient" absent from body | critical |
| R12.5 | medical conditions lowercase | critical |
| R12.9-10| `however`/`therefore` punctuation pattern | critical |
| R13.10| "ASAP" rewritten as "at your earliest convenience" | critical |
| R14.2 | discharge intro template | critical |
| R14.4 | discharge omits FH/SH/smoking/PMH | critical |
| R14.6 | "admitted with" not "was presented with" | critical |
| R14.9 | discharge lists all investigations incl. normal | critical |
| R14.12| "treatment for" not "treatment from" | critical |
| R15.2 | non-medical referral: translate all jargon | critical |
| R15.7 | imperative plan items are for GP, not specialist | critical |

Non-critical rules (formatting hints, tense refinements, vocab choices) are
still persisted but surface as **`info` or `minor`** findings.

### 4.2 Layer 2 — Rule engine (`lib/rulebook/writing-rules.ts`)

```ts
export interface WritingLintFinding {
  ruleId: string;
  severity: RuleSeverity;
  message: string;
  quote?: string;
  start?: number;   // char offset in the candidate's letter
  end?: number;
  fixSuggestion?: string;
}

export function lintWritingLetter(input: {
  letterText: string;
  letterType: LetterType;
  recipientSpecialty?: string;
  recipientName?: string | null;
  patientAge: number;
  diagnosisKeywords: string[];
  caseNotes: CaseNotesModel;
  profession: ExamProfession;
}): WritingLintFinding[]
```

Checks are **deterministic and pure**:

- Regex/AST detectors for layout rules (date header, blank lines,
  salutation/Re: adjacency, trailing `Yours sincerely`).
- Tokenised content detectors for lexicon rules (contractions, Latin
  abbreviations, "patient" / "smoker" / "next visit" forbidden terms).
- Case-notes cross-references: if case notes mention smoking → letter must
  include it (unless recipient is OT); if case notes end with "review in
  6 weeks" → closure must mention a review date.
- Structural detectors: paragraph count, intro-contains-purpose, urgent
  opener, discharge opener variants.

Every detector has a unit test with worked good/bad examples pulled from
the rulebook's own example lines.

.NET mirror at
`backend/src/OetLearner.Api/Services/WritingRules.cs`
to keep backend AI-assistive scoring identical.

### 4.3 Layer 3 — Surfaces

**Candidate writing editor** (`components/domain/writing-editor.tsx`
and `app/writing/player/page.tsx`):

- Live-lint panel beside the editor (like a code-editor problems panel).
- Each finding renders as a clickable card with the rule ID, severity
  chip (critical = red, major = amber, minor = blue, info = slate),
  quote highlight in the text, and a one-line fix suggestion.
- Toggle: "Show critical only", "Hide minor", so learners can focus.
- On submit, the submission summary shows **rule coverage**: e.g.
  "28/32 critical rules respected, 2 major issues remaining."

**Writing templates** (`app/writing/library/`):

- Six tabs, one per letter type, each showing the rulebook template with
  placeholders, the must-include / must-exclude checklist per that
  letter, and 1 worked example from `Project Real Content/Writing_/*`.

**AI pre-fill / assistive scoring**
(`WritingCoachService.cs`, `ContentGenerationService.cs`):

- Before submission: engine annotations fed to the candidate.
- On expert request: engine annotations attached to the expert's review
  queue item so the reviewer sees AI-flagged rule violations alongside
  their own notes.
- Never *replaces* an expert grade — labelled "AI advisory" and visually
  distinct (already a project convention per
  `lib/mock-expert-data.ts`).

**Expert review console** (`app/expert/review/writing/[reviewRequestId]`):

- Rubric panel auto-populates 6 criteria (Purpose 0–3, Content 0–7,
  etc.).
- Quick-insert chips for the most common rule findings (one click =
  append `R03.4 — missing smoking/drinking mention` to the feedback).
- Expert scores feed back into the rule catalog's analytics — "which
  rules do experts most often cite" is a core dataset.

**Analytics** (`ExpertService.cs`, `AnalyticsIngestionService.cs`):

- New event: `writing_rule_violation` with `ruleId`, `severity`,
  `letterType`, `submissionId`.
- Per-learner: a rule heatmap of their most-violated rules.
- Per-cohort: content-effectiveness analytics showing which rules
  correlate with pass/fail bands.

**Study plan generator** (`LearnerService.cs`):

- Weakest-3-rules drill: automatically schedules a targeted practice
  task that exposes the candidate to letters where their weakest rules
  apply.

**Scoring**:

- Assistive raw score builder uses rule severity × count to produce a
  **signal**, never a final grade. Final grading still flows through
  `OetScoring.GradeWriting(scaled, country)` from the canonical
  scoring module, with country-aware thresholds (350 for UK/IE/AU/NZ/CA,
  300 for US/QA).

---

## 5. Speaking Implementation

### 5.1 Layer 1 — Speaking rulebook JSON

Files:

```
rulebooks/speaking/common/assessment-criteria.json   # linguistic 0-6 bands + clinical A-E indicators
rulebooks/speaking/common/warmup-questions.json      # standard warm-up bank (not scored)
rulebooks/speaking/common/layman-glossary.json       # jargon -> plain English (RULE 06-11)
rulebooks/speaking/medicine/rulebook.v1.json         # 55 rules, medicine-profession-scoped
rulebooks/speaking/medicine/card-templates/
  first-visit-routine.md
  first-visit-emergency.md
  follow-up.md
  examination.md
  already-known-patient.md
  breaking-bad-news.md
```

Card taxonomy matches the 6 folders in
`Project Real Content/Speaking_/Card 1 … Card 6`.

### 5.2 Layer 2 — Speaking rule engine

Speaking cannot be linted purely from text like Writing (output is audio
+ transcript). The engine therefore exposes two entry points:

```ts
// Pre-exam planning / guidance (rule text + card type -> phrasing helpers)
export function getSpeakingGuidance(
  cardType: SpeakingCardType,
  profession: ExamProfession,
): SpeakingGuidance;

// Post-exam transcript audit (expert- or AI-assisted)
export function auditSpeakingTranscript(input: {
  transcript: SpeakingTurn[];
  cardType: SpeakingCardType;
  profession: ExamProfession;
}): SpeakingAuditFinding[];
```

`auditSpeakingTranscript` runs detectors inspired by the rulebook:

- RULE 06/07/10/11: scan candidate turns for medical jargon tokens
  (`CT scan`, `MRI`, `endoscopy`, `-scopy`, `abdomen`, `paediatrician`,
  etc.) — emit `jargon_used` findings.
- RULE 22: ping-pong metric. If any candidate turn exceeds a configurable
  duration threshold without a patient response → `monologue_detected`.
- RULE 23: flag the string "What is your weight" (and paraphrases) in
  first few turns.
- RULE 27: graduated-negotiation detector. If the word "reduce" appears
  before "quit"/"cessation" in smoking-related turns → flag.
- RULE 32: detect "you have hypertension" after a single BP reading →
  `overdiagnosis_risk`.
- RULE 44 (Breaking Bad News): silence after diagnosis. Using the audio
  timeline, measure gap after the diagnosis turn; <3s → `silence_too_short`.
- RULE 15 / RULE 20 / RULE 21: turn-stage classifier (greeting →
  diagnosis → recap → closure). Missing stages produce
  `stage_missing: recap` findings.

The classifier is rule-driven and transparent; no black-box ML.

.NET mirror at
`backend/src/OetLearner.Api/Services/SpeakingRules.cs`.

### 5.3 Layer 3 — Surfaces

**Speaking selection screen** (`app/speaking/selection/`):

- Six card-type cards (matching the 6 rulebook folders). Each opens with
  the card-type rulebook template, a 30-second prep-strategy cue
  (RULE 49), and the warm-up bank.

**Speaking player** (`app/speaking/roleplay/[id]`,
`app/speaking/task/[id]`):

- Structured 3-min prep timer + 5-min role-play timer enforced
  (RULE 01).
- Prep-screen shows the card with RULE 49 tooling: underline key words,
  attach a 1–2 word label to each task.
- Live ping-pong meter during role-play (RULE 22) — visual hint only,
  not scored in real time.
- Warm-up intro preceding every session (RULE 50, from the Intro
  Questions PDF).

**Speaking transcript / review** (`app/speaking/transcript/[id]`,
`app/speaking/results/[id]`):

- Transcript rendered turn-by-turn with the rule-engine findings
  overlaid. Clicking a finding reveals the rulebook text and the
  recommended phrase.
- Rubric card: linguistic bands (Intelligibility, Fluency,
  Appropriateness, Resources of Grammar) on 0–6 scale, clinical
  indicators A1–E5 on 0–3 scale. Bands align exactly with the public
  OET rubric.

**Expert review console** (`app/expert/review/speaking/[reviewRequestId]`,
`app/speaking/expert-review/[id]`):

- Rubric panel populated from the common assessment-criteria JSON.
- Quick-insert chips: "Add RULE 44 — silence too short",
  "Add RULE 27 — started with reduction", etc.
- The expert's score maps back through canonical scoring
  (`OetScoring.GradeSpeaking(scaled)` — universal 350/500) so the
  learner sees a consistent pass/fail status regardless of who reviewed.

**Breaking Bad News coach** (new — `app/speaking/bad-news-coach/`,
component `components/domain/bad-news-coach.tsx`):

- Structured walkthrough of RULE 40 → 48 (tone, support system,
  warning shots, deliver, silence, respond, hope, support).
- Each step includes exemplar phrases and forbidden patterns.
- Optional drill mode: step-by-step guided practice with the engine
  auditing each turn.

**Lifestyle & negotiation drills** (new — nested under
`app/speaking/drills/`):

- Smoking negotiation drill (RULE 27) with enforced step order.
- Alcohol assessment drill (RULE 30).
- Diet / exercise drill (RULE 24-25).
- BMI-sensitive weight drill (RULE 23).

### 5.4 Speaking scoring

Final scoring continues to route through `OetScoring.GradeSpeaking(scaled)`
(350/500 universal). The rulebook engine produces **signals** (rule
findings + coverage of turn stages), not scaled scores. Experts translate
signals + their own judgement into the rubric sliders.

---

## 6. Data Model Changes

### 6.1 New domain tables (.NET / EF Core)

```
RulebookVersions
  id, examKind ('writing' | 'speaking'), profession, version, publishedAt, isActive

RulebookRules
  id, rulebookVersionId, code ('R03.4' | 'RULE_27'), section, title,
  body (long), severity, appliesToJson, examplesJson, checkId, paramsJson

SubmissionRuleFindings
  id, submissionId (writing OR speaking), ruleId, severity, message,
  quote, start, end, source ('engine' | 'expert'),
  createdAt, resolvedAt

SpeakingTurnStages
  id, transcriptId, turnIndex, stage ('greeting' | 'empathy' | ...)
```

Migrations live in
`backend/src/OetLearner.Api/Data/Migrations/<timestamp>_AddRulebookTables.cs`.

### 6.2 Existing entities — additions

- `WritingSubmission.ruleCoverageJson` — snapshot of engine findings at
  submit time (immutable, audit-safe).
- `SpeakingSubmission.ruleCoverageJson` — same pattern.
- `ReviewRequest.rulebookVersionId` — which rulebook version the reviewer
  scored against (so old reviews stay explainable after rulebook updates).

### 6.3 Content updates

- `Content.metadata` for writing and speaking items gains
  `letterType`, `cardType`, `profession`, `rulebookVersion` so the lint
  engine can select the right ruleset.
- Existing seeded mock data in `lib/mock-data.ts` and
  `backend/src/OetLearner.Api/Services/SeedData.cs` updated to include
  these fields.

---

## 7. API Surface

### 7.1 New endpoints (`WritingCoachEndpoints.cs`, `PrivateSpeakingEndpoints.cs`,
and a new `RulebookEndpoints.cs`)

```
GET  /v1/rulebooks/writing/:profession          -> active rulebook + rules
GET  /v1/rulebooks/speaking/:profession         -> active rulebook + rules
GET  /v1/rulebooks/writing/:profession/rule/:code
GET  /v1/rulebooks/speaking/:profession/rule/:code

POST /v1/writing/lint                           -> body: { letterText, letterType, recipient, caseNotes }
                                                   -> 200: WritingLintFinding[]
POST /v1/speaking/audit                         -> body: { transcript, cardType }
                                                   -> 200: SpeakingAuditFinding[]

GET  /v1/learner/writing/rule-heatmap           -> per-learner rule-level analytics
GET  /v1/learner/speaking/rule-heatmap

POST /v1/admin/rulebooks/:kind/:profession/publish  (admin-only: promote a draft rulebook version)
GET  /v1/admin/rulebooks                           (admin-only: list versions, status)
```

All endpoints JWT-authenticated; admin endpoints require the
`ManageContent` permission (see `lib/admin-permissions.ts`).

### 7.2 Client wrappers (`lib/api.ts`)

```ts
export async function lintWriting(payload: LintWritingPayload): Promise<WritingLintFinding[]>
export async function auditSpeaking(payload: AuditSpeakingPayload): Promise<SpeakingAuditFinding[]>
export async function fetchWritingRulebook(profession: ExamProfession): Promise<WritingRulebook>
export async function fetchSpeakingRulebook(profession: ExamProfession): Promise<SpeakingRulebook>
export async function fetchWritingRuleHeatmap(): Promise<RuleHeatmap>
export async function fetchSpeakingRuleHeatmap(): Promise<RuleHeatmap>
```

---

## 8. UI / UX Work

### 8.1 New pages / components

| Route | Purpose |
|---|---|
| `app/writing/library/` (expand) | One sub-page per letter type with template + must-include/exclude checklist |
| `app/writing/rulebook/[code]` | Deep-link to any writing rule (`/writing/rulebook/R03.4`) |
| `app/speaking/rulebook/[code]` | Deep-link to any speaking rule (`/speaking/rulebook/RULE_27`) |
| `app/speaking/bad-news-coach/` | Guided walkthrough of RULE 40–48 |
| `app/speaking/drills/smoking-negotiation/` | Graduated negotiation drill |
| `app/speaking/drills/breaking-bad-news/` | BBN drill with silence measurement |
| `app/progress/rule-heatmap/` | Per-learner rule performance |
| `app/admin/rulebooks/` | Admin CMS for drafting, diffing and publishing rulebook versions |

### 8.2 New components (`components/domain/`)

- `writing-lint-panel.tsx` — findings list, severity chips, inline quote
  highlighter, "Show critical only" toggle.
- `writing-rule-chip.tsx` — small pill showing `R03.4` + severity colour.
- `speaking-transcript-annotator.tsx` — renders transcript with engine
  findings inline.
- `speaking-rubric-slider.tsx` — 0–6 or 0–3 sliders matching the public
  rubric, driven by the assessment-criteria JSON.
- `bad-news-coach.tsx` — stepper UI for RULE 40–48.

### 8.3 Existing components — touched

- `components/domain/writing-editor.tsx` — add the lint panel wiring and
  a "Run linter" button.
- `components/domain/writing-case-notes-panel.tsx` — add "must include"
  badges to each data point (smoking, allergy, family history) so the
  candidate sees which lines are marked `relevant` / `semi-relevant` /
  `irrelevant` per the selected letter type and recipient.
- `components/domain/subtest-switcher.tsx` — rulebook-aware hover
  tooltips.

---

## 9. Assessment, Grading, and Scoring Integration

- All final pass/fail and grade-letter calculations continue to route
  through the canonical scoring module (`lib/scoring.ts` +
  `OetScoring.cs`). No threshold logic is duplicated.
- Rulebook findings feed **advisory** scores, not final grades — but
  they drive:
  - the "readiness" prediction for a candidate,
  - the AI pre-fill estimate on the expert queue,
  - the weak-area detector in `LearnerService.cs` (replacing the current
    heuristic that only uses scaled averages).
- Writing country-aware pass (UK/IE/AU/NZ/CA = 350, US/QA = 300) is
  unchanged; rulebook findings appear regardless of country but the
  `passed` flag still uses `OetScoring.GradeWriting(scaled, country)`.

---

## 10. Tests

### 10.1 TypeScript (`lib/rulebook/__tests__/`)

- `writing-rules.test.ts` — every CRITICAL rule has at least one positive
  and one negative fixture. Target: ≥ 120 assertions.
- `speaking-rules.test.ts` — every detector (jargon, monologue,
  BBN silence, smoking-negotiation order, overdiagnosis, stage coverage)
  has positive and negative fixtures. Target: ≥ 80 assertions.
- `rulebook-loader.test.ts` — JSON schema validation and profession
  resolution.

### 10.2 .NET (`backend/tests/OetLearner.Api.Tests/`)

- `WritingRulesTests.cs` — mirrors the TS suite. Cross-consistency
  fixture: same letter text must produce the same findings on both sides.
- `SpeakingRulesTests.cs` — mirrors the TS suite on transcripts.
- `RulebookVersioningTests.cs` — publish / rollback / active-version
  selection.

### 10.3 E2E (`tests/e2e/`)

- `writing-lint-flow.spec.ts` — submit a broken discharge letter, see
  critical findings, fix them, see the count fall to zero.
- `speaking-rulebook-navigation.spec.ts` — follow a deep-link to
  `/speaking/rulebook/RULE_44`, verify content renders.

Success gate before shipping any feature phase: **tsc, lint, vitest,
dotnet test, npm run build all clean** (same gate this project already
enforces).

---

## 11. Content Authoring Workflow

Non-engineer content changes must not require a code review:

1. Author edits `rulebooks/writing/medicine/rulebook.v1.json` (or
   drafts a `v2`).
2. CI validates the JSON against the schema, runs the rule engine's
   invariant tests against the new content, and produces a diff.
3. Admin CMS (`app/admin/rulebooks/`) shows the draft side-by-side with
   the active version and a list of affected rules.
4. Admin with `ManageContent` permission clicks "Publish v2" — a new
   `RulebookVersions` row becomes `isActive=true`.
5. All in-flight submissions are pinned to the version they were
   linted against; future submissions use v2. No silent migration.

---

## 12. Delivery Plan — Phased Milestones

**Phase 1 — Foundations (no UI impact)**
- Extract rulebook text to `rulebooks/*.json` with schema validation.
- Build Layer 2 engines (TS + .NET) with unit tests. **Goal: 200+
  assertions all green before any UI ships.**
- Add `RulebookVersions` / `RulebookRules` / `SubmissionRuleFindings`
  tables + migration.

**Phase 2 — Writing assistive experience**
- Wire engine into `WritingCoachEndpoints.cs` (`POST /v1/writing/lint`).
- Add `writing-lint-panel.tsx`; hook into `writing-editor.tsx`.
- Update `app/writing/library/` with per-letter-type templates and
  must-include checklists.
- Deep-link `app/writing/rulebook/[code]`.
- Expert review console gains rule-chip quick-inserts.

**Phase 3 — Speaking assistive experience**
- Wire engine into speaking endpoints (`POST /v1/speaking/audit`).
- Add `speaking-transcript-annotator.tsx`.
- Rubric sliders backed by `assessment-criteria.json`.
- Deep-link `app/speaking/rulebook/[code]`.
- Expert review console gains rule-chip quick-inserts.

**Phase 4 — Drills & coaches**
- `/speaking/bad-news-coach/` with RULE 40–48 stepper.
- Smoking-negotiation drill with graduated-order enforcement.
- Diet / exercise / alcohol / weight drills.

**Phase 5 — Analytics & study-plan integration**
- Per-learner rule heatmaps and cohort views.
- Study-plan generator picks content that exposes learner's weakest
  rules.
- Admin content-effectiveness analytics include rule-level breakdown.

**Phase 6 — Admin CMS for rulebook versioning**
- `app/admin/rulebooks/` draft / diff / publish flow.
- Version pinning on in-flight submissions and reviews.

**Phase 7 — Profession expansion scaffolding**
- Duplicate Medicine JSON as templates for Nursing / Dentistry /
  Pharmacy, wired into the same engine.
- Backfill profession-specific content in
  `Project Real Content/` as Dr. Hesham provides it.

Every phase ends with: migration applied, tests green, docs updated,
`docs/SCORING.md`-style rulebook reference doc regenerated from JSON.

---

## 13. Operational Hygiene

- **Docs:** new `docs/RULEBOOKS.md` (parallel to `docs/SCORING.md`)
  documents the rulebook pipeline, version policy, and authoring steps.
- **AGENTS.md:** add a top bullet: "Writing and Speaking rulebook logic
  MUST route through `lib/rulebook/*` (TS) or
  `OetLearner.Api.Services.WritingRules` /
  `OetLearner.Api.Services.SpeakingRules` (.NET). Never reimplement rule
  checks in surfaces."
- **Gitignore:** `_extracted/` and `_tmp_rulebook_*/` excluded.
- **Commit discipline:** match the 7-commit recipe recently shipped —
  foundations → assistive → drills → analytics → admin → professions.

---

## 14. Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| Over-aggressive linter frustrates learners | Medium | Severity tiers + "show critical only" default |
| False positives on content rules | High | Every detector has worked pos/neg fixtures; expert feedback backfeeds tuning |
| Rulebook drift vs. examiner reality | High | Versioned rulebooks + pinned submissions + Dr. Hesham owns editorial authority |
| Performance of live-lint on large letters | Low | Engine is O(n) regex/AST; debounce at 300 ms in the editor |
| Mobile UX crowding with lint panel | Medium | Panel collapses to a summary chip on mobile; findings accessible via bottom sheet |
| Profession expansion makes rulebook mismatched | Medium | JSON schema rejects entries that lack `profession`; admin CMS blocks publish until all critical rules have worked fixtures |

---

## 15. Acceptance Criteria — Phase-by-Phase

- **Phase 1 passes when:** every CRITICAL rule in both rulebooks exists
  as a JSON entry and has a passing detector test; schema validation is
  green; .NET and TS engine outputs match on a 20-letter, 10-transcript
  gold set.
- **Phase 2 passes when:** the writing player shows lint findings in
  ≤ 500 ms after the user stops typing for 300 ms; expert reviewers can
  insert rule-chip feedback in one click; deep-link
  `/writing/rulebook/R03.4` renders the rule.
- **Phase 3 passes when:** transcript annotator renders for a real
  speaking submission; the rubric sliders persist expert scores; deep-link
  `/speaking/rulebook/RULE_27` renders the rule.
- **Phase 4 passes when:** the BBN coach enforces stage order in practice
  mode; smoking-negotiation drill fails the candidate if they start with
  "reduce".
- **Phase 5 passes when:** the progress dashboard shows the learner's
  top-3 weakest rules and the study plan proposes drills for them.
- **Phase 6 passes when:** an admin can draft a v2 rulebook, diff it
  against v1, publish it, and in-flight submissions stay pinned to v1.
- **Phase 7 passes when:** a Nursing rulebook JSON file is drop-in
  loadable with no schema or engine code changes.

---

## 16. What This Plan Explicitly Rejects

- **Hard-coding rule text in TypeScript or C#.** Never. Content lives in
  JSON under `rulebooks/`.
- **Black-box ML for rule detection.** Every detector is transparent,
  deterministic, and testable. ML can *assist* (e.g., stage classification
  on transcripts) but never replaces the rule layer.
- **Silent replacement of expert grading.** Rule findings are advisory.
  Final grades always flow through the canonical scoring module.
- **Duplicating the rulebook in surfaces.** All rulebook access goes
  through the engine; the admin CMS is the only writable surface.
- **Profession drift.** Every JSON entry is profession-tagged from day
  one.

---

## 17. Immediate Next Actions

1. Create the `rulebooks/` directory with the JSON schema file
   (`rulebooks/schema/rulebook.schema.json`).
2. Transcribe Writing rulebook R01.1 – R16.8 into
   `rulebooks/writing/medicine/rulebook.v1.json`.
3. Transcribe Speaking rulebook RULE 01 – RULE 55 into
   `rulebooks/speaking/medicine/rulebook.v1.json`.
4. Copy `Speaking Assessment Criteria.pdf` content into
   `rulebooks/speaking/common/assessment-criteria.json`.
5. Draft `lib/rulebook/types.ts` + `lib/rulebook/writing-rules.ts` skeleton
   + unit test scaffold (Phase 1 kick-off).
6. Open a tracking issue per phase; each phase merges via the same
   commit discipline used for scoring.

On your signal, I start with step 1 — the JSON schema and the Writing
rulebook transcription — and work forward through the phases.

---

## Appendix A — High-Fidelity Rulebook Extractions (second pass)

A second extraction pass was run with **PyMuPDF** (layout-aware) and
**pdfplumber** (table-aware) in addition to the first pypdf pass.
This recovered four artefacts that are now first-class inputs to the
engine and the JSON rulebooks:

### A.1 Writing — Exact letter structure flow (R04.1)

The single authoritative letter skeleton. The engine's layout linter
enforces this byte-for-byte; any deviation produces a finding.

```
Address
[blank line]
Date
[blank line]
Salutation
Re: line                ← NO blank line above this line (R04.2 critical)
[blank line]
Introduction
[blank line]
Body paragraph 1
[blank line]
Body paragraph 2
[blank line]
…
Body paragraph N        (max 4, R03.9; min 2, R08.1)
[blank line]
Closure
[blank line]
Yours sincerely, / Yours faithfully,   (sincerely iff named recipient, R06.11)
[1–2 blank lines]
Doctor
```

### A.2 Writing — Adult vs. Child naming matrix (R06.10 + R08.15)

Deterministic lookup baked into the engine. Any body text referencing
a minor with a title, or an adult with no title, fires a critical
finding.

| Context                        | Children (Day 1 – 17 yr)                    | Adults (18 yr+)                                 |
|--------------------------------|---------------------------------------------|-------------------------------------------------|
| Re: line                       | `Re: Sara Miller` (full name, no title)     | `Re: Ms Sara Miller` (title + full name)        |
| Body — first mention / para    | First name only: `"Sara presented with..."` | Title + last name: `"Ms Miller presented with..."` |
| Body — same paragraph          | Pronoun: `"she reported..."`                | Pronoun: `"she reported..."`                    |
| Body — next paragraph          | First name again: `"Sara returned..."`      | Title + last name again: `"Ms Miller returned..."` |

Age-in-Re-line variants (R06.8):

| Situation         | Re: line format                         | In introduction                 |
|-------------------|-----------------------------------------|---------------------------------|
| DOB given         | `Re: Ms Sara Miller D.O.B: 01/01/1984`  | Age optional in intro           |
| Age only given    | `Re: Ms Sara Miller, aged 40`           | Do NOT repeat age in intro      |
| Both given        | `Re: Ms Sara Miller D.O.B: 01/01/1984`  | Use DOB in header; age optional |

### A.3 Speaking — Jargon → Plain English glossary (RULE 06–12)

Drop-in seed for `rulebooks/speaking/common/layman-glossary.json`. The
detector flags any direct use of a left-column term in a candidate turn
unless immediately followed by a plain-English explanation per RULE 12.

| Medical term                    | Plain English                            | Example phrase                                   |
|---------------------------------|------------------------------------------|--------------------------------------------------|
| CT scan / MRI scan              | Imaging scan                             | "We'll do an imaging scan of your chest"         |
| Ultrasound                      | Gel scan / Tummy scan                    | "A gel scan of your abdomen"                     |
| X-ray                           | X-ray (unchanged)                        | "We'll take an X-ray of your leg"                |
| Endoscopy / Colonoscopy / -scopy| Camera test                              | "A camera test to look inside"                   |
| IV / IM Injection               | A jab                                    | "A quick jab in your arm"                        |
| Subcutaneous injection          | Tiny injection under the skin            | "A small injection under your skin"              |
| Spinal injection                | Tiny injection in your back              | "A tiny injection in your back"                  |
| ECG                             | Tracing of your heartbeat                | "A tracing to check your heartbeat"              |
| EEG                             | Measuring brain activity                 | "Measuring electrical activity in your brain"    |
| Blood pressure / BP             | Pressure in your blood vessels           | "Let me check the pressure in your vessels"      |
| Abdomen                         | Tummy                                    | "Any pain in your tummy?"                        |
| Oesophagus                      | Food pipe                                | "Irritation in your food pipe"                   |
| Trachea                         | Windpipe                                 | "Swelling in your windpipe"                      |
| Uterus                          | Womb                                     | "A scan of your womb"                            |
| Paediatrician                   | Children's specialist                    | "Refer to a children's specialist"               |
| Gynaecologist                   | Women's health specialist                | "A women's health specialist can help"           |
| Endocrinologist                 | Hormone specialist                       | "A hormone specialist would be best"             |
| Hypertension                    | High blood pressure / higher side        | "Your reading is on the higher side"             |

### A.4 Speaking — 13-stage Consultation State Machine (Appendix of rulebook)

The rulebook's own "Card Structure at a Glance" table is modelled as a
state machine. The transcript auditor classifies each candidate turn
into one of these stages; missing stages generate
`stage_missing` findings. Stage 12 (Recap) and 13 (Closure) are
particularly important in scoring (RULE 20, 21, 52).

| # | Stage        | What to do                            | Canonical example phrase                                       |
|---|--------------|---------------------------------------|----------------------------------------------------------------|
| 1 | Greeting     | Introduce self, invite to sit         | `"Hello, I'm Dr. X. Please take a seat."`                      |
| 2 | Opening      | Open-ended question                   | `"How can I help you today?"`                                  |
| 3 | Listening    | Active listening signals              | `"I see... Right... Mm-hmm..."`                                |
| 4 | Empathy      | Acknowledge experience                | `"That must have been very difficult."`                        |
| 5 | Permission   | Ask to proceed                        | `"Would you mind if I asked some questions?"`                  |
| 6 | Questions    | Explore symptoms                      | `"Can you tell me more about the pain?"`                       |
| 7 | Diagnosis    | State in plain language + feedback    | `"It seems you may have... Have you heard of this?"`           |
| 8 | Causes       | Explain causes plainly                | `"One main cause is..."`                                       |
| 9 | Lifestyle    | Counsel sensitively                   | `"I'd strongly advise you to consider..."`                     |
|10 | Treatment    | Explain + feedback                    | `"I'd like to prescribe... How does that sound?"`              |
|11 | Reassurance  | Reduce anxiety                        | `"I want to reassure you we can manage this well."`            |
|12 | Recap        | 30-second summary                     | `"So to recap what we've discussed..."`                        |
|13 | Closure      | Close warmly                          | `"Please don't hesitate to come back anytime."`                |

### A.5 Speaking — Breaking Bad News 7-step protocol (RULE 40–47)

Modelled as a strict-order sub-state-machine, only engaged on
`cardType === 'breaking_bad_news'`. Violating step order is a
`critical` finding. The silence-measurement detector (RULE 44) consumes
the audio timeline, not just the transcript.

1. **Tone** (RULE 40) — soft, low, empathetic throughout.
2. **Support system** (RULE 41) — "Before we discuss your results, is there anyone you'd like to have here with you?"
3. **Warning shots** (RULE 42) — "I'm afraid the results are not quite what we had hoped for..."
4. **Deliver diagnosis** (RULE 43) — "I'm very sorry to tell you — the results are showing signs of cancer."
5. **Silence** (RULE 44) — STOP SPEAKING for **3–4 seconds**. Measurable.
6. **Respond to emotion** (RULE 45) — "I'm so sorry. I know this is a lot to take in. Please take all the time you need."
7. **Hope + next steps** (RULE 46) — "We caught this at an early stage, and there are effective treatment options available."
8. **End with support system** (RULE 47) — "I'm here for you, and my number is available whenever you need anything."

Proportionality (RULE 48): this full 7-step protocol applies only to
cancer / serious diagnoses. Minor bad news (fractures, mild results)
receives a light acknowledgement + reassurance only. The engine reads
`cardType` to decide which rule set to apply.

### A.6 Speaking — Smoking negotiation ladder (RULE 27)

Graduated-order enforcement. The detector scans candidate turns in
order; any turn matching "reduce"/"cut down"/"try to smoke less"
before a turn matching "quit"/"stop"/"cessation" is a **critical**
finding.

1. **Step 1 — Black Area:** Recommend complete cessation.
2. **Step 2:** If refused, offer NRT / nicotine alternatives.
3. **Step 3 — Grey Zone:** Negotiate a reduction.

Plus mandatory ancillary phrases:

- RULE 28 — empathy: "I understand it's not easy..."
- RULE 29 — support resource: "We have a smoking cessation clinic here..."

### A.7 Writing — Latin abbreviations translation table (R11.1)

Drop-in seed for the layout/lexicon detector. Any Latin token from the
left column that is not replaced by its right-column equivalent fires a
**critical** finding.

| Latin   | Must translate to       |
|---------|-------------------------|
| od / om | once a day              |
| bd / bid| twice a day             |
| tds / tid| three times a day      |
| qds / qid| four times a day       |
| stat    | immediately             |
| prn     | as needed               |
| ASAP    | at your earliest convenience (R13.10) |

### A.8 Writing — Conjunction punctuation patterns (R12.9–R12.14)

Each pattern becomes a regex detector with a fix suggestion:

| Linker        | Preceding | Following | Example                                           |
|---------------|-----------|-----------|---------------------------------------------------|
| however       | `;`       | `,`       | `She reported nausea; however, vomiting was absent.` |
| therefore/thus| `;`       | `,`       | `She had chest pain; therefore, ECG was performed.`  |
| in addition   | `;`       | `,`       | `Nausea was reported; in addition, she had fever.`   |
| in addition to| *(none)*  | noun/ING  | `In addition to vomiting, she reported nausea.`      |
| together with | *(none)*  | noun/ING  | `The rash, together with fever, was noted.`          |
| along with    | *(none)*  | noun/ING  | *(as above)*                                      |
| as well as    | *(none)*  | noun/ING  | *(as above)*                                      |
| as (=because) | `,`       | *(none)*  | `Treatment was changed, as she showed no improvement.` |
| for which     | `,`       | *(none)*  | `She has hypertension, for which she was commenced on amlodipine.` |

Subject-verb agreement after linkers (R12.13 / R12.14) is covered by a
dedicated detector:

- **Proximity rule** (`or` in negatives): verb agrees with nearest subject.
- **Reverse rule** (`along with / together with / in addition to`): verb
  agrees with the word **before** the linker.

### A.9 Speaking — Assessment rubric scaffold

Straight from the OET public rubric, already extracted cleanly in the
first pass:

- **Linguistic criteria (band 0–6):** Intelligibility, Fluency,
  Appropriateness of Language, Resources of Grammar and Expression.
- **Clinical communication criteria (indicator 0–3 per cluster):**
  - A — Relationship building (A1–A4)
  - B — Understanding & incorporating the patient's perspective (B1–B3)
  - C — Providing structure (C1–C3)
  - D — Information gathering (D1–D5)
  - E — Information giving (E1–E5)

Each indicator is a separate slider in the expert review console,
bound directly to `rulebooks/speaking/common/assessment-criteria.json`.
Expert scores for these indicators feed the engine's "rule coverage"
analytics so we can correlate rule-level signals with the public
criteria.

---

### A.10 Extraction reproducibility

The raw, layout-fidelity text (per page), the markdown-ised tables,
and the embedded images are all reproducible from the PDFs in
`Project Real Content/` using PyMuPDF + pdfplumber. The extraction
output lives in `_extracted/` and is **gitignored** (temporary build
artefact, not content-of-record). If the rulebooks change, regenerate
with:

```bash
python -m pip install --upgrade pymupdf pdfplumber pypdf
# re-run extraction script (kept in git history for reference)
```

This concludes the deep understanding pass. Every rule — numbered,
coded, tabled, or diagrammed — is captured. The engine + JSON
transcription of Phase 1 can now proceed with zero guesswork.
