# Recalls Data Entry Plan - Zero Hallucination Workflow

> Status: Draft for review
> Date: 2026-05-03
> Scope: Professional data-entry workflow for Recalls source documents and vocabulary recall banks, with source preservation, exact structure retention, and zero invented content.

Execution packet:

- `docs/recalls-data-entry/PRD.md`
- `docs/recalls-data-entry/PROGRESS.md`
- `docs/recalls-data-entry/source-register.template.csv`
- `docs/recalls-data-entry/master-manifest.template.csv`
- `docs/recalls-data-entry/validation-checklist.md`
- `docs/recalls-data-entry/reviewer-signoff-workflow.md`
- `docs/recalls-data-entry/system-hardening-backlog.md`

## 1. Goal

Enter recall data into the OET platform with maximum fidelity to the supplied source documents. The work must preserve source structure, avoid inferred or AI-created facts, keep every row traceable to a source location, and make errors detectable before anything becomes learner-visible.

This plan treats the data-entry job as a controlled content-ingestion project, not as manual copy-paste into production.

## 2. Documents and System Areas Analyzed

Repository documents and code reviewed:

- `docs/RECALLS-MODULE-PLAN.md` - Recalls architecture, learner surface, admin direction, listen-and-type behavior, TTS and future admin surface.
- `docs/VOCABULARY-MODULE.md` - canonical vocabulary entity contract, publish gate, admin import expectation, AI gateway rules.
- `docs/CONTENT-UPLOAD-PLAN.md` - content provenance, source-file import discipline, `ContentPaper` / `ContentPaperAsset` / `MediaAsset` separation, publish workflow, audit requirements.
- `app/admin/recalls/bulk-upload/page.tsx` - current admin CSV entry surface.
- `lib/api.ts` - current `RecallsBulkUploadRow` and `adminBulkUploadRecalls()` client contract.
- `backend/src/OetLearner.Api/Endpoints/RecallsEndpoints.cs` - legacy admin upload endpoint at `/v1/admin/recalls/bulk-upload`, now disabled for production safety.
- `backend/src/OetLearner.Api/Services/Recalls/RecallsService.cs` - current bulk upload behavior.
- `backend/src/OetLearner.Api/Domain/VocabularyEntities.cs` - canonical `VocabularyTerm` storage fields and limits.

Important limitation: no extra chat attachments were available in this session. If the actual recall source documents are separate files outside the repo, they must be supplied or placed in a fixed source folder before data entry begins.

## 3. Current System Reality

### 3.1 Preferred existing Vocabulary import path

The repository already has a safer admin Vocabulary import path. It should be the preferred current route for production-quality Recalls vocabulary entry because Recalls is backed by `VocabularyTerm` rows.

Existing endpoints:

```text
POST /v1/admin/vocabulary/import/preview?importBatchId=<batch>
POST /v1/admin/vocabulary/import?dryRun=true&importBatchId=<batch>
POST /v1/admin/vocabulary/import?dryRun=false&importBatchId=<batch>
GET  /v1/admin/vocabulary/import/batches/<batch>
GET  /v1/admin/vocabulary/import/batches/<batch>/export
POST /v1/admin/vocabulary/import/batches/<batch>/rollback
```

Supported CSV header aliases include:

```text
term
definition
exampleSentence | example
category
difficulty
professionId | profession
examTypeCode | examType
ipaPronunciation | ipa | pronunciation
audioUrl | audio
contextNotes | context
synonyms
sourceProvenance | provenance
```

Current backend behavior for this path:

