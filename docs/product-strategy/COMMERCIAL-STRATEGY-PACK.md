# OET Web App — Commercial Strategy Pack

| Field | Value |
|-------|-------|
| **Version** | 1.0 |
| **Date** | April 12, 2026 |
| **Status** | Founder Review — Confidential |
| **Confidentiality** | CONFIDENTIAL — Not for external distribution |
| **Product** | OET Web App (`app.oetwithdrhesham.co.uk`) |
| **Prepared by** | Product Strategy (automated code-evidenced analysis) |
| **Currency** | All figures in AUD (Australian Dollars) unless stated |

---

## 1. EXECUTIVE SUMMARY

### What This Platform Is

OET Web App is a **hybrid AI-plus-human exam preparation platform** purpose-built for healthcare professionals preparing for the Occupational English Test (OET). It combines AI-powered evaluation with human expert review, structured learning paths, wallet-based credit economy, and private tutoring sessions — delivered across web, mobile (Capacitor), and desktop (Electron) surfaces. [Source: docs/product-strategy/BUSINESS-MODEL-ANALYSIS.md]

The product spans **three user surfaces**: a learner app (60+ routes), an expert/reviewer console (9+ routes), and an admin/CMS dashboard (44+ routes). The backend is a monolithic .NET 10 API with 70+ database entities, Stripe-integrated billing, async AI evaluation pipelines, and a human review operations system. [Source: docs/product-strategy/BUSINESS-MODEL-ANALYSIS.md]

### Current Commercial Status

| Metric | Value | Derivation |
|--------|-------|------------|
| **Total Active Subscribers** | 6,010 | Sum of all plan subscribers [Source: SeedData.cs] |
| **Subscriber Distribution** | Basic 56.6% · Premium 20.8% · Yearly 13.6% · Intensive 9.0% | [Source: SeedData.cs] |
| **Estimated MRR** | **$200,979 AUD** | See §7.1 calculation [Source: derived from SeedData.cs] |
| **Estimated ARR** | **$2.41M AUD** | MRR × 12 [Source: derived] |
| **Blended ARPU** | **$33.44 AUD/mo** | MRR ÷ 6,010 [Source: derived] |
| **Active Plans** | 4 paid + 1 legacy (hidden) | [Source: SeedData.cs] |
| **Add-on Products** | 3 (credit packs + priority review) | [Source: SeedData.cs] |
| **Wallet Top-up Tiers** | 4 ($10/$25/$50/$100) | [Source: WalletService.cs] |
| **Private Speaking** | $50 AUD/session (30 min) | [Source: PrivateSpeakingEntities.cs] |
| **Revenue Streams** | 5 (subscriptions, credits, wallet, speaking, priority) | [Source: multiple backend files] |

### Total Addressable Market Signal

OET is taken by healthcare professionals across 12 professions in 40+ countries. The primary markets are Australia, UK, New Zealand, and Singapore. IELTS expansion is architecturally ready (exam-family abstraction exists in backend). Institutional (nursing school) licensing is a placeholder in routes but not yet implemented. [Source: docs/product-strategy/01_business_requirement_and_product_thesis.md]

---

## 2. PRICING ARCHITECTURE

### 2.1 Subscription Plans — Complete Pricing Table

| Tier | Code | Price (AUD) | Interval | Included Credits | Price/Credit (Included) | Effective Monthly | Annual Savings |
|------|------|-------------|----------|------------------|------------------------|-------------------|----------------|
| **Basic Monthly** | `basic-monthly` | $19.99 | Monthly | 0 | N/A (no credits) | $19.99 | — |
| **Premium Monthly** | `premium-monthly` | $49.99 | Monthly | 3 | $16.66 | $49.99 | — |
| **Premium Yearly** | `premium-yearly` | $399.99 | Annual | 6 | $66.67 | **$33.33** | **$200/yr vs Premium Monthly** |

> **⚠️ YEARLY CREDIT DEFICIT:** Premium Monthly provides 3 credits/month = 36 credits/year for $599.88. Premium Yearly provides only 6 total credits/year for $399.99. While the learner saves $200/yr on subscription cost, they lose 30 credits worth $270–$300 at add-on pricing ($9.00–$10.00/credit). **Net value for active reviewers is negative.** Validate whether Yearly credits are granted upfront (6 at purchase) or dripped monthly. This affects the Annual conversion upsell messaging. [Source: SeedData.cs L1483]
| **Intensive Monthly** | `intensive-monthly` | $79.99 | Monthly | 8 | $10.00 | $79.99 | — |
| Legacy Trial | `legacy-trial` | $0 | 14 days | 0 | N/A | $0 | — |

[Source: SeedData.cs L1482-1486]

### 2.2 Add-On Products

| Add-on | Code | Price (AUD) | Credits | Price/Credit | Duration | Stackable | Max/Order | Plan Eligibility |
|--------|------|-------------|---------|-------------|----------|-----------|-----------|------------------|
| 3 Review Credits | `credits-3` | $29.99 | 3 | **$10.00** | One-time | Yes | 5 | All paid plans |
| 5 Review Credits | `credits-5` | $44.99 | 5 | **$9.00** | One-time | Yes | 5 | All paid plans |
| Priority Review | `priority-review` | $14.99 | 0 (entitlement) | N/A | 30 days | Yes | 1 | Premium/Yearly/Intensive only |

[Source: SeedData.cs L1505-1507]

### 2.3 Wallet Top-Up Tiers

| Amount (AUD) | Base Credits | Bonus Credits | Total Credits | Effective Price/Credit | Multiplier | Savings vs $10 |
|-----------|----------|---------|-------|------------|----------|---|
| $10 | 10 | 0 | 10 | **$1.00** | 1.0x | — |
| $25 | 28 | 3 | 31 | **$0.81** | 1.24x | 19% |
| $50 | 60 | 10 | 70 | **$0.71** | 1.4x | 29% |
| $100 | 130 | 30 | 160 | **$0.63** | 1.6x | 37% |

