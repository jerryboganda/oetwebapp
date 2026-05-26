---
name: "OET Backend ASP.NET Core"
description: "Use when: editing ASP.NET Core, Minimal API endpoints, EF Core, services, DTOs, runtime settings, storage, auth, or backend tests."
applyTo: "backend/**/*.cs,backend/**/*.csproj,backend/**/*.json,backend/**/*.sln"
---

# Backend Rules

- Follow the Minimal API structure under `backend/src/OetLearner.Api`: endpoints in `Endpoints/`, services in `Services/`, contracts in `Contracts/`, entities in `Domain/`, data in `Data/`.
- Keep pass/fail and scaled score logic inside `OetLearner.Api.Services.OetScoring`; do not inline score thresholds.
- Route Writing/Speaking/Grammar/Pronunciation/Conversation rules through rulebook services, not direct JSON reads from endpoint or UI code.
- Every AI invocation must use the grounded gateway and usage recording path; do not add ungrounded prompts or direct provider calls.
- Content upload and audio/media storage must go through `IFileStorage` or the domain storage service. Do not write raw files with `File.*` or `Path.*` from service code.
- Runtime secrets must flow through `IRuntimeSettingsProvider.GetAsync()` with env fallback. Do not read mutable secrets directly from `IOptions<T>` in services.
- Run backend build/test checks through `docker exec oet-local-api`, never host `dotnet` for heavy work.