# Docker Status — This Machine

## Docker Desktop: REMOVED

Docker Desktop has been fully uninstalled from this Windows machine. All dev work runs natively:
- PostgreSQL 17 via Windows service `postgresql-17`
- .NET SDK 10.0.203 (dotnet CLI)
- Node.js 22.15.0 / npm 10.9.2

## AGENTS.md Docker Rules Override

AGENTS.md says "heavy tasks run in Docker." On THIS machine, that rule is suspended because Docker doesn't exist. Run everything natively:
- `dotnet build/run/test` directly
- `npm run dev/test/lint` directly  
- `npx tsc --noEmit` directly
- No `docker exec`, no `docker compose`, no container commands

## Validation Commands (native equivalents)

```powershell
# Type-check
npx tsc --noEmit

# Lint
npm run lint

# Unit tests
npm test

# Backend build
dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj

# Backend run
dotnet run --project backend/src/OetLearner.Api/OetLearner.Api.csproj --launch-profile http

# Backend test (if test project exists)
dotnet test backend/OetLearner.sln
```

## Do NOT attempt
- `docker exec oet-local-web ...` — container doesn't exist
- `docker compose -f docker-compose.local.yml ...` — Docker not installed
- `npm run docker:tsc` / `npm run docker:lint` / `npm run docker:test` — these need Docker
