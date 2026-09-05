# Monetization, AI Credits, Billing and Unit-Economics Contract

## 1. Core commercial model

Two independent axes:

1. **Content entitlement** — what proprietary material the learner owns and may retrieve/use.
2. **AI entitlement** — Free/Plus/Pro/Ultimate/Enterprise feature power and allowance.

AI Credits are a third meter for high-cost actions; they do not replace the tier or duplicate content entitlement.

## 2. Initial pricebook from source

All values live in an effective-dated product pricebook/configuration layer and may be superseded by approved newer values.

| Tier | Monthly | 3-month | Annual | Base text |
|---|---:|---:|---:|---:|
| Free | £0 | n/a | n/a | 20 / rolling 30 days |
| Plus | £7.99 | £21.99 | £79.99 | ~400 |
| Pro | £14.99 | £39.99 | £149.99 | ~1,200 |
| Ultimate | £26.99 | £72.99 | £269.99 | ~3,000 / fair use |

Ultimate public promise/sale must respect Stage 3 gate even if pricebook/schema exists earlier.

## 3. Tier outcome policy

Do not implement tiers as message limits only. Policy covers profile depth, plan frequency, memory duration, Error DNA/Fingerprint, course-aware grounding, drills, file/voice, Speaking modes, assessment, analytics, proactive coaching, video awareness, tutor handoff and model priority.

Source file/image limits: Plus 5/month, Pro 25/month, Ultimate 100/month. Treat as configurable values.

## 4. Free/demo rules

Anonymous demo ~3 interactions before registration; registered Free ~20 messages/rolling 30 days; visible usage counter; mini diagnostic/plan preview/navigation/public Q&A; short speaking demo; no unrestricted paid source access.

## 5. AI Credits — one wallet

User sees **AI Credits** only. Never introduce a second “Power Actions” currency. Backend may use provenance buckets: subscription-included, course-granted, purchased top-up, promotional/admin and migrated/grandfathered. Existing unspent credits migrate 1:1 unless a separately approved rule changes that.

## 6. Initial charge schedule

| Action | Credits |
|---|---:|
| Full Writing assessment | 2 |
| Full Speaking assessment | 2 |
| Full Reading exam analysis | 1 |
| Full Listening exam analysis | 1 |
| Deep cross-subtest mock | TO VERIFY after live cost test |
| Voice role play/live voice | TO VERIFY after voice spike; meter by time/cost |
| Large PDF/extended audio | TO VERIFY after live cost test |

Never infer missing charges.

## 7. Included wallet placeholders

Source provisional monthly values: Free 0, Plus 2, Pro 8, Ultimate 20. These are **not production-approved wallets**. Recalculate after live beta using approved fully loaded cost/credit and tier AI cost ceiling. If economics fail, reduce included wallet or raise price through product approval.

## 8. Top-up packs

| Pack | Price |
|---|---:|
| 5 credits | £5.99 |
| 15 credits | £13.99 |
| 30 credits | £23.99 |

Channel-specific sale only if net revenue/credit exceeds approved multiple of target cost/credit. That multiple is TO VERIFY.

## 9. Charge lifecycle

1. calculate capability and exact charge;
2. show user charge/balance;
3. receive explicit confirmation;
4. create idempotency key/reservation if architecture needs it;
5. run action;
6. commit charge only under defined success contract;
7. on technical failure restore/release automatically;
8. record trace/provenance/cost/result;
9. make retry safe.

For async jobs, reservation/settlement can be separate but user-visible accounting remains correct.

## 10. Ledger invariants

Immutable transaction history; atomic balance update; unique idempotency key; retry cannot double-charge; technical failure cannot permanently consume credit; reversals reference original entries; purchase-level reversal/unused-credit revocation supported; expiry provenance-aware; audit every grant/spend/expiry/restore/reversal.

## 11. Refunds/chargebacks/cancellation

The ledger supports approved policy for subscription cancellation, cooling-off refund, top-up refund, partially consumed purchased credits, chargeback/dispute and subscription/course/promo grant revocation. Exact legal treatment is TO VERIFY; build primitives/state machine rather than hard-code guessed policy.

## 12. Course bundles and add-ons

Course purchase unlocks proprietary content scope, not strongest AI tier by default. Source 1/3-month add-ons: Plus £6.99 / £18.99; Pro £12.99 / £34.99; Ultimate £23.99 / £64.99. Any included AI period needs defined expiry. Premium “Ultimate Course” may grant Ultimate for a defined 60/90-day prep window only if economics/product approve.

## 13. Contextual paywalls

Paywall request contains requested capability and learner/tier context. Frontend receives current price, benefits and reason from configuration. Conversation/pending action survives checkout and entitlement re-evaluation.

## 14. Subscription lifecycle

Support where provider/system permits purchase, upgrade/downgrade, renewal, cancellation, dunning/retry, reactivation/resit offer, refund/chargeback state, regional price display and webhook idempotency. Do not require support tickets for routine self-service if APIs allow it.

## 15. Unit-cost ceilings

Initial source ceilings per active paid user/month: Plus ≤ £1.00 AI variable cost, Pro ≤ £2.20, Ultimate ≤ £4.20. Initial AI-only guardrail targets ≥75% of **net revenue after tax/channel fees** versus AI variable cost, before support/hosting. Total contribution also subtracts support/tutor/correction/refund/hosting.

## 16. Fully loaded cost per AI Credit

Target is TO VERIFY in live beta. Cost includes attributable LLM/reasoning, embedding/reranking, voice/STT/TTS/telephony, file/media processing and other variable delivery. Reserve a TO VERIFY portion of tier ceiling for non-credit text/RAG/memory/files; only remainder funds included credits. Expected/p95 action cost must fit `(credits charged × approved unit cost)`.

## 17. Regional/channel economics

Maintain region/channel pricebook. Do not rely on IP alone for regional eligibility; use billing country/store region controls. Channel fees/tax are effective-dated configuration. If a local price/app-store channel breaks contribution floor: reduce expensive allowance, adjust credit/price, choose another channel or withhold sale there.

## 18. Cohort economics

Track owned/warm, cold organic/referral, paid cold, resit/reactivation, web/mobile store and profession/tier/region separately. Model short 2/3/4-month lifetime; voluntary vs involuntary churn; dunning recovery. The source interim ~£9.85 max paid CAC under a 3:1 3-month contribution assumption is temporary policy, not live evidence.

## 19. Cost/revenue dashboard

Expose gross/net revenue, AI variable cost, credit grants/spend/redemption, top-up revenue, support/tutor/correction cost inputs, refunds/chargebacks, contribution, margin alert and structurally-unprofitable feature alert by relevant tier/feature/user/profession/cohort/channel/region.

## 20. Emergency controls

Independent switches for voice, heavy files/audio, charge schedule, tier access, provider route and all new credit consumption. During ledger incident preserve balances. Recovery includes reconciliation and explicit re-enable approval.