- Preview returns valid, invalid, duplicate, row-level error, and warning counts.
- Preview reports same-file duplicate keys and existing database conflicts by normalized `(term, examTypeCode, professionId)` before dry run.
- Preview, dry run, and commit accept an `importBatchId`; if omitted, the backend generates one.
- Dry-run is supported and a clean dry run records a server-side confirmation tied to the import batch ID and uploaded file hash.
- Commit requires `dryRun=false` plus a matching clean dry-run confirmation for the same batch and file; omitted `dryRun` defaults to non-committing dry-run behavior.
- New rows are inserted as `Status = draft`.
- `SourceProvenance` is required per row and is stamped with `batch=<importBatchId>;` for committed imports.
- Duplicate existing terms are skipped rather than overwritten.
- Admins can fetch a batch summary and download a batch CSV export from the immutable commit ledger.
- Rollback is archive-only for draft rows in the commit ledger; active rows are blocked and physical delete is disabled.
- An audit entry is written for committed imports.

This path is not perfect, but it is safer than the dedicated Recalls bulk upload for high-accuracy work.

### 3.2 Legacy Recalls bulk upload path

The legacy Recalls bulk upload endpoint is disabled for production safety. `POST /v1/admin/recalls/bulk-upload` returns `409 legacy_recalls_import_disabled` and directs operators to the safe Vocabulary preview/dry-run/import workflow.

Historical behavior of the disabled route is documented here so it is not accidentally reintroduced without controls.

Current supported CSV columns:

```text
term, definition, exampleSentence, category, difficulty,
ipa, americanSpelling, synonymsCsv, examTypeCode, professionId
```

Required fields:

```text
term, definition
```

Current backend behavior for the Recalls path:

- Upsert key is `(Term, ExamTypeCode, ProfessionId)`.
- Empty `ExamTypeCode` defaults to `OET`.
- Empty `Category` defaults to `general`.
- Empty `Difficulty` defaults to `medium`.
- New imported rows are inserted immediately with `Status = active`.
- `SourceProvenance` is hard-coded to `admin:bulk-csv`.
- Synonyms are stored as a JSON array derived from `synonymsCsv`.
- Existing rows are updated, not duplicated, when the key matches.

Production warning: this historical path could overwrite definitions and make rows active immediately. It must stay disabled unless rebuilt with preview, conflict review, draft status, source trace fields, audit, reconciliation, and batch rollback controls.

### 3.3 Canonical vocabulary fields

`VocabularyTerm` can hold more than the current CSV importer exposes:

```text
Id, Term, Definition, ExampleSentence, ContextNotes, ExamTypeCode,
ProfessionId, Category, Difficulty, IpaPronunciation, AudioUrl,
AudioSlowUrl, AudioSentenceUrl, AmericanSpelling, AudioMediaAssetId,
ImageUrl, SynonymsJson, CollocationsJson, RelatedTermsJson,
SourceProvenance, Status, CreatedAt, UpdatedAt
```

Relevant length limits from the entity:

```text
Term: 128
Definition: 1024
ExampleSentence: 2048
ContextNotes: 1024
ExamTypeCode: 16
ProfessionId: 32
Category: 64
Difficulty: 16
IpaPronunciation: 64
AmericanSpelling: 128
SourceProvenance: 512
Status: 16
```

### 3.4 Remaining gap between a perfect workflow and current importers

The current Vocabulary importer is much closer to the required workflow than the Recalls uploader, but neither path alone is enough for a 100% accuracy, zero-hallucination production ingestion job.

Main remaining gaps:

- No durable per-row source document ID, page, row, screenshot, or quote in the live `VocabularyTerm` schema.
- `SourceProvenance` is limited to 512 characters, so it can only store a compact source pointer, not the full evidence record.
- The disabled legacy Recalls uploader used defaults like `general` and `medium`, which can hide missing source data.
- The Recalls uploader has minimal frontend CSV parsing and is disabled for production use.
- The safe Vocabulary backend importer now parses full CSV records, including quoted commas, escaped quotes, UTF-8 text, and quoted multiline fields.
- Batch IDs, batch export, clean dry-run confirmation, commit ledgering, draft archive rollback, manifest reconciliation, and frontend reconciliation upload now exist for the safe Vocabulary importer.
- No two-person verification workflow.
- Some useful fields are not importable today: durable source trace fields and any future common-mistake or similar-sounding metadata.

