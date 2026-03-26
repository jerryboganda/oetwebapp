# OET Learner Backend

ASP.NET Core 10 + PostgreSQL learner backend for the OET web app.

## What is implemented

- Versioned learner API under `/v1`
- PostgreSQL-backed EF Core persistence
- Development auth handler for local frontend/backend integration
- Seeded learner data, taxonomy, content, attempts, evaluations, readiness, billing, reviews, mocks
- Background job processing for:
  - writing evaluation
  - speaking transcription
  - speaking evaluation
  - study plan regeneration
  - mock report generation
  - review completion
- Swagger/OpenAPI enabled
- Integration tests for critical learner flows

## Run locally

### 1) Start PostgreSQL + API with Docker

```bash
docker compose -f docker-compose.backend.yml up --build
```

### 2) Or run API directly

First start PostgreSQL separately, then:

```bash
dotnet run --project backend/src/OetLearner.Api/OetLearner.Api.csproj
```

The API runs on `http://localhost:5198` by default.

## Production deployment

Use the root-level production stack and VPS runbook in [DEPLOYMENT.md](../DEPLOYMENT.md).

## Frontend integration

Set this in the frontend env:

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:5198
```

## Useful commands

```bash
dotnet build backend/OetLearner.sln
dotnet test backend/OetLearner.sln
powershell -ExecutionPolicy Bypass -File .\scripts\probe-production.ps1
```

Or from `package.json`:

```bash
npm run backend:run
npm run backend:build
npm run backend:test
```
