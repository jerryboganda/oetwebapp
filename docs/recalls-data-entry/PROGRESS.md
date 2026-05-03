# Recalls Data Entry Progress

> Last updated: 2026-05-03
> Mode: Ralph-style loop - research, plan, implement artifacts, validate, review, repeat

## Current Status

Artifact packet created and safe Vocabulary admin import/CRUD path hardened for Recalls-specific fields, full CSV parsing, required source provenance, taxonomy validation, clean dry-run confirmation, batch IDs, batch export/reconciliation, archive-only draft rollback, and legacy-route safety. Actual data entry is blocked until the recall source documents are supplied or placed in a fixed intake folder.

## Loop Log

| Loop | Status | Result |
| --- | --- | --- |
| 1. Instruction load | Complete | Loaded repo instructions and mission-critical content rules. |
| 2. Codebase research | Complete | Mapped Recalls, Vocabulary, admin import, source provenance, and content-paper boundaries. |
| 3. Specialist research | Complete | Researcher and planner confirmed safer Vocabulary import route and artifact needs. Critic subagent did not return a response, so independent final review remains required. |
| 4. Plan artifact | Complete | Created `docs/RECALLS-DATA-ENTRY-PLAN.md`. |
| 5. Execution packet | Complete | Created PRD, progress tracker, templates, validation checklist, sign-off workflow, and hardening backlog. |
| 6. Validation | Complete | `git diff --check` returned no whitespace errors for the packet. |
| 7. Final review | Complete | Independent reviewer found the `americanSpelling` import gap, source-register hash timing issue, and duplicate-preview limitation. Packet updated to address them. |
| 8. Import hardening | Complete | Admin Vocabulary CRUD/import now preserves Recalls spelling/audio/relation fields and blocks normalized composite conflicts before commit. |
| 9. Code validation | Complete | TypeScript, ESLint, backend build, focused admin import tests, and VocabularyService tests passed. Independent reviewer re-check found no remaining blocking code issues. |
| 10. Batch controls | Complete | Safe Vocabulary importer now supports import batch IDs, clean dry-run confirmation, immutable commit ledgering, batch summary, CSV export, archive-only draft rollback, and a disabled legacy Recalls bulk endpoint/UI redirect. |
| 11. Batch validation | Complete | Backend build, TypeScript, ESLint, focused admin import tests, batch rollback/export tests, dry-run gate tests, legacy-route block test, VocabularyService tests, and `git diff --check` passed. |
| 12. Parser and reconciliation | Complete | Safe Vocabulary importer now supports quoted multiline CSV records, controlled category/difficulty validation, exam/profession reference validation, and server-side manifest reconciliation. |

## Key Findings

- Recalls vocabulary data is backed by `VocabularyTerm`.
- Preferred route: `/v1/admin/vocabulary/import/preview?importBatchId=<batch>` then `/v1/admin/vocabulary/import?dryRun=true&importBatchId=<batch>` then commit with `dryRun=false&importBatchId=<batch>`.
- Batch summary, CSV export, server-side manifest reconciliation, and archive-only draft rollback are available under `/v1/admin/vocabulary/import/batches/<batch>` after a committed batch creates its commit ledger.
- Legacy route `/v1/admin/recalls/bulk-upload` is disabled for production safety and the admin UI now directs operators to the safe Vocabulary importer.
- `americanSpelling`, Recalls audio variants, audio media asset ID, collocations, and related terms are wired through the safer Vocabulary importer plus admin create/edit UI; batch export includes them for reconciliation before publish.
- Full papers, PDFs, audio, scripts, answer keys, writing case notes, model answers, and speaking role cards should not be flattened into Recalls vocabulary rows.
- No actual recall source files were attached in this session.

## Artifact Inventory

| Artifact | Status | Purpose |
| --- | --- | --- |
| `../RECALLS-DATA-ENTRY-PLAN.md` | Complete | Professional end-to-end operating plan. |
| `PRD.md` | Complete | Requirements and acceptance criteria. |
| `PROGRESS.md` | In progress | Ralph-style tracker. |
| `source-register.template.csv` | Complete | Source file registration template. |
| `master-manifest.template.csv` | Complete | Row-level evidence and import manifest. |
| `validation-checklist.md` | Complete | Blocking validation gates. |
| `reviewer-signoff-workflow.md` | Complete | Human review and approval process. |
| `system-hardening-backlog.md` | Complete | Engineering backlog before large-scale production entry. |

## Blockers

1. Actual recall source documents are not present as chat attachments.
2. Private batch archive location is not yet chosen.
3. Publish blocking on unresolved reconciliation mismatches is not yet automated.
4. Frontend reconciliation upload UX is not yet exposed on the import page.

## Next Actions

1. Confirm source document location.
2. Choose the private batch archive location.
3. Run a 10-20 row pilot batch through the templates and importer preview/dry-run path.
4. Archive the batch summary/export and reconcile every field against the approved manifest before publish.
5. Add publish blocking for unresolved reconciliation mismatches before high-volume recurring imports.
