# Content Upload & Authoring — Implementation Plan

> **Status**: planning document, ready for implementation. Companion to
> [`AGENTS.md`](../AGENTS.md), [`SCORING.md`](SCORING.md), [`RULEBOOKS.md`](RULEBOOKS.md),
> and [`AI-USAGE-POLICY.md`](AI-USAGE-POLICY.md). Once approved, work proceeds
> as labelled slices the same way the AI Usage subsystem did.

---

## 1. Source-of-truth analysis (what your real content actually is)

I went through every file in `Project Real Content/` end-to-end. Here is what
the project's actual learning material looks like, normalised:

### 1.1 Listening (same for all professions)

Three samples. Each sample is one **paper** containing:

```
Listening Sample N/
├─ Audio N/
│   └─ Audio N.mp3                        ← single MP3 per paper, ~30–55 MB
├─ Listening Sample N Question-Paper.pdf  ← stimulus
├─ Listening Sample N Audio-Script.pdf    ← transcript
└─ Listening Sample N Answer-Key.pdf      ← canonical answers
```

**Implications:**
- One audio + three PDFs per paper. Audio sizes (35–55 MB) are well above the
  current 25 MB `StorageOptions.MaxUploadBytes` and 10 MB `MediaEndpoints` limit.
- Profession-agnostic. One row in DB, presented to all medicine / nursing /
  dentistry / etc. learners.
- Three paper-level documents (question / script / answer) plus one media
  asset. Either we stitch them into a multi-part `ContentItem.DetailJson`
  blob, or we treat them as first-class **paper assets** (recommended; see §3).

### 1.2 Reading (same for all professions)

Three samples. Two PDFs per sample:

```
Reading Sample N/
├─ Part A Reading.pdf       ← Part A (expeditious reading, 4 medical texts on one topic)
└─ Reading Part B&C.pdf     ← Part B (short extracts from healthcare contexts) + Part C (long passages)
```

**Implications:**
- Profession-agnostic.
- Two-document structure. Part A and B+C are scored differently — we will need
  per-part metadata (timing, max raw score, scoring grid) on the same paper.

### 1.3 Speaking (profession-aware: this folder is medicine)

Six role-play **cards** + two cross-cutting documents + one rulebook:

```
Speaking_/
├─ Speaking Assessment Criteria (same for all professions).pdf
├─ Speaking Intro Questions - Warm Up Questions (same for all professions).pdf
├─ Speaking Rulebook (Medicine Only)/OET_Speaking_Rulebook_v2.pdf
├─ Card 1 (Already known Pt)/1.pdf
├─ Card 2 (Follow up Visit)/2.pdf
├─ Card 3 (Follow up visit)/3.pdf
├─ Card 4 (Examination Card) MOST IMPORTANT TYPE/4.pdf
├─ Card 5 (First visit - Emergency Card)/5.pdf
└─ Card 6 (First Visit)/Card 6.pdf
```

**Implications:**
- Two **content kinds** under "Speaking": role-play cards (per-profession) and
  reference docs (cross-cutting). Plus a rulebook (already handled by the
  existing rulebook loader).
- **Card type taxonomy** is explicit in the filenames: `already_known_pt`,
  `follow_up_visit`, `examination`, `first_visit_emergency`, `first_visit_routine`.
  Already present in `AiGroundingContext.CardType` — must align.
- "MOST IMPORTANT TYPE" is editorial weighting; we need a `priority`/`weight`
  field admins can flag.

### 1.4 Writing (profession-aware: this folder is medicine)

Six writing tasks + one rulebook. Each task is a **case-notes + answer-sheet pair**:

```
Writing_/
├─ Writing RuleBook (Medicine only)/OET_Writing_Rulebook_FINAL.pdf
├─ Writing 1 (Routine Referral)/
│   ├─ Ms Sarah Miller - Case Notes (...).pdf       ← stimulus
│   └─ Ms Sarah Miller - Answer Sheet (...).pdf     ← model answer
├─ Writing 2 (Referral to non medical profession ...)/
│   ├─ <case notes>.pdf
│   └─ <answer sheet>.pdf
... etc
```

