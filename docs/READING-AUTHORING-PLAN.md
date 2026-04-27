# Reading Authoring — Implementation Plan

> **Status**: planning document. Companion to `CONTENT-UPLOAD-PLAN.md`,
> `AGENTS.md`, and `SCORING.md`.
>
> The Content Upload subsystem (already built) handles storing Reading PDFs.
> This plan is about the **next layer**: turning those PDFs into
> machine-graded, interactive Reading tests with the exact OET paper
> structure — 42 scored items across Parts A, B, C — so learners can take
> the test inside the app and we can auto-grade against `30/42 ≡ 350/500`
> per `lib/scoring.ts`.
>
> This was explicitly flagged as a non-goal of the upload plan (see §14 of
> `docs/CONTENT-UPLOAD-PLAN.md`). Reading needs its own subsystem because
> PDFs don't carry the structured Q&A data that grading requires.

---

## 1. Source-of-truth analysis

### 1.1 What's in `Project Real Content/Reading/`

Three samples, two PDFs per sample:

```
Reading Sample 1/
├─ Part A Reading ( Diarrhea & Dehydration in Children ).pdf   ← 623 KB
└─ Reading Part B&C.pdf                                          ← 4.4 MB

Reading Sample 2/
├─ Part A Reading.pdf                                            ← 1.0 MB
└─ Reading Part B&C _.pdf                                        ← 766 KB

Reading Sample 3/
├─ Reading Part A _.pdf                                          ← 955 KB
└─ Reading Part B&C.pdf                                          ← 836 KB
```

**Same for all professions.** Already modeled correctly by
`ContentPaper.AppliesToAllProfessions=true` in the upload subsystem.

### 1.2 The actual OET Reading paper structure (canonical)

OET Reading is a fixed-shape 60-minute paper, 42 scored items:

| Part | Time | Items | Item type | Texts |
|---|---|---|---|---|
| **Part A** | 15 min (strict) | **20** | `matching` + `short_answer` + `sentence_completion` | **4 medical texts** on a single clinical topic (variable length — Text C may include large tables/graphs; no length cap is enforced) |
| **Part B** | 45 min shared with C | **6** | `multiple_choice` (A/B/C) | **6 short extracts from different healthcare contexts** (policies, notices, guidelines, clinical communications) |
| **Part C** | 45 min shared with B | **16** (8 per text × 2) | `multiple_choice` (A/B/C/D) | **2 long healthcare articles**, 8 questions each |
| **Total** | **60 min** | **42** | — | **12 texts** |

Raw → scaled mapping for Listening/Reading is canonical:
**`30/42 ≡ 350/500`** (Grade B pass). Already enforced in `lib/scoring.ts`.

The three files in your folder are **official-format papers** that match
this shape exactly. Part A is its own PDF because it has its own 15-minute
strict sub-timer (papers are physically collected at 15 min by OET). Parts
B + C share one 45-minute window so they can be combined in a single PDF.

### 1.3 What's already in the codebase

**Schemas present:**
- `lib/types/subtest-content-schemas.ts` — `ReadingPassage`, `ReadingQuestion`, `ReadingTaskDetail`, `ReadingModelAnswer` types exist but nothing persists or renders them
- `lib/scoring.ts` — canonical raw↔scaled conversion for Listening/Reading (30/42 = 350)
- `ContentPaper` / `ContentPaperAsset` / `MediaAsset` — PDF storage working
- `ContentPaperAsset.Part` — already supports "A" / "B+C"
- Admin bulk-import + paper editor — already working

**Schemas missing:**
- No DB entities persisting `ReadingQuestion`
- No endpoints serving structured questions to learners
- No player UI for picking answers
- No grading endpoint
- No "split Parts B+C into B and C" tooling
- No authoring UI for entering questions

### 1.4 What this tells us

Two problems to solve:

1. **Structure capture**: turn a 42-question paper into a machine-gradable tree (`paper → parts → texts → questions → options`) and let admins author/edit it via a UI.
2. **Item rendering + grading**: show each question type correctly in the learner player, capture answers, grade against the key, emit a timed result.

Both have to preserve the canonical scoring contract (30/42 = 350 exactly) and the existing `ContentPaper.Part` field semantics.

---

## 2. Data model

Five new entities. All additive. No breaking changes to existing tables.

