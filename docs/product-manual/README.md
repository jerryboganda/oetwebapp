# OET Platform Product Manual

This directory contains the project-grounded product manual package for the OET preparation platform.

Audit status: refreshed on 2026-05-13 against 304 Next.js page routes, 2 Next.js route-handler files, and 50 backend endpoint-folder files.

Snapshot note: the route/API counts in this package are the current manual snapshot. Older static key stats in repository instruction files should be treated as stale until those files are refreshed.

## Documents

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Admin Dashboard and CMS Manual](./admin-dashboard-cms-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md) — mission-critical hard invariants, configuration options, background workers, SignalR hubs, notification catalog, retention windows, glossary, admin permission keys, edge-state contracts, platform parity matrix, observability, support flows, and the release/QA quick start.

## Supporting Working File

- [_Audit Fact Base](./_audit-fact-base.md)

## Release and QA Quick Start

The single-source quick start lives in the [Reference Appendix, Section 13](./reference-appendix.md#13-release-and-qa-quick-start). Use it before tagging a release. Mission-critical hard invariants live in [Section 1](./reference-appendix.md#1-mission-critical-hard-invariants).

## Recommended Reading Paths

1. Product and leadership: start with the master manual, then read the cross-system workflows.
2. Learner experience: read the learner manual, then the route/API index for the newer learning, community, and commercial surfaces.
3. Expert operations: read the expert manual, then the cross-system review and private-speaking workflows.
4. Sponsor operations: read the sponsor manual, then the admin enterprise/billing sections.
5. Admin and engineering: read the admin manual, the route/API index, and the audit fact base.
6. Release QA and smoke planning: start with the route/API index, then the cross-system validation section, then the role-manual QA focus sections.
7. Content and assessment authors: start with the admin content/module authoring sections, then the mission-critical rulebook, scoring, and upload contracts.

## Status Vocabulary

- `implemented`: the surface exists in the current UI/API/helper layer and the normal successful path has an implementation to exercise.
- `partial`: the surface exists but is feature-flagged, content-dependent, heuristic, seed-backed, transitional, or not fully data-driven.
- `unclear`: the route or API is referenced, but the evidence is not strong enough to document it as complete behavior.

These labels do not mean end-to-end release validation or production readiness by themselves. Release sign-off still needs the validation flows listed in the cross-system manual and any role-specific QA focus areas.

## Source Labels

- `ROUTE-SNAPSHOT-2026-05-13`: current `app/**/page.tsx` route snapshot
- `NEXT-ROUTE-HANDLERS-2026-05-13`: current `app/api/**/route.ts` route-handler snapshot
- `ENDPOINT-SNAPSHOT-2026-05-13`: current backend endpoint-folder snapshot; 49 route-mapping files plus one admin route-builder helper
- `NAV-LEARNER`, `NAV-EXPERT`, `NAV-ADMIN`, `NAV-SPONSOR`: route-shell navigation evidence
- `AUTH-ROLE-ROUTES`: default route and role-routing evidence from `lib/auth-routes.ts`
- `MISSION-CRITICAL-RULES`: project rulebook, scoring, AI, content-upload, and module invariants from `AGENTS.md` and linked domain docs

## Keeping This Package Current

- Update the route/API index whenever page routes or backend endpoint-folder files change materially.
- Regenerate the route count from `app/**/page.tsx`, the route-handler count from `app/api/**/route.ts`, and the endpoint-folder count from `backend/src/OetLearner.Api/Endpoints/*.cs`.
- Keep README, route index, and audit fact base source labels in agreement.
- Verify all product-manual Markdown links.
- Re-check `partial` labels for feature flags, seed data, published-content dependencies, heuristics, legacy redirects, and release-only validation gaps.
- Update the manual issue register when a known follow-up is resolved or a new cross-system contradiction is discovered.
- Keep role manuals aligned with the four portals: learner, expert, sponsor, and admin.
- Do not summarize mission-critical scoring, AI, rulebook, content-upload, Reading, Grammar, Pronunciation, Conversation, or result-card behavior in ways that contradict the authoritative domain specs.

Revision source: `ROUTE-SNAPSHOT-2026-05-13`.