**Implications:**
- **Letter-type taxonomy** is explicit in the filenames: `routine_referral`,
  `urgent_referral`, `non_medical_referral`, `update_discharge`,
  `update_referral_specialist_to_gp`, `transfer_letter`. Aligns with
  `AiGroundingContext.LetterType`.
- Two-document pairing per task: case-notes (stimulus) + answer-sheet (model
  answer). Currently `ContentItem.CaseNotes` is a string and `ModelAnswerJson`
  is a JSON blob — neither models a PDF properly.

### 1.5 Cross-cutting / shared

- `Scoring System.txt` — already canonicalised in `lib/scoring.ts` /
  `OetScoring`. Not uploaded; reference.
- `OET_Speaking_Rulebook_v2.pdf`, `OET_Writing_Rulebook_FINAL.pdf` — already
  loaded by `RulebookLoader`. Not user-uploadable; loaded from
  `rulebooks/**/rulebook.v*.json`.
- `Create Similar Table Formats for Results to show to Candidates/*.jpg` —
  these are screenshots of OET official result tables, used as **reference
  images for designing the result-card UI**, not learner content. Excluded
  from the upload pipeline — they belong as design assets in `public/`.

### 1.6 What this tells us about the data model gap

Your existing `ContentItem` was clearly designed around a **single-document
practice item** (one writing task, one reading passage). Your real content
has three patterns it doesn't model cleanly:

| Pattern | Real-world example | Current model fit |
|---|---|---|
| Single audio + multiple companion PDFs | Listening paper | ❌ no first-class media linkage; audio doesn't fit `MediaAsset` audio types either |
| Multi-part single paper with shared scoring | Reading Part A + B&C | ❌ no part/section concept |
| Stimulus PDF + model-answer PDF pair | Writing case-notes + answer sheet | 🟡 storable as JSON blobs but not properly auditable, not previewable, no separate access control |
| Profession-scoped vs cross-cutting same content | Speaking rulebook (medicine) vs Speaking criteria (all) | 🟡 `ProfessionId` exists but no notion of "applies to all" |

This is the real reason we need a deliberate content-upload subsystem rather
than just "upload PDFs to MediaAsset."

---

## 2. What the upload subsystem must deliver

Eight functional requirements, derived from §1:

1. **Multi-asset papers** — one logical "paper" can bundle 1 audio + N PDFs +
   metadata, atomically created/updated/published.
2. **Part-aware structure** — Reading Part A vs B+C, Listening Sections 1-4,
   Writing case-notes vs answer-sheet — all addressable individually.
3. **Profession scoping** — "applies to all professions" is a first-class flag,
   not absence of `ProfessionId`. Required for the explicit Listening/Reading
   "same for all professions" semantics from your folders.
4. **Versioning & rollback** — admins must be able to upload v2 of a paper
   without breaking learners mid-attempt; publish workflow with revision
   history (`ContentRevision` already exists; reuse).
5. **Bulk import** — admins must be able to point at a folder structure
   matching §1 and have the system propose a draft import they approve.
6. **Large-file uploads** — 50 MB+ MP3s reliably; resumable when possible.
7. **Authoring console** — drag-drop, preview, validation, "missing
   answer-key" detection, publish gate that requires QA sign-off.
8. **Discoverability for learners** — same hierarchy through `ContentPackage`
   + `PackageContentRule` so paid tiers see what they paid for.

---

## 3. Proposed data model

I will **extend, not replace** the existing entities. Here's the shape.

### 3.1 New: `ContentPaper` (the missing parent)

A "paper" is the user-facing unit a learner picks: "Listening Sample 1",
"Writing 3 (Urgent Referral)", "Speaking Card 4". `ContentItem` stays as the
sub-unit (one part of a paper).