### 2.1 `ReadingPart`

A reading paper has exactly three parts. One row per `(paper_id, part_code)`.

```csharp
public enum ReadingPartCode { A = 1, B = 2, C = 3 }

public class ReadingPart
{
    public string Id { get; set; }                 // pk
    public string PaperId { get; set; }            // FK → ContentPaper.Id
    public ReadingPartCode PartCode { get; set; }
    public int TimeLimitMinutes { get; set; }      // A=15, B+C share 45
    public int MaxRawScore { get; set; }           // A=20, B=6, C=16
    public string? Instructions { get; set; }      // rendered above Part
}
```

Indexed `(paper_id, part_code)` unique.

### 2.2 `ReadingText`

One text (passage) within a part. Part A has 4 medical texts on a single topic (variable length — no cap). Part B has 6 short extracts from different healthcare contexts (policies, notices, guidelines, clinical communications). Part C has 2 long articles.

```csharp
public class ReadingText
{
    public string Id { get; set; }
    public string ReadingPartId { get; set; }      // FK → ReadingPart.Id
    public int DisplayOrder { get; set; }
    public string Title { get; set; }              // e.g. "Text 1: Oral Rehydration"
    public string? Source { get; set; }            // "BMJ 2019" — copyright provenance
    public string BodyHtml { get; set; }           // sanitised HTML
    public int WordCount { get; set; }             // indicator for reading time
    public string? TopicTag { get; set; }          // "diarrhea_dehydration" for Part A
}
```

Indexed `(reading_part_id, display_order)`.

### 2.3 `ReadingQuestion`

One scorable item.

```csharp
public enum ReadingQuestionType
{
    /// <summary>Part A: match a statement to text 1-4.</summary>
    MatchingTextReference = 0,
    /// <summary>Part A: gap-fill / short-answer, exact-match graded.</summary>
    ShortAnswer = 1,
    /// <summary>Part A: sentence completion against a phrase bank.</summary>
    SentenceCompletion = 2,
    /// <summary>Part B: 3-option multiple choice.</summary>
    MultipleChoice3 = 3,
    /// <summary>Part C: 4-option multiple choice.</summary>
    MultipleChoice4 = 4,
}

public class ReadingQuestion
{
    public string Id { get; set; }
    public string ReadingPartId { get; set; }      // FK
    public string? ReadingTextId { get; set; }     // nullable — some Part A items span texts
    public int DisplayOrder { get; set; }          // 1..N within the part
    public int Points { get; set; }                // 1 by default; schema-flexible
    public ReadingQuestionType QuestionType { get; set; }
    public string Stem { get; set; }               // the question text
    public string OptionsJson { get; set; }        // ["A","B","C"] or {"A":"…","B":"…"} depending on type
    public string CorrectAnswerJson { get; set; }  // "B" | ["A","C"] | "approx 5 ml/kg"
    public string? ExplanationMarkdown { get; set; }
    public string? SkillTag { get; set; }          // "inference" | "vocabulary_in_context" | …
    public bool CaseSensitive { get; set; }        // for ShortAnswer grading
    public string? AcceptedSynonymsJson { get; set; } // ["ORT","oral rehydration therapy"]
}
```

Indexed `(reading_part_id, display_order)`.

### 2.4 `ReadingAttempt` + `ReadingAnswer`

A learner's run through a paper, and one row per answered question.

```csharp
public enum ReadingAttemptStatus { InProgress = 0, Submitted = 1, Expired = 2, Abandoned = 3 }

public class ReadingAttempt
{
    public string Id { get; set; }
    public string UserId { get; set; }
    public string PaperId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; } // StartedAt + paper.TimeLimitMinutes
    public ReadingAttemptStatus Status { get; set; }
    public int? RawScore { get; set; }              // populated on grade
    public int? ScaledScore { get; set; }           // derived via oetRawToScaled
    public int MaxRawScore { get; set; }            // always 42 for real papers
    public int Version { get; set; }                // paper revision at start time
}

public class ReadingAnswer
{
    public string Id { get; set; }
    public string ReadingAttemptId { get; set; }    // FK
    public string ReadingQuestionId { get; set; }   // FK
    public string UserAnswerJson { get; set; }      // "A" | "text string" | {...}
    public bool? IsCorrect { get; set; }            // null until graded
    public int PointsEarned { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }
}
```

