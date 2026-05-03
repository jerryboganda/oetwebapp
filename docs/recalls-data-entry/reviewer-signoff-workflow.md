# Recalls Reviewer Sign-Off Workflow

This workflow controls human review for Recalls data-entry batches.

## Status Model

| Status | Meaning | Exit Rule |
| --- | --- | --- |
| `intake_locked` | Source files registered and checksummed | Source register frozen |
| `transcribed_a` | Operator A completed entry | All assigned rows entered |
| `transcribed_b` | Operator B completed entry | All assigned rows entered |
| `diff_resolved` | A/B differences adjudicated | No unresolved differences |
| `validated` | Automated validation complete | Zero blocking validation errors |
| `dry_run_passed` | Import preview and dry run accepted | Counts match manifest |
| `imported_draft` | Rows imported as draft | Backend result archived |
| `reconciled` | Imported rows match manifest | Export diff has zero blockers |
| `reviewer_approved` | SME approved content | Signed row or batch approval |
| `published_active` | Rows visible to learners | Publisher approval complete |
| `rejected` | Row or batch rejected | Reason recorded |
| `deferred` | Row held for later decision | Uncertainty reason recorded |

## Role Responsibilities

| Role | Responsibilities |
| --- | --- |
| Entry Operator A | First independent transcription from source to manifest |
| Entry Operator B | Second independent transcription without copying Operator A |
| Adjudicator | Resolves field differences using source evidence only |
| SME Reviewer | Confirms medical/OET accuracy and editorial classifications |
| Content Publisher | Approves learner visibility after reconciliation |
| System Admin | Runs preview, dry run, import, export, archive, and rollback controls |

## Approval Rules

- A row cannot move to `validated` with unresolved A/B differences.
- A row cannot move to `dry_run_passed` without source ID, location, and quote.
- A row cannot move to `imported_draft` with `uncertaintyFlag=true`.
- A row cannot move to `reviewer_approved` if any field was invented or unmarked editorial.
- A row cannot move to `published_active` unless post-import reconciliation matches the manifest.
- If post-import reconciliation fails before publish, the System Admin must archive the batch export and rollback result, then keep affected rows archived until a corrected batch is approved. Rollback does not modify active rows.
- A rejected row remains in the manifest and archive with its reason.

## Sign-Off Record

Each batch sign-off must record:

```text
importBatchId
sourceRegisterSha256
masterManifestSha256
importPayloadSha256
validationReportSha256
previewResultSha256
dryRunResultSha256
commitResultSha256
batchExportSha256
rollbackResultSha256IfUsed
reconciliationReportSha256
smeReviewer
smeReviewTimestamp
publisher
publishApprovalTimestamp
approvedRowCount
rejectedRowCount
deferredRowCount
notes
```

## Escalation Rules

- Illegible source: defer row and request source clarification.
- Medical ambiguity: SME reviewer decides or rejects.
- Conflicting source documents: adjudicator opens conflict note; no import until resolved.
- Duplicate existing term: use update-review status; do not overwrite silently.
- Missing rights/provenance: reject or defer source until rights are clarified.