```csharp
public class ContentPaper
{
    public string Id { get; set; }                 // pap_listening_2026_01
    public string SubtestCode { get; set; }        // listening | reading | writing | speaking
    public string Title { get; set; }              // "Listening Sample 1"
    public string Slug { get; set; }               // listening-sample-1
    public string? ProfessionId { get; set; }      // null = "all professions"
    public bool AppliesToAllProfessions { get; set; }
    public string Difficulty { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public ContentStatus Status { get; set; }      // draft|in_review|published|archived
    public string? PublishedRevisionId { get; set; }
    public string? CardType { get; set; }          // speaking only: routine_referral, examination, ...
    public string? LetterType { get; set; }        // writing only: routine_referral, urgent_referral, ...
    public int Priority { get; set; }              // editorial weight ("MOST IMPORTANT TYPE")
    public string TagsCsv { get; set; }            // free-form, e.g. "dermatology,acne"
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedByAdminId { get; set; }
}
```

Indexed by `(SubtestCode, Status)`, `(ProfessionId, SubtestCode)`,
`(CardType)`, `(LetterType)`.

### 3.2 New: `ContentPaperAsset` (the typed slot)

One row per file attached to a paper, with a **role**:

```csharp
public enum PaperAssetRole
{
    Audio = 0,              // Listening MP3
    QuestionPaper = 1,      // PDF stimulus (any subtest)
    AudioScript = 2,        // Listening transcript
    AnswerKey = 3,          // Listening / Reading
    CaseNotes = 4,          // Writing stimulus
    ModelAnswer = 5,        // Writing reference answer
    RoleCard = 6,           // Speaking role-play card
    AssessmentCriteria = 7, // Cross-cutting reference
    WarmUpQuestions = 8,    // Speaking warm-up
    Supplementary = 99,     // anything else
}

public class ContentPaperAsset
{
    public string Id { get; set; }
    public string PaperId { get; set; }
    public PaperAssetRole Role { get; set; }
    public string? Part { get; set; }              // "A", "B+C", "Section1" — nullable
    public string MediaAssetId { get; set; }       // FK to existing MediaAsset
    public string? Title { get; set; }             // optional override
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }            // the "main" asset for its role
    public DateTimeOffset CreatedAt { get; set; }
    public ContentPaper Paper { get; set; } = null!;
    public MediaAsset MediaAsset { get; set; } = null!;
}
```

Indexed by `(PaperId, Role)`, with a **unique constraint** on
`(PaperId, Role, Part, IsPrimary=true)` so we can never have two "primary
audio" or two "primary question paper" for the same part.

### 3.3 Extend: `MediaAsset`

Three small additions:

- **Audio support**: add `audio/mpeg`, `audio/mp4` to allowed types and bump
  `StorageOptions.MaxUploadBytes` per role. Tracked via per-role limits, not
  one global cap (Listening MP3 needs 100 MB ceiling; admin avatars can stay
  at 5 MB).
- **Checksums**: `Sha256` field for dedup detection across uploads. Stop
  storing the same 50 MB MP3 twice.
- **Processing state**: extend `MediaAssetStatus` enum to track the
  background pipeline (uploaded → scanning → processed → ready / failed).

### 3.4 Extend: `ContentRevision`

Already exists with `ContentItemId`. Generalise so it can also reference
`PaperId`. Either:
- (a) add `PaperId` nullable + index, or
- (b) rename the FK column generically to `TargetId` + `TargetType`.

I recommend **(a)** — additive, safer for the existing data.

### 3.5 Reuse: `ContentPublishRequest` + `AuditEvent`

Already in place. Plug `ContentPaper` into the existing multi-stage approval
workflow (editor review → publisher approval). No new tables needed.

---

## 4. Storage strategy

Three distinct storage zones, each with different durability/access rules:

| Zone | What lives here | Backend | Access |
|---|---|---|---|
| **`uploads/staging/`** | In-progress chunked uploads | Local disk (existing) | Admin only, 24h TTL cleanup job |
| **`uploads/published/`** | Production paper assets, content-addressed by `Sha256` | Local disk now → S3/R2 later via `IFileStorage` interface | Signed URL, 5-min TTL |
| **`uploads/derived/`** | Generated thumbnails, waveforms, page previews | Same as published | Cacheable, longer TTL |

