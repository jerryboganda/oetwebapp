---
description: "Use when editing Next.js, React, TypeScript, Tailwind, motion, frontend API, or shared UI files in the OET app."
name: "OET Frontend"
applyTo: ["app/**/*.ts", "app/**/*.tsx", "components/**/*.ts", "components/**/*.tsx", "contexts/**/*.ts", "contexts/**/*.tsx", "hooks/**/*.ts", "hooks/**/*.tsx", "lib/**/*.ts", "lib/**/*.tsx", "pages/**/*.ts", "pages/**/*.tsx", "middleware.ts", "next.config.ts"]
---
# OET Frontend Instructions

- Use Next.js App Router patterns. Prefer Server Components; add `'use client'` only for state, effects, browser APIs, or event handlers.
- Use TypeScript strictly. Avoid `any`, unchecked casts, and nullable path assumptions.
- Null-check `useParams()` and `usePathname()` results.
- Import animations from `motion/react`, not `framer-motion`.
- Use direct imports such as `@/components/ui/button`; do not add new re-export-only barrels.
- Frontend/backend HTTP calls must use `apiClient` or typed helpers in `lib/api.ts`, except documented exceptions in `AGENTS.md`.
- Preserve component API facts: `Badge` uses `danger`, `Button` uses `primary`, `LearnerPageHeroModel` uses `description`, and `CurrentUser` uses `userId`, `displayName`, `isEmailVerified`.
- Follow `DESIGN.md` and existing UI primitives. Keep operational tools dense, readable, and responsive.
- For forms and external data, validate at boundaries with Zod or existing typed helpers.
- Tests should prefer React Testing Library and `@testing-library/user-event`; avoid ambiguous regex selectors.