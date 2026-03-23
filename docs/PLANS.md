# Plans

## Current Focus

- Bootstrap repository intelligence and Codex operating scaffolding.
- Keep product behavior unchanged while adding durable repo memory and agent instructions.
- Bootstrap implementation is complete and the repo now passes `npm run build` and `npm run lint`.
- The repo is currently build-clean and lint-clean, and the captured production build log is warning-free.
- The full PolytronX rebrand is complete across visible UI, metadata, logos, and served public assets.
- Re-implement the clean-baseline Turbopack-only migration and verify it with a production build plus targeted route smoke on the shared shell and high-risk widget routes.
- Restore the thread-defined OET product layer on top of the template shell:
  learner, expert, and admin route families, background-image-only auth flows,
  mock role sessions, shared OET contracts/data/query helpers, and admin-managed
  enrollment taxonomy synced into the sign-up wizard.
- Add a post-signup confirmation flow so `Create Account` lands on a proper
  success screen with account summary, subscription/session details, and clear
  actions for login, support email, and WhatsApp help.
- Replace the broken forgot-password shortcut with a proper 4-step reset flow:
  email entry, dedicated reset OTP, new-password creation, and reset-success
  confirmation before returning to login.
- Rebuild `/app/settings` as a learner-specific settings workspace using a lean
  OET page-shell mode, Axelit-style left-rail tabs, persisted mock settings
  data, and a clean separation from the full `/app/billing` route.

## Repository Shape

- Frontend-heavy Next.js 15 admin template.
- 154 `page.tsx` routes across dashboard, apps, auth pages, charts, forms, icons, maps, tables, UI kits, and utility pages.
- 3 App Router route handlers in `src/app/**/route.ts`.
- 4 server-action modules for auth, blog, projects, and textarea flows.
- One shared app shell and no current CI, lockfile, or test suite.
- Shared OET role surfaces now live under `/app`, `/expert`, and `/admin`.
- Background auth routes are the only supported auth contract:
  `/auth-pages/sign-in-with-bg-image`, `/sign-up-with-bg-image`,
  `/password-reset-img`, `/password-create-img`, `/lock-screen-img`,
  `/two-step-verification-img`.
- A lockfile and focused Vitest suite are now checked in for the OET helpers,
  route contracts, schemas, and taxonomy store.

## Known Backlog

- Stale aliases in `tsconfig.json`.
- Sass include-path drift in `next.config.ts`.
- Sidebar route typos and casing drift in `src/Data/Sidebar/sidebar.ts`.
- Mocked auth actions that only redirect.
- No checked-in tests, CI workflow, or lockfile.
- Keep the local Tabler wrapper in `src/lib/icons/tabler.ts` aligned with actual icon imports until upstream Turbopack handling of the aggregate package entrypoint is no longer risky.
- Taxonomy persistence is still frontend-only via persisted Zustand until a real
  backend phase replaces it.

## Working Rules

- Prefer small, serial changes when the surface includes the shared shell, sidebar, customizer, or data-table wrapper.
- Open a plan for any multi-step feature, refactor, or migration.
- Update this file when the repo shape or operating assumptions change.
- Keep future brand work aligned to `PolytronX` exactly in user-facing copy, titles, and logos.
- Keep new RHF + `reactstrap` forms on the `bindReactstrapInput(...)` adapter.
- If localhost suddenly returns a black page or `Internal Server Error`, clear
  stale `node.exe` dev processes before assuming the current code is broken.

## Bootstrap Checklist

- Repo layout documented.
- Commands documented.
- Shared hotspots documented.
- Agent roles documented.
- Known drift documented.
- Validation complete: production build passes and lint passes with no warnings or errors.
- Console noise cleanup complete: Sass deprecations, autoprefixer alignment warnings, and the unused Iconify build-time route have been removed or silenced.
- Rebrand cleanup complete: old Axelit/marketplace branding has been removed from source, metadata, runtime logos, and served asset paths.
- Next step: use the repo docs to guide future feature work instead of rediscovering the template structure from scratch.