Conclusion: for a high-stakes data-entry job, use a staged master spreadsheet/manifest first, then import through the admin Vocabulary preview/dry-run/draft path. Do not upload directly from raw source documents through `/admin/recalls/bulk-upload` for production.

## 4. Zero Hallucination Contract

The following rules are mandatory:

1. Every data value must be one of:
   - exact source transcription,
   - deterministic normalization of source text,
   - explicit human editorial classification,
   - system metadata such as checksum or import batch ID.
2. AI must not invent missing definitions, examples, IPA, synonyms, categories, or difficulty.
3. If a value is absent from the source, mark it as absent in staging. Do not silently fill it.
4. If a curator adds a classification that is not written in the source, it must be marked as editorial, with reviewer initials and date.
5. Preserve the original source quote and source location for every row.
6. No row becomes learner-visible until it passes automated validation and human review.
7. All rejected or uncertain rows remain in the manifest with a reason. They are not deleted.

## 5. Source Preservation Model

Before transcription, create a source register.

Required source register fields:

```text
sourceDocumentId
sourceFileName
sourceFilePath
sha256
archivePath
fileType
sourceKind
subtest
professionId
appliesToAllProfessions
sourceDateOrVersion
ownerOrSupplier
rightsStatus
notes
```

Example `sourceDocumentId` format:

```text
recalls-src-2026-05-03-001
```

The register must be frozen before row entry begins. If a source file changes, it receives a new checksum and new source document ID.

Durable archive rule:

- Store the source register, master manifest, reviewed import payload, validation report, and backend import result in a controlled batch archive.
- The archive can live in private project storage, a restricted content archive, or another agreed location, but it must not be a temporary chat transcript.
- If source files are copyrighted, sensitive, or too large, do not commit them to Git; commit only the plan/template and keep the private archive path in the batch register.
- Each imported row's `SourceProvenance` should carry a compact pointer such as `batch=<id>;src=<sourceDocumentId>;p=<page>;row=<row>;manifest=<manifestSha256-prefix>`.
- The master manifest remains the complete evidence record.
- The `manifestSha256` value is computed at batch close/sign-off, not during source-register freeze.

## 6. Master Data-Entry Schema

Use a master staging CSV or spreadsheet with a wider schema than the current importer. The current importer receives only the supported subset after QA.

Recommended master schema:

```text
importBatchId
rowId
sourceDocumentId
sourceFileName
sourcePage
sourceSection
sourceRowOrLine
sourceQuote
term
canonicalBritishTerm
americanSpelling
definition
exampleSentence
category
oetPart
difficulty
ipa
synonymsCsv
commonMistakesCsv
similarSoundingCsv
collocationsCsv
relatedTermsCsv
contextNotes
examTypeCode
professionId
appliesToAllProfessions
entryOperator
entryTimestamp
reviewer
reviewTimestamp
reviewStatus
editorialFieldsCsv
uncertaintyFlag
uncertaintyReason
finalImportAction
postImportVocabularyTermId
```

Field policy:

- `term` / `canonicalBritishTerm`: exact source spelling first; British spelling is canonical only when the source or project rule explicitly supports it.
- `definition`: exact source definition when provided. If absent, do not create one unless a subject-matter reviewer writes and marks it as editorial.
- `exampleSentence`: source sentence only, unless editorially authored and marked.
- `category`, `difficulty`, `oetPart`: controlled values; if not in the source, classify only through explicit reviewer decision.
- `ipa`: source-only or verified specialist entry. Do not generate automatically.
- `synonymsCsv`, `collocationsCsv`, `relatedTermsCsv`: source-only unless reviewer-authored and marked.
- `sourceQuote`: exact source excerpt supporting the row.
- `uncertaintyFlag`: required whenever the source is illegible, ambiguous, incomplete, duplicated, or contradictory.