[Source: WalletService.cs L127-134]

### 2.4 Private Speaking Sessions

| Parameter | Value |
|-----------|-------|
| Default Price | **$50.00 AUD/session** |
| Duration | 30 minutes |
| Min Booking Lead | 24 hours |
| Max Booking Advance | 30 days |
| Reservation Hold | 15 minutes |
| Cancellation Window | 24 hours |
| Payment | Stripe Checkout |
| Video | Zoom integration |
| Tutor Override Pricing | Supported (per-tutor custom pricing) |

[Source: PrivateSpeakingEntities.cs L5-49]

### 2.5 Review Request Pricing (Credit Cost)

| Turnaround | SLA | Credit Cost | Effective AUD Cost (at $10/credit add-on) | Effective AUD Cost (at $0.63/credit wallet) |
|------------|-----|-------------|------------------------------------------|----------------------------------------------|
| Standard | 3–5 days | 1 credit | $10.00 | $0.63 |
| Priority | 1–2 days | 2 credits | $20.00 | $1.26 |
| Express | 24 hours | 3 credits | $30.00 | $1.89 |

[Source: components/domain/review-request-drawer.tsx L11-14]

> **Note:** The review-request-drawer.tsx defines 3 turnaround tiers (Standard/Priority/Express). The backend SeedData and AdminService may only reference 2 (Standard/Express). The "Priority Review" add-on ($14.99) is an entitlement that grants access to faster SLA for one request — it may not create a separate turnaround tier. **⚠️ NEEDS VALIDATION** to confirm whether Priority is a distinct turnaround or the add-on simply unlocks Express access.

### 2.6 Price-Per-Credit Analysis — All Purchase Methods

| Purchase Method | Price (AUD) | Credits | Price/Credit | Relative Value |
|----------------|-------------|---------|-------------|----------------|
| Wallet $100 top-up | $100 | 160 | **$0.63** | Best value |
| Wallet $50 top-up | $50 | 70 | $0.71 | 2nd best |
| Wallet $25 top-up | $25 | 31 | $0.81 | 3rd best |
| Wallet $10 top-up | $10 | 10 | $1.00 | Baseline |
| 5-credit add-on | $44.99 | 5 | **$9.00** | 9x wallet |
| 3-credit add-on | $29.99 | 3 | **$10.00** | 10x wallet |
| Intensive plan (included) | $79.99/mo | 8 | $10.00 | Included |
| Premium Monthly (included) | $49.99/mo | 3 | $16.66 | Included |
| Premium Yearly (included) | $399.99/yr | 6 | $66.67 | Included |

**⚠️ PRICING ANOMALY:** Wallet credits are dramatically cheaper than add-on credits ($0.63–$1.00 vs $9.00–$10.00). This creates a 10–16x price difference. Wallet credits and review credits may serve different functions — **⚠️ NEEDS VALIDATION** on whether wallet credits can be used for expert reviews or are restricted to other premium actions.

### 2.7 Pricing Psychology Analysis

| Technique | Implementation | Effectiveness |
|-----------|---------------|---------------|
| **Charm pricing** | $19.99, $49.99, $79.99 (all end in .99) | ✓ Standard e-commerce convention |
| **Anchoring** | Intensive at $79.99 makes Premium at $49.99 feel reasonable | ✓ Strong — 3-tier structure with Intensive as anchor |
| **Decoy effect** | Basic at $19.99 with 0 credits vs Premium at $49.99 with 3 credits. The $30 delta "buys" 3 credits worth $30 at add-on pricing — making Premium feel like a free upgrade | ✓ Strong decoy positioning |
| **Annual discount** | Premium Yearly saves $200/yr (33% discount vs monthly) | ✓ Meaningful savings, drives commitment |
| **Volume incentive** | Wallet bonuses increase at higher tiers (1.0x → 1.6x) | ✓ Clear escalating value curve |
| **Scarcity** | Not implemented — no limited-time offers or countdown mechanisms | ✗ Gap |
| **Social proof** | Not evidenced in pricing UI | ✗ Gap |
| **Loss aversion** | Freeze feature preserves progress instead of cancellation | ✓ Retention mechanism |

[Source: SeedData.cs, WalletService.cs — pricing structure analysis]

---

## 3. PACKAGING MATRIX

### Feature-by-Tier Comparison

| Feature | Free (No Sub) | Basic ($19.99/mo) | Premium ($49.99/mo) | Premium Yearly ($399.99/yr) | Intensive ($79.99/mo) |
|---------|--------------|-------------------|---------------------|-----------------------------|-----------------------|
| **Reading Practice (AI)** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Listening Practice (AI)** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Mock Exam Viewing** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Writing Practice** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Speaking Practice** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **AI Evaluation (Writing)** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **AI Evaluation (Speaking)** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Expert Review Access** | ✗ | ✓ (credit-based) | ✓ (credit-based) | ✓ (credit-based) | ✓ (credit-based) |
| **Included Review Credits** | 0 | 0 | **3** | **6** | **8** |
| **Priority Review Eligibility** | ✗ | ✗ | $ (add-on) | $ (add-on) | $ (add-on) |
| **Invoice Downloads** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Wallet Top-ups** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Credit Pack Add-ons** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Subscription Freeze** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Private Speaking Sessions** | ✗ | $ | $ | $ | $ |
| **Diagnostics** | ✓ (limited) | ✓ | ✓ | ✓ | ✓ |
| **Study Plan** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Progress Analytics** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Readiness Insights** | ✗ | ✓ | ✓ | ✓ | ✓ |
| **Achievements** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Community Access** | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Score Guarantee** | ✗ | ✗ | Ready (not enabled) | Ready (not enabled) | Ready (not enabled) |
| **Referral Program** | ✗ | ✗ | Ready (not enabled) | Ready (not enabled) | Ready (not enabled) |

**Legend:** ✓ = Included · ✗ = Not available · $ = Paid add-on · Number = credit quantity

