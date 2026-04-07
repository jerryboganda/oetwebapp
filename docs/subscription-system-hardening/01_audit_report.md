# Subscription System Audit Report

## Scope

This audit covers the learner billing surface, signup catalog, Stripe checkout handoff, admin billing controls, and the subscription entry points in the frontend.

## Current State

- The backend already exposes a strong billing domain with plans, add-ons, coupons, quotes, invoices, and webhook processing.
- Stripe is used as the checkout rail, while the internal database remains the source of truth for entitlements and subscription state.
- The learner UI had a billing page, but the user journey still relied on hardcoded comparison data and an incomplete navigation path.
- The signup flow loaded exam, profession, and session choices, but did not surface the published billing catalog.

## Findings

- Billing plans needed explicit catalog visibility in the signup response.
- The learner navigation needed a dedicated subscription entry point.
- The subscriptions page needed live plan data, not a static feature matrix.
- Add-on visibility needed to respect the active plan instead of exposing every global extra.
- Contract coverage for the signup catalog needed to assert the billing plan payload.

## Risk Summary

- Medium risk: stale frontend pricing or entitlement copy could confuse learners.
- Medium risk: checkout success could look complete before webhook reconciliation finishes.
- Low risk: existing backend billing lifecycle handling already covers most of the critical state transitions.
