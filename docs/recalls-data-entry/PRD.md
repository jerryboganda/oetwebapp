# Recalls Data Entry PRD

> Status: Draft v1
> Date: 2026-05-03
> Owner: Content operations plus platform engineering
> Related plan: `docs/RECALLS-DATA-ENTRY-PLAN.md`

## 1. Purpose

Create a professional, auditable workflow for entering Recalls source data into the OET platform with source fidelity, preserved document structure, and zero invented content.

The output of this PRD is not merely a CSV. It is a controlled ingestion process with source registration, double-key transcription, validation, import preview, draft import, reconciliation, and reviewer sign-off before learner visibility.

## 2. Current Repository Findings

- Recalls vocabulary data is stored as `VocabularyTerm` rows.
- The preferred current production route is the admin Vocabulary importer:
  - `POST /v1/admin/vocabulary/import/preview?importBatchId=<batch>`
  - `POST /v1/admin/vocabulary/import?dryRun=true&importBatchId=<batch>`
  - `POST /v1/admin/vocabulary/import?dryRun=false&importBatchId=<batch>`
  - `GET /v1/admin/vocabulary/import/batches/<batch>`
  - `GET /v1/admin/vocabulary/import/batches/<batch>/export`
  - `POST /v1/admin/vocabulary/import/batches/<batch>/rollback`
- The Vocabulary importer supports preview, dry run, draft status for new rows, row validation, per-row `SourceProvenance`, duplicate skip behavior, and audit logging.
- The Vocabulary importer now stamps committed rows with `batch=<importBatchId>;`, records a clean dry-run confirmation by batch and file hash, writes an immutable commit ledger, supports batch summary/export, and can archive draft rows by batch before publish.
- The Vocabulary importer preview now reports same-file duplicate keys and existing database conflicts by normalized `(term, examTypeCode, professionId)` before dry run.
- The legacy Recalls bulk route, `POST /v1/admin/recalls/bulk-upload`, is disabled for production safety and returns `409 legacy_recalls_import_disabled`.
- Full papers, PDFs, audio, answer keys, writing prompts, model answers, and speaking cards are not Recalls vocabulary rows. They belong in the `ContentPaper` / `ContentPaperAsset` pipeline.
- No extra chat attachments were available during this analysis. The actual recall source files must be supplied or placed in a fixed intake folder before entry begins.

## 3. Goals

1. Preserve every source document and its structure.
2. Enter only source-backed or explicitly reviewer-authored data.
3. Prevent hallucinated terms, definitions, IPA, examples, synonyms, categories, and difficulty labels.
4. Keep every imported row traceable to source document, page or section, row or line, exact quote, batch ID, and reviewer.
5. Import rows as draft first, then activate only after reconciliation and approval.
6. Produce a reusable workflow for future batches.

## 4. Non-Goals

- Do not auto-generate missing definitions, IPA, examples, synonyms, or categories.
- Do not OCR and import unreviewed text directly into production.
- Do not use the legacy Recalls bulk uploader for production batches.
- Do not commit private, copyrighted, or licensed source files to Git.
- Do not import full subtest papers as vocabulary rows.

## 5. Roles

- Entry Operator A: first independent transcription.
- Entry Operator B: second independent transcription.
- Adjudicator: resolves A/B differences against the source.
- SME Reviewer: approves clinical accuracy and editorial classifications.
- Content Publisher: approves learner visibility.
- System Admin: owns import route, batch archive, rollback, and reconciliation.

## 6. Source Types And Routing

| Source type | Route |
| --- | --- |
| Term bank, word list, spelling list, synonym list, definition table | Recalls/Vocabulary data-entry workflow |
| PDF question paper, audio, answer key, script, role card, case notes, model answer | `ContentPaper` / `ContentPaperAsset` workflow |
| Rulebook or scoring reference | Rulebook/scoring canonical workflow |
| Result-card screenshot or design reference | Design asset workflow |
| Ambiguous source type | Manual classification before entry |

