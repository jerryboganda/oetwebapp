# Agent State - Manual-only Tutor Book delivery

Last updated: 2026-07-24

## Goal

Make the standalone `tutor-book` product deliverable manually through WhatsApp without granting any learner-platform content, subtest, Tutor Book Reader, or Recalls access.

## Implemented

- Added the explicit `["none"]` subtest scope; empty `[]` remains the legacy all-subtests value.
- Added an admin “No platform or subtest access” control that clears modules, credits, trials, entitlements, and content overrides and selects manual material delivery.
- Server validation normalizes the no-platform scope to zero grants and rejects incompatible delivery.
- Manual fulfillment now uses the purchased immutable plan version and records external delivery without activating the subscription.
- Entitlement resolution fails low for external-only products, including historical Active subscription rows.
- Canonical Tutor Book seed data now has no modules/bundled unlock and uses manual WhatsApp delivery.
- Added a guarded production migration that aligns plans/versions and clears unintended standalone Tutor Book unlocks on existing subscriptions.
- Candidate/admin completion messaging distinguishes externally delivered material from released platform access.

## Validation

- `pnpm exec tsc --noEmit --pretty false`: passed.
- Focused manifest and policy test sources added; no further lengthy build/test run per owner instruction.
- Tutor Book manifest assertion: passed.
- `git diff --check`: passed.
- Independent focused review completed; identified blockers were addressed and sent for final re-check.

## Next Step

Commit the explicit scoped files, push `main`, and confirm the production deploy workflow was triggered.
