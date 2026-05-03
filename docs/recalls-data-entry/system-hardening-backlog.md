# Recalls Data Entry System Hardening Backlog

This backlog is for engineering work that should happen before large-scale production data entry.

## P0 - Required Before Large Production Entry

### P0.1 Disable Or Gate Legacy Recalls Bulk Upload

Status: implemented. Backend route returns `409 legacy_recalls_import_disabled`; admin UI routes operators to the safe Vocabulary importer.

Problem: `/v1/admin/recalls/bulk-upload` inserts active rows, hard-codes provenance, and updates existing rows.

Acceptance criteria:

- Production operators are routed to the Vocabulary import path.
- Legacy Recalls route is feature-gated, admin-hidden, or upgraded.
- Documentation warns against production use.

### P0.2 Add Import Batch IDs

Status: implemented for the safe Vocabulary importer. Preview, clean dry run, commit, batch summary, export, rollback, and audit logging use the same `importBatchId`; clean dry-run confirmation and commit ownership are recorded in the existing idempotency ledger. A dedicated batch table is still future work.

Problem: imports are not grouped as durable operational batches.

Acceptance criteria:

- Every import has `importBatchId`.
- Preview, dry run, commit, reconciliation, and sign-off reference the same batch.
- Audit log includes batch ID.

### P0.3 Add Post-Import Export/Reconciliation Tooling

Status: implemented for the safe Vocabulary importer. Batch summary and CSV export read from the immutable commit ledger and include Recalls-specific fields. `/v1/admin/vocabulary/import/batches/{importBatchId}/reconcile` accepts the approved manifest CSV and reports matched, missing, extra, mismatched, invalid, and duplicate manifest rows.

Problem: operators need a reliable way to compare imported rows against the approved manifest.

Acceptance criteria:

- Export rows by batch or provenance prefix.
- Export includes all imported `VocabularyTerm` fields needed for reconciliation.
- Tool reports exact matches, missing rows, extra rows, and field mismatches.

### P0.4 Define Durable Source Archive Strategy

Problem: `SourceProvenance` cannot store full evidence.

Acceptance criteria:

- Approved private archive location exists.
- Compact source pointer format is documented.
- Batch archive contains source register, manifest, payload, validation, import, reconciliation, and sign-off artifacts.

### P0.5 Include Recalls-Specific Fields In Batch Reconciliation

Status: partially implemented. Batch export and reconciliation include `AmericanSpelling`, audio variants, `AudioMediaAssetId`, collocations, and related terms. Publish blocking on unresolved reconciliation mismatches remains future work.

Problem: Recalls uses `VocabularyTerm.AmericanSpelling`, audio variants, audio media asset IDs, collocations, and related terms. Production publish needs a batch reconciliation report that proves these fields survived import.

Acceptance criteria:

- Batch export/reconciliation surfaces include `AmericanSpelling`, audio variants, `AudioMediaAssetId`, collocations, and related terms.
- Reconciliation reports mismatches between manifest values and stored `VocabularyTerm` values.
- Rows cannot be published while reconciliation mismatches remain.

## P1 - Strongly Recommended

### P1.1 Shared Full CSV Parser

Status: implemented for the safe Vocabulary importer. It now parses full CSV records with quoted commas, escaped quotes, UTF-8 text, and quoted multiline fields; unclosed quoted fields are rejected.

Problem: current import parsing handles quoted values on one physical line but not multiline CSV fields.

Acceptance criteria:

- Shared parser supports full RFC 4180 or import payload explicitly rejects multiline fields.
- Parser tests cover commas, quotes, blank optional fields, UTF-8, and multiline policy.

### P1.2 Taxonomy Validation

Status: implemented for the safe Vocabulary importer. Category and difficulty are required and validated against the approved vocabulary taxonomy; exam type and profession references are checked against configured reference data.

Problem: categories and difficulty values can drift from rulebook-approved values.

Acceptance criteria:

- Import validates category and difficulty against approved vocabulary taxonomy.
- Unknown values require editorial approval.
- Preview reports taxonomy warnings before commit.

### P1.3 Conflict Review For Existing Terms

Problem: existing rows may be skipped or overwritten depending on route.

Status: partially addressed for the safe Vocabulary importer; preview and dry run now report normalized composite conflicts. Operator disposition and approved update workflow remain future work.

Acceptance criteria:

- Existing `(term, examTypeCode, professionId)` conflicts appear in preview.
- Operators can choose skip, create review task, or approved update.
- No destructive overwrite occurs silently.

### P1.4 Draft-Only Production Imports

Problem: learner visibility must not precede review.

Acceptance criteria:

- All production imports create draft rows.
- Publish action requires reconciliation and reviewer sign-off.
- Active status cannot be set by CSV alone.

## P2 - Operational Maturity

### P2.1 Row-Level Evidence Store

Acceptance criteria:

- System can store source document ID, page, row, quote hash, reviewer, and manifest row ID outside the 512-character provenance field.
- Admin UI can show evidence for a term.

### P2.2 Rollback By Batch

Status: implemented for draft rows imported through the safe Vocabulary importer. Rollback archives draft rows only; physical delete is disabled. Active rows are blocked from rollback and require explicit publish-level handling.

Acceptance criteria:

- Imported draft rows can be archived by batch before publish.
- Rollback report is archived.
- Published rows require explicit publisher approval before rollback.

### P2.3 Import UI Enhancements

Status: implemented for preview, dry run, commit, batch ID, batch summary, batch export, draft rollback, and safe routing away from the legacy Recalls uploader.

Acceptance criteria:

- Admin UI exposes preview, dry run, commit, batch ID, source provenance format, and downloadable result reports.
- UI prevents production use of unsafe route.

### P2.4 Focused Tests

Status: implemented for the backend safe Vocabulary importer. Tests cover preview, clean dry run, commit gate, draft status, duplicates/conflicts, provenance, full CSV parsing, taxonomy validation, reconciliation, export, and rollback behavior. Frontend coverage remains future work.

Acceptance criteria:

- Backend tests cover preview, dry run, draft status, duplicates, provenance, CSV edge cases, and taxonomy validation.
- Frontend tests cover import UI warnings and blocked unsafe operations.
