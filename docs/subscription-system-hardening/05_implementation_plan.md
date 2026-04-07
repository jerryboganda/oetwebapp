# Implementation Plan

## Phase 1: Catalog Truth

- Add billing plans to the signup catalog contract.
- Wire the frontend signup hook to fetch billing plans.
- Display the live published plan catalog in both registration variants.

## Phase 2: Learner Navigation

- Add a `Subscriptions` item to the learner sidebar.
- Add `/subscriptions` as the canonical route.
- Preserve `/billing` as an alias.

## Phase 3: Billing Page Hardening

- Replace the hardcoded comparison matrix with a live data matrix.
- Filter add-ons by active-plan compatibility.
- Show post-checkout banners for success, cancel, failure, and expiry states.

## Phase 4: Quality Gates

- Add backend contract coverage for the signup catalog.
- Update the billing page test to reflect the subscriptions copy.
- Run frontend and backend validation commands before closure.
