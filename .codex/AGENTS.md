# Agent Instructions

## Package Manager
- Use pnpm with the committed `pnpm-lock.yaml`.
- On Windows PowerShell, prefer `cmd /c "pnpm run <script>"` when pnpm script execution is blocked.

## Core Commands
| Task | Command |
| --- | --- |
| Typecheck | `pnpm exec tsc --noEmit` |
| Lint | `pnpm run lint` |
| Frontend tests | `pnpm test` |
| Backend tests | `pnpm run backend:test` |
| Build | `pnpm run build` |
| Encoding gate | `pnpm run check:encoding` |

## Repo Scope
- Frontend: `app/`, `components/`, `contexts/`, `hooks/`, `lib/`.
- Backend: `backend/src/OetLearner.Api/`, `backend/tests/`.
- Desktop/mobile: `electron/`, `android/`, `ios/`, `capacitor-web/`.
- Infrastructure: `Dockerfile`, `docker-compose*.yml`, `scripts/backup/`.

## Key Conventions
- Preserve unrelated dirty worktree changes.
- Prefer existing app, API, auth, upload, scoring, and EF Core patterns.
- Keep shared contracts synchronized across frontend, backend, and tests.
- Use `-LiteralPath` for Windows paths containing spaces or route brackets.
- Use root `AGENTS.md`, `README.md`, and `docs/agent-operating-model.md` for deeper context.

## Production Checks
- Do not treat homepage `200` as sufficient.
- Verify container health, restart counts, logs, direct service health endpoints, migrations, and backups.
- For VPS database SQL from Windows, pipe SQL through SSH into `docker exec -i ... psql`.

## Commit Attribution
AI commits MUST include:
```text
Co-Authored-By: (the agent model's name and attribution byline)
```
