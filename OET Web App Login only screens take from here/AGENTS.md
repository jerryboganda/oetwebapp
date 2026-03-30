# OET Auth Operating Guide

## Repository Shape

- This repo is now a focused Next.js 15 auth-only app for the OET flow.
- The root app shell lives in [`src/app/layout.tsx`](src/app/layout.tsx).
- Shared auth components and styles live under [`src/app/auth-pages/_components`](src/app/auth-pages/_components).
- Auth helpers, mock flow logic, validation, and enrollment data live under [`src/lib/auth`](src/lib/auth).
- There is no backend service, dashboard workspace, mobile app, or CI workflow in the current checkout.

## Commands

- Use `npm run dev` for local development.
- Use `npm run build` before treating source changes as complete.
- Use `npm run lint` for code-quality checks.
- Use `npm run start` to smoke the production build.
- Use `npm run format` only when formatting the repo intentionally.
- There is no checked-in `typecheck` or `test` script today; add one before depending on it.

## Done Means

- The changed auth behavior matches the intended result.
- `npm run build` and `npm run lint` pass when code changes are involved.
- UI-heavy auth changes are checked in the browser or by targeted validation.
- Relevant docs and plans are updated when routes, commands, or operating assumptions change.
- No deleted dashboard/template code is accidentally reintroduced.

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

- Do not change auth route contracts or flow sequencing without checking all linked screens first.
- Do not add dashboard, CMS, or learner workspace assumptions back into the repo unless explicitly requested.
- Do not add backend or mobile infrastructure assumptions; this repository does not currently have those layers.
