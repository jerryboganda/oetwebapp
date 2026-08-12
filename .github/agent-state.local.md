# Agent State (local)

## Goal
Batch of 3 spec cards: (6) pending payment approval cycle UX, (7) Egypt-only regional gateways, (8) admin-managed multiple Stripe accounts. ALL DONE & SHIPPED.

## Shipped
Commit 7378ff635 pushed to main (b4d1ccf8c..7378ff635) — blue/green prod deploy triggered.
13 files, +1111/-21:
- Task 6: app/billing/manual-payment/page.tsx (pending message, rejected reason + resubmit link), app/billing/page.tsx (upload-proof card), QuoteId in ManualPaymentDto (BillingExpansionEndpoints.cs + lib/api/billing-expansion.ts).
- Task 7: LearnerEndpoints.cs /payment-gateways region-aware (inEgypt gate on easykash/checkoutcom/paymob/paytabs).
- Task 8: StripeAccountProfile entity + migration 20260901090000, DbSet, RuntimeSettingsProvider overlay, StripeAccountAdminEndpoints.cs (CRUD/default/test-connection), Program.cs registration, admin UI app/admin/billing/stripe-accounts/page.tsx, lib/admin-navigation.tsx nav+breadcrumb.

## Validation
Targeted compile-error scan: only 2 errors in my files among owner-WIP errors; both fixed (Stripe.net 47.4 GetSelfAsync 2-arg — proven via throwaway compile; file-scoped AdminId/AdminName extensions added). Frontend props verified against components. Full backend build CANNOT pass due to pre-existing owner WIP errors (LearnerService, MockService, Listening*, ReadingLearnerEndpoints…) — not mine.

## Working tree note
Owner WIP preserved uncommitted in LearnerEndpoints.cs (requiresAdminReview hunks) and Program.cs (session-revocation + TTS policy hunks) — restored exactly after commit via TEMP backups (now deleted).

## Next step
None — awaiting owner verification on live production.

# Latest LR coding checkpoint - 2026-08-13 (Reading passage Q&A attempt scoping)

- Reading grounded passage Q&A now requires the exact submitted attempt ID,
  learner ownership, matching paper and published revision, and membership of
  the passage in that attempt's question scope. A learner cannot use another
  submitted attempt on the same paper to request an unrelated passage.
- The result UI and API client pass the finalized attempt ID, and a focused
  subset-scope regression covers included versus excluded passages. Only
  bounded source assertions and scoped `git diff --check` were run; no full
  validation, CI/CD, push, deployment, or live acceptance was run.