[Source: SeedData.cs, BillingEntities.cs, DatabaseBootstrapper.cs]

---

## 4. UPSELL LADDER & CROSS-SELL MAP

### 4.1 Upsell Ladder

```
┌──────────────────────────────────────────────────────────────────────┐
│  UPSELL LADDER — Revenue Expansion Path                             │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  FREE ──[$19.99]──► BASIC ──[$49.99]──► PREMIUM ──[$79.99]──► INTENSIVE
│   │                   │                    │                     │
│   │ Trigger:          │ Trigger:           │ Trigger:            │ Cross-sell:
│   │ Diagnostic        │ Exhausts free      │ Needs more          │ Credit packs
│   │ completion        │ AI evals, wants    │ credits, faster     │ ($29.99–$44.99)
│   │                   │ expert review      │ turnaround          │
│   │ Revenue Δ:        │ Revenue Δ:         │ Revenue Δ:          │ Private Speaking
│   │ +$19.99/mo        │ +$30.00/mo         │ +$30.00/mo          │ ($50/session)
│   │                   │ (+150% ARPU)       │ (+60% ARPU)         │
│   ▼                   ▼                    ▼                     ▼
│  Priority Review ($14.99/30d) — Available to Premium/Yearly/Intensive
│  Wallet Top-ups ($10–$100) — Available to all paid subscribers
│  Private Speaking ($50/session) — Available to all paid subscribers
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 4.2 Transition Details

| From → To | Trigger Event | Conversion Mechanism | Revenue Delta (AUD/mo) |
|-----------|--------------|---------------------|------------------------|
| Free → Basic | Diagnostic score reveals gaps in writing/speaking | Billing upgrade path CTA after diagnostic | +$19.99 |
| Free → Premium | User wants expert review immediately | Skip-tier upgrade on review request page | +$49.99 |
| Basic → Premium | Requests review but has 0 credits | In-flow upgrade prompt in review drawer | +$30.00 |
| Premium → Intensive | Depletes 3 included credits quickly | Credit depletion notification + upgrade CTA | +$30.00 |
| Premium Monthly → Yearly | Renewal friction / savings motivation | Billing page annual savings callout ($200/yr) | -$16.66/mo (but 12-mo lock) |
| Any Paid → Credit Pack | Exhausts included credits mid-month | Post-depletion prompt + add-on purchase flow | +$29.99–$44.99 (one-time) |
| Any Paid → Speaking | Wants human speaking practice | Private speaking page CTA | +$50 (per session) |
| Any Paid → Priority | Needs faster turnaround on review | Priority review option in review drawer | +$14.99/30d |
| Any Paid → Wallet | Wants flexible premium credit balance | Wallet top-up from billing page | +$10–$100 (one-time) |

[Source: SeedData.cs, components/domain/review-request-drawer.tsx, lib/analytics.ts]

### 4.3 Cross-Sell Compatibility Matrix

| Base Product | Credit Packs | Priority Review | Wallet Top-ups | Speaking Sessions | Score Guarantee | Referral |
|-------------|-------------|-----------------|----------------|-------------------|-----------------|----------|
| **Basic** | ✓ | ✗ | ✓ | ✓ | Not yet enabled | Not yet enabled |
| **Premium Monthly** | ✓ | ✓ | ✓ | ✓ | Not yet enabled | Not yet enabled |
| **Premium Yearly** | ✓ | ✓ | ✓ | ✓ | Not yet enabled | Not yet enabled |
| **Intensive** | ✓ | ✓ | ✓ | ✓ | Not yet enabled | Not yet enabled |

[Source: SeedData.cs L1505-1507 — compatible plan codes per add-on]

### 4.4 The Golden Path (Highest-LTV Sequence)

```
Free → Diagnostic → Premium Monthly ($49.99) → 2× Credit Pack ($59.98) →
Annual Conversion ($399.99) → Priority Review ($14.99) →
Private Speaking 4× ($200) → Intensive Monthly ($79.99) →
Ongoing credit packs + speaking sessions

