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
