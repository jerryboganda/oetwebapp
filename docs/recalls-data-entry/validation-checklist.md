# Recalls Data Entry Validation Checklist

Use this checklist for every batch. A row or batch with a blocking failure must not be imported or published.

## 1. Source Intake

- [ ] Every source file has a `sourceDocumentId`.
- [ ] Every source file has a SHA-256 checksum.
- [ ] Every source file has an approved archive path.
- [ ] Source file rights status is recorded.
- [ ] Source kind is classified before entry.
- [ ] Source register is frozen before transcription starts.
- [ ] Any changed source file receives a new source document ID.

## 2. Structure Preservation

- [ ] Document structure is mapped before row entry.
- [ ] Source page, section, row, line, or block is recorded for every row.
- [ ] Exact `sourceQuote` is present for every importable row.
- [ ] Original source spelling is preserved in the manifest.
- [ ] Normalized fields are traceable to exact source text or editorial approval.

## 3. Double-Key Entry

- [ ] Operator A entry is complete.
- [ ] Operator B entry is complete.
- [ ] Automated A/B diff is complete.
- [ ] All differences are adjudicated against the source.
- [ ] Adjudicator decisions are recorded.

## 4. Zero-Hallucination Controls

- [ ] No missing definition was invented.
- [ ] No missing IPA was invented.
- [ ] No missing example sentence was invented.
- [ ] No missing synonym was invented.
- [ ] No missing category or difficulty was silently inferred.
- [ ] Editorial fields are listed in `editorialFieldsCsv`.
- [ ] Uncertain rows are marked and excluded from import.

## 5. Field Validation

- [ ] `term` is present and within 128 characters.
- [ ] `definition` is present and within 1024 characters.
- [ ] `exampleSentence` is within 2048 characters.
- [ ] `contextNotes` is within 1024 characters.
- [ ] `examTypeCode` is within 16 characters.
- [ ] `professionId` is within 32 characters or blank for general.
- [ ] `category` is within 64 characters.
- [ ] `difficulty` is within 16 characters.
- [ ] `ipa` is within 64 characters.
- [ ] `americanSpelling` is within 128 characters.
- [ ] Audio URL fields are within 256 characters.
- [ ] `audioMediaAssetId` is within 64 characters.
- [ ] `sourceProvenance` is within 512 characters.
- [ ] `sourceProvenance` plus `batch=<importBatchId>;` stays within 512 characters.
- [ ] `sourceProvenance` includes compact source/manifest pointer after the batch prefix.

## 6. Controlled Values

- [ ] `examTypeCode` is explicitly approved.
- [ ] `professionId` exists or is approved as new reference data.
- [ ] `category` matches approved taxonomy or is marked editorial.
- [ ] `difficulty` matches approved taxonomy or is marked editorial.
- [ ] `reviewStatus` is one of the approved workflow statuses.
- [ ] `finalImportAction` is `import`, `skip`, `defer`, `update-review`, or `reject`.

## 7. CSV And Payload Safety

- [ ] If multiline fields are present, import uses the safe Vocabulary importer with parser hardening deployed.
- [ ] Commas inside fields are quoted.
- [ ] Quotes inside fields are escaped.
- [ ] Header names match the selected importer.
- [ ] Row count in payload matches approved importable row count.
- [ ] Legacy Recalls bulk payload is not used for production.

## 8. Preview And Dry Run

- [ ] One `importBatchId` is chosen for preview, dry run, commit, export, rollback, and sign-off.
- [ ] Vocabulary import preview completed.
- [ ] Preview response returned the expected `importBatchId`.
- [ ] Invalid row count is zero or all invalid rows are intentionally excluded.
- [ ] Duplicate report reviewed.
- [ ] Built-in preview composite duplicate report for `(term, examTypeCode, professionId)` is reviewed.
- [ ] External manifest duplicate report agrees with built-in preview.
- [ ] Existing database conflicts have written disposition.
- [ ] Rows with `americanSpelling` are imported only through the safe Vocabulary path and reconciled before publish.
- [ ] Dry-run completed.
- [ ] Dry-run used the same `importBatchId` as preview.
- [ ] Dry-run is clean: zero skipped rows, zero duplicate rows, and zero failed rows.
- [ ] Dry-run confirmation is server-recorded for the same batch ID and file hash.
- [ ] Dry-run imported/skipped/duplicate/failed counts match expectations.
- [ ] Any warning has written disposition.

## 9. Commit

- [ ] Commit uses `/v1/admin/vocabulary/import?dryRun=false&importBatchId=<batch>`.
- [ ] Commit upload is the same file that produced the latest clean dry-run confirmation.
- [ ] Commit result is archived.
- [ ] Imported rows are draft.
- [ ] No unexplained skipped or failed rows remain.
- [ ] Import operator and timestamp are recorded.

## 10. Post-Import Reconciliation

- [ ] Exported `VocabularyTerm` rows are compared to the manifest.
- [ ] Server reconciliation endpoint is run with the approved manifest CSV for the committed `importBatchId`.
- [ ] Reconciliation result is clean: zero missing rows, zero extra rows, zero mismatched rows, and zero invalid manifest rows.
- [ ] Batch summary is archived.
- [ ] Batch CSV export is archived.
- [ ] Term, definition, example, context, category, difficulty, IPA, American spelling, audio URLs, audio media asset ID, synonyms, collocations, related terms, exam type, profession, status, and provenance all match expected values.
- [ ] Mismatches are resolved or rows remain draft.
- [ ] If mismatch cannot be resolved before publish, batch rollback archives draft rows and the rollback result is archived.
- [ ] Active rows are not modified by rollback.
- [ ] `postImportVocabularyTermId` is written back to the master manifest.
- [ ] Reconciliation report is archived.

## 11. Review And Publish

- [ ] SME reviewer approves every row to publish.
- [ ] Content publisher approves learner visibility.
- [ ] No row with `uncertaintyFlag=true` is active.
- [ ] No row with unresolved editorial fields is active.
- [ ] Batch sign-off record is archived.
