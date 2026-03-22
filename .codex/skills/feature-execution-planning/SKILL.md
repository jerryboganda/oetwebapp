---
name: feature-execution-planning
description: Turn repo goals into phased, dependency-aware execution plans. Use when a task spans multiple files, routes, shared shell components, validation steps, or needs a clear plan before code changes.
---

# Feature Execution Planning

## Purpose
- Turn a repo goal into a concrete plan with milestones, risks, dependencies, and validation.
- Keep the plan executable so another agent can start work without guessing.

## Workflow
- Start from the actual repository shape and identify the files, routes, or shells that matter.
- Separate discovery, implementation, and validation into distinct phases.
- Call out what can run in parallel and what must stay serialized.
- Write acceptance criteria before edits begin.

## Repo context
- Next.js 15 App Router with routes under `src/app` and a single global shell.
- Shared shell lives in `src/app/layout.tsx` and `src/Component/Layouts/DefaultLayout.tsx`.
- Small API surface: three `route.ts` handlers and four server action modules.
- No tests, CI, or lockfile, so plan validation around `npm run lint` and `npm run build`.

## Output
- Summarize the goal, scope, assumptions, and key risks.
- Name the exact files or directories that will change.
- Include the narrowest validation steps that prove the work is done.
- Keep the result concise and decision-ready.
