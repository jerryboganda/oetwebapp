# Agent State - Catalog Storefront Navigation

Last updated: 2026-07-16

## Goal

Make the per-package feature-bullets (tick-list) editor discoverable in the admin panel and explain how admins add ticks to pricing cards.

## Implemented This Run

- Added **Catalog Storefront** (`/admin/billing/storefront`) to the admin sidebar under Commerce & Settings.
- Added the matching page-title rule for `/admin/billing/storefront`.
- Added a **Catalog storefront** button on `/admin/billing/pricing` so admins editing plans can jump straight to the display-copy editor.

## Files Touched

- `lib/admin-navigation.tsx`
- `app/admin/billing/pricing/page.tsx`

## How to Add Ticks

1. In the admin panel go to **Commerce & Settings → Catalog Storefront** (`/admin/billing/storefront`).
2. Under **Per-package presentation**, select the package.
3. Fill in **Feature bullets (one per line)** — each line becomes a green checkmark bullet on the pricing card.
4. (Optional) Set tagline, icon, accent, featured ribbon, display order.
5. Click **Save changes**; the public `/pricing` and `/catalog` pages update immediately.

## Validation

- Inspected `CatalogPlanCard` (`components/domain/catalog/catalog-plan-card.tsx`) and `planFeatureBullets` (`lib/catalog-presentation.ts`) to confirm feature bullets already render with `CheckCircle2` ticks.
- `git diff --check`: passed.
- TypeScript / lint could not run because `node_modules` is not installed on this host; changes are limited to imports and JSX already used elsewhere.

## Blockers / Remaining Risk

- None.

## Next Step

Deploy and verify the new nav item renders and links to the Storefront editor.
