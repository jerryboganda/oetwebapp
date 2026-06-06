---
name: "Frontend"
description: "Use when editing Next.js, React, TypeScript, Tailwind, motion, frontend routes, components, hooks, contexts, shared UI, or frontend API calls in the OET app."
applyTo: "app/**/*.ts,app/**/*.tsx,components/**/*.ts,components/**/*.tsx,contexts/**/*.ts,contexts/**/*.tsx,hooks/**/*.ts,hooks/**/*.tsx,lib/**/*.ts,lib/**/*.tsx,pages/**/*.ts,pages/**/*.tsx,middleware.ts,next.config.ts"
---

# Frontend Rules

Stack: Next.js 16 (App Router), React 19, TypeScript (strict), Tailwind CSS v4, motion v12.

## Structure & types

- Prefer Server Components. Add `'use client'` only for state, effects, browser APIs, or event handlers.
- Use TypeScript strictly. Avoid `any`, unchecked casts, and nullable-path assumptions.
- Null-check `useParams()` and `usePathname()` — they can return null.
- Use direct imports such as `@/components/ui/button`. Do not add new re-export-only barrels.

## Networking

- Frontend/backend HTTP calls go through `apiClient` or typed helpers in `lib/api.ts`.
- The full list of documented exceptions (route handlers, external URLs, analytics beacons, raw
  streaming/progress uploads, service-worker/runtime bridge code, lower-level `lib/network/**`)
  lives in `AGENTS.md` — follow that list; do not duplicate or invent new exceptions.
- For forms and external data, validate at boundaries with Zod (already used across the app,
  e.g. `lib/auth/schemas.ts`, `lib/writing/zod.ts`) or existing typed helpers.

## Animation

- Import animation from `motion/react`, never `framer-motion`.

## Theming (Tailwind v4, CSS-first)

- Dark mode is **class-based** via `next-themes` (`attribute="class"`). The `@custom-variant dark`
  in `globals.css` ensures `dark:` utilities only fire when `.dark` is on an ancestor.
- Never add `@media (prefers-color-scheme: dark)` rules. Use the standard `dark:` prefix only.
- Learner-facing cards/surfaces use light backgrounds (`bg-white`, `bg-surface`, token-based) with
  subtle colored borders/accents per `DESIGN.md`. Never use `bg-gray-900`, `bg-blue-950`, or
  saturated dark fills as the default learner card/container surface.

## Component API facts (prevent recurring mistakes)

- `Button` uses variant `primary` (not `default`).
- `Badge` uses variant `danger` (not `destructive`).
- `LearnerPageHeroModel` uses `description` (not `subtitle`).
- `CurrentUser` uses `userId`, `displayName`, `isEmailVerified`.

## Design & tests

- Follow `DESIGN.md` and existing UI primitives. For admin UI, also load `admin-hallmark.instructions.md`.
- Tests: prefer React Testing Library + `@testing-library/user-event`; avoid ambiguous regex selectors.
  See `testing.instructions.md`.

Validation commands run on the host — see `validation.instructions.md`.
