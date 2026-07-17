# AGENTS.md - OET Prep Platform

> **Synchronized with `CLAUDE.md`.** This file is the authoritative source of truth
> for the repository-wide agent instructions. `CLAUDE.md` and
> `.github/copilot-instructions.md` are copies. If a rule conflicts with this file,
> this file wins. Update all three files together when changing repo-wide rules.

This file is always loaded. Keep startup lean and defer detail until it is actually needed.

## Source Of Truth

1. `AGENTS.md` (this file)
2. `CLAUDE.md`
3. `.github/copilot-instructions.md`
4. Matching `.github/instructions/*.instructions.md` files
5. Domain docs referenced by `AGENTS.md`
6. Nearby code and tests

Repository-specific OET rules win over generic framework, skill, plugin, or agent defaults.
Before product catalogue, checkout, entitlement, dashboard, add-on, Tutor Book, or course expiry work, load `docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md` and preserve its product IDs, pricing, flags, entitlement templates, and acceptance criteria.

## Lean Context Policy

- Do not eager-load broad skill catalogs, prompt libraries, generated bundles, or whole-codebase docs.
- The generic vendored catalogs that lived under `.github/agents/`, `.github/skills/`, and `.github/awesome-copilot/` have been removed. Only project-local OET agents (`oet-*`) remain in `.github/agents/` and a single useful skill (`acreadiness-assess`) in `.github/skills/`. Do not restore broad `awesome-*` catalogs without an explicit user request. Project-local Codex skills live under `.codex/skills/oet-*`.
- Load a skill, agent, or doc only when the current task clearly needs it.
- Prefer targeted searches and local file reads over Repomix or broad scans.

## Default Workflow

- For non-trivial work, first read `PROGRESS.md` and `.github/agent-state.local.md` if present; continue from the state file only when it matches the newest user request.
- Classify the task area, inspect existing patterns, and identify invariants.
- Use a todo list for multi-step work.
- Prefer focused tests for behavior changes and bug fixes.
- Make minimal edits that fit existing boundaries.
- Review the diff for OET contracts, security, tests, and regressions.
- Verify with the lightest meaningful host command before reporting done.
- Before handoff, update `.github/agent-state.local.md` with goal, touched files, validation, blockers, and next concrete step.

Ask only when a missing decision blocks correctness or safety.

## Routing

- Bugs/failing commands: reproduce or inspect the failure, identify root cause, fix incrementally, rerun focused validation.
- Frontend: follow Next.js App Router, React 19, TypeScript, Tailwind, direct imports, `motion/react`, and `apiClient` rules.
- Backend: follow ASP.NET Core Minimal API, EF Core, PostgreSQL, DI services, DTO contracts, cancellation tokens, and server-side authorization.
- Security/auth/AI/uploads/scoring/rulebooks/runtime settings/deployment: load the matching domain docs before editing.
- Admin UI: load admin Hallmark instructions and keep operational UI dense, restrained, accessible, and scan-friendly.
- Review/audit requests: lead with findings ordered by severity.

## 🚢 Ship-It Workflow — COMPULSORY (owner directive 2026-07-05)

Standing owner directive for **every** development/debugging task. Overrides any "only push when asked" default and any nudge toward heavy pre-merge testing:

1. Do the task properly (correctness/root-cause still matter).
2. Run ONE lightweight, targeted check (touched-area typecheck/build, or the single relevant test, or a quick repro). **No full-length, multi-suite test marathons.** Don't block on flaky CI (QA Smoke is chronically red — ignore it).
3. **Fast-feedback rule:** never run lengthy builds, heavy CI flows, or full validation suites unless the user explicitly asks. Prefer small quick checks, focus on coding, and trust the user to report any errors.
4. Commit → push to `main` → deploy to production (`gh pr merge <#> --squash --admin --delete-branch`, or push `main`; pushing `main` triggers the blue/green prod deploy). Stage explicit paths, never `git add -A`. Never commit secrets/`.env*`.
5. Report what shipped in 1–2 lines and STOP. **The owner verifies on live production** and reports back any issue. Don't linger on CI or re-test.

Only skip the auto-push if the user explicitly says "don't push" for that task.

## Execution Locality

Local validation runs directly on the Windows host via PowerShell or `cmd` (Node 22.x, pnpm 10.33.0,
.NET 10.x installed):

- Frontend: `pnpm exec tsc --noEmit`, `pnpm run lint`, `pnpm test`, `pnpm run build`.
- Backend: `pnpm run backend:build`, `pnpm run backend:test`.
- If PowerShell quoting breaks a script, use `cmd /c "pnpm run <script>"`.

Do not run validation on the production VPS. Docker compose files are for deployment/packaging, not a
required local validation path. See `.github/instructions/validation.instructions.md`.

## Storage Persistence

All media/user files must use persistent storage at `/var/opt/oet-learner/storage`.

- Every API-running `docker-compose*.yml` must set `Storage__LocalRootPath: /var/opt/oet-learner/storage`.
- Media/user file I/O must go through `IFileStorage` or `S3CompatibleFileStorage`.
- Never use raw `File.*`, `Path.*`, or `Directory.*` for media/user data.
- Never run `docker compose down -v`, `docker volume rm`, or recreate postgres or storage volumes without a verified backup and explicit approval.