`MediaStorageService` already abstracts the local-disk path. Wrap it behind
an `IFileStorage` interface so the S3 swap is a one-line DI change later, not
a rewrite. **Do not** introduce S3 yet — your VPS deploy is fine for current
scale, and adding it now means new failure modes for no benefit.

Concrete path conventions:

```
uploads/
├─ staging/{adminId}/{uploadSessionId}/{partNumber}.bin
├─ published/{sha256[0..2]}/{sha256[2..4]}/{sha256}.{ext}
└─ derived/{mediaAssetId}/thumb-{size}.{ext}
```

Content-addressing via SHA-256 means two papers referencing the same audio
share one file. Cheap dedup, no orphan-tracking complexity.

---

## 5. Upload protocol

Three upload modes. Admin picks based on file size / source:

### 5.1 Direct upload (existing) — for ≤ 5 MB PDFs and images

`POST /v1/admin/papers/{paperId}/assets` (multipart). Reuses
`MediaEndpoints.HandleUploadAsync` shape with adjusted limits per role.

### 5.2 Chunked / resumable upload — for ≤ 100 MB MP3s

Three endpoints, idempotent, designed so a dropped connection at 80% does not
lose work:

```
POST   /v1/admin/uploads                  → { uploadId, chunkSizeBytes, expiresAt }
PUT    /v1/admin/uploads/{uploadId}/parts/{partNumber}    (binary body)
POST   /v1/admin/uploads/{uploadId}/complete              → { mediaAssetId, sha256, sizeBytes }
DELETE /v1/admin/uploads/{uploadId}                       (cleanup)
```

Server holds parts in `staging/`, computes SHA-256 streamingly during
`complete`, atomically moves to `published/{sha256}.ext`, deletes staging,
returns a real `MediaAsset` row. Existing `UploadSession` entity already has
the right shape — extend rather than create new.

### 5.3 Folder import — for the bootstrap of your existing PDF library

Admin uploads a ZIP of one of the §1 folder structures. Server:

1. Unzips into `staging/`
2. Walks the tree applying a **convention parser** (folder name → `Sample N`,
   subfolder `Audio N` → role=Audio, file ending `Answer-Key.pdf` → role=AnswerKey,
   etc.)
3. Produces a **proposed import manifest** as JSON.
4. Admin reviews, edits titles/types, and clicks "Approve import".
5. Server creates `ContentPaper` + `ContentPaperAsset` + `MediaAsset` rows
   atomically, attaching the staged files via SHA-256 dedup.
6. Status starts as `draft` — nothing visible to learners until the existing
   `ContentPublishRequest` flow approves it.

This is the path that gets your current PDF library into the system in a
single afternoon instead of sample-by-sample.

---

## 6. Convention parser (the bootstrapping shortcut)

The parser is a deterministic regex/keyword matcher specific to the OET file
naming you already use. Documented so admins know what filenames are
auto-recognised:

| Filename pattern | Detected role | Detected metadata |
|---|---|---|
| `*.mp3` inside `Audio*` folder | `Audio` | n/a |
| `*Question-Paper.pdf` / `*Question Paper.pdf` | `QuestionPaper` | n/a |
| `*Audio-Script.pdf` | `AudioScript` | n/a |
| `*Answer-Key.pdf` / `*Answers.pdf` | `AnswerKey` | n/a |
| `*Case Notes*.pdf` | `CaseNotes` | extract patient name from filename |
| `*Answer Sheet*.pdf` | `ModelAnswer` | n/a |
| Folder `Writing N (Urgent Referral)` | paper | `LetterType=urgent_referral` |
| Folder `Card N (Examination Card)` | paper | `CardType=examination` |
| Filename `Reading Part A.pdf` | `QuestionPaper`, `Part="A"` | n/a |
| Filename `Reading Part B&C.pdf` | `QuestionPaper`, `Part="B+C"` | n/a |
| Folder name contains `(Medicine only)` | paper | `ProfessionId=medicine` |
| Folder name contains `same for all professions` | paper | `AppliesToAllProfessions=true` |

