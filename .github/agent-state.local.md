# Agent State - Profession-First Videos and Materials

Last updated: 2026-07-23

## Goal

Make the admin Course Videos and Course Materials screens profession-first while preserving canonical media records and keeping raw Bunny/folder tools under Advanced views.

## Implemented

- Added the six-profession course matrix and server-side video write/publish validation.
- Added profession-first video and material course-map endpoints and primary admin views.
- Added structured material scope fields plus guarded production backfill and video target correction migration.
- Kept canonical IDs in every projection; no media, progress, assignment, entitlement, or download row is copied or deleted.
- Added matrix-aware video draft preselection and access editing.
- Kept raw Bunny collections and raw material folders as Advanced consoles.
- Versioned the idempotent production metadata alignment script.
- Added direct pre-scoped folder creation, file upload, and canonical edit actions to every profession/material section and General English.
- Added canonical preservation counts plus explicit unmapped-ID reporting to both course-map APIs.
- Routed learner video visibility through the same authoritative matrix used by admin writes/projections.
- Kept General English independent from OET subtest restrictions while retaining its module/audience gates.
- Added RTL proof for profession-only roots and shared canonical edit propagation, plus expanded backend matrix/backfill/access tests.

## Validation

- `pnpm exec tsc --noEmit`: passed.
- Focused Vitest/RTL: 2 files, 4 tests passed.
- Metadata dry-run: 117 mapped, 0 unmapped; 26 English shared, 16 Arabic L/R shared, 48 Medicine+Physiotherapy, 16 Nursing, 11 Pharmacy.
- `git diff --check`: passed.
- Local .NET compile was stopped at owner request because it exceeded the quick-check window.

## Next Step

Commit and push the explicit completion files to `main`; owner will verify the production admin pages and report any runtime issue.
