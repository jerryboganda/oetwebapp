---
name: "OET Frontend Next.js"
description: "Use when: editing Next.js, React, TypeScript, Tailwind, frontend routes, components, hooks, contexts, or frontend API calls."
applyTo: "app/**/*.ts,app/**/*.tsx,components/**/*.ts,components/**/*.tsx,contexts/**/*.ts,contexts/**/*.tsx,hooks/**/*.ts,hooks/**/*.tsx,lib/**/*.ts,lib/**/*.tsx"
---

# Frontend Rules

- Use Next.js App Router patterns. Prefer Server Components; add `use client` only when client state, effects, browser APIs, or event handlers require it.
- Use direct imports such as `@/components/ui/button`; do not add re-export-only barrels.
- Frontend/backend HTTP calls must use `apiClient` or typed helpers in `lib/api.ts`, except documented low-level/network/route-handler exceptions.
- `Button` uses variant `primary`, not `default`. `Badge` uses variant `danger`, not `destructive`.
- `LearnerPageHeroModel` uses `description`, not `subtitle`. `CurrentUser` uses `userId`, `displayName`, and `isEmailVerified`.
- Import animation from `motion/react`, not `framer-motion`.
- Keep UI changes aligned with `DESIGN.md` and existing component patterns. For admin UI, also load the admin Hallmark instruction.
- **Dark mode is class-based** (managed by `next-themes` with `attribute="class"`). The `@custom-variant dark` in `globals.css` ensures Tailwind `dark:` utilities only fire when the `.dark` class is on an ancestor — never from OS `prefers-color-scheme`. Do NOT add `@media (prefers-color-scheme: dark)` rules directly. All dark mode styling uses the standard `dark:` Tailwind prefix.
- **Learner-facing cards/surfaces must use light backgrounds** (`bg-white`, `bg-surface`, token-based) and subtle colored borders/accents per DESIGN.md ("Airy cream canvas, not dense dark chrome"). Never use `bg-gray-900`, `bg-blue-950`, or saturated dark fills as the default card/container surface on learner pages.