## Frontend Rules

- Use App Router pages under `app/**/page.tsx`; prefer Server Components.
- Add `'use client'` only when needed.
- `useParams()` and `usePathname()` can return null; guard them.
- Import motion from `motion/react`, not `framer-motion`.
- Prefer direct imports over new barrel files.
- HTTP calls from app/components/hooks/lib go through `apiClient` or typed helpers in `lib/api.ts`, except route handlers, external URLs, analytics beacons, raw streaming/progress uploads, service-worker/runtime bridge code, or lower level `lib/network/**` implementation.
- Component API gotchas: `Badge` variant is `danger`; `Button` variant is `primary`; `LearnerPageHeroModel` uses `description`; `CurrentUser` uses `userId`, `displayName`, and `isEmailVerified`.

## Backend Rules

- Minimal API endpoints live under `backend/src/OetLearner.Api/Endpoints/`.
- Services, DTOs, entities, data, security, and configuration stay in their existing backend folders.
- Use DI, cancellation tokens where appropriate, server-side authz, and EF Core PostgreSQL patterns already present in the codebase.

## OET Domain Invariants

Load the named docs before editing these surfaces.

- Product portfolio: before editing product catalogue, checkout, entitlements, dashboards, add-ons, writing assessments, speaking sessions, Tutor Book access, recalls, or course expiry logic, read `docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md` and preserve its product IDs, pricing, flags, entitlement templates, and acceptance criteria.
- Scoring: use `lib/scoring.ts` or `OetScoring`; never inline pass thresholds. Anchor: Listening/Reading `30/42 == 350/500`; Writing is country-aware; Speaking is 350. See `docs/SCORING.md`.
- Rulebooks: use `lib/rulebook` or backend Rulebook services; never read rulebook JSON directly from UI/endpoints. See `docs/RULEBOOKS.md`.
- AI calls: route through grounded gateway helpers/services; every call records one usage row; never bypass grounding. See `docs/AI-USAGE-POLICY.md`.
- Content uploads: use `ContentPaper -> ContentPaperAsset -> MediaAsset`, chunked admin upload endpoints, `IFileStorage`, provenance, publish gates, and audit events. See `docs/CONTENT-UPLOAD-PLAN.md`.
- Statement of Results: do not restyle the CBLA-style card; use `lib/adapters/oet-sor-adapter.ts`. See `docs/OET-RESULT-CARD-SPEC.md`.
- Reading, grammar, pronunciation, and conversation are server-authoritative; preserve their scoring, rulebook, ASR/TTS/provider, entitlement, retention, and publish-gate contracts. See the matching docs in `docs/`.
- Runtime settings/secrets: services read through `IRuntimeSettingsProvider`, with encrypted DB value over env fallback and audit on writes. See `docs/ADMIN-RUNTIME-SETTINGS.md`.

## Admin UI

For `app/admin/**`, `components/domain/admin/**`, or `components/admin/**`, load `.github/instructions/admin-hallmark.instructions.md` and preserve the admin design discipline. Do not apply generic landing-page treatment to admin tools.

## Validation Ladder

Run the smallest relevant host command, then expand if risk demands it:

```powershell
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
pnpm run build
pnpm run backend:build
pnpm run backend:test
pnpm run check:encoding
pnpm run test:e2e:smoke
```

Report exactly what ran, what did not run, and any remaining risk.

## Map Of AI-Direction Files

Precedence: this `AGENTS.md` / `CLAUDE.md` and `.github/copilot-instructions.md` are always on. File-scoped
instructions load by `applyTo` glob. Repo rules beat generic skill/agent/plugin defaults.

- `AGENTS.md` — always-on repo contract; authoritative for storage persistence, the `apiClient`
  exception list, OET domain invariants, and this file map.
- `CLAUDE.md` — always-on Claude Code copy of the repo contract.
- `.github/copilot-instructions.md` — always-on lean startup: source of truth, lean context, routing.
- `.github/instructions/agentic-workflow.instructions.md` — continuity protocol, default loop, agents.
- `.github/instructions/frontend.instructions.md` — Next.js/React/TS/Tailwind/motion UI rules.
- `.github/instructions/backend.instructions.md` — ASP.NET Core / EF Core / services / DTOs.
- `.github/instructions/security-ai.instructions.md` — canonical security, AI grounding, scoring,
  rulebooks, secrets, prompt defense.
- `.github/instructions/testing.instructions.md` — Vitest/RTL/Playwright/xUnit conventions.
- `.github/instructions/validation.instructions.md` — host validation command ladder.
- `.github/instructions/deployment.instructions.md` — Docker/CI/CD/storage/VPS/desktop/mobile.
- `.github/instructions/admin-hallmark.instructions.md` — admin operational UI discipline.
- `.codex/AGENTS.md` — Codex-CLI agent operating model (host commands, production checks, commit attribution).
- `.tools/autoskills/AGENTS.md` — scoped to `.tools/autoskills/` only (pnpm supply-chain hardening).