## 7. Data Entry Workflow

### Phase 1 - Intake and lock source files

1. Place all recall source documents in a fixed intake folder.
2. Compute SHA-256 for every file.
3. Create the source register.
4. Assign each document a stable `sourceDocumentId`.
5. Mark the source register as frozen for the batch.

Exit condition: every source file has a checksum, ID, and rights/provenance note.

### Phase 2 - Document structure analysis

For each source document, map its structure before extracting rows:

```text
document -> section -> table/list/block -> row -> field
```

Record whether the source is:

- table-based,
- list-based,
- PDF text,
- scanned image/OCR,
- spreadsheet,
- audio-related source,
- mixed format.

Exit condition: the source map explains where every data row will come from.

### Phase 3 - Double-key transcription

Use two independent passes:

1. Operator A enters rows into the master schema.
2. Operator B independently enters the same rows without seeing Operator A's output.
3. Automated diff compares both entries field-by-field.
4. Differences go to adjudication.

Exit condition: no unresolved diff remains between the two passes.

### Phase 4 - Normalization without invention

Allowed deterministic normalization:

- trim outer whitespace,
- normalize line endings,
- convert repeated internal spaces to one space where structure is not meaningful,
- preserve source capitalization in `sourceQuote`,
- apply CSV escaping,
- normalize JSON arrays for supported array fields.

Not allowed without reviewer marking:

- rewriting definitions,
- adding examples,
- changing medical terminology,
- inferring categories,
- assigning difficulty from intuition,
- generating IPA,
- adding synonyms from memory or AI.

Exit condition: every normalized field either traces to source text or is explicitly marked editorial.

### Phase 5 - Automated validation

Run validation before any upload.

Required checks:

- Required fields are present for rows intended to import.
- Field lengths do not exceed backend limits.
- `examTypeCode` fits 16 characters and defaults are explicit.
- `professionId` values exist in the platform or are approved new references.
- Duplicate key report for `(term, examTypeCode, professionId)`.
- CSV quoting passes RFC 4180-compatible parser checks.
- JSON array fields parse correctly.
- Source fields are populated: document ID, page/row/quote.
- No row marked `uncertaintyFlag=true` is importable.
- No row with unresolved review status is importable.

Exit condition: validation report has zero blocking errors.

### Phase 6 - Import preparation

Create two outputs:

1. Archive manifest: full master schema, retained permanently.
2. Import CSV or API payload: only fields supported by the selected importer.

Preferred admin Vocabulary import payload fields:

```text
term
definition
exampleSentence
category
difficulty
ipa
ipaPronunciation
audioUrl
audioSlowUrl
audioSentenceUrl
audioMediaAssetId
contextNotes
synonyms
collocations
relatedTerms
americanSpelling
sourceProvenance
examTypeCode
professionId
```

Recalls-specific note: the safe Vocabulary importer and admin create/edit UI now preserve `americanSpelling` / `VocabularyTerm.AmericanSpelling`, audio variants, `AudioMediaAssetId`, collocations, and related terms. Post-import reconciliation must still compare those fields before publish.

The legacy Recalls import payload is narrower:

```text
term
definition
exampleSentence
category
difficulty
ipa
americanSpelling
synonymsCsv
examTypeCode
professionId
```

Do not allow missing `category` or `difficulty` to silently become `general` or `medium` unless those values were explicitly approved as editorial defaults for the batch.

Exit condition: import payload row count equals the approved importable row count.

### Phase 7 - Dry run or staging import

Preferred current implementation before real entry:

