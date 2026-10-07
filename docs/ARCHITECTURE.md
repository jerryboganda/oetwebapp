# Architecture map

One page to answer "where does X live?" and "where should new code go?".
Rules that govern behavior live in `AGENTS.md`; domain specs are indexed in
[`docs/README.md`](README.md). This file only maps the terrain.

## System at a glance

```
 Browser / Capacitor (android/, ios/) / Tauri (src-tauri/)
        │  all three load the same web app; shells are remote-URL thin clients
        ▼
 Next.js 16 web app ── app/ (routes) · components/ · hooks/ · contexts/ · lib/
        │  lib/api/client.ts → HTTPS + Bearer/CSRF
        ▼
 ASP.NET Core Minimal API ── backend/src/OetLearner.Api
        │  EF Core → PostgreSQL (+pgvector) · IFileStorage → media volume / S3
        │  SignalR hubs · BackgroundJobProcessor (Postgres SKIP LOCKED queue)
        ▼
 Providers: AI (via the grounded gateway only) · Stripe/PayPal/Whop/… · Brevo · LiveKit · Zoom · Bunny
```

Production: push to `main` → `.github/workflows/build-images.yml` builds GHCR
images (+ the migration SQL artifact) → `.github/workflows/production-deploy.yml`
applies migrations and runs `scripts/deploy/auto-deploy-ghcr.sh` blue/green on
the VPS (`docker-compose.production.yml`, project `oetwebsite`). The VPS never builds.

## Where things live

| Question | Answer |
| --- | --- |
| Routes / pages | `app/**/page.tsx` (App Router). Admin under `app/admin/`, expert under `app/expert/`. |
| UI components | Primitives in `components/ui/` (design-synced). Product UI in `components/domain/**`; admin in `components/admin/**`. |
| Frontend HTTP | `lib/api/client.ts` (transport: headers, CSRF, bearer, retries) → `lib/api.ts` facade re-exporting `lib/api/*.ts` domain clients. Never raw `fetch` outside the exceptions in AGENTS.md. |
| Frontend state / data hooks | `lib/query/` (TanStack Query keys + hooks), `lib/stores/` (zustand), `hooks/`, `contexts/`. |
| Shared frontend helpers | `lib/domain/` (datetime, format, chart palette), `lib/money.ts`, `lib/utils.ts` (`cn`). Shared DTO types in `lib/types/` and `lib/mock-data.ts` (despite the name: shared domain types). |
| Scoring | `lib/scoring.ts` ↔ backend `OetScoring` / `IAssessmentScoreConversionService`. Nowhere else. |
| Rulebooks | JSON in `rulebooks/` (embedded in the API, imported by `lib/rulebook/`). Writing packs are generated from `docs/canonical-rules/`. |
| API routes | `backend/src/OetLearner.Api/Endpoints/*Endpoints.cs`, one static class per area with a `MapX(this IEndpointRouteBuilder)` extension, mapped in `Program.cs`. |
| Business logic | `backend/.../Services/**`, one folder per domain (Writing, Speaking, Listening, Reading, Billing, Ai, Rulebook, Content, Companion, …). Big services are `partial` classes split as `X.<Area>.cs`. |
| Auth / authorization | `Program.cs` (JWT, ~45 policies), `Security/` (permission evaluator, CSRF guard, session revocation, device trust), role/permission constants in `Domain/AuthEntities.cs`. Endpoints opt in with `.RequireAuthorization("<Policy>")`; there is no fallback policy. |
| Errors | Services throw `ApiException`; the central handler in `Program.cs` maps it to `{code,message,fieldErrors,retryable,supportHint,correlationId}`. `lib/api/client.ts` reads `code`/`message`/`title`. |
| Database | `Data/LearnerDbContext*.cs` (partial per feature), entities in `Domain/`, hand-authored migrations in `Data/Migrations/` (ADR 0001: never ship raw `dotnet ef migrations add` output). |
| Configuration | Typed options in `Configuration/`. Secrets and mutable settings through `IRuntimeSettingsProvider` (encrypted DB value over env). Env templates: `.env*.example`. |
| AI calls | Only through `IAiGatewayService` (`Services/Rulebook/AiGatewayService.cs`) / `IDirectAiCallRecorder`; one `AiUsageRecord` per provider call. |
| Payments | `Services/Billing/**`; one gateway class per provider in `Billing/Gateways/`. One webhook handler per provider under `/v1/payment/webhooks/*`. |
| Files / media | `IFileStorage` / `S3CompatibleFileStorage` only; content uploads via `ContentPaper → ContentPaperAsset → MediaAsset`. |
| Background work | `Services/BackgroundJobProcessor*.cs` (queued jobs) and `BackgroundService` workers registered with `AddHostedService` in `Program.cs`. |
| Real-time | SignalR hubs in `Hubs/` (plus Notification/MockLiveRoom hubs under `Services/`). No backplane: hubs rely on the single active slot. |
| Frontend tests | Deleted 2026-10-08 (commit `f1b855bcc`, tag `last-commit-with-tests`): the co-located vitest suites and the Playwright suite in `tests/e2e/`. |
| Backend tests | Deleted 2026-10-08 (commit `f1b855bcc`, tag `last-commit-with-tests`): the `backend/tests/OetLearner.Api.Tests` xUnit suite. |
| CI | `.github/workflows/`: `build-images.yml` + `production-deploy.yml` (production), path-filtered gates for speaking, mobile, tauri, rulebooks. CI runs typecheck and lint only; test code was deleted 2026-10-08 (commit `f1b855bcc`). |
| Scripts | `scripts/` indexed in [`scripts/README.md`](../scripts/README.md); deploy-critical ones in `scripts/deploy/` and `scripts/ship/`. |
| Native shells | `capacitor.config.ts` + `android/` + `ios/` (remote URL shell), `src-tauri/` (remote-only desktop, IPC allow-list in `capabilities/`). |
| Sidecars | `agent-console/` (owner-only ops console, own workflow and compose file), `agent-gateway/` (dormant OpenAI-compatible gateway; routes off by default). |

