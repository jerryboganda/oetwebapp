---
description: "Use when editing ASP.NET Core, Minimal API, EF Core, PostgreSQL, backend auth, services, DTOs, or migrations."
name: "OET Backend"
applyTo: ["backend/**/*.cs", "backend/**/*.csproj", "backend/**/*.sln", "backend/**/*.json"]
---
# OET Backend Instructions

- Keep Minimal API endpoint files thin. Put business logic in services and contracts in DTOs.
- Use dependency injection and explicit service boundaries. Do not reach around services from endpoints.
- Enforce auth and authorization server-side for every sensitive action.
- Use EF Core safely with parameterized queries and migrations. Do not concatenate SQL with user input.
- Prefer immutable request/response DTOs, nullable annotations, and cancellation tokens on async operations.
- Validate request data before persistence or downstream service calls.
- Log useful server-side context without leaking secrets, tokens, PII, stack traces, or private paths to clients.
- Preserve `AiUsageRecord` behavior for AI calls: success, provider error, and refusal must each record exactly one usage row.
- For PostgreSQL production changes, use expand-contract patterns, avoid table-locking migrations, and document rollback or forward-fix plans.
- Verify with `npm run backend:build` and `npm run backend:test` when backend code changes warrant it.