- Choose one `importBatchId` and reuse it for preview, dry run, commit, export, reconciliation, and sign-off.
- Use `POST /v1/admin/vocabulary/import/preview?importBatchId=<batch>` for row-level validation.
- Use `POST /v1/admin/vocabulary/import?dryRun=true&importBatchId=<batch>` for dry-run counts.
- Review duplicates, invalid rows, warnings, and source-provenance compact pointers.
- Require a clean dry run and explicit admin approval to commit.
- After commit, use `GET /v1/admin/vocabulary/import/batches/<batch>`, `/export`, and `POST /v1/admin/vocabulary/import/batches/<batch>/reconcile` for reconciliation.
- If reconciliation fails before publish, use `POST /v1/admin/vocabulary/import/batches/<batch>/rollback` to archive draft rows. Active rows are not changed.

Built-in preview now reports composite duplicates by `(term, examTypeCode, professionId)` and existing-database conflicts before dry run. It is still not a substitute for full manifest validation, source evidence checks, unsupported-field review, and post-import reconciliation.

Recommended improvement before large entry:

- Add publish blocking for unresolved reconciliation mismatches.
- Keep `/v1/admin/recalls/bulk-upload` disabled unless it is rebuilt to the same standard.

If the source register, manifest, reviewer workflow, and archive are not ready yet, import only into a staging/dev database first.

Exit condition: dry-run/staging counts match the manifest exactly.

### Phase 8 - Commit import

Commit only after sign-off.

For the preferred Vocabulary importer:

- Upload through `/v1/admin/vocabulary/import?dryRun=false&importBatchId=<batch>` only after preview and dry-run match the manifest.
- The commit upload must be the same file that produced the latest clean dry-run confirmation.
- Keep inserted rows in `draft` until review/publish.
- Capture backend result: importBatchId, imported, skipped, duplicates, failedRows, errors.
- Treat any skipped row or backend error as a failed batch until adjudicated.

For the legacy Recalls importer:

- Do not use `/admin/recalls/bulk-upload` for production entry unless it has been hardened.
- Capture backend result: inserted, updated, skipped, errors.
- Treat any skipped row or backend error as a failed batch until adjudicated.

Exit condition: backend result has the expected inserted/updated counts and zero unexplained errors.

### Phase 9 - Post-import reconciliation

Export imported `VocabularyTerm` rows and compare them against the approved manifest.

Compare at least:

```text
Term
Definition
ExampleSentence
ContextNotes
Category
Difficulty
IpaPronunciation
AmericanSpelling
AudioUrl
AudioSlowUrl
AudioSentenceUrl
AudioMediaAssetId
SynonymsJson
CollocationsJson
RelatedTermsJson
ExamTypeCode
ProfessionId
Status
SourceProvenance
```

Exit condition: every imported field matches the approved manifest or has an approved transformation note.

### Phase 10 - Publish and learner visibility

For a strict workflow, rows should be imported as `draft` and then activated after review. The preferred Vocabulary importer already inserts new rows as `draft`. The legacy Recalls importer inserts as `active`, so one of these controls should be used if that path is ever used:

- implement draft import before production data entry, or
- import into staging first and promote only after reconciliation, or
- temporarily hide new batch rows behind an admin-controlled feature gate.

Exit condition: only reviewed, reconciled rows become visible to learners.

## 8. Recommended System Improvements Before Production Data Entry

These are strongly recommended before entering a large recall dataset.

1. Use the existing Vocabulary preview/dry-run/draft importer as the default production route.
2. Use the implemented import batch ID and audit log for every preview, dry run, commit, export, reconciliation, rollback, and sign-off.
3. Use the implemented archive-only draft-row batch rollback before learner release if reconciliation fails.
4. Add a durable source-evidence table or archive pointer strategy for source document ID, page, row, quote, and manifest checksum.
5. Use the implemented batch export for reconciliation and add automated mismatch reporting that includes `AmericanSpelling`, audio variants, `AudioMediaAssetId`, collocations, and related terms.
6. Add backend validation for allowed difficulty/category/profession values.
7. Add shared full RFC 4180 parsing if multi-line fields are needed; otherwise explicitly ban multi-line fields in import payloads.
8. Extend the Vocabulary import DTO to optionally ingest `collocationsCsv`, `relatedTermsCsv`, and compact source trace fields.
9. Keep `/v1/admin/recalls/bulk-upload` disabled before production use.
10. If hardening the Recalls uploader, add preview, dry-run, draft status, per-row `SourceProvenance`, duplicate/conflict review, and audit events.
11. Add automated post-import manifest diffing around the implemented batch export.