## Where to put new code

- **New learner/admin feature:** route in `app/`, UI in `components/domain/<area>/`, client in
  `lib/api/<area>.ts` (re-export from `lib/api.ts` only if other modules expect it there),
  endpoint file `Endpoints/<Area>Endpoints.cs`, service in `Services/<Area>/`, DTOs in
  `Contracts/` or next to the endpoint if used only there, tests beside each.
- **A service passes ~2,000 lines:** make it `partial` and add `X.<Area>.cs` files. Keep the
  constructor, fields with initializers and shared private helpers in the core file.
- **A helper used by two services in the same domain:** an `internal static` class in that
  domain folder (see `Services/Writing/WritingServiceHelpers.cs`). Truly cross-domain helpers go in
  `Services/Common/`. Don't create `utils/`/`misc/` dumping grounds.
- **Configuration:** a typed options class in `Configuration/` or a runtime setting; never a
  new raw `Environment.GetEnvironmentVariable` in a service.
- **One-off data repair:** `scripts/ops/` (kept as the audit trail); scratch goes in
  `scripts/_*` (gitignored), never `ops/` or the repo root.

## Things that look odd but are intentional

- Android id `com.oetwithdrhesham.app` vs iOS `com.oetprep.learner`: frozen store identities.
- `lib/mock-data.ts` holds real shared types; renaming it would touch hundreds of imports.
- `docker-compose.staging.yml` is not a deploy path; `production-deploy.yml` ships only `docker-compose.production.yml`.
- Multi-exam scoring strategies (`Services/Scoring/`, `Services/ExamSession/`) compile and are
  unit-tested but are not wired into DI; OET scoring goes through `OetScoring` directly.
- ~8 EF entities have no reads or writes in code; they back live tables and stay.
