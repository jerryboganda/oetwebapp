# AGENTS.md - OET Prep Platform

> Multi-surface OET preparation platform with a Next.js 15 web app, ASP.NET Core 10 API, Electron desktop shell, and Capacitor 6 mobile shell.

## Local Baseline

- Frontend: Next.js 15.4, React 19, TypeScript 5.9, Tailwind CSS 4, motion v12
- Backend: ASP.NET Core 10, EF Core, PostgreSQL 17, SignalR
- Desktop: Electron 41
- Mobile: Capacitor 6
- Default local URLs:
  - Frontend: `http://localhost:3000`
  - Backend: `http://localhost:5198`
- .NET SDK: `10.0.201`

## Read First

Read the repo spine before changing code:

- `AGENTS.md`
- `README.md`
- `docs/agent-operating-model.md`
- `docs/SCORING.md`
- `docs/RULEBOOKS.md`
- `docs/AI-USAGE-POLICY.md` if you are touching AI flows
- `docs/CONTENT-UPLOAD-PLAN.md` if you are touching uploads
- `docs/OET-RESULT-CARD-SPEC.md` if you are touching the result card
- `docs/READING-AUTHORING-PLAN.md`
- `docs/READING-AUTHORING-POLICY.md`
- `docs/plan/`
- `docs/superpowers/plans/`

## Setup

```bash
npm install
npm run dev
npm run backend:run
npm run backend:watch
npm run desktop:dev
npm run mobile:sync
npm run mobile:run:android
npm run mobile:run:ios
```

If PowerShell blocks local scripts, run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

## Verification

Always validate before you finish work.

```bash
npx tsc --noEmit
npm run lint
npm test
npm run build
npm run backend:build
npm run backend:test
```

E2E checks:

```bash
npm run test:e2e:install
npm run test:e2e:auth
npm run test:e2e
npm run test:e2e:smoke
npm run test:e2e:desktop
npm run test:e2e:report
```

## Working Rules

- Preserve unrelated edits. If the checkout is dirty, inspect `git status` first and do not revert work you did not make.
- Prefer a separate worktree for multi-file or risky work. Keep shared-contract changes isolated and serialized.
- Use shallow, direct-child agents when the task benefits from delegation. Start with:
  - `repo_cartographer`
  - `execplan_strategist`
  - `backend_owner`
  - `frontend_owner`
  - `api_contract_guard`
  - `qa_validator`
- Do not add new project skills or MCP servers during bootstrap unless the task explicitly requires them.
- Keep the current Codex routing stack intact:
  - `jcodemunch-mcp`
  - `cc_token_saver_mcp`
  - `context-mode`

## Serialized Paths

Coordinate before editing any of these surfaces:

- `backend/src/OetLearner.Api/Program.cs`
- `lib/api.ts`
- `lib/auth-client.ts`
- `lib/scoring.ts`
- `lib/rulebook/index.ts`
- `middleware.ts`
- `next.config.ts`
- `capacitor.config.ts`
- `electron/`
- `components/domain/OetStatementOfResultsCard.tsx`

## Critical Guardrails

- All scoring must go through `lib/scoring.ts` or `OetLearner.Api.Services.OetScoring`. Never inline pass/fail thresholds.
- All writing and speaking rule enforcement must go through `lib/rulebook` or `OetLearner.Api.Services.Rulebook`.
- All AI calls must use the grounded gateway (`buildAiGroundedPrompt()` in TypeScript or `AiGatewayService.BuildGroundedPrompt()` plus `CompleteAsync()` in .NET).
- The result card in `components/domain/OetStatementOfResultsCard.tsx` is contract-driven. Do not restyle it without the spec and a pixel check.
- Content uploads must go through `IFileStorage`. Do not write raw upload files directly.
- Reading authoring must preserve the canonical `20 + 6 + 16 = 42` item structure.
- Import motion components from `motion/react`, not `framer-motion`.
- `Badge` uses variant `danger`, not `destructive`.
- `Button` uses variant `primary`, not `default`.
- `useParams()` and `usePathname()` can return `null`; always null-check them.

## Backend Rules

- Backend code lives in `backend/src/OetLearner.Api/`.
- Use Minimal API patterns with endpoints in `Endpoints/`, services in `Services/`, DTOs in `Contracts/`, entities in `Domain/`, and migrations in `Data/Migrations/`.
- The backend local port is `5198`.
- Keep auth, scoring, rulebook, and public API contract changes tightly scoped and well tested.

## Frontend Rules

- App Router only. All pages live under `app/*/page.tsx`.
- Server Components are the default. Add `use client` only when necessary.
- Use Tailwind CSS 4 utilities. Avoid CSS modules.
- Keep React and UI work aligned with the existing design system and motion conventions.

## Deployment Notes

- Production frontend: `app.oetwithdrhesham.co.uk`
- Production API: `api.oetwithdrhesham.co.uk`
- Production Docker project: `oetwebsite`
- Web healthcheck: `GET /api/health`
- Docker volumes use the `oetwebsite_` prefix
- Never delete postgres data without a backup
