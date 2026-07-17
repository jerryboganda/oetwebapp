---
name: "Shared Libraries"
description: "Use when editing shared TypeScript utilities, API clients, scoring helpers, rulebook adapters, schemas, or any code under lib/."
applyTo: "lib/**/*.ts,lib/**/*.tsx"
---

# Shared Libraries (`lib/`)

This folder contains cross-cutting TypeScript code used by the Next.js frontend and (where noted) the backend contracts.

## Responsibilities

- `lib/api.ts` and `lib/network/**` — typed HTTP layer; all app/component/hook API calls go through `apiClient`.
- `lib/scoring.ts` — OET score conversion and pass/fail logic. Never inline pass thresholds elsewhere.
- `lib/rulebook/**` — rulebook resolution adapters and helpers. UI/endpoints must not read rulebook JSON directly.
- `lib/auth/**` — client auth utilities, schemas, and session helpers.
- `lib/**/*ai*.ts` — AI gateway clients and prompt builders. Every AI call records one usage row.
- Top-level `lib/*.ts` — domain utilities, constants, and shared schemas (Zod).

## Rules

- Keep utilities framework-agnostic when possible. Do not import Next.js or React into pure helpers.
- Validate external data at boundaries with Zod or existing typed helpers.
- Never expose secrets, tokens, or private paths from library code.
- Prefer direct imports; do not add new barrel files.
- For behavior changes, add or update unit tests in `tests/` or co-located `*.test.ts` files.

## Validation

```powershell
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
```
