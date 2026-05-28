---
name: "OET Docker Heavy Tasks"
description: "Use when: running builds, tests, installs, lint, type-checks, Playwright, dotnet, Docker, desktop, mobile, scripts, or validation in the OET repo."
applyTo: "package.json,package-lock.json,Dockerfile,docker-compose*.yml,backend/**,app/**,components/**,contexts/**,hooks/**,lib/**,tests/**,playwright*.config.ts,vitest.config.ts,scripts/**"
---

# Docker Heavy Task Rules

- Heavy validation runs in local Docker Desktop containers, not directly on Windows and not on the VPS.
- Use `docker exec oet-local-web <command>` for frontend lint, type-check, tests, and builds.
- Use `docker exec oet-local-api <command>` for backend build, restore, test, publish, and EF operations.
- Allowed host operations are lightweight reads, file edits, `git status`/diff/log/show, trivial scoped searches, and Docker orchestration commands.
- If Docker Desktop is not running, report the blocker. Do not fall back to host `npm`, host `dotnet`, or VPS validation.
- The VPS `oet-dev` is production deployment only. Never run test, build, lint, or exploratory validation commands there.
- Historical PRD or PROGRESS notes that mention VPS validation are stale when they conflict with `AGENTS.md`.

## Storage Persistence (MISSION CRITICAL)

- Every `docker-compose*.yml` that runs the API MUST set `Storage__LocalRootPath: /var/opt/oet-learner/storage`.
- Without this, files go to the default `App_Data/storage` inside the container filesystem and are **permanently deleted** on rebuild.
- All file I/O MUST route through `IFileStorage`. Never use `File.*` / `Path.*` / `Directory.*` directly for media data.
- Named volumes (`oet_local_storage`, `oet_learner_storage`, `oetwebsite_oet_learner_storage`) persist across rebuilds.
- **NEVER** run `docker compose down -v` or `docker volume rm` on storage/postgres volumes without a verified backup.
- The backend crashes in Production (or logs CRITICAL in Dev) if it detects a relative storage path inside a container.