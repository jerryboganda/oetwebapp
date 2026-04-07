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

## MCP Usage

- `jcodemunch-mcp` is installed in the user Python environment for this workspace and should be preferred for code navigation when available.
- For repository exploration, prefer the jcodemunch MCP workflow in `.claude/mcp/jcodemunch/CLAUDE.md` and `.claude/mcp/jcodemunch/AGENT_HOOKS.md` over raw file browsing.
- Keep this project on the no-venv path unless a dependency conflict forces isolation.
- `context-mode` is installed globally for this workspace; prefer its `ctx stats`, `ctx doctor`, `ctx upgrade`, and `ctx purge` flows when the request is about context savings or routing.
- VS Code Copilot routing for `context-mode` lives in `.vscode/mcp.json` and `.github/hooks/context-mode.json`; keep both files aligned with upstream expectations.
- `cc_token_saver_mcp` is cloned in `.claude/mcp/cc_token_saver_mcp` and registered in the user Claude config; prefer it for short, isolated generation/refactor/documentation/review subtasks when the local LLM endpoint is available.
- The `cc-token-saver` server launches from `.claude/mcp/cc_token_saver_mcp/launch.py` so it always starts in the repo root and can load its `.env` file.
- For this workspace, route every prompt through `cc-token-saver` first when the request can be handled by the local LLM; only fall back to the main model when the task needs repo-wide reasoning, multi-step orchestration, or the local server is unavailable.

## Refreshing Skills

The repo keeps a vendored copy of `autoskills` at `.tools/autoskills/`.

Use the refresh script to re-scan the project and update project-scoped skills:

- Dry run:
  `powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1 -DryRun`
- Install/update:
  `powershell -ExecutionPolicy Bypass -File .\scripts\refresh-autoskills.ps1`

Default agents are `universal`, `codex`, and `claude-code`.
