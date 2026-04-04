# OET Prep Agent Guide

This repository uses project-scoped skills installed through `skills.sh` and the vendored `autoskills` scanner in `.tools/autoskills/`.

## Skill Routing

Use the installed project skills based on the area being changed:

- `next-best-practices`, `vercel-react-best-practices`, `vercel-composition-patterns`, `tailwind-css-patterns`, and `typescript-advanced-types`
  Use for `app/`, `components/`, `contexts/`, `hooks/`, and frontend code in `lib/`.
- `frontend-design`, `accessibility`, and `seo`
  Use for UI polish, content hierarchy, metadata, accessibility, responsive behavior, and page-level quality improvements.
- `playwright-best-practices` and `vitest`
  Use for `tests/e2e/`, `playwright*.config.ts`, and frontend/unit test work.
- `nodejs-backend-patterns` and `nodejs-best-practices`
  Use for Node-based tooling, Electron support scripts, route handlers, and utility code.
- `electron-pro`
  Use for `electron/`, desktop packaging, preload security, updater flows, and desktop runtime behavior.
- `dotnet-best-practices`, `aspnet-minimal-api-openapi`, and `dotnet-design-pattern-review`
  Use for everything under `backend/`.

## Validation

Run the smallest relevant checks before closing work:

- Frontend: `npm run lint`, `npm test`, `npm run build`
- Backend: `npm run backend:test`, `npm run backend:build`
- Browser E2E: `npm run test:e2e` or `npm run test:e2e:smoke`
- Desktop E2E: `npm run test:e2e:desktop`

## Refreshing Skills

The repo keeps a vendored copy of `autoskills` at `.tools/autoskills/`.

Use the refresh script to re-scan the project and update project-scoped skills:

- Dry run:
  `powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1 -DryRun`
- Install/update:
  `powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1`

Default agents are `universal`, `codex`, and `claude-code`.