Indexed `(user_id, status)` on attempt, `(reading_attempt_id, reading_question_id)` unique on answer.

### 2.5 Relationships

```
ContentPaper  (subtestCode="reading")
  └── ContentPaperAsset  (role=QuestionPaper, part="A" | "B+C")
  └── ReadingPart        (A, B, C)  ← NEW
        └── ReadingText  (4 / 6 / 2)
        └── ReadingQuestion  (20 / 6 / 16 = 42)

ReadingAttempt (per user per paper)
  └── ReadingAnswer (one per attempted question)
```

The PDF assets remain — they're the source-of-truth artefact, useful for admin verification, for an "original paper" download button, and because licensing provenance requires it.

---

## 3. Authoring flow — three modes, all admin-operable

### 3.1 Mode A: Hand-authored (precise, slow)

For the first Reading papers you publish and for any paper where accuracy is critical. Admin:

1. Opens the paper editor (already built: `/admin/content/papers/[paperId]`).
2. New **"Reading structure"** tab appears because `subtestCode === 'reading'`.
3. Tab lays out three sections: Part A, B, C. Each with per-part time limit field pre-filled from OET defaults (15 / 45 / 45).
4. Within each part, admin clicks **"+ Text"** to add a passage. Tiptap (or existing rich-text) lets them paste content with bold/underline/lists intact. Source attribution required.
5. Within each text (or outside, for Part A matching), admin clicks **"+ Question"** and picks question type. Form adapts by type:
   - **MultipleChoice3/4**: 3 or 4 option fields + correct-answer radio
   - **MatchingTextReference**: statement text + checkbox list of texts 1–4 (multi-select allowed for OET matching)
   - **ShortAnswer**: stem + correct answer(s) + accepted synonyms + case-sensitive toggle
   - **SentenceCompletion**: stem with a `____` marker + answer bank words
6. Live-validation: "Part A has 17/20 questions" counter, blocks publish if ≠ 42 total.

### 3.2 Mode B: AI-assisted extract-from-PDF (fast, reviewed)

Leverages the existing Content Upload subsystem. The admin:

1. Uploads the Part A / Part B+C PDFs exactly as they do now.
2. Clicks **"Extract reading structure"** on the editor tab.
3. Server-side:
   - PDF text extraction via `IPdfTextExtractor` (the pluggable slot already exists in Slice 7 of the upload plan; PdfPig fills it in production).
   - Grounded AI call via `AiGatewayService` (feature code `reading.authoring_extract`, admin-only, platform key) that takes the extracted text and returns a **JSON manifest** matching the `ReadingPart` / `ReadingText` / `ReadingQuestion` shape.
   - Strict JSON-schema validation on the response. Any shape deviation → reject and log.
4. Result stages as a **draft structure** on the paper. Admin reviews, edits anything wrong, and approves.
5. Only after approval does the structure become the live paper structure.

This is not "trust the AI" — this is "let the AI do 80% of the typing, let the admin do 100% of the verification." The approval step is required; there is no auto-publish path.

### 3.3 Mode C: JSON import (bulk, power-user)

For migrating a pre-existing question bank or for engineer-side seeding:

- `POST /v1/admin/papers/{paperId}/reading-structure` accepts a JSON body matching a documented schema.
- Idempotent — re-uploads replace the draft structure atomically.
- Used by the seeder to bootstrap sample papers for development.

---

## 4. Learner experience

### 4.1 Reading player (`/reading/player/[paperId]` — already a route)

Three tabs for Parts A / B / C. Each tab:

- **Top bar**: countdown timer (part-specific for A; combined for B+C). When the part is A, the timer physically locks the tab when it hits 0 and silently submits part-A answers (matches OET paper-collection rule). When B/C share a timer, expiry submits both.
- **Main pane**: scrollable text (or list of texts for Part A). Preserves bold/italic/line breaks from authored HTML. PDF not rendered here — structured content only, because that's what's graded.
- **Answer pane (right on desktop, sheet on mobile)**: question list with the correct input widget per type. Auto-saves per keystroke / per radio-pick to `ReadingAnswer` so a refresh doesn't lose progress.
- **Question navigator**: dots showing answered/flagged/unanswered per part.
- **Submit button**: grey until all questions answered, turns blue when ≥1 answered. Confirmation dialog summarises answered/total before submit.

