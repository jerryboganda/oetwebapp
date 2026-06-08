# Agent State - OET 2026 Portfolio

Last updated: 2026-06-07

## Goal

Finish PR #38 (`feat/oet-2026-entitlement-conformance`) with GitHub Actions as the broad validation gate, then merge to `main` and deploy through the CI/GHCR production path only after required checks are green.

## Current Fix Checkpoint

- QA Smoke run `27108225615` passed frontend unit/lint/tsc/build, SBOM/SCA, and all E2E shards; backend shards failed.
- Focused backend checks now green locally:
  - `WritingReviewEntitlementConsumptionTests.AdminCancel_OfEntitlementReview_RestoresOneEntitlement|AdminReopen_OfCancelledEntitlementReview_ReturnsToQueueNotAwaitingPayment`
  - `RuntimeSettingsProviderZoomTests`
  - `AuthFlowsTests.SeedData_EnsuresUnifiedAuthAccountsForLearnerExpertAndAdmin|CriticalFlowsTests.BootstrapEndpoint_ReturnsLearnerProfileAndReferences|CriticalFlowsTests.WritingSubmission_QueuesAndCompletesEvaluation`
  - `VocabularyAudioWorkerTests.Backfill_PicksUpTermsWithoutAudio`

## Current Touched Files

- `backend/src/OetLearner.Api/Services/AdminService.cs`
- `backend/tests/OetLearner.Api.Tests/Infrastructure/TestWebApplicationFactory.cs`
- `backend/tests/OetLearner.Api.Tests/RuntimeSettingsProviderZoomTests.cs`
- `backend/tests/OetLearner.Api.Tests/VocabularyAudioWorkerTests.cs`
- `backend/tests/OetLearner.Api.Tests/WritingReviewEntitlementConsumptionTests.cs`
- `PROGRESS.md`

## Remaining Backend Failures To Triage

- `AdminFlowsTests.AdminVocabularyImport_*`
- `RecallsAudioEntitlementTests.Queue_exposes_term_id_but_never_cached_audio_urls`
- `RecallsAudioEntitlementTests.Quiz_never_returns_cached_audio_urls`
- `LearnerSurfaceContractTests.ListeningPaperAttempt_SubmitsCanonicalScoreAndPolicySafeReview`
- `Classes.ClassNotificationServiceTests.SendReminderAsync_ProducesDistinctDedupeKeysPerLeadWindow`
- `AdminEndpointAuthorizationInventoryTests.AdminMutations_RequirePerUserWriteRateLimit`
- `ContentBulkImportE2ETests.*`
- `ContentPaperBulkActionTests.Bulk_delete_removes_archived_paper_and_its_authoring_children`

## Validation Rule

Keep using focused local `dotnet test --filter` for red/green proof only. Push feature branch and let GitHub Actions run broad backend/frontend/build/E2E gates. Do not deploy until PR checks are green and merged to `main`.