Anything not matched is flagged in the manifest as `Supplementary` — admin
manually classifies before approval.

---

## 7. Authoring UI (admin)

One Content Hub page `/admin/content/papers` with three views:

### 7.1 List view (`/admin/content/papers`)
- Filters: subtest, profession, status, card-type, letter-type, search.
- Reuses existing `DataTable`, `FilterBar`, `Badge`, `EmptyState`.
- Bulk actions: archive, reassign profession, change status.
- Quick "+ New Paper" + "Bulk Import" buttons.

### 7.2 Editor view (`/admin/content/papers/[paperId]`)
- Header: title, subtest, profession (dropdown including "All professions"),
  difficulty, status badge, publish/archive actions.
- Tabs:
  1. **Assets** — drag-drop zone per role slot. Each slot shows thumbnail/
     waveform/page preview, "Replace" button, and a "Set primary" toggle.
     Detects "missing required asset" (e.g. Listening paper with no answer
     key) and blocks publish.
  2. **Metadata** — `CardType` / `LetterType` dropdowns (only shown for
     speaking/writing), tags, priority, scenario type, criteria focus.
  3. **Preview** — rendered as the learner will see it, calls the same
     learner endpoints in dry-run mode.
  4. **Revisions** — full revision history via existing `ContentRevision`.
  5. **Publish workflow** — connects to existing `ContentPublishRequest`.

