# PolytronX Next Operating Guide

## Repository Shape
- This repo is a Next.js 15 admin template with a single global app shell, many route-group pages, a small set of App Router API handlers, and a few mock server actions.
- The main application shell lives in [`src/app/layout.tsx`](src/app/layout.tsx) and [`src/Component/Layouts/DefaultLayout.tsx`](src/Component/Layouts/DefaultLayout.tsx).
- Navigation and shell behavior are driven by [`src/Data/Sidebar/sidebar.ts`](src/Data/Sidebar/sidebar.ts), [`src/Component/Layouts/Sidebar/MenuItem.tsx`](src/Component/Layouts/Sidebar/MenuItem.tsx), [`src/Component/Layouts/Sidebar/HorizontalNav.tsx`](src/Component/Layouts/Sidebar/HorizontalNav.tsx), and [`src/Component/Customizer/index.tsx`](src/Component/Customizer/index.tsx).
- Data fixtures and template content live under `src/Data`; shared shell components live under `src/Component`; route handlers and page groups live under `src/app`.
- There is no backend service, mobile app, CI workflow, or test suite in the current checkout.

## Commands
- Use `npm run dev` for local development.
- Use `npm run build` before treating source changes as complete.
- Use `npm run lint` for code-quality checks.
- Use `npm run start` to smoke the production build.
- Use `npm run format` only when formatting the repo intentionally.
- There is no checked-in `typecheck` or `test` script today; add one before depending on it.

## Done Means
- The changed behavior matches the intended result.
- `npm run build` and `npm run lint` pass when code changes are involved.
- Shared-shell changes are checked in the browser or by targeted validation when the surface is UI-heavy.
- Relevant docs and plans are updated when the work changes architecture, commands, routes, contracts, or operating assumptions.
- No unrelated files are churned and no known shared-shell regression is left unverified.

## Subagents
- Use `repo_cartographer` for read-only architecture mapping and file-path tracing.
- Use `execplan_strategist` for multi-step work that crosses routes, layouts, data fixtures, and validation.
- Use `frontend_owner` for UI and route-group changes.
- Use `api_contract_guard` whenever request/response payloads, server actions, or route handlers change.
- Use `qa_validator` before wrap-up for targeted verification.
- Use `security_reviewer` for auth, storage, external fetches, or any code that can expose data.
- Use `refactor_guard` for shared-shell refactors and coupling-heavy cleanup.
- Keep agent fan-out shallow. Prefer direct children only unless there is a very strong reason to recurse.

## Plans And Documentation
- Create or update `docs/PLANS.md` before large features, refactors, migrations, or cross-cutting cleanup.
- Record lasting decisions in `docs/decision-log.md` when the repo learns something future work should not rediscover.
- Treat repo intelligence as durable documentation, not chat memory.

## Worktrees
- Use a worktree when the task is isolated, long-running, experimental, or likely to touch shared-shell files.
- Keep shared-shell edits serialized. Do not let multiple workers rewrite the layout, sidebar, customizer, or data-table layers at once.
- Use separate worktrees for independent feature streams when parallelism will reduce collisions.

## Research
- Use official docs, local repo evidence, and targeted browser verification when the task depends on framework behavior or UI output.
- Prefer docs/web research only when repo inspection is not enough or when the behavior is version-sensitive.
- Keep research targeted and cite the specific source or file path in the summary.

## Approval Boundaries
- Do not touch shared shell/navigation files, config files that affect the whole app, or dependency management without a clear reason and a validation plan.
- Do not change external API route behavior, auth-action flow, or storage key names without checking the consumer paths.
- Do not rename or normalize template typos, aliases, or route strings as part of a bootstrap pass unless the change is explicitly scoped and verified.
- Do not add mobile or backend infrastructure assumptions; this repository does not currently have those layers.
