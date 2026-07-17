# Shared Libraries (`lib/`)

Cross-cutting TypeScript utilities and domain helpers used by the OET Prep Platform frontend.

## What lives here

- **`api.ts`** / **`network/`** — typed API client and low-level networking helpers.
- **`scoring.ts`** — OET score conversion and pass/fail logic.
- **`rulebook/`** — rulebook adapters and resolution helpers.
- **`auth/`** — client-side auth schemas and session utilities.
- **`*ai*.ts`** — AI gateway prompt builders and grounded-completion helpers.
- **Top-level utilities** — Zod schemas, constants, formatting helpers, and shared types.

## Design rules

- Keep pure helpers free of React/Next.js imports when possible.
- Validate at boundaries with Zod.
- Never expose secrets or private paths.
- Route all app/component/hook HTTP calls through `apiClient`.
- Use `lib/scoring.ts` and `lib/rulebook/` instead of inlining thresholds or reading rulebook JSON directly.

## Running tests

From the repository root:

```powershell
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
```