## 9. Accuracy Controls

Use all of these controls for a 100% accuracy target:

- Double-key transcription.
- Automated field-by-field diff.
- Source quote required per row.
- Source page/section/row required per row.
- Reviewer sign-off required per row.
- No uncertain row imported.
- No implicit defaults without explicit approval.
- Post-import database export compared against manifest.
- Batch-level rollback available before learner release.
- Final sampled UI verification after import.

Important: a 100% accuracy target is a process goal. It is only credible if every field is traceable and independently verified. It cannot be achieved by trusting a single manual entry pass.

## 10. Handling Ambiguity

When the source is unclear:

1. Mark `uncertaintyFlag=true`.
2. Copy the ambiguous source text into `sourceQuote`.
3. Add `uncertaintyReason`.
4. Do not import the row.
5. Send to subject-matter reviewer.
6. Record reviewer decision.
7. Import only after the decision is documented.

No one should guess.

## 11. Structure Preservation Rules

Preserve structure at three levels:

### Source structure

Keep the original documents unchanged, checksummed, and linked from every row.

### Logical structure

Retain source sections and row order using:

```text
sourceDocumentId
sourceSection
sourcePage
sourceRowOrLine
rowId
```

### Platform structure

Map only appropriate fields into `VocabularyTerm`. Do not flatten non-vocabulary documents into vocabulary rows unless they truly contain vocabulary recall items.

If the source document is a full paper, PDF, audio, answer key, or role card, it belongs in the `ContentPaper` / `ContentPaperAsset` pipeline, not the Recalls vocabulary importer.

## 12. Data Mapping Decision Tree

Use this decision tree for every source document:

1. Is it a list/table of terms, definitions, examples, spellings, or recall prompts?
   - Yes: stage as Recalls/Vocabulary rows.
   - No: continue.
2. Is it a PDF/audio/question paper/answer key/model answer/role card?
   - Yes: use `ContentPaper` / `ContentPaperAsset` workflow.
   - No: continue.
3. Is it a rulebook or scoring reference?
   - Yes: do not import as learner recall data. Route through rulebook/scoring canonical files.
   - No: continue.
4. Is it a design/reference screenshot?
   - Yes: do not import as Recalls data.
   - No: classify manually before any entry.

## 13. Final Acceptance Criteria

A recall data-entry batch is complete only when:

- All source files are checksummed and registered.
- Every imported row has source document ID, source location, and source quote.
- Every row has completed double-key verification or equivalent reviewer sign-off.
- Automated validation has zero blocking errors.
- Dry-run/staging counts match expected counts.
- Production import result has no unexplained skipped rows or errors.
- Post-import export matches the approved manifest.
- No rows with unresolved uncertainty are visible to learners.
- Reviewer sign-off is stored with the batch archive.

## 14. Immediate Next Steps

1. Confirm the actual recall source document folder or provide the missing attachments.
2. Create the source register and master staging sheet for the batch.
3. Choose the private archive location for source files, manifests, validation reports, exports, rollbacks, and sign-offs.
4. Run a pilot batch of 10-20 rows through the full workflow.
5. Review pilot discrepancies, refine rules, then proceed with full entry.

## 15. Non-Negotiables

- No source, no row.
- No quote, no import.
- No reviewer sign-off, no learner visibility.
- No AI-authored missing facts.
- No silent defaults.
- No destructive overwrite of existing terms without conflict review.
- No production use of the disabled legacy Recalls bulk route until it has preview, draft, provenance, reconciliation, and rollback controls equivalent to the safe Vocabulary importer.
