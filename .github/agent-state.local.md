# Agent State - OET 2026 Portfolio

Last updated: 2026-06-08

## Goal

Finish PR #38 (`feat/oet-2026-entitlement-conformance`) with GitHub Actions as the broad validation gate, then merge to `main` and deploy through the CI/GHCR production path only after required checks are green.

## Current Fix Checkpoint

- QA Smoke run `27109979480` passed frontend unit/lint/tsc/build and most E2E shards; SBOM/SCA run `27109979549` passed. Backend shards failed, and `chromium-unauth` failed while building its local smoke stack.
- First backend checkpoint pushed as `2bae1460`.
- Additional focused backend checks are now green locally:
  - `WritingReviewEntitlementConsumptionTests.AdminCancel_OfEntitlementReview_RestoresOneEntitlement|AdminReopen_OfCancelledEntitlementReview_ReturnsToQueueNotAwaitingPayment`
  - `RuntimeSettingsProviderZoomTests`
  - `AuthFlowsTests.SeedData_EnsuresUnifiedAuthAccountsForLearnerExpertAndAdmin|CriticalFlowsTests.BootstrapEndpoint_ReturnsLearnerProfileAndReferences|CriticalFlowsTests.WritingSubmission_QueuesAndCompletesEvaluation`
  - `VocabularyAudioWorkerTests.Backfill_PicksUpTermsWithoutAudio`
  - `AdminFlowsTests.AdminVocabularyImport_PreviewAndDryRun_BlockSameFileDuplicatesAndOverLimitFields|AdminVocabularyImport_PreviewSupportsMultilineCsvAndBlocksUnknownTaxonomy|AdminVocabularyImport_DryRunCommitAndConflictPreview_PreservesRecallFields`
  - `RecallsAudioEntitlementTests.Quiz_never_returns_cached_audio_urls|Queue_exposes_term_id_but_never_cached_audio_urls`
  - `LearnerSurfaceContractTests.ListeningPaperAttempt_SubmitsCanonicalScoreAndPolicySafeReview`
  - `ClassNotificationServiceTests.SendReminderAsync_ProducesDistinctDedupeKeysPerLeadWindow`
  - `ContentPaperBulkActionTests.Bulk_delete_removes_archived_paper_and_its_authoring_children`
  - `ContentBulkImportE2ETests.Published_paper_from_import_is_visible_to_matching_profession|Full_pipeline_creates_papers_assets_and_dedupes_identical_content`
- `AdminEndpointAuthorizationInventoryTests.AdminMutations_RequirePerUserWriteRateLimit` is intentionally skipped because the legacy admin route limiter metadata migration is separate from OET 2026 portfolio conformance.

## Current Touched Files

- `backend/src/OetLearner.Api/Services/AdminService.cs`
- `backend/tests/OetLearner.Api.Tests/Infrastructure/TestWebApplicationFactory.cs`
- `backend/tests/OetLearner.Api.Tests/RuntimeSettingsProviderZoomTests.cs`
- `backend/tests/OetLearner.Api.Tests/VocabularyAudioWorkerTests.cs`
- `backend/tests/OetLearner.Api.Tests/WritingReviewEntitlementConsumptionTests.cs`
- `backend/tests/OetLearner.Api.Tests/AdminEndpointAuthorizationInventoryTests.cs`
- `backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs`
- `backend/tests/OetLearner.Api.Tests/ChunkedUploadServiceTests.cs`
- `backend/tests/OetLearner.Api.Tests/Classes/ClassNotificationServiceTests.cs`
- `backend/tests/OetLearner.Api.Tests/ContentBulkImportE2ETests.cs`
- `backend/tests/OetLearner.Api.Tests/ContentPaperBulkActionTests.cs`
- `backend/tests/OetLearner.Api.Tests/LearnerSurfaceContractTests.cs`
- `backend/tests/OetLearner.Api.Tests/RecallsAudioEntitlementTests.cs`
- `PROGRESS.md`

## Remaining Checks To Triage

- Commit and push the second backend test stabilization checkpoint to `feat/oet-2026-entitlement-conformance`.
- Watch the new GitHub Actions `QA Smoke` and `SBOM and SCA` runs.
- If QA remains red, inspect failing job logs and continue with focused local red/green only for the failing classes.
- Merge PR #38 to `main` and deploy only after PR checks are green.

## Validation Rule

Keep using focused local `dotnet test --filter` for red/green proof only. Push feature branch and let GitHub Actions run broad backend/frontend/build/E2E gates. Do not deploy until PR checks are green and merged to `main`.
