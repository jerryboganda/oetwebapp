# Module Specifications

## Signup Catalog

- Return published billing plans alongside exam types, professions, sessions, and external auth providers.
- Hide inactive or unpublished plans from the signup flow.
- Preserve admin display order in the response.

## Learner Registration

- Show the published plan catalog during enrollment preview.
- Keep session selection behavior unchanged.
- Do not let learners pick unpublished or hidden billing plans during signup.

## Subscriptions Page

- Show current plan, renewal state, add-ons, invoices, and comparison data.
- Show payment status banners after checkout redirects.
- Derive the comparison matrix from live plan data.

## Billing Add-ons

- Filter extras by the current plan or a global applicability rule.
- Keep quote generation server-side and validated.

## Admin Billing Console

- Continue to manage publish/archive state, display order, entitlements, add-ons, coupons, and learner subscription inspection from the backend-admin surface.

## Backend Validation

- Reject unpublished or inactive plans in learner-facing quote paths.
- Keep webhook processing idempotent and replay-safe.
- Record reconciliation metadata for later troubleshooting.