### 7.3 Bulk import view (`/admin/content/papers/import`)
- Upload ZIP → server parses → manifest review table.
- Each row: detected file → suggested role → editable fields → diff against
  existing papers (so a re-import doesn't duplicate).
- "Approve all" or per-row approve.

All three views go behind `AdminContentWrite` policy (existing) plus the new
publish flow uses `AdminContentPublish`.

---

## 8. Learner-facing changes

Mostly **nothing changes** at the route level — `/listening`, `/reading`,
`/writing`, `/speaking` already exist. The learner data layer changes:

- Listening flow now fetches `ContentPaper` instead of `ContentItem`. The
  `Audio` asset URL goes to the existing player; the `QuestionPaper` PDF
  becomes an inline PDF viewer; `Answer-Key` and `Audio-Script` are gated
  behind the "Show after submit" toggle (already a learner UX pattern in the
  existing diagnostic results screen).
- Writing flow shows `CaseNotes` PDF as the stimulus and `ModelAnswer` PDF
  appears post-submission.
- Speaking shows the `RoleCard` PDF + warm-up questions PDF.
- Reading shows Part A first, Part B+C second, with separate timers — the
  `Part` field on `ContentPaperAsset` drives this.

The `ContentPackage` + `PackageContentRule` entities already control
"who sees what" and don't need changes — they just point at `ContentPaper`
IDs the same way they currently point at `ContentItem` IDs.

---

## 9. AI integration touchpoints

The grounded AI gateway already accepts `LetterType` and `CardType` in
`AiGroundingContext`. With papers carrying these fields explicitly:

- Writing grading prompts can be built directly from `ContentPaper.LetterType`
  + the case-notes PDF text (extracted at upload time → stored in
  `ContentPaper.ExtractedText` for prompt assembly).
- Speaking grading prompts pick up `ContentPaper.CardType` automatically.
- Listening / Reading don't currently use AI grading (objective scoring); no
  AI hooks needed beyond the existing summarisation/explanation features
  which already work paper-agnostic.

PDF text extraction is a one-time async job that runs after upload and
populates `ContentPaper.ExtractedTextJson` + `ContentPaperAsset.ExtractedText`.
Use **PdfPig** (MIT-licensed, pure C#, no native deps) — preferred over
iTextSharp's GPL ambiguity.

---

## 10. Security & validation

Every upload path enforces:

1. **Allow-list MIME types and extensions** per role (audio role accepts only
   `audio/mpeg|audio/mp4`; PDF roles only `application/pdf`).
2. **Magic-byte verification** — content-type sniffing, not trust the
   client header. PDFs must start `%PDF-`; MP3s must have ID3 or sync frame.
3. **Per-role size limits** — `Audio` 100 MB, PDF roles 25 MB, image roles
   5 MB. Configurable via `StorageOptions`.
4. **Virus scan hook** — `IUploadScanner` interface with a no-op default and
   an optional ClamAV-via-subprocess implementation for production.
5. **Path traversal** — already covered in `MediaStorageService.ResolvePath`,
   keep that behaviour.
6. **Rate limit** — admin write rate limit already exists (`PerUserWrite`).
   Bulk import gets its own tighter limiter (1 import per 5 min per admin).
7. **Audit** — every paper mutation, asset upload, and publish action emits
   an `AuditEvent` row.
8. **Permissions** — `AdminContentWrite` to draft/edit, `AdminContentPublish`
   to publish. Already wired.

---

## 11. Compliance & retention

- **No learner PII in stimulus content** — Writing case-notes use synthetic
  patients ("Ms Sarah Miller"), fine. Audit at upload time: a content
  reviewer must confirm the paper contains no real patient data before
  publish.
- **Copyright provenance** — add `ContentPaper.SourceProvenance` (e.g.
  "Source: Project Real Content folder supplied by the project owner.
  Internal practice use only. Redistribution requires rights review.").
  Required field before publish. Surfaces in admin UI as a recommended default
  with manual override.
- **Retention** — published papers kept indefinitely (educational
  reference). Drafts auto-archived after 90 days of inactivity. Staged
  uploads cleaned at 24h.

---

## 12. Implementation slices

Same shape as the AI Usage subsystem — small, mergeable, independently
revertable. Each slice is roughly one PR.

### Slice 1 — Domain & migrations *(no UI yet)*
- Add `ContentPaper`, `ContentPaperAsset`, `PaperAssetRole` enum.
- Extend `MediaAsset` with `Sha256`, audio MIME types, processing state.
- Extend `ContentRevision` with nullable `PaperId`.
- Hand-written migration (matching the project's established style).
- Reference data migration: zero rows. Existing `ContentItem` untouched.
- Tests: entity round-trip, unique constraint on primary asset per role.

### Slice 2 — Storage abstraction + chunked upload
- Introduce `IFileStorage` interface in front of `MediaStorageService`.
- Add SHA-256 streaming hash + content-addressed `published/` layout.
- Implement chunked upload endpoints + extend `UploadSession` entity.
- Per-role size config in `StorageOptions`.
- Background staging-cleanup hosted service (24h TTL).
- Tests: chunked happy path, resume after interrupted part, dedup on
  identical SHA.

### Slice 3 — Paper CRUD endpoints + admin list page
- `GET/POST/PUT/DELETE /v1/admin/papers` + `/papers/{id}/assets`.
- New `/admin/content/papers` list page reusing existing CMS components.
- Audit + permission gating on every mutation.
- Tests: CRUD, permission denial, audit event written.

### Slice 4 — Paper editor + asset management UI
- `/admin/content/papers/[paperId]` editor with the four tabs.
- Drag-drop upload using the chunked protocol.
- Required-role validation (Listening must have audio + question paper +
  answer key before publish).
- Preview tab that renders the learner view.
- Tests: editor renders all roles, missing-asset block fires, publish
  workflow integration.

### Slice 5 — Convention parser + bulk import
- Parser implementation matching §6 table.
- ZIP upload endpoint with manifest generation.
- `/admin/content/papers/import` review-and-approve UI.
- Idempotency: re-uploading the same SHA against an existing paper updates
  rather than duplicates.
- Tests: parser unit tests with fixture filenames from §1, end-to-end import
  of a fixture ZIP.

### Slice 6 — Learner data-layer migration
- Add `ContentPaper` accessors to learner endpoints (Listening / Reading /
  Writing / Speaking).
- Backwards compat: existing `ContentItem`-based flows keep working; new
  `ContentPaper` flows take precedence when both exist for a given subtest.
- Tests: learner flows render real-content fixtures end-to-end.

### Slice 7 — PDF text extraction + AI integration
- PdfPig package addition.
- Background job (`BackgroundJobItem` already exists) that extracts text
  per asset and populates the paper.
- Wire `LetterType` / `CardType` from `ContentPaper` into AI gateway calls.
- Tests: extraction round-trip, AI grading uses paper-derived stimulus.

### Slice 8 — Hardening
- Magic-byte sniffing.
- ClamAV scanner interface.
- Admin bulk-import rate limit.
- Provenance required-field enforcement.
- Penetration review of upload endpoints.
- Load test: 50 concurrent 50 MB uploads, verify no leaks in `staging/`.

Each slice ships green: unit tests + frontend type-check + lint. No slice
breaks an existing learner flow.

---

## 13. Operator handoff items

Items that genuinely require operator action, not code:

1. **Convert your `Project Real Content/` folder into a ZIP per subtest** so
   Slice 5's import works on day one. Leave folder names exactly as they are
   — the parser keys off them.
2. **Decide the storage backend for production** before Slice 8: stay on the
   VPS local disk, or move to S3/R2. Either works; the abstraction is in
   place for both.
3. **Author the provenance copy** for each paper so QA can confirm the
   recommended rights wording before approving publish.
4. **Confirm the result-table screenshots** in `Project Real Content/Create
   Similar Table Formats…` are reference only (my read) — if they need to
   appear inside the app, that is a separate UI design task, not an upload
   pipeline item.

---

## 14. Non-goals (deliberately excluded)

To keep the plan honest, here is what this subsystem does **not** try to do:

- **Auto-grade Listening / Reading from PDF**. Objective MCQ grading needs
  per-question structured data, which PDFs don't carry. Capturing that is a
  separate Q&A authoring epic, not part of the upload pipeline.
- **DRM on the PDFs**. Standard signed URLs + CSP are sufficient; PDF DRM is
  expensive, fragile, and easily bypassed via screenshot.
- **WYSIWYG PDF editor**. Out of scope; admins re-upload corrected PDFs.
- **Live transcription of MP3s**. Audio-script PDFs already exist for every
  paper. Adding Whisper-style transcription is a separate AI epic if you
  ever want auto-captioning for accessibility.
- **Replacing the rulebook loader**. Rulebooks stay file-based per
  `RULEBOOKS.md`. They are not "content papers"; conflating them would
  break the grounding invariant.

---

## 15. Why this design holds up under change

Future-proofing isn't a buzzword; here's the specific evidence:

- **New subtest type** (e.g. OET ever adds a "Vocabulary" subtest): add a
  `SubtestCode` value, add the relevant `PaperAssetRole`s, parser learns one
  more pattern. No schema migration.
- **New profession** (e.g. pharmacy, optometry): add to `ProfessionReference`
  table; existing papers with `AppliesToAllProfessions=true` automatically
  surface. No upload changes.
- **Storage migration to S3**: implement `IFileStorage` against AWS SDK,
  swap DI registration, run a one-time copy job. No endpoint changes, no
  client changes.
- **New file type** (e.g. video for speaking model answers): add a role,
  extend MIME allow-list, add per-role size cap. No paper-model changes.
- **Bulk content licensing deal** (e.g. you license 200 papers from a
  publisher): bulk import handles it; provenance field captures the deal.
- **AI grading expansion** (e.g. you add Listening AI grading): the paper
  already has structured per-part metadata, so a new feature code in
  `AiFeatureCodes` + a grader service is all that's needed.

The shape that makes this hold up is the separation of `ContentPaper`
(curatorial unit) from `ContentPaperAsset` (file slot) from `MediaAsset`
(physical file). Anything you'll want to do later changes one of those three
layers, not all of them.