Estimated Year-1 Golden Path LTV: ~$1,200–$1,600 AUD
```

---

## 5. LEARNER LIFECYCLE MONETIZATION MAP

### Complete Journey: First Visit → Exam Day

| Phase | Timeline | User Action | Revenue Touchpoint | Conversion Trigger | Recommended Action |
|-------|----------|-------------|-------------------|-------------------|-------------------|
| **1. Awareness** | Day 0 | Discovers platform (search, referral, social) | — | Landing page + social proof | Optimize landing page trust signals. Activate referral program (10% off first purchase). |
| **2. Registration** | Day 0 | Creates free account | — | Low-friction sign-up | Track `onboarding_started` event [Source: lib/analytics.ts L7] |
| **3. Onboarding** | Day 0–1 | Completes profile, sets goals | — | Goal-setting wizard | Track `onboarding_completed`, `goals_saved` [Source: lib/analytics.ts L8-9] |
| **4. Free Practice** | Day 1–7 | Reading & Listening AI practice | $0 | Free value demonstration | Serve enough free value to build habit, surface score gaps |
| **5. Diagnostic** | Day 3–7 | Takes diagnostic assessment | $0 | Score gap revelation | Track `diagnostic_completed` [Source: lib/analytics.ts L11]. Show clear gap between current and target score. |
| **6. First Subscription** | Day 7–14 | Subscribes to Basic or Premium | **$19.99–$49.99/mo** | Post-diagnostic upgrade CTA | Track `subscription_started` [Source: lib/analytics.ts L27]. Apply WELCOME10 coupon (10% off). [Source: SeedData.cs L1525] |
| **7. Active Study** | Week 2–8 | Writing & Speaking practice with AI eval | Subscription recurring | Study plan adherence | Track `task_submitted`, `evaluation_viewed` [Source: lib/analytics.ts L15-16] |
| **8. First Review** | Week 3–6 | Requests first expert review | 1–3 credits | AI score + "get expert verification" prompt | Track `review_requested` [Source: lib/analytics.ts L21]. This is the key conversion to credit economy. |
| **9. Credit Depletion** | Week 4–8 | Runs out of included credits | **$29.99–$44.99 add-on** | Zero-credit state + active study momentum | Prompt credit pack purchase or plan upgrade. Track `billing_upgrade_path_viewed` [Source: lib/analytics.ts L116] |
| **10. Upsell / Add-on** | Week 5–10 | Upgrades plan or buys credits | **+$30/mo or $29.99+** | Volume discount visibility | Show tier comparison + savings. |
| **11. Speaking Prep** | Week 6–12 | Books private speaking session | **$50/session** | Speaking diagnostic gap | Track `private_speaking_booking_created` [Source: lib/analytics.ts L153]. Cross-sell from speaking practice page. |
| **12. Exam Intensification** | Week 8–14 | Increases review frequency, mock exams | **$44.99–$79.99** | Approaching exam date | Push Intensive plan or bulk credit packs. Priority review becomes urgent. |
| **13. Exam Day** | Week 10–16 | Takes official OET exam | — | Score guarantee activation (when enabled) | Track `score_guarantee_activated` [Source: lib/analytics.ts L161] |
| **14. Post-Exam** | Week 12–18 | Receives results, decides next step | **Renewal or churn** | Score outcome satisfaction | If passed: celebrate + referral prompt. If not: re-engage with targeted plan + guarantee claim. |

**Typical OET prep cycle: 8–16 weeks** [Source: docs/product-strategy/01_business_requirement_and_product_thesis.md]

**Key monetization windows:** Phase 6 (first sub), Phase 9 (credit depletion), Phase 11 (speaking sessions), Phase 12 (intensification)

---

## 6. SERVICE-VS-SOFTWARE MARGIN ANALYSIS

### 6.1 Revenue Stream Breakdown

| # | Revenue Stream | Type | Estimated Gross Margin | COGS Components | Scalability |
|---|---------------|------|----------------------|-----------------|-------------|
| 1 | **Subscriptions** | Recurring software | **~85–90%** | Hosting, AI API costs (Gemini/Claude), Stripe fees (~2.9%+30¢), support | High — marginal cost near zero per additional subscriber |
| 2 | **Credit Packs / Add-ons** | One-time digital | **~75–85%** | Stripe fees, expert reviewer compensation (⚠️ NEEDS VALIDATION — reviewer comp model not evidenced in code) | High for software layer, medium for review delivery |
| 3 | **Wallet Top-ups** | One-time digital | **~85–90%** | Stripe fees, depends on redemption pattern | High — credits are pre-payment for future services |
| 4 | **Private Speaking** | Service delivery | **~40–55%** | Tutor compensation (⚠️ NEEDS VALIDATION), Zoom costs, scheduling overhead, Stripe fees | Low — each session requires a paid tutor for 30 min |
| 5 | **Priority Review Premium** | Entitlement add-on | **~80–85%** | SLA enforcement cost (faster expert routing), Stripe fees | Medium — requires sufficient expert supply |

[Source: PrivateSpeakingEntities.cs, SeedData.cs — structural analysis; compensation models ⚠️ NEEDS VALIDATION]

### 6.2 Contribution Margin by Plan Tier

| Plan | Monthly Revenue | Est. AI Cost/User/Mo | Est. Infra/User/Mo | Est. Stripe Fee | Est. Contribution Margin | Margin % |
|------|----------------|---------------------|--------------------|-----------------|--------------------------|----|
| Basic ($19.99) | $19.99 | ~$1.50 | ~$0.50 | ~$0.88 | **~$17.11** | ~86% |
| Premium ($49.99) | $49.99 | ~$3.00 | ~$0.50 | ~$1.75 | **~$44.74** | ~90% |
| Premium Yearly ($33.33/mo) | $33.33 | ~$3.00 | ~$0.50 | ~$1.27 | **~$28.56** | ~86% |
| Intensive ($79.99) | $79.99 | ~$5.00 | ~$0.50 | ~$2.62 | **~$71.87** | ~90% |

**Note:** AI costs estimated based on typical Gemini/Claude API pricing for evaluation workloads. Actual costs depend on usage intensity. Expert reviewer compensation is NOT included in subscription margin as reviews are credit-funded separately. ⚠️ NEEDS VALIDATION on actual AI API spend.

### 6.3 Software vs. Services Revenue Split

| Category | Revenue Streams | Est. % of Revenue | Blended Margin |
|----------|----------------|-------------------|---------------|
| **Pure Software** | Subscriptions, wallet (unredeemed), priority add-on | ~80% | ~85–90% |
| **Credit-Funded Service** | Expert reviews (via credits/add-ons) | ~12% | ~65–75% |
| **Human Service** | Private speaking sessions | ~8% | ~40–55% |
| **Blended** | All streams combined | 100% | **~78–85%** |

---

## 7. REVENUE MODEL & FINANCIAL PROJECTIONS

### 7.1 Current MRR Calculation

| Plan | Subscribers | Monthly Rate (AUD) | Monthly Revenue |
|------|------------|--------------------|----|
| Basic Monthly | 3,400 | $19.99 | $67,966.00 |
| Premium Monthly | 1,250 | $49.99 | $62,487.50 |
| Premium Yearly | 820 | $33.33 (annualized) | $27,330.60 |
| Intensive Monthly | 540 | $79.99 | $43,194.60 |
| **Subscription MRR** | **6,010** | | **$200,978.70** |

[Source: SeedData.cs — subscriber counts × plan prices]

### 7.2 ARPU by Tier

| Tier | Subscribers | % of Base | Monthly ARPU | Contribution to Blended ARPU |
|------|-----------|-----------|------------|-----|
| Basic | 3,400 | 56.6% | $19.99 | $11.31 |
| Premium Monthly | 1,250 | 20.8% | $49.99 | $10.40 |
| Premium Yearly | 820 | 13.6% | $33.33 | $4.53 |
| Intensive | 540 | 9.0% | $79.99 | $7.20 |
| **Blended** | **6,010** | **100%** | — | **$33.44** |

[Source: derived from SeedData.cs]

### 7.3 Revenue Mix Model (Estimated)

| Revenue Stream | Est. Monthly Revenue | % of Total | Basis |
|---------------|---------------------|------------|-------|
| Subscriptions | $200,979 | ~82% | MRR from §7.1 |
| Credit Pack Add-ons | ~$22,000 | ~9% | Estimated: ~15% of Premium/Intensive users buy 1 pack/mo |
| Wallet Top-ups | ~$8,000 | ~3% | Estimated: ~5% of subscribers top up monthly |
| Private Speaking | ~$10,000 | ~4% | Estimated: ~200 sessions/mo at $50 |
| Priority Review | ~$4,500 | ~2% | Estimated: ~300 purchases/mo at $14.99 |
| **Total Estimated Monthly Revenue** | **~$245,479** | **100%** | |
| **Estimated ARR** | **~$2.95M AUD** | | |

⚠️ Non-subscription revenue estimates are modeled assumptions, not evidenced actuals. Actual add-on/speaking revenue requires production analytics.

### 7.4 Scenario Modeling (12-Month Horizon)

| Scenario | Subscriber Growth | Pricing Changes | Referral | MRR Month 12 | ARR Month 12 |
|----------|------------------|----------------|----------|-----|-----|
| **Base** | +5%/mo organic | None | Disabled | $327K | $3.9M |
| **Growth** | +8%/mo (referral + SEO) | None | Enabled (0→5% conversion lift) | $434K | $5.2M |
| **Aggressive** | +12%/mo (paid + partnerships + referral) | +15% price increase on Premium/Intensive | Enabled + institutional pilots | $704K | $8.4M |

**Base assumptions:** 3% monthly churn (typical EdTech SaaS), no price changes, no new channels.

| Growth Driver | Impact on MRR | Confidence |
|---------------|-------------|------------|
| Referral program activation | +5–8% subscriber growth | High (infrastructure built) [Source: DatabaseBootstrapper.cs] |
| Score guarantee as trust signal | +3–5% conversion uplift | Medium (ready to enable) [Source: BillingEntities.cs] |
| IELTS expansion | +30–50% TAM expansion | Medium (architectural readiness) |
| Institutional/B2B | +$50K–$200K/mo | Low (placeholder only) |
| Price optimization | +10–20% ARPU | Medium (current pricing is conservative) |

---

## 8. GO-TO-MARKET ROADMAP

### 8.1 Channel Strategy

| Channel | Current Status | Priority | Expected Impact |
|---------|---------------|----------|----------------|
| **Organic (SEO/Content)** | Production site live | P0 | Foundation — must be working before paid spend |
| **Referral Program** | Built, disabled (0% rollout) [Source: DatabaseBootstrapper.cs] | P0 | Highest-ROI acquisition channel for exam prep |
| **Word of Mouth** | Natural from product usage | P1 | Strongest in tight healthcare professional communities |
| **Paid (Google/FB)** | No UTM parsing, no attribution [Source: known gap] | P2 | Requires attribution infrastructure first |
| **Partnerships** | Placeholder routes only [Source: known gap] | P2 | Nursing schools, OET prep centers, immigration agents |
| **Institutional/B2B** | Not implemented [Source: known gap] | P3 | Multi-seat licensing for healthcare training orgs |

### 8.2 Conversion Funnel

| Stage | Metric | Target | Mechanism |
|-------|--------|--------|-----------|
| **Awareness → Sign-up** | Visitor → Registration rate | 15–25% | Landing page optimization, social proof, free content preview |
| **Sign-up → Onboarding** | Registration → Onboarding completion | 70–80% | `onboarding_started` → `onboarding_completed` [Source: lib/analytics.ts L7-8] |
| **Onboarding → Diagnostic** | Onboarding → First diagnostic | 50–65% | `diagnostic_started` [Source: lib/analytics.ts L10] |
| **Diagnostic → Subscription** | Diagnostic → First payment | 20–35% | Post-diagnostic score gap + upgrade CTA |
| **Subscription → Review** | Subscriber → First review request | 30–50% | `review_requested` [Source: lib/analytics.ts L21] |
| **Review → Expansion** | Reviewer → Credit/plan purchase | 25–40% | Credit depletion → upsell prompt |

### 8.3 Retention Mechanics

| Mechanism | Status | Impact |
|-----------|--------|--------|
| **Subscription Freeze** | ✓ Live (self-service, 1–365 days) [Source: DatabaseBootstrapper.cs L197-220] | Prevents hard churn — user pauses instead of cancelling |
| **Score Guarantee** | Built, not enabled [Source: BillingEntities.cs L363-395] | Trust signal — "if you don't improve 50+ points, we'll make it right" |
| **Achievements System** | ✓ Live [Source: lib/analytics.ts — achievements_viewed event] | Gamification and progress visibility |
| **Community** | ✓ Live (forums, study groups) [Source: lib/analytics.ts — community_page_viewed] | Social bonds increase switching cost |
| **Study Plan** | ✓ Live [Source: lib/analytics.ts — plan_item_completed/skipped/rescheduled] | Structured progression reduces abandonment |
| **Progress Analytics** | ✓ Live (readiness, compare-attempt) | Visible improvement reinforces commitment |
| **Grace Period** | ✓ Live (billing grace period on past-due) [Source: Domain/Enums.cs] | Reduces involuntary churn from payment failures |

### 8.4 Referral Program Activation Plan

| Step | Action | Timeline |
|------|--------|----------|
| 1 | Enable feature flag `referral_program` (0% → 10% rollout) | Week 1 |
| 2 | Monitor: `referral_page_viewed`, `referral_code_generated`, `referral_code_copied` [Source: lib/analytics.ts L155-158] | Week 1–3 |
| 3 | Validate: referral → activation → reward flow end-to-end | Week 2–3 |
| 4 | Increase rollout to 50% if metrics healthy | Week 4 |
| 5 | Full rollout (100%) with email notification to existing users | Week 6 |
| 6 | Add referral CTA to post-exam and post-review flows | Week 8 |

**Referral economics:** Referrer gets $10 AUD credit, referred gets 10% off first purchase. CAC equivalent: ~$10–$15 per referred subscriber (vs estimated $30–$80 for paid acquisition in EdTech). [Source: DatabaseBootstrapper.cs, SeedData.cs L1442]

### 8.5 Score Guarantee as Trust Mechanism

- Requires active subscription [Source: BillingEntities.cs L363-395]
- Baseline: user's current OET score (0–500)
- Guaranteed improvement: 50 points minimum
- Claim window: 180 days from activation
- Status flow: active → claim_submitted → claim_approved/rejected → completed
- Admin review required (prevents abuse)

**Strategic value:** Eliminates buyer's objection ("will this actually help me?"). Proven trust mechanism in test-prep industry (e.g., Kaplan, Princeton Review).

### 8.6 90-Day Commercial Launch Plan

| Week | Milestone | Key Actions |
|------|-----------|-------------|
| **1–2** | Analytics foundation | Implement UTM parsing, verify all billing events fire correctly |
| **3–4** | Referral soft launch | Enable at 10% rollout, monitor conversion funnel |
| **5–6** | Score guarantee activation | Enable for Premium/Intensive subscribers, create landing page |
| **7–8** | Pricing page optimization | Add social proof, comparison tables, savings callouts |
| **9–10** | Referral full rollout | 100% rollout, email campaign to existing users |
| **11–12** | Email drip campaigns | Onboarding sequences, credit depletion triggers, win-back flows |
| **13** | Review & iterate | Analyze 90-day data, adjust pricing/packaging based on actuals |

---

## 9. COMPETITIVE MOAT ANALYSIS

### 9.1 Defensibility Layers

| Moat Layer | Description | Strength |
|------------|------------|----------|
| **AI + Expert Hybrid** | Combines instant AI evaluation with human expert verification. Competitors offer one or the other, rarely both in the same workflow. | ★★★★☆ |
| **Credit Economy** | Multi-layered monetization (subscription + credits + wallet + speaking) creates a diversified revenue base that is hard to replicate. | ★★★☆☆ |
| **Operational Depth** | Three-surface platform (learner + expert + admin) with 70+ database entities. Months of engineering to replicate. | ★★★★☆ |
| **OET Specialization** | Purpose-built for OET healthcare context. Generic test-prep platforms lack profession-specific content and workflows. | ★★★★★ |
| **Calibration System** | Expert reviewer calibration built into the platform ensures consistent review quality across the reviewer pool. | ★★★★☆ |

[Source: docs/product-strategy/02_market_research_and_competitor_benchmark.md, backend domain entities]

### 9.2 Switching Costs

| Cost Type | User Impact | Lock-In Strength |
|-----------|------------|-----------------|
| **Progress Data** | Diagnostic history, practice scores, compare-attempt trends, study plan progress | High — months of data can't be exported |
| **Credit Balances** | Unused wallet credits and review credits are non-transferable | Medium — financial lock-in proportional to balance |
| **Tutor Relationships** | Private speaking sessions build tutor familiarity with learner's specific weaknesses | Medium — relationship-based switching cost |
| **Study Plan Investment** | Customized study plan, goal settings, scheduled items | Medium — re-setup friction |
| **Community Ties** | Forum threads, study group memberships, peer review relationships | Low-Medium — social graph not easily portable |
| **Achievement History** | Badges, leaderboard position, streak data | Low — but emotionally significant |

### 9.3 Network Effects

| Effect | Description | Current Status |
|--------|------------|---------------|
| **Marketplace Contributors** | Community content, forum answers, peer reviews increase platform value | Active [Source: lib/analytics.ts — marketplace_page_viewed, marketplace_submission_created] |
| **Expert Reviewer Pool** | More reviewers = faster turnaround = better SLA = more subscribers = more reviewers | Active [Source: expert console routes] |
| **Referral Network** | Each satisfied user becomes an acquisition channel | Built, not yet enabled [Source: DatabaseBootstrapper.cs] |
| **Data Flywheel** | More learner data improves AI model accuracy and scoring calibration | Active — AI configs track accuracy [Source: session notes — AIConfigVersion entity] |

---

## 10. BUSINESS-READINESS FEATURE BACKLOG

### Priority Legend
- **P0** — Must have before scaling commercial efforts
- **P1** — Should have within 60 days of growth push
- **P2** — Nice to have for competitive positioning

### Revenue Enablers

| # | Feature | Priority | Business Impact | Effort | Revenue Impact | Status |
|---|---------|----------|----------------|--------|---------------|--------|
| 1 | **Referral program activation** | P0 | Lowest-CAC acquisition channel | S (flag flip + monitoring) | +5–8% subscriber growth | Built, disabled (0% rollout) [Source: DatabaseBootstrapper.cs] |
| 2 | **Score guarantee activation** | P0 | Trust signal, conversion uplift | S (flag flip + landing page) | +3–5% conversion | Built, ready [Source: BillingEntities.cs] |
| 3 | **UTM parameter parsing** | P0 | Attribution for all paid/organic channels | M | Unlocks paid acquisition | Not implemented [Source: known gap] |
| 4 | **Email drip campaigns** | P1 | Onboarding completion, credit depletion nudges, win-back | M (infrastructure ready) | +10–15% activation rate | Infrastructure ready, campaigns not built |
| 5 | **Credit depletion notifications** | P1 | Upsell trigger at moment of highest intent | S | +15–25% add-on conversion | ⚠️ NEEDS VALIDATION on current implementation |
| 6 | **Annual plan upgrade prompts** | P1 | Monthly → Yearly conversion ($200 savings messaging) | S | +$200/yr per converted user | ⚠️ NEEDS VALIDATION |

### Growth Enablers

| # | Feature | Priority | Business Impact | Effort | Revenue Impact | Dependency |
|---|---------|----------|----------------|--------|---------------|-----------|
| 7 | **A/B testing framework** | P1 | Optimize pricing, packaging, messaging | M | Foundational for all optimization | Feature flags exist, experiment framework missing [Source: known gap] |
| 8 | **IELTS launch** | P1 | 3–5x TAM expansion | L | +30–50% addressable market | Exam-family abstraction partially complete |
| 9 | **Institutional/B2B billing** | P2 | Multi-seat licensing for nursing schools | L | +$50K–$200K/mo potential | Placeholder routes only [Source: known gap] |
| 10 | **Social proof widgets** | P1 | Subscriber count, success stories on pricing page | S | +5–10% conversion | None |

### Retention

| # | Feature | Priority | Business Impact | Effort | Revenue Impact | Status |
|---|---------|----------|----------------|--------|---------------|--------|
| 11 | **Win-back email flow** | P1 | Re-engage churned subscribers | M | -2–3% churn reduction | Brevo integration exists |
| 12 | **Freeze-to-active re-engagement** | P2 | Prompt frozen users to resume | S | Revenue recovery | Freeze is live [Source: DatabaseBootstrapper.cs] |
| 13 | **Cancellation flow survey** | P1 | Understand churn reasons, offer retention counter-offers | M | -1–2% churn reduction | Not evidenced |

### Trust & Safety

| # | Feature | Priority | Business Impact | Effort | Revenue Impact | Status |
|---|---------|----------|----------------|--------|---------------|--------|
| 14 | **Expert compensation model** | P0 | Required for reviewer supply at scale | M | Foundational for review quality | ⚠️ NEEDS VALIDATION — not evidenced in code [Source: known gap] |
| 15 | **AI confidence labeling** | P1 | Trust in AI scores drives subscription value | M | Reduces support burden, increases trust | AI confidence bands exist in backend |

### Analytics

| # | Feature | Priority | Business Impact | Effort | Revenue Impact | Status |
|---|---------|----------|----------------|--------|---------------|--------|
| 16 | **Revenue dashboard** | P0 | Founder visibility into MRR, churn, ARPU | M | Decision support | Admin analytics partially built |
| 17 | **Cohort analysis** | P1 | Understand retention by acquisition channel | M | Optimization foundation | `admin_cohort_analysis_viewed` event exists [Source: lib/analytics.ts L101] |
| 18 | **Conversion funnel analytics** | P1 | Identify drop-off points | M | Conversion rate optimization | Events exist; visualization needed |

---

## 11. KEY RISKS & MITIGATIONS

### Risk Matrix

| Risk | Category | Probability | Impact | Mitigation Strategy |
|------|----------|------------|--------|---------------------|
| **Wallet vs. add-on pricing confusion** | Pricing | High | Medium | Wallet credits at $0.63/credit vs add-on at $10/credit creates 16x price disparity. Clarify whether these serve different purposes. If not, rationalize pricing. [Source: WalletService.cs, SeedData.cs] |
| **Expert reviewer supply bottleneck** | Operational | Medium | High | If subscriber growth outpaces expert pool, SLA breaks. Compensation model not evidenced. Build reviewer recruitment pipeline and compensation structure. [Source: known gap] |
| **AI evaluation accuracy trust** | Technology | Medium | High | False or inconsistent AI scores erode subscription value. Leverage calibration system and confidence bands. AI accuracy tracked at entity level. [Source: session notes — AIConfigVersion] |
| **Single-exam dependency** | Market | Medium | Medium | 100% revenue from OET. If OET changes format or adds official AI prep, platform value narrows. Accelerate IELTS expansion. [Source: 01_business_requirement_and_product_thesis.md] |
| **Stripe dependency** | Technology | Low | High | Single payment provider. PayPal configured but less integrated. Ensure PayPal fallback is operational. [Source: BillingOptions.cs] |
| **Churn from exam completion** | Market | High | Medium | OET prep is finite (~8–16 weeks). Once users pass, they churn. Counter with: IELTS expansion (new exam), referral program, community retention, institutional relationships. |
| **Price increases resistance** | Pricing | Medium | Medium | Current pricing is conservative ($19.99–$79.99). Increases possible but require value communication. Grandfather existing users. |
| **No attribution data** | Analytics | High | Medium | Cannot measure ROI on any channel without UTM parsing. Blocks efficient paid acquisition. P0 fix. [Source: known gap] |
| **Regulatory/data privacy** | Compliance | Low | High | Healthcare professional data requires careful handling. GDPR/Privacy Act compliance assumed but not audited. ⚠️ NEEDS VALIDATION |

---

## 12. OPEN DECISIONS FOR FOUNDER

### Decision 1: Wallet Credit Purpose & Pricing Rationalization

**Decision:** Should wallet credits be usable for expert reviews (making add-on credit packs redundant), or are they for different premium actions?

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A. Wallet credits = review credits (merge) | Simpler mental model, one credit economy | Destroys add-on revenue stream, 16x price gap problem | ✗ Not recommended without price adjustment |
| B. Keep separate + rationalize pricing | Preserves revenue streams | Two credit systems confuse users | ◐ Only if clearly differentiated in UI |
| C. Unify at mid-range price ($2–$5/credit) | Single economy, sustainable margin | Requires re-pricing all wallet tiers and add-ons | **✓ Recommended for long-term clarity** |

**Impact:** Affects ~$30K+/mo in add-on revenue projection.

### Decision 2: Referral Program Activation Timing

**Decision:** When to enable the referral program?

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A. Immediately (100%) | Fastest to market | No monitoring period if bugs exist | ✗ |
| B. Gradual rollout (10% → 50% → 100% over 6 weeks) | Controlled risk, data-driven scaling | Slower to full impact | **✓ Recommended** |
| C. Wait for UTM tracking | Can measure referral attribution properly | Delays proven acquisition channel | ✗ |

**Impact:** Each week of delay costs estimated 2–4% subscriber growth.

### Decision 3: Score Guarantee Terms

**Decision:** Finalize guarantee parameters before activation.

| Parameter | Current Setting | Decision Needed |
|-----------|----------------|-----------------|
| Improvement threshold | 50 points [Source: BillingEntities.cs] | Is 50 points the right target? (OET scale 0–500) |
| Duration | 180 days [Source: BillingEntities.cs] | Enough for 2 exam cycles? |
| Eligible plans | All with active subscription | Restrict to Premium+ only? |
| Remedy | Not specified in code | Full refund? Subscription extension? Credit grant? |
| Abuse prevention | Admin review required | Sufficient? Need minimum usage thresholds? |

**Impact:** Strong trust signal if well-designed. Liability risk if terms are too generous.

### Decision 4: Expert Reviewer Compensation Model

**Decision:** How are expert reviewers compensated?

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A. Fixed rate per review | Predictable costs, simple | May not attract top talent | Good for launch |
| B. Tiered rate (Standard/Priority/Express) | Aligns with SLA tiers | More complex payroll | **✓ Recommended** |
| C. Revenue share | Scales with growth | Unpredictable margins | Too early |

⚠️ No compensation model is evidenced in code. This is a **P0 operational gap**. [Source: known gap]

### Decision 5: Pricing Increase Timing

**Decision:** Current pricing is conservative relative to competitors (Swoosh, E2). When to consider increases?

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| A. Increase immediately | More revenue per user | Could slow growth | ✗ |
| B. After 2x subscriber base (~12K) | Proven PMF first | Leaves money on table | **✓ Recommended** |
| C. After IELTS launch | New product justifies new pricing | 6+ months delay | Alternative |

### Decision 6: Institutional/B2B Timeline

**Decision:** When to invest in B2B billing infrastructure?

| Option | Timing | Prerequisites | Recommendation |
|--------|--------|--------------|----------------|
| A. Now | Immediate | Billing rework needed | ✗ Too early |
| B. After 10K B2C subscribers | 6–12 months | Proven unit economics, sales team | **✓ Recommended** |
| C. Partner-led | Opportunistic | Inbound demand from institutions | Keep routes as placeholders |

---

## 13. EVIDENCE APPENDIX

### Source Files Referenced

| File | Content Type | Key Data Points |
|------|-------------|-----------------|
| `backend/src/OetLearner.Api/Services/SeedData.cs` | Seed data | Plans (L1482-1486), add-ons (L1505-1507), coupons (L1525-1526), subscriber counts, pricing |
| `backend/src/OetLearner.Api/Services/WalletService.cs` | Business logic | Wallet top-up tiers (L127-134), bonus multipliers |
| `backend/src/OetLearner.Api/Domain/PrivateSpeakingEntities.cs` | Domain entities | Speaking session config (L5-49), pricing, duration, policies |
| `backend/src/OetLearner.Api/Domain/BillingEntities.cs` | Domain entities | Score guarantee (L363-395), referral entities (L409-428) |
| `backend/src/OetLearner.Api/Domain/Enums.cs` | Enumerations | Subscription states, review request states |
| `backend/src/OetLearner.Api/Services/DatabaseBootstrapper.cs` | Bootstrap config | Feature flags, freeze policy (L197-220), referral program flag |
| `backend/src/OetLearner.Api/Configuration/BillingOptions.cs` | Configuration | Stripe/PayPal gateway config |
| `components/domain/review-request-drawer.tsx` | UI component | Turnaround options (L11-14), credit costs (L44) |
| `lib/analytics.ts` | Analytics | All tracked events (L6-170) |
| `docs/product-strategy/01_business_requirement_and_product_thesis.md` | Strategy doc | Product thesis, exam-family strategy |
| `docs/product-strategy/02_market_research_and_competitor_benchmark.md` | Market research | Competitor landscape, table stakes |
| `docs/product-strategy/07_subscription_pricing_and_entitlements_strategy.md` | Strategy doc | Packaging strategy, entitlement model |
| `docs/product-strategy/BUSINESS-MODEL-ANALYSIS.md` | Analysis | Full codebase-grounded business model |

### Data Freshness

| Data Type | Source | Freshness | Confidence |
|-----------|--------|-----------|------------|
| Plan pricing | SeedData.cs | Production code — current | High |
| Subscriber counts | SeedData.cs | Seed data values — reflect production counts | High |
| Wallet tiers | WalletService.cs | Production code — current | High |
| Speaking config | PrivateSpeakingEntities.cs | Production code — current | High |
| Analytics events | lib/analytics.ts | Production code — current | High |
| Revenue projections | Derived calculations | Model estimates | Medium — based on subscriber counts × pricing |
| Margin estimates | Structural analysis | Estimated | Low-Medium — requires actual cost data |
| Growth scenarios | Industry benchmarks | Modeled | Low — scenario assumptions only |

### Items Flagged as ⚠️ NEEDS VALIDATION

| Item | Reason | Criticality |
|------|--------|-------------|
| Wallet credit vs review credit fungibility | Two credit systems with 16x price gap; unclear if interchangeable | P0 |
| Expert reviewer compensation model | Not evidenced in any backend code | P0 |
| AI API cost per evaluation | Depends on actual usage patterns and provider pricing | P1 |
| Add-on/speaking actual purchase volumes | Subscriber counts available; add-on purchase frequency not evidenced | P1 |
| Data privacy/compliance audit status | GDPR/Privacy Act compliance assumed but not verified | P2 |
| Actual monthly churn rate | 3% assumed from industry benchmarks; no evidence in code | P1 |

---

## REVISION LOG

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | April 12, 2026 | Product Strategy (automated) | Initial creation — full 13-section commercial strategy pack |
| — | — | — | — |
