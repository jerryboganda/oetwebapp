# Decision Log

## 2026-03-22

- Treat this checkout as a frontend-only Next.js 15 admin template for operating-model purposes.
- Document the app as a single-shell template with route-group pages, thin route handlers, and mock server actions rather than a backend-driven product.
- Add repo-level Codex guidance in `AGENTS.md` and keep the generated documentation site under `documentation/` untouched.
- Prefer shallow, direct-child subagents only. Use `repo_cartographer`, `execplan_strategist`, `frontend_owner`, `api_contract_guard`, `qa_validator`, `security_reviewer`, and `refactor_guard` as the default project roles.
- Record known drift instead of fixing it in the bootstrap pass: stale aliases, Sass include-path mismatch, sidebar route typos/casing drift, and missing tests/CI/lockfile.
- Keep project-scoped MCP usage minimal until a stable, clearly useful server is justified by future work.
- Clear production-build blockers by fixing a `react-select` callback contract in `Select2Page.tsx`, normalizing mixed CRLF/LF line endings introduced during edits, fixing a flag icon lookup type error, and converting a Leaflet ref callback to return `void`.
- Treat the template’s widespread raw `<img>` usage as intentional and disable `@next/next/no-img-element` in `eslint.config.mjs` rather than rewriting dozens of static image blocks.
- Clear the remaining lint warnings by tightening static data and hook dependencies in the select, icon, file-manager, table, and sidebar components.
- Quiet the remaining build console noise by modernizing Sass `if()` calls, converting `end` alignment values to `flex-end`, silencing dependency deprecations through `next.config.ts`, and deleting the unused Iconify animation route that was triggering build-time network retries.
- Final state: `npm run lint` and `npm run build` both pass cleanly with no warnings in the captured build log.

## 2026-03-23

- Complete the client-facing rebrand from Axelit to PolytronX across app metadata, landing pages, auth logos, footer/customizer copy, invoice contact details, and legal/demo text.
- Replace served logo usage with new text-based PolytronX SVG marks and move the old public logo binaries out of the runtime path into `.codex/rebrand-archive/` with neutral filenames so they cannot leak through the product.
- Mask baked-in old branding inside landing and modal preview artwork with a lightweight `PreviewBrandImage` overlay component instead of redesigning the existing layouts.
- Preserve routes, flows, styling, and color palette while removing marketplace-style wording such as template/theme sales copy from visible UI and metadata.
- Validation after the rebrand: global source search found no remaining `Axelit`, `CodeCanyon`, or `ThemeForest` references outside excluded/generated areas, and both `npm run lint` and `npm run build` passed.
- Re-implement the Turbopack migration from a clean baseline by making Turbopack the default dev and build path, removing checked-in webpack-specific app config and direct webpack dependencies, and aliasing `@tabler/icons-react` to a local deep-import wrapper so Turbopack never evaluates the package's aggregate entrypoint.
- Preserve the Tabler demo route by rendering from `icons-list.mjs` plus the existing Tabler webfont classes instead of enumerating the React icon namespace, and replace the UI-kit social button `require("@tabler/icons-react")[iconName]` pattern with an explicit brand-icon map.
- Treat App Router `error.tsx` and `not-found.tsx` as shell children only, returning the page component directly rather than nesting new `<html>` and `<body>` tags, which Turbopack is less tolerant of during prerendering.
- Serve Leaflet's default marker assets through bundled imports from `leaflet/dist/images/*` instead of assuming legacy files exist under `/public/images/leafletmaps/images`.
- Restore the OET product layer from thread memory instead of inventing a new UI language: keep the existing PolytronX template shell and compose learner, expert, and admin screens from dashboard-native cards, tables, alerts, charts, and route patterns.
- Treat the background-image auth pages as the only supported auth contract. All internal auth links, redirects, demo cards, and sidebar entries should point to `/auth-pages/sign-in-with-bg-image`, `/sign-up-with-bg-image`, `/password-reset-img`, `/password-create-img`, `/lock-screen-img`, and `/two-step-verification-img`.
- Keep mock auth session handling role-aware: demo users are `learner@oet.app`, `expert@oet.app`, and `admin@oet.app`, and successful auth flows redirect to `/app/dashboard`, `/expert/queue`, or `/admin/content` respectively.
- Keep the reusable password reveal control centralized in `src/Component/CommonElements/ThemedPasswordInput.tsx`, and wrap auth-shell password fields through `src/app/auth-pages/_components/PasswordField.tsx` so the shared eye-toggle behavior is preserved.
- Preserve the signup wizard as a three-step background-image flow and source its exam, profession, session, and country options from the persisted enrollment taxonomy store instead of hardcoded arrays.
- Keep enrollment taxonomy CRUD frontend-only for now via `src/lib/oet/stores/enrollment-taxonomy-store.ts`; admin edits are expected to persist in browser storage and immediately affect the sign-up wizard without backend writes.
- Route successful sign-up submissions to a dedicated background-image confirmation screen instead of dropping directly into OTP. The confirmation page should summarize the learner's selected exam/profession/session/country plus session pricing details, then offer explicit actions for login, support email, and WhatsApp contact.
- Keep forgot-password separate from signup OTP. The recovery journey must be:
  email entry -> reset OTP verification -> new-password screen -> reset-success
  confirmation -> login. The forgot-password path should never ask for the
  current password and should always return to sign-in instead of auto-login.
- Use `bindReactstrapInput(...)` whenever `react-hook-form` is paired with `reactstrap` inputs. This is the project-level fix for the invisible field-binding bug where values looked filled but validation still treated them as empty.
- Keep learner, expert, and admin role navigation isolated from the template navigation through `src/Data/Sidebar/oetNavigation.ts` plus the route-aware sidebar switch in `src/Component/Layouts/Sidebar/index.tsx`.
- Keep the learner sidebar intentionally mid-density: use explicit top-level groups for Workspace, Planning, Reading, Listening, Writing, Speaking, Mocks & Reviews, and Account instead of collapsing the learner product into three oversized buckets or exposing deep attempt/result detail pages in navigation.
- Add a lean `OetPageShell` mode for workspace-style routes that need the
  learner breadcrumb/title framing without the hero board, KPI row, and
  chart/activity chrome. `/app/settings` is the first consumer of this mode.
- Treat `/app/settings` as the learner account/settings hub and keep
  `/app/billing` as the dedicated billing center. The settings subscription tab
  may summarize plan state and deep-link to billing, but it should not duplicate
  invoices or purchase flows.
- Keep learner settings tabs deep-linkable through `?tab=` with the supported
  ids `profile`, `activity`, `security`, `privacy`, `notifications`,
  `subscription`, `connections`, and `delete`.
- Treat clean, product-facing auth URLs as canonical in production:
  `/login`, `/register`, `/register/success`, `/forgot-password`,
  `/forgot-password/verify`, `/reset-password`, `/reset-password/success`,
  `/verify`, and `/lock-screen`. Keep legacy `auth-pages/*` URLs as permanent
  redirects rather than the public contract.
- Persist learner settings client-side through a dedicated settings store so
  profile, notification, privacy, security, and connection changes survive
  refresh without introducing backend writes in this phase.
- Preserve the recovered role guards in `src/app/app/layout.tsx`, `src/app/expert/layout.tsx`, and `src/app/admin/layout.tsx`, backed by `src/lib/auth/session.server.ts`.
- Operational note: when localhost suddenly fails with a black screen or `Internal Server Error`, kill stale `node.exe` processes and restart the dev server before treating it as a code regression.
