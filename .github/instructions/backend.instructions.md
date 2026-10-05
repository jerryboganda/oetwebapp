---
name: "Backend"
description: "Use when editing ASP.NET Core, Minimal API endpoints, EF Core, PostgreSQL, services, DTOs, entities, runtime settings, storage, backend auth, or backend tests."
applyTo: "backend/**/*.cs,backend/**/*.csproj,backend/**/*.json,backend/**/*.sln"
---

# Backend Rules

Stack: ASP.NET Core Minimal API, EF Core, PostgreSQL, SignalR, .NET 10.

## Structure

Follow the layout under `backend/src/OetLearner.Api`:

- `Endpoints/` — thin Minimal API endpoint files. No business logic.
- `Services/` — business logic and service boundaries.
- `Contracts/` — request/response DTOs.
- `Domain/` — entities.
- `Data/` — EF Core context, configuration, migrations.

Keep endpoints thin; do not reach around services from endpoints. Use dependency injection,
immutable DTOs, nullable annotations, and cancellation tokens on async operations.

- Services over ~2,000 lines are `partial` and split as `X.<Area>.cs`; the core file keeps the
  primary constructor, every field with an initializer, and shared private helpers.
- Every route declares its auth intent: `.RequireAuthorization(...)` (usually on the group) or an
  explicit `.AllowAnonymous()`. There is no fallback policy; `EndpointRegistrationTests` enforces it.

## Errors

- New or edited code signals failures with `throw ApiException.<Kind>(code, message)`
  (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `PaymentRequired`,
  `TooManyRequests`, `ServiceUnavailable`). The central handler in `Program.cs` turns it into
  `{code, message, fieldErrors, retryable, supportHint, correlationId}`, which `lib/api/client.ts`
  reads.
- Inline `Results.X(new { error = ... })`, raw-string bodies and `Results.Problem` are legacy
  shapes. Don't add new ones, and don't "fix" existing ones without checking the frontend
  consumer: changing a response shape is a contract change.
- Never echo `ex.Message` from framework/EF exceptions to clients.

## Data & safety

- Use EF Core with parameterized queries and migrations. Never concatenate SQL with user input.
- Validate request data before persistence or downstream service calls.
- Enforce auth and authorization server-side for every sensitive action.
- Log useful server-side context without leaking secrets, tokens, PII, stack traces, or private
  paths to clients.
- For PostgreSQL production changes, use expand-contract patterns, avoid table-locking migrations,
  and document rollback or forward-fix plans.

## OET domain invariants (pointers — full rules live in security-ai + docs)

- Scoring: keep pass/fail and scaled-score logic in `OetLearner.Api.Services.OetScoring`. Never
  inline thresholds. See `docs/SCORING.md`.
- Rulebooks: route Writing/Speaking/Grammar/Pronunciation/Conversation rules through rulebook
  services. No direct rulebook JSON reads from endpoints. See `docs/RULEBOOKS.md`.
- AI: every invocation uses the grounded gateway and records exactly one `AiUsageRecord`. See
  `security-ai.instructions.md` and `docs/AI-USAGE-POLICY.md`.
- Storage: media/audio I/O goes through `IFileStorage` / the domain storage service. Never use raw
  `File.*` / `Path.*` / `Directory.*` for media data. See `deployment.instructions.md`.
- Runtime secrets: read through `IRuntimeSettingsProvider.GetAsync()` with env fallback. Do not read
  mutable secrets directly from `IOptions<T>` in services. See `docs/ADMIN-RUNTIME-SETTINGS.md`.

Validation: the API compiles in `Build images` (`dotnet publish`); no `dotnet test` runs anywhere (owner directive 2026-10-06, the owner QAs manually) — see `validation.instructions.md`.
