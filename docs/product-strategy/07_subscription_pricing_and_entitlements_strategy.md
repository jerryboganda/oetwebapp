# 07 - Subscription Pricing and Entitlements Strategy

## Document intent

This file treats the current billing domain as verified product reality and proposes a cleaner packaging strategy on top of it.

Verified billing foundations already exist in code:

- plans
- add-ons
- coupons and redemptions
- quotes
- subscriptions and subscription items
- invoices
- wallet balances and wallet transactions
- payment transactions
- payment webhook events

The job is therefore not to invent a billing domain. The job is to package it coherently and enforce entitlements strictly.

## Pricing principles

1. OET stays the flagship paid product.
2. Human review is premium and finite by default, not unlimited by accident.
3. AI feedback is fast and scalable, but premium trust comes from escalation, expert review, and clearer reporting.
4. Wallet credits should be used for flexible premium actions, not as a confusing replacement for subscriptions.
5. IELTS should launch on the same entitlement backbone once its task model is operational.
6. PTE should not be sold broadly until its dedicated practice engine exists.

## Proposed product packaging

### OET packaging

| Tier | Positioning | Recommended shape |
| --- | --- | --- |
| Free | diagnostic and low-friction entry | limited diagnostics, a small number of practice attempts, AI-assisted feedback, no included expert review |
| OET Core | self-study subscription | wider task access, AI evaluation, study-plan and readiness features, limited mock access |
| OET Plus | committed learner subscription | more mock volume, deeper analytics, some included premium credits, richer readiness guidance |
| OET Review | premium outcome tier | priority support, more included review credits, faster turnaround promises, premium reporting and readiness blockers |

### Add-on catalog

| Add-on | Role |
| --- | --- |
| expert review credit pack | flexible purchase for writing or speaking review |
| priority turnaround | premium turnaround on selected reviews |
| extra mock bundle | extra full-mock volume without forcing plan change |
| wallet top-up | general premium credit balance for approved paid actions |

## Recommended entitlement model

### Shared entitlement categories

| Entitlement | Notes |
| --- | --- |
| diagnostics_access | included in Free and above |
| practice_access | core self-study gate |
| ai_evaluation_access | allowed broadly, but can be rate-limited by plan |
| mock_access | limited by plan or purchased bundles |
| readiness_insights_access | deeper reporting reserved for paid tiers |
| expert_review_credits | finite included or purchased credits |
| priority_review_access | premium-only or add-on |
| premium_analytics_access | compare-attempt and advanced insights |

### Billing behavior rules

| Rule | Rationale |
| --- | --- |
| premium actions must validate entitlements server-side | prevents UI-only gating failures |
| checkout completion should be idempotent | prevents double credits or duplicate subscription mutation |
| webhook events must be normalized and persisted | enables auditability and reconciliation |
| subscription state must drive access immediately | avoids paid-but-blocked or cancelled-but-still-open surfaces |
| wallet credits should only be consumed for explicit premium actions | keeps accounting and learner expectations clear |

## Wallet strategy

The wallet should support flexibility, not complexity.

Recommended uses:

- expert review purchases
- priority review uplift
- extra mock unlocks
- approved premium one-off services

Recommended non-uses:

- replacing every subscription action with credits
- hiding true product pricing behind a gamified credit economy

## Coupon strategy

Use coupons for clear commercial goals:

| Goal | Coupon use |
| --- | --- |
| new-user conversion | first-month or first-checkout discount |
| monthly-to-annual conversion | annual upgrade incentive |
| recovery | targeted win-back offers |
| partnerships | channel-specific acquisition offers |
| urgency | exam-date-window offers where operationally sensible |

Avoid:

- permanent discount dependence
- coupons that bypass entitlement logic
- coupon combinations that break quote consistency

## Exam-family packaging strategy

### OET

Keep as premium flagship with the richest review and readiness narrative.

### IELTS

Launch later on the same subscription and add-on structure, with IELTS-specific content and reporting mapped onto the same entitlement backbone.

### PTE

Do not offer a major PTE paid package until:

- the question-type engine exists
- the timing and simulation model is credible
- the analytics and remediation loops are PTE-native

## Recommended commercial experiments

1. Free-to-Core conversion test using diagnostic-to-plan nudges.
2. Core-to-Plus test using compare-attempt analytics and mock depth.
3. Plus-to-Review test using premium expert review visibility and SLA promises.
4. Monthly-to-annual test once churn and activation baselines are stable.
5. Review-credit pack test for learners who want premium trust without upgrading plans.

## Guardrails

- never present AI feedback as an official score
- never make human review appear unlimited unless it truly is
- never let sandbox or fallback payment behavior leak into production billing logic
- never launch IELTS or PTE pricing before their task models are product-real
