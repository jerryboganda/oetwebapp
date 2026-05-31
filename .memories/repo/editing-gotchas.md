# Editing Gotchas — OET Repo

## File Naming
- `_*`-prefixed files at repo ROOT are auto-deleted by `.gitignore`. Put scratch/diagnostic files in `backend/` or another subdirectory (e.g. `backend\_model_create.sql`).

## PowerShell + psql
- Never pass SQL with double quotes via `psql -c "..."` in PowerShell — quotes get mangled. Use a `.sql` file with `-f`.
- For quick inline queries, use single-quoted SQL: `psql -tAc 'SELECT 1;'`
- Multiline PowerShell chains with `&` and paths break when combined via `;`. Prefer separate commands or one-liner.

## dotnet Build
- Cold build ≈ 5:30. Always `dotnet build` after editing `.cs` files before `dotnet run --no-build`.
- The `--no-build` flag skips compilation — use only when you're certain binaries are current.
- `npm run backend:run` = `dotnet run` (includes build). `npm run backend:watch` = `dotnet watch`.

## EF Migrations
- Migrations are broken for fresh DB apply (model drift). Use model-rebuild approach via `backend\_model_create.sql`.
- `--emit-create-script <absolute-path>` flag in Program.cs generates full DDL from current EF model.
- After schema changes, re-emit: `dotnet run --project backend/src/OetLearner.Api/OetLearner.Api.csproj -- --emit-create-script C:\full\path\output.sql`
- New migrations still work for incremental changes on an existing DB (just can't apply from scratch via migrations alone).

## Auth / Identity
- ASP.NET Core Identity `PasswordHasher<ApplicationUserAccount>` PBKDF2 v3.
- Expert/Admin login REQUIRES `EmailVerifiedAt` to be non-null — set it in seed SQL.
- Admin requires a row in `AdminPermissionGrants` with at least one permission (e.g. `system_admin`).
- Expert requires a row in `ExpertUsers` with `IsActive=true`.
- Learner requires a row in `Users` with `AccountStatus='active'`.

## Frontend
- `NEXT_PUBLIC_API_BASE_URL` must match backend port (5198 in dev, NOT 8080/5062).
- `.env.local` may have stale port references — backend is definitively 5198 (http profile).
- Turbopack: `npx next dev --turbopack` (faster than webpack mode).

## Test Imports
- Vitest globals (`describe`, `it`, `expect`, `vi`) are auto-available — no imports needed.
- `motion/react` not `framer-motion`. Mock with Proxy pattern.
- `@testing-library/user-event` not `fireEvent` for async handlers.
