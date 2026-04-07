# Subscription System Gap Analysis

## Backend Gaps

- Signup catalog did not surface billing plans.
- Frontend-facing billing types were missing `entitlements` on plans.
- Billing plan visibility was not reflected in the learner signup journey.
- Subscription state verification needed stronger test coverage.

## Frontend Gaps

- The learner sidebar did not expose `Subscriptions`.
- The billing page still used hardcoded comparison content.
- The signup flow only previewed sessions, not the published catalog.
- Add-on cards were not filtered by the active plan.

## Product Gaps

- Learners could not easily inspect their current plan, available upgrades, invoices, or payment status in one place.
- The system did not clearly separate the internal subscription model from the Stripe checkout rail.

## Resolution Strategy

- Keep the internal database authoritative.
- Keep Stripe Checkout Sessions as the payment rail.
- Expose the published catalog in signup and subscriptions views.
- Filter extras and plan comparisons from live backend data only.