### 4.2 Reading results (`/reading/results/[attemptId]`)

- OET Statement of Results card (already built, pixel-faithful) with the scaled score in Reading.
- Per-question breakdown: your answer / correct answer / explanation (if the admin authored one). Collapsed by default; click to expand per question.
- **Retry** button that opens a *fresh* attempt on the same paper (doesn't overwrite history — each attempt is a new row).

### 4.3 Download original PDF

Present on both player and results pages: a **"Download original paper (PDF)"** button that serves the authored `ContentPaperAsset` with role `QuestionPaper`, for learners who prefer paper. Separate from the graded structure. Already handled by the Content Upload subsystem.

---

## 5. Scoring contract (MISSION CRITICAL)

Every grading call routes through `lib/scoring.ts` / `OetLearner.Api.Services.OetScoring`. Sequence:

1. Learner submits. Server validates: attempt belongs to user, status=InProgress, not expired.
2. `ReadingGradingService.GradeAsync(attemptId)`:
   - Joins `ReadingAnswer` to `ReadingQuestion`.
   - Per question type:
     - `MultipleChoice3/4` / `MatchingTextReference`: exact match on `correctAnswerJson`.
     - `ShortAnswer` / `SentenceCompletion`: normalised compare (trim, collapse whitespace, respect `CaseSensitive`, accept any value in `AcceptedSynonymsJson`).
   - Sum `PointsEarned` → `RawScore`. Max is **always 42** for real papers; the service throws `InvalidPaperStructure` if total question points ≠ 42 on a published paper (caught by the publish gate, not at grade time for defence in depth).
3. Scaled score derived: `oetRawToScaled(rawScore)` — this is the only place raw→scaled mapping happens.
4. Grade letter derived: `oetGradeFromScaled(scaledScore)`.
5. Attempt record updated: `Status=Submitted`, `RawScore`, `ScaledScore`, `SubmittedAt`.
6. Audit event written: `ReadingAttemptGraded`.

**What never happens**: inline `if (raw >= 30)`. Inline threshold comparisons. Client-side grading. Any of these = bug.

---

## 6. Admin UI — the authoring console

Adds one tab to the existing `/admin/content/papers/[paperId]` editor when `subtest === 'reading'`:

### 6.1 Structure tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Part A · 15 min · 20 items       [Edit settings]                │
│  ┌─ Texts (4) ──────────────────────────────┐ [+ Add text]      │
│  │ Text 1: ORT for mild dehydration   [Edit] [↑↓] [✕]           │
│  │ Text 2: Hospital referral criteria [Edit] [↑↓] [✕]           │
│  │ Text 3: …                                                    │
│  └────────────────────────────────────────────┘                │
│  ┌─ Questions (17/20) ⚠ 3 more needed ───────┐ [+ Add question] │
│  │ Q1  matching   "In which text is…"         [Edit] [✕]        │
│  │ Q2  short_ans  "What dosage of …"          [Edit] [✕]        │
│  │ …                                                            │
│  └────────────────────────────────────────────┘                │
└─────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────┐
│ Part B · 6 items (shares 45 min with C)                         │
│ …                                                               │
└─────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────┐
│ Part C · 16 items (shares 45 min with B)                        │
│ …                                                               │
└─────────────────────────────────────────────────────────────────┘

Total: 37/42 scored items — publish blocked until 42.
[Extract from PDF (AI)] [Validate] [Save draft] [Publish]
```

Each question edit opens a modal with the per-type form layout from §3.1.

### 6.2 Validate action

Dry-runs the publish gate:

- Part A exactly 20 items, Part B exactly 6, Part C exactly 16.
- Every `ReadingQuestion.CorrectAnswerJson` parses correctly.
- Every question either has a `ReadingTextId` or the question type is `MatchingTextReference`.
- No duplicate `DisplayOrder` within a part.
- Per-part time limits set.

Returns a checklist of pass/fail rows so the admin sees exactly what's missing.

### 6.3 Preview action

Opens the learner player in a `/preview` route scoped to the admin's session, grading disabled. Useful for "does this actually read right?" before publish.

---

## 7. Grading contract + edge cases

| Case | Behaviour |
|---|---|
| Learner skips a question | `ReadingAnswer` row absent → 0 points |
| Learner selects "C" but question is 3-option | Rejected at save time |
| Timer expires mid-part | Server auto-submits; any saved answers count, unsaved = 0 |
| Paper revised after attempt starts | Attempt uses the `Version` snapshot it started against; mid-flight structure changes do not affect in-progress attempts |
| Admin publishes paper with 41 items | Publish gate blocks with "Must have exactly 42 items" error |
| Learner submits twice (network replay) | Idempotent: first submit wins, second returns the existing result |
| Grade arrives before all answers saved (race) | Submit endpoint drains the answer write buffer before grading |
| Paper archived mid-attempt | In-progress attempts complete normally; new attempts blocked |

---

## 8. Security, copyright, audit

- **Authored content copyright**: every `ReadingText.Source` required before publish, same enforcement as the existing `ContentPaper.SourceProvenance` rule.
- **Answer leakage**: `CorrectAnswerJson` and `ExplanationMarkdown` are **never** serialised on the learner's question-fetch endpoint. Two DTOs: `ReadingQuestionLearnerDto` (no answers) and `ReadingQuestionAdminDto` (full). Enforced at the endpoint layer, not left to "please remember not to include it."
- **Audit events** on every mutation: `ReadingPartCreated`, `ReadingQuestionUpdated`, `ReadingAttemptSubmitted`, `ReadingAttemptGraded` — standard pattern used by Content Upload already.
- **Rate limits**: attempt submit limited to 1 per 30 seconds per user (replay protection). Question fetch cached per-user per-attempt.
- **Admin AI extraction**: the `reading.authoring_extract` feature code runs on platform keys only (admin tooling per the existing `PlatformOnlyFeatures` list in the credential resolver).

---

## 9. Implementation slices

Same pattern as Content Upload: each slice independently mergeable and independently green.

### Slice R1 — Domain entities + migrations
- `ReadingPart`, `ReadingText`, `ReadingQuestion`, `ReadingAttempt`, `ReadingAnswer` + enums.
- Hand-written migration in project style.
- DbSets registered. Unique index on `(paper, part_code)` partial-unique.
- Tests: entity round-trip, enum persistence, FK cascade rules.

### Slice R2 — Structure service + validation
- `IReadingStructureService`: CRUD parts/texts/questions, reorder, validate.
- Publish gate extension: Reading papers require 20/6/16 item split per part.
- `ReadingPaperValidator`: returns a structured report (PartA: ok, PartB: missing 2 items, …).
- Tests: validator table, ordering, CRUD.

### Slice R3 — Admin endpoints + authoring UI
- `/v1/admin/papers/{paperId}/reading-structure` — GET / PUT (whole structure) + granular PATCH per part.
- `/admin/content/papers/[paperId]` gets the new "Reading structure" tab.
- All the inline question editors with per-type forms.
- Tests: endpoint auth + permission, DTO shape correctness, audit events.

### Slice R4 — Learner fetch + attempt lifecycle
- `GET /v1/papers/{slug}/reading` — structured payload, **no answers**.
- `POST /v1/reading/attempts` — start attempt, returns deadline.
- `PUT /v1/reading/attempts/{id}/answers/{questionId}` — autosave.
- `POST /v1/reading/attempts/{id}/submit` — grade.
- Server-side auto-expire job for abandoned attempts.
- Tests: no-answer-leak DTO guard, timer enforcement, idempotent submit.

### Slice R5 — Grading service (scoring canonical)
- `ReadingGradingService.GradeAsync`.
- Per-question-type grader strategies.
- Integration with `lib/scoring.ts` / `OetScoring` for raw→scaled.
- Tests: full grading table (one fixture per question type × right/wrong × edge cases), verify `raw === 30 → scaled === 350` boundary exactly.

### Slice R6 — Learner player UI
- `/reading/player/[paperId]` — three-tab structure with part timer, question list, answer panes.
- Autosave integration.
- Unanswered navigator, flag-for-review.
- Tests: happy-path render, timer expiry triggers submit, autosave persists.

### Slice R7 — Results page + SoR integration
- `/reading/results/[attemptId]` — OET SoR card plus per-question breakdown.
- Retry button → new attempt row.
- Tests: renders scaled score correctly, explanation gating respects admin field.

### Slice R8 — AI extraction (authoring Mode B)
- Feature code `reading.authoring_extract` added to `AiFeatureCodes` + classified as platform-only admin tooling.
- JSON schema contract for the expected AI response.
- Server-side validation; any shape mismatch → refuse, no DB writes.
- Admin UI "Extract from PDF" button + review/approve flow.
- Tests: schema rejection, happy path produces a draft structure, approval promotes it.

### Slice R9 — Hardening + observability
- Rate limit on submit endpoint.
- Auto-expire job for abandoned attempts (hourly).
- Analytics: `reading_attempt_started`, `reading_attempt_submitted`, `reading_attempt_expired`.
- Load test: 100 concurrent attempts autosaving.
- Penetration review of endpoints, especially the no-answer-leak guarantee.

---

## 10. Non-goals (deliberately excluded)

- **Rendering the original PDF inline for grading.** PDFs aren't structured data. The learner sees authored HTML for grading purposes; the PDF remains available as a download.
- **AI-graded Reading.** Reading is objective; the grading contract is exact-match against the author's correct answer key. No AI in the grading path.
- **Dynamic question generation.** Every question is authored and reviewed. We do not generate variants at runtime.
- **Adaptive / item-response theory.** The existing 30/42 → 350 mapping is fixed by OET. We don't invent an adaptive scoring curve.
- **Reading rulebook.** Reading doesn't have a rulebook like Writing/Speaking — it's pure objective MCQ. No new rulebook files.

---

## 11. How this composes with what's already built

| Layer | Provides | Reading authoring uses |
|---|---|---|
| Content Upload (Slices 1–8, done) | PDF storage, SHA dedup, publish gate on papers, admin CMS surface | Stores the source Part A / Part B+C PDFs; publish gate extended by Reading validator |
| AI Gateway + grounding (done) | Grounded prompt refusal, usage accounting, credential resolver | Mode B extraction runs through here with a new feature code |
| Scoring (`lib/scoring.ts`, done) | Canonical raw↔scaled, country-aware thresholds | Reading grader uses `oetRawToScaled` exclusively |
| OET SoR card (done) | Pixel-faithful result card | Reading results page renders it |
| Audit events (pre-existing) | Every admin mutation logged | Reading authoring + attempts emit standard audit rows |

This plan adds no new cross-cutting primitive. Everything new is Reading-specific, built on top of what's already proven green.

---

## 12. Operator handoff items

1. **Author Reading Sample 1 by hand first**. You already have the PDF; authoring it manually once (or with AI-extract reviewed in Mode B) validates the admin UI and produces a known-good golden fixture to test the grader against.
2. **Supply the correct answer keys**. The PDFs in `Project Real Content/Reading/` contain questions but not answer keys — those are typically in a separate teacher pack. Admin UI requires correct answers as structured data.
3. **Decide the retry policy**. My default: unlimited retries, each a fresh attempt, best score shown on `/progress`. Tell me if you want to cap attempts per paper per user.
4. **Confirm timer strictness**. Default: Part A is hard-locked at 15 min (matches OET). Tell me if you want soft (grace period + warning banner) for practice.

---

## 13. Why this design holds up under change

- **New question type** (e.g. drag-and-drop): add one enum value, one UI widget, one grader strategy. No schema migration.
- **New subtest** (Listening structured grading when you're ready for it): copy the pattern — `ListeningPart`, `ListeningQuestion`, same shape. `ReadingPart`'s PartCode enum is deliberately not generic, so the Listening version stays type-safe on its own axis.
- **Rich text evolves** (e.g. tables inside passages): `BodyHtml` is HTML; sanitise on input, render on output, done.
- **Question banks / randomised papers**: add a `ReadingQuestionBank` table and a `PaperAssemblyRule`; `ReadingPart` becomes a view over rule output. Out of scope today but the data model doesn't resist it.
- **Multilingual**: add `ReadingText.LocaleCode`; current data is single-language. Not needed for OET (English-only exam) but trivial if you ever want Spanish study aids.
- **Question-level analytics** (e.g. "Q17 has a 15% correct rate — too hard?"): `ReadingAnswer` already carries `IsCorrect` and `AnsweredAt`. Analytics is a SQL view away.

The shape that makes this hold up is the separation of **paper structure** (hierarchy: paper → parts → texts → questions) from **attempt data** (user's answers) from **grading rules** (canonical scoring). Change one, the others don't move.
