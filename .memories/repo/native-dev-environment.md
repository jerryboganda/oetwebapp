# Native Windows Dev Environment — Reference

## Architecture (Docker REMOVED — native only)

- **Backend**: ASP.NET Core 10, port **5198** (`http` launch profile in `Properties/launchSettings.json`).
- **Frontend**: Next.js 15.4 + Turbopack, port **3000**. Start with `npx next dev --turbopack`.
- **Database**: PostgreSQL 17.2, Windows service `postgresql-17`, port 5432, creds `postgres/postgres`.
- **PG binaries**: `C:\Program Files\PostgreSQL\17\bin` (psql.exe, pg_isready.exe, etc.).
- **Toolchain**: .NET SDK 10.0.203, Node 22.15.0, npm 10.9.2, `dotnet ef` 10.0.7.
- **NO Docker Desktop** — everything runs natively. AGENTS.md Docker rules are overridden for this machine.
- **NO Visual Studio / MSVC / nmake / cl / vswhere** — cannot build native C extensions (pgvector).

## Databases

| Name | Purpose | Rules |
|------|---------|-------|
| `oet_learner_dev` | Development database | Safe to drop/recreate |
| `oet_learner` | Production-like (24 real users, PROD schema) | **NEVER touch** |

## Backend Startup

- Cold `dotnet build` ≈ 5:30. Always build before run if code changed.
- Run: `dotnet run --project backend/src/OetLearner.Api/OetLearner.Api.csproj --launch-profile http`
- Env vars needed: `ASPNETCORE_ENVIRONMENT=Development`, `PGPASSWORD=postgres`, `Bootstrap__SeedDemoData=false`
- Health: `GET http://localhost:5198/health` → `{"status":"ok","database":"ok",...}`
- Also: `/health/live`, `/health/ready`
- Auth endpoint: `POST /v1/auth/sign-in` with `{email, password, rememberMe}` body

## Frontend Startup

- Env vars: `NEXT_PUBLIC_API_BASE_URL=http://localhost:5198`, `APP_URL=http://localhost:3000`, `API_PROXY_TARGET_URL=http://127.0.0.1:5198`
- Run: `npx next dev --turbopack` (from project root)
- Hot reload built-in via Turbopack.

## Desktop Launchers

- **Start**: `C:\Users\Administrator\Desktop\Start OET App.bat` → calls `scripts/OET-Launch.ps1`
- **Stop**: `C:\Users\Administrator\Desktop\Stop OET App.bat` → calls `scripts/OET-Stop.ps1`
- Launcher steps: PG service start → backend runner → health poll (300s timeout) → frontend runner → poll (180s) → browser open
- Backend runner: `.local-deploy\runners\backend-api.cmd` (generated at launch)
- Frontend runner: `.local-deploy\runners\frontend-web.cmd` (generated at launch)
- Logs: `.local-deploy\logs\backend.log`, `.local-deploy\logs\frontend.log`

## Users (dev DB)

| Role | Email | Password | Auth ID |
|------|-------|----------|---------|
| admin | manwara575@gmail.com | 12345678 | admin-manwara575 |
| expert | xhsjs5901@gmail.com | 12345678 | expert-xhsjs5901 |
| learner | mindreader420123@gmail.com | 12345678 | student-mindreader420123 |

- Password hash (PBKDF2 v3): `AQAAAAIAAYagAAAAEKHy2WIBv9aeCwLYrwnkbsPHVodmSiOfVuic+8EM5UYdSZtbjeO1CvgGOJzPWkuy9w==`
- All have `EmailVerifiedAt` set (required for admin/expert login).
- Admin has `system_admin` permission grant.
- Expert has row in `ExpertUsers` (IsActive=true, specialty=nursing).
- Learner has row in `Users` (nursing, OET, active, onboarding done).

## Dev DB Rebuild Procedure (if needed)

The EF migrations are broken (32 tables never created by any migration, middle migrations ALTER non-existent tables). Use model-based rebuild:

```powershell
$env:PGPASSWORD = 'postgres'
$psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

# 1. Drop & recreate (ONLY oet_learner_dev!)
& $psql -U postgres -h localhost -c "DROP DATABASE IF EXISTS oet_learner_dev;"
& $psql -U postgres -h localhost -c "CREATE DATABASE oet_learner_dev;"

# 2. Apply EF model schema (pgvector stripped)
& $psql -U postgres -d oet_learner_dev -v ON_ERROR_STOP=1 -f "backend\_model_create.sql"

# 3. Stamp migration history (164 rows → boot won't try to re-migrate)
& $psql -U postgres -d oet_learner_dev -v ON_ERROR_STOP=1 -f "backend\_stamp_history.sql"

# 4. Create users
& $psql -U postgres -d oet_learner_dev -v ON_ERROR_STOP=1 -f "backend\_create_users.sql"
```

Result: 409 tables, 164 migrations stamped, head = `20260618090000_AddVocabularyFreePreview`.

## pgvector Status

- **Not installed** (no MSVC to build from source).
- Code tolerates missing extension: DatabaseConfiguration.cs:55 comment says "Harmless when the DB does not yet have the vector extension installed — the connection still works, only queries that touch a Vector column will fail."
- Two nullable `vector(1536)` columns omitted from dev schema: `WritingExemplarEmbeddings.Embedding` and `WritingScenarioEmbeddings.Embedding`.
- JSON fallback (`EmbeddingJson text`) is intact and used by `WritingExemplarEmbeddingService.FindClosestAsync` when vector column is null.
- `UseVector()` called unconditionally at DatabaseConfiguration.cs:55 and LearnerDbContextFactory.cs:40 — registers the type mapper but doesn't crash without the extension.
- To enable later: install pgvector for PG17, `CREATE EXTENSION vector;`, add the 2 columns back.

## Migration Drift Facts

- 32 tables exist in EF model snapshot but NO migration creates them (first appeared in snapshot of `20260527211242_Audit20260528_RulebookCompliance`).
- `20260530185804_AddWritingExamModuleClosure` ALTERs `WritingTutorReviews` which no migration creates → breaks migration-based apply.
- `20260526171052_AddListeningPathwaySchemaGenerated` had duplicate `ClassRecordingEmbeddings` (fixed — removed duplicate).
- `--emit-create-script` flag added to Program.cs (around line 1627): emits full EF DDL to a file, then exits. Needs ABSOLUTE output path.

## PowerShell Gotchas

- PowerShell mangles double-quoted SQL via `psql -c "..."` — use `.sql` file with `-f` instead.
- Or use multiple `-c` flags on one line (no semicolons inside quotes).
- `_*`-prefixed files at repo ROOT get auto-deleted by gitignore rules; put scratch files in `backend\_*.sql`.
- `${var}:` syntax needed when variable is immediately followed by `:` in double-quoted strings.
- `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` needed for unsigned scripts.

## Connection String (dev)

`Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres`

Configured in `backend/src/OetLearner.Api/appsettings.Development.json` under `ConnectionStrings:DefaultConnection`.

## Build Warnings (pre-existing, non-blocking)

37 warnings on clean build — all are nullable reference warnings and unused parameters in pre-existing code. Zero errors required for commit.