## 7. Data Model Mapping

The master manifest keeps more evidence than the live database can store.

| Manifest field group | System destination |
| --- | --- |
| `term`, `definition`, `exampleSentence`, `category`, `difficulty`, `ipa`, `examTypeCode`, `professionId`, audio URLs, audio media asset ID | `VocabularyTerm` fields |
| `synonymsCsv`, `collocationsCsv`, `relatedTermsCsv` | `VocabularyTerm` JSON array fields through importer conversion |
| `contextNotes` | `VocabularyTerm.ContextNotes` |
| `sourceProvenance` | Compact pointer in `VocabularyTerm.SourceProvenance` |
| `americanSpelling` | `VocabularyTerm.AmericanSpelling` through the safe Vocabulary importer and admin create/edit UI; must be included in reconciliation before publish |
| source document, page, row, quote, reviewer, uncertainty fields | Batch archive and master manifest |
| common mistakes, similar-sounding words | Preserve in manifest until importer/schema supports them |

## 8. Workflow Requirements

### FR-01 Source Register

Every source file must have `sourceDocumentId`, file path, SHA-256, archive path, file type, source kind, rights status, and notes before transcription.

### FR-02 Master Manifest

Every row must have row ID, source document ID, source location, exact quote, entered fields, uncertainty status, reviewer status, final action, compact provenance pointer, and post-import ID.

### FR-03 Double-Key Entry

Two independent operators must enter the same source rows. Differences must be diffed and adjudicated before validation.

### FR-04 Zero-Hallucination Rules

Missing source data remains missing. Reviewer-authored fields must be listed in `editorialFieldsCsv`. No AI-authored missing facts are allowed.

### FR-05 Validation

Validation must block rows with missing required fields, field length violations, invalid taxonomy values, duplicate keys needing review, missing source quote, unresolved uncertainty, invalid CSV encoding, or unapproved editorial fields.

### FR-06 Import Route

Production batches must use the Vocabulary preview, clean dry-run confirmation, draft import, batch export, and archive-only draft rollback route. The legacy Recalls uploader is blocked for production unless rebuilt to the same standard.

Rows that require `americanSpelling` for Recalls British/American spelling behavior must use the safe Vocabulary importer/admin path and must be reconciled before publish.

### FR-07 Reconciliation

After import, exported `VocabularyTerm` rows must be compared to the approved manifest. Any mismatch blocks publishing.

### FR-08 Learner Visibility

Rows stay draft until reconciliation and reviewer approval are complete. No uncertain row becomes active.

### FR-09 Archive

Each batch archive must include source register, master manifest, import payload, validation report, preview result, dry-run result, commit result, reconciliation report, and sign-off record.

## 9. Acceptance Criteria

A batch is complete only when:

- Source register is frozen and checksummed.
- Every imported row has source ID, location, quote, and compact provenance pointer.
- Double-key differences are resolved.
- Validation has zero blocking errors.
- Preview and dry-run counts use the same `importBatchId`, the dry run is clean, and the commit file matches the clean dry-run file hash.
- Vocabulary preview and external manifest validation have reported composite duplicates by `(term, examTypeCode, professionId)`, existing database conflicts, and unsupported-field blocks.
- Import result has zero unexplained failures.
- Batch reconciliation/export matches the manifest, including `AmericanSpelling`, audio variants, `AudioMediaAssetId`, synonyms, collocations, and related terms.
- SME reviewer and publisher have signed off.
- Rows are still draft until approval.
- The batch archive is stored in the approved private location.

## 10. Open Blockers

- Actual recall source documents were not attached in this session.
- The private archive location must be chosen before entry starts.
- Publish blocking on unresolved reconciliation mismatches is not yet automated.
- Large-scale production entry still needs a completed source register, master manifest, reviewer workflow, and private archive before any real import.
