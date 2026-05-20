# Phases 2-7 build summary (pre-deploy)

**Date**: 2026-05-20
**Branch**: `cleanup/remove-demo-dummy-seed-placeholder-data`
**Build status**: backend ✅ 0 warn 0 err · frontend ✅ tsc clean · vitest ✅ 1316/1316 pass · dotnet test running

---

## New backend tables (5 EF migrations)

| Migration | Table | Purpose |
|---|---|---|
| `20260520113807_AddRecallDocuments` | `RecallDocuments` | Recall PDF library |
| `20260520115259_AddScoringPolicies` | `ScoringPolicies` | Scoring System singleton |
| `20260520120501_AddRulebookReferencePdf` | adds `ReferencePdfAssetId` to `RulebookVersions` | PDF companion to JSON rulebooks |
| `20260520121654_AddResultTemplateAssets` | `ResultTemplateAssets` | Mock-result table image gallery |
| `20260520123258_AddSpeakingSharedResources` | `SpeakingSharedResources` | Shared warm-up + assessment-criteria PDFs |

All migrations are additive (no DROP / ALTER COLUMN on existing tables; only `ALTER TABLE ... ADD COLUMN ReferencePdfAssetId NULL` on RulebookVersions).

## New endpoints (admin + learner)

### Phase 2 — Recall PDFs library
- `GET /v1/admin/recall-documents` (list, filterable subtest+status, paginated)
- `GET /v1/admin/recall-documents/{id}`
- `POST /v1/admin/recall-documents` (multipart upload — file+title+subtestCode+periodLabel+...)
- `PUT /v1/admin/recall-documents/{id}` (edit metadata)
- `POST /v1/admin/recall-documents/{id}/publish`
- `POST /v1/admin/recall-documents/{id}/archive`
- `POST /v1/admin/recall-documents/{id}/unarchive`
- `DELETE /v1/admin/recall-documents/{id}` (soft-archive)
- `GET /v1/recall-documents` (learner, scoped by profession)

### Phase 3 — Result templates
- `GET /v1/admin/result-templates`
- `POST /v1/admin/result-templates` (image upload, jpg/png/webp)
- `PUT /v1/admin/result-templates/{id}`
- `POST /v1/admin/result-templates/{id}/activate`
- `POST /v1/admin/result-templates/{id}/deactivate`
- `DELETE /v1/admin/result-templates/{id}` (hard delete)

### Phase 4 — Rulebook PDF companion
- `POST /v1/admin/rulebooks/{id}/reference-pdf` (multipart upload)
- `DELETE /v1/admin/rulebooks/{id}/reference-pdf`
- `GET /v1/rulebooks/{kind}/{profession}/reference-pdf` (learner)

### Phase 5 — Scoring System
- `GET /v1/admin/scoring-policy` (active version)
- `PUT /v1/admin/scoring-policy` (create new active version, archive prior)
- `GET /v1/admin/scoring-policy/history`
- `GET /v1/scoring-policy` (learner)

### Phase 6 — Speaking shared resources
- `GET /v1/admin/speaking/shared-resources` (filter by kind/profession)
- `POST /v1/admin/speaking/shared-resources` (multipart upload)
- `POST /v1/admin/speaking/shared-resources/{id}/publish`
- `POST /v1/admin/speaking/shared-resources/{id}/archive`
- `DELETE /v1/admin/speaking/shared-resources/{id}` (soft-archive)
- `GET /v1/speaking/shared-resources` (learner, scoped by profession)

### Phase 7 — Real Content folder importer
- `POST /v1/admin/imports/real-content-folder/stage` (ZIP upload, returns proposals)
- `POST /v1/admin/imports/real-content-folder/{sessionId}/commit` (creates Drafts in all 5 systems + RulebookVersion PDF attachments + ScoringPolicy)

All admin endpoints require `AdminContentWrite` or `AdminContentPublish` per existing policy patterns. Linter-added security:
- `IUploadContentValidator` + `IUploadScanner` checks on every uploaded file
- `AuditEvent` rows on every state-changing operation

## New admin pages

| Route | Purpose |
|---|---|
| `/admin/content/recalls-library` | List + upload + publish/archive recall PDFs |
| `/admin/content/result-templates` | Gallery + upload + activate/deactivate |
| `/admin/content/scoring-system` | Markdown + JSON editor with history viewer |
| `/admin/content/speaking/shared-resources` | List + upload + publish/archive warm-up + assessment criteria |
| `/admin/content/imports/real-content-folder` | Drag-drop ZIP → review proposals → commit |

## New learner pages

| Route | Purpose |
|---|---|
| `/recalls/documents` | Read-only recall PDF library, profession-scoped |

## API client additions (`lib/api.ts`)

37 new typed functions covering CRUD for all 5 new resources plus the importer stage/commit cycle.

## Permission additions (`lib/admin-permissions.ts`)

5 new admin route entries, all gated on `ContentRead` / `ContentWrite` / `ContentPublish`.

---

## Update — 20 May 2026 evening

- VPS migrated from `185.252.233.186` (decommissioned) → `68.183.32.122` (`oet-dev`).
- Phase 2-7 code already on the new VPS at commit `e3fa70dd "feat: Phase 2-7 — AI Assistant, Content Admin UIs, Production Readiness"` plus DI fix `b8025272` — but containers were running an older image (endpoints returned 404).
- TTS swap commit `305d675d` (DO Qwen3 Voice Design British male) lives on `cleanup/remove-demo-dummy-seed-placeholder-data` and a clean cherry-pick on `deploy/phase-2-7` (pushed to origin).
- Historical deploy attempt used `scripts/deploy/deploy-direct.sh`; current production deploys must use an immutable 40-character `DEPLOY_REF` SHA with release evidence. SSH alias is now `oet-dev`.
- Local zip of `Project Real Content/` prepared at `_audit/Project-Real-Content.zip` (120 MB) — ready to drag-drop into the new importer wizard once endpoints are live. This is a local-only artifact and is ignored by git.

## What's *not* yet done (post-deploy)

- **Phase 1 publish** of complete Listening drafts — re-audit needed on the new VPS (paper IDs may differ from the original 185.x audit).
- **AI/TTS backfill** of remaining incomplete Listening drafts via `TTS__ForceProvider=digitalocean node scripts/admin/retry-listening-tts.mjs --paper-id <id>` per Step 7 of `deployment-runbook.md`.
- **Manual content uploads** — user uploads `_audit/Project-Real-Content.zip` via the new importer wizard at `/admin/content/imports/real-content-folder`, reviews proposals, and publishes drafts from each admin page.

## Verification still owed (after deploy)

- Verify prod EF migrations were applied automatically by `DatabaseBootstrapper` inside the new API container; do not run ad hoc `dotnet ef database update` unless a rollback/repair plan explicitly calls for it.
- Re-run Phase 0 audit script (`_audit/production-content-audit.md` instructions) to confirm new endpoints respond and don't break existing learner pages
- Spot-check each new admin page renders for the admin user (`manwara575@gmail.com`)
- Confirm learner (`mindreader420123@gmail.com`) sees `/recalls/documents` and the published scoring-policy card on dashboard
