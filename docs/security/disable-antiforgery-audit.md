# DisableAntiforgery Audit

Review date: 2026-05-09

Scope: every backend endpoint call to `DisableAntiforgery()` under `backend/src/OetLearner.Api/Endpoints`.

## Summary

- Total exceptions found: 8.
- Pattern: exceptions are limited to multipart, raw-body, chunked-upload, ZIP-import, or CSV-import endpoints.
- Compensating controls: authenticated route groups, granular admin or learner policies, per-user write rate limits on mutation uploads, ownership checks where applicable, file validation, size limits, storage services, and audit events on admin mutations.
- Endpoint-level evidence now covers the exact exception inventory, required auth policies, write-rate limits, unauthenticated denial, wrong-permission denial, and correct-permission requests passing auth/authz and antiforgery layers for the upload/import exceptions.

Focused validation on 2026-05-09:

```powershell
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~EndpointRegistrationTests|FullyQualifiedName~DisableAntiforgeryUploadEndpointAuthorizationTests|FullyQualifiedName~MediaEndpointSecurityTests.Upload|FullyQualifiedName~PronunciationEndpointsTests.UploadAndScore" --no-restore
```

Result: 39/39 tests passed.

## Endpoint Inventory

### Pronunciation Audio Upload

- Endpoint: `POST /v1/pronunciation/drills/{drillId}/attempt/{attemptId}/audio`.
- File: `backend/src/OetLearner.Api/Endpoints/PronunciationEndpoints.cs`.
- Reason: accepts raw audio body or multipart form upload.
- Authorization: pronunciation route group is learner-authenticated; service receives `http.UserId()` and validates attempt ownership.
- Rate limiting: `PerUserWrite` on the audio upload endpoint.
- Alternate protection: authenticated learner identity, owned attempt, MIME and length handling in the pronunciation service, server-side scoring pipeline.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `PronunciationEndpointsTests.UploadAndScore_RequiresAuthenticatedLearner` and `UploadAndScore_RejectsAuthenticatedNonLearner` verify auth gates; `UploadAndScore_Persists_Assessment_And_Progress` verifies the learner audio path reaches scoring.

### Generic Media Upload

- Endpoint: `POST /v1/media/upload`.
- File: `backend/src/OetLearner.Api/Endpoints/MediaEndpoints.cs`.
- Reason: multipart `IFormFile` upload.
- Authorization: authenticated `/v1/media` route group.
- Rate limiting: `PerUserWrite` on upload.
- Alternate protection: media user resolution, max upload size, content validator, upload scanner, storage service.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/auth/rate-limit metadata; `MediaEndpointSecurityTests.Upload_requires_authenticated_user` verifies unauthenticated denial; `Upload_rejects_declared_pdf_when_magic_bytes_do_not_match` verifies authenticated uploads reach content validation without a CSRF token.

### Admin Chunked Upload Part

- Endpoint: `PUT /v1/admin/uploads/{uploadId}/parts/{partNumber:int}`.
- File: `backend/src/OetLearner.Api/Endpoints/ContentPapersAdminEndpoints.cs`.
- Reason: raw stream body for large chunked uploads.
- Authorization: `AdminContentWrite` group policy.
- Rate limiting: `PerUserWrite` group policy.
- Alternate protection: upload session state, part number validation, declared size limits, storage service, admin identity on session start.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminContentRead` wrong-permission denial, and `AdminContentWrite` requests passing auth/authz and antiforgery layers; `ChunkedUploadServiceTests` cover chunk handling.

### Admin ZIP Import Stage

- Endpoint: `POST /v1/admin/imports/zip`.
- File: `backend/src/OetLearner.Api/Endpoints/ContentPapersAdminEndpoints.cs`.
- Reason: multipart ZIP upload.
- Authorization: `AdminContentWrite` group policy.
- Rate limiting: `PerUserWrite` group policy.
- Alternate protection: content bulk import service, manifest validation, safe path handling, staged approval before commit.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminContentRead` wrong-permission denial, and `AdminContentWrite` requests passing auth/authz and antiforgery layers; `ContentBulkImportE2ETests` cover import staging and validation.

### Admin User CSV Import

- Endpoint: `POST /v1/admin/users/import`.
- File: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`.
- Reason: multipart CSV upload.
- Authorization: `AdminUsersWrite` endpoint policy.
- Rate limiting: `PerUserWrite`.
- Alternate protection: admin identity, import parser validation, audit events in `AdminService`.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminUsersRead` wrong-permission denial, and `AdminUsersWrite` requests passing auth/authz and antiforgery layers; `AdminUsers_BulkImport_*` admin flow tests cover successful, duplicate, invalid, and empty CSV behavior.

### Admin Vocabulary Import Preview

- Endpoint: `POST /v1/admin/vocabulary/import/preview`.
- File: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`.
- Reason: multipart CSV upload for dry-run preview.
- Authorization: `AdminContentRead` endpoint policy.
- Rate limiting: `PerUserWrite`.
- Alternate protection: admin identity, parser validation, dry-run import batch semantics.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminContentWrite` wrong-permission denial, and `AdminContentRead` requests passing auth/authz and antiforgery layers; admin vocabulary import flow tests cover parser and preview behavior.

### Admin Vocabulary Import Commit

- Endpoint: `POST /v1/admin/vocabulary/import`.
- File: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`.
- Reason: multipart CSV upload.
- Authorization: `AdminContentWrite` endpoint policy.
- Rate limiting: `PerUserWrite`.
- Alternate protection: admin identity, parser validation, dry-run option, import batch tracking, audit events.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminContentRead` wrong-permission denial, and `AdminContentWrite` requests passing auth/authz and antiforgery layers; admin vocabulary import flow tests cover dry-run, commit gating, duplicates, taxonomy validation, and rollback behavior.

### Admin Vocabulary Import Reconcile

- Endpoint: `POST /v1/admin/vocabulary/import/batches/{importBatchId}/reconcile`.
- File: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`.
- Reason: multipart reconciliation CSV upload.
- Authorization: `AdminContentRead` endpoint policy.
- Rate limiting: `PerUserWrite`.
- Alternate protection: import batch lookup, parser validation, reconciliation service rules.
- Evidence: `EndpointRegistrationTests.Program_DisableAntiforgeryExceptions_AreExplicitlyControlled` verifies the route/method/policy/rate-limit metadata; `DisableAntiforgeryUploadEndpointAuthorizationTests` verifies unauthenticated denial, `AdminContentWrite` wrong-permission denial, and `AdminContentRead` requests passing auth/authz and antiforgery layers; `AdminVocabularyImport_BatchExportAndRollback_ArchivesDraftRows` covers reconciliation behavior.

## Future Additions Checklist

- New `DisableAntiforgery()` calls must be added to this audit in the same PR.
- Each exception must name the upload/body reason, auth policy, rate limit, validation service, and test coverage.
- Prefer signed upload/session patterns for large files and raw audio; do not add unauthenticated mutation endpoints.
