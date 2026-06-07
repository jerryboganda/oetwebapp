# OET with Dr. Ahmed Hesham — 2026 Product Portfolio Implementation Spec

> Claude Code + Codex friendly Markdown hand-off.  
> Source PDFs converted: **Full Recorded Courses** and **Separate Packages** developer specifications.  
> Prepared for: web-app developer / agentic coding assistants.  
> Target domain: `app.oetwithdrhesham.co.uk`  
> Version: v1.0, May 2026.

---

## How coding agents must use this file

Use this file as the canonical implementation contract for the OET product catalogue, course unlocks, add-ons, dashboard modules, entitlement counters, and purchase gating.

Recommended repository placement:

```text
/docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md
```

Recommended `CLAUDE.md` pointer:

```markdown
# CLAUDE.md

Read `/docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md` before modifying product catalogue, checkout, entitlements, dashboards, add-ons, writing assessments, speaking sessions, Tutor Book access, recalls, or course expiry logic.
```

Recommended `AGENTS.md` pointer:

```markdown
# AGENTS.md

Before making any product, checkout, entitlement, dashboard, add-on, or course-access change, read `/docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md` and preserve its product IDs, pricing, flags, entitlements, and acceptance criteria.
```

---

## Non-negotiable implementation rules

1. `product_id` is the canonical stable identifier for database seed data, routing, Stripe/payment mapping, checkout logic, enrolment creation, entitlement templates, and dashboard unlocks.
2. Every product has three independent boolean flags:
   - `writing_addons`
   - `speaking_addons`
   - `tutor_book_discount`
3. If a flag is `FALSE`, hide that section completely. No card, no button, no upsell banner, no placeholder, no disabled CTA.
4. Add-ons are not standalone dashboards. Writing assessment add-ons, extra speaking session add-ons, and the £32 Tutor Book add-on must only appear inside eligible parent products.
5. Server-side eligibility checks are mandatory before opening checkout for any add-on.
6. After successful payment webhook verification, create exactly one enrolment for the purchased SKU and apply the SKU entitlement template.
7. Only the purchased product dashboard unlocks. Other products remain locked behind product detail CTAs.
8. Permanent entitlements, especially Tutor Book, ignore expiry. Course products expire based on configured `access_duration_days`.
9. Pharmacy course is `DRAFT`; do not expose it publicly until price and launch approval are confirmed.
10. Do not implement demo/fake entitlements or placeholder SKUs in production.

---

## Flag definitions

| Flag | Controls | Visible only when TRUE | When FALSE |
|---|---|---|---|
| `writing_addons` | 3/5/7/10 letter assessment add-ons | Show add-on cards on product detail and dashboard | Hide all writing add-on UI completely |
| `speaking_addons` | Extra private Speaking sessions | Show £18/1-session and £34/2-session add-on options | Hide all speaking add-on UI completely |
| `tutor_book_discount` | Discounted £32 Tutor Book add-on | Offer £32 Tutor Book add-on instead of £45 standalone | Hide discounted add-on completely |

### Eligibility lists

**Writing add-ons visible on:** Full Condensed Recorded Course, Full Nursing Course all variants, Full Pharmacy Course after activation, Full Crash Course all variants, Writing Crash Course all variants, Mega Special Package, Double Special Package.

**Speaking add-ons visible on:** 1 Private Speaking Session, 2 Private Speaking Sessions, Speaking Crash Course, Double Special Package, Mega Special Package.

**Tutor Book £32 add-on visible on:** Full Condensed Recorded Course, Full Nursing Course all variants, Full Pharmacy Course after activation, Full Crash Course all variants, Writing Crash Course all variants, Speaking Crash Course, Double Special Package, Mega Special Package.

---

## Platform-wide user flow

| Step | Name | Required behaviour |
|---:|---|---|
| 1 | Listing | Public catalogue cards show title, profession, sub-tests, price, access duration, and `View Details` CTA. |
| 2 | Product detail | Premium product page shows hero, what is included, materials, bonuses, access rules, conditional add-ons, and payment CTA. |
| 3 | Account + payment | Candidate creates/logs in, completes secure payment, server verifies order, entitlements are written. |
| 4 | Dedicated dashboard | Only purchased product unlocks. Modules shown depend on product type and entitlement template. |
| 5 | Ongoing usage | Track lesson progress, materials, writing assessments, speaking sessions, AI credits, recall updates, and add-on purchases. |
| 6 | Expiry / extension | Access expires after configured duration. Extension CTA appears with small extension fee where allowed. |

---

## Full Recorded Courses — pricing matrix

| Product | Category | Access | W | S | TB£32 | Price |
|---|---|---:|:---:|:---:|:---:|---:|
| Full Condensed Recorded OET Course — Medicine | FULL COURSE | 180 days | ✓ | — | ✓ | £100 |
| Full Condensed Recorded Course + The Tutor Book | BUNDLE | 180 days | ✓ | — | — | £135 |
| Full Nursing OET Course | FULL COURSE | 180 days | ✓ | — | ✓ | £60 |
| Nursing Course + Assessment Package | BUNDLE | 180 days | ✓ | — | ✓ | £70 |
| Nursing Premium Bundle | BUNDLE | 180 days | ✓ | — | ✓ | £85 |
| Full Pharmacy OET Course | FULL COURSE | 180 days | ✓ | — | ✓ | Coming soon |
| Basic English Course — Preparation for OET | FOUNDATION | 180 days | — | — | — | £35 |
| Full Crash Course — General OET | FULL CRASH COURSE | 180 days | ✓ | — | ✓ | £60 |
| Full Crash Course + 3 Writing Assessments | BUNDLE | 180 days | ✓ | — | ✓ | £70 |
| Full Crash Course + 5 Writing Assessments | BUNDLE | 180 days | ✓ | — | ✓ | £80 |


W = Writing letter assessment add-ons. S = Extra private Speaking session add-ons. TB£32 = eligible for discounted Tutor Book add-on.

---

## Full Recorded Courses — product catalogue

### Full Condensed Recorded OET Course — Medicine

| Field | Value |
|---|---|
| `product_id` | full-condensed-medicine |
| `category_slug / category` | FULL COURSE |
| `price_gbp` | £100 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine |
| `duration` | 40+ hours |
| `format` | Recorded video + structured materials |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Writing Assessments, Speaking Session, AI Practice, Recalls, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=1 | ai_credits=5 | tutor_book=false |
| `bundle_components` | course + materials + recalls + assessments + speaking + AI credits |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- 160+ Listening exams, including exams beyond Jahshan/Benchmark
- 100+ Reading exams with answer keys and rationales
- 90+ Writing tasks covering all main letter types
- 100+ Speaking cards across scenarios and card types
- Recent recall updates plus old recalls from 2023 onwards
- Bonuses: 5 Writing letter assessments, 1 private Speaking session, 5 AI credits
- Continuous Q&A support during the access period

**Developer implementation notes**

- When bundled +Tutor Book SKU (£135) is purchased, set tutor_book=true on the same entitlement record and unlock Tutor Book module.
- Add-on purchases (3/5/7/10 letters) increment writing_assessments_remaining.
- Tutor Book add-on at discounted £32 is available and must appear in dashboard Add-ons.

### Full Condensed Recorded Course + The Tutor Book

| Field | Value |
|---|---|
| `product_id` | full-condensed-medicine-tbook |
| `category_slug / category` | BUNDLE |
| `price_gbp` | £135 |
| `access_duration` | 180 days for course + permanent Tutor Book entitlement |
| `profession` | Medicine |
| `duration` | 40+ hours + book |
| `format` | Recorded video + materials + watermarked PDF book |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Tutor Book, Writing Assessments, Speaking Session, AI Practice, Recalls, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=1 | ai_credits=5 | tutor_book=true |
| `bundle_components` | full course + Tutor Book PDF + Telegram channel |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Full Condensed Recorded Course (£100)
- The Tutor Book — First Edition 2026, watermarked and personalised
- Private Telegram channel access for book updates
- Continuous book + recall updates

**Developer implementation notes**

- Serve Tutor Book PDF watermarked with buyer name/email at download time.
- If candidate already owns Tutor Book add-on (£32), block this SKU to avoid double charge.
- Writing letter assessment add-ons remain purchasable and increment writing_assessments_remaining.

### Full Nursing OET Course

| Field | Value |
|---|---|
| `product_id` | full-nursing |
| `category_slug / category` | FULL COURSE |
| `price_gbp` | £60 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Nursing |
| `duration` | 30+ hours |
| `format` | Recorded video + nursing-specific materials |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Recalls, Add-ons |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | course only |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Full Nursing OET course across Listening, Reading, Writing, Speaking
- Recall-based Nursing Writing letters and model answers
- Recall-based Nursing Speaking cards with expected ideas
- Listening + Reading practice library
- Continuous Q&A support during access period

**Developer implementation notes**

- Writing Assessments and AI Practice modules may be visible but locked at 0 and purchasable through Add-ons.
- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Nursing Course + Assessment Package

| Field | Value |
|---|---|
| `product_id` | full-nursing-assessment |
| `category_slug / category` | BUNDLE |
| `price_gbp` | £70 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Nursing |
| `duration` | 30+ hours |
| `format` | Recorded video + materials + writing assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Writing Assessments, AI Practice, Recalls, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=0 | ai_credits=5 | tutor_book=false |
| `bundle_components` | course + 5 assessments + 5 AI credits |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Full Nursing OET Course (£60)
- 5 Writing letter assessments with 48–72h turnaround, Friday off
- Detailed correction + voice-note feedback via WhatsApp
- 5 free AI credits for instant practice feedback

**Developer implementation notes**

- Speaking Session module remains hidden because there is no entitlement.
- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Nursing Premium Bundle

| Field | Value |
|---|---|
| `product_id` | full-nursing-premium |
| `category_slug / category` | BUNDLE |
| `price_gbp` | £85 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Nursing |
| `duration` | 30+ hours + 11+ hours foundation |
| `format` | Recorded video + materials + foundation course |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Basic English, Materials Library, Writing Assessments, AI Practice, Recalls, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=0 | ai_credits=5 | tutor_book=false | basic_english=true |
| `bundle_components` | Nursing course + 5 assessments + 5 AI credits + Basic English |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Nursing Course + Assessment Package (£70)
- Basic English Course Preparation for OET (11+ hours)
- Foundation grammar, vocabulary and sentence-formation training
- Course booklet for the Basic English module

**Developer implementation notes**

- Basic English module unlocks as a separate left-nav entry.
- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Full Pharmacy OET Course

| Field | Value |
|---|---|
| `product_id` | full-pharmacy |
| `category_slug / category` | FULL COURSE |
| `price_gbp` | Coming soon (DRAFT — not for public UI) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Pharmacy |
| `duration` | 25+ hours |
| `format` | Recorded video + pharmacy-specific materials |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Recalls, Add-ons |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | course only |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Full Pharmacy OET course across Listening, Reading, Writing, Speaking
- Pharmacy-specific Writing examples and model answers
- Pharmacy Speaking cards with expected ideas and language
- Recall-based practice resources
- Continuous Q&A support during access period

**Developer implementation notes**

- STATUS: DRAFT. Hide from all public UI until price is confirmed by Dr Ahmed.
- When price is confirmed, mirror the Nursing SKU shape: base + assessment + premium variants.
- Pharmacy complaint-letter intros must contain the request; closing line uses "…contact the pharmacy".
- Tutor Book add-on at discounted £32 is available after product activation.

### Basic English Course — Preparation for OET

| Field | Value |
|---|---|
| `product_id` | basic-english |
| `category_slug / category` | FOUNDATION |
| `price_gbp` | £35 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | All disciplines |
| `duration` | 11+ hours |
| `format` | Recorded video + course booklet |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Basic English Lessons, Vocabulary, Grammar, Listening Foundations, Study Plan, Booklet |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false | basic_english=true |
| `bundle_components` | course + booklet |
| `recall_updates` | No |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Fully recorded preparatory English course
- Essential grammar from zero, framed around OET production
- Medical and healthcare vocabulary base
- Sentence formation for OET-style communication
- Conversation and listening foundations for healthcare
- Course booklet for simplified explanations
- Full study plan

**Developer implementation notes**

- Booklet is delivered as a downloadable PDF and must be watermarked.
- No writing add-ons, speaking add-ons, or Tutor Book discount.

### Full Crash Course — General OET

| Field | Value |
|---|---|
| `product_id` | crash-course |
| `category_slug / category` | FULL CRASH COURSE |
| `price_gbp` | £60 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | All disciplines (Medicine focus) |
| `duration` | 20+ hours |
| `format` | Recorded video + selected materials |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Recalls, Add-ons |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | course only |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Condensed recorded course across the four sub-tests
- High-yield strategies and practical techniques
- Recall-based guidance
- Selected study materials and Listening recalls

**Developer implementation notes**

- Two bundled SKUs exist: crash-3letters (£70) and crash-5letters (£80).
- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Full Crash Course + 3 Writing Assessments

| Field | Value |
|---|---|
| `product_id` | crash-3letters |
| `category_slug / category` | BUNDLE |
| `price_gbp` | £70 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | All disciplines |
| `duration` | 20+ hours |
| `format` | Recorded video + 3 letter assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Writing Assessments, Recalls, Add-ons |
| `entitlements` | writing_assessments=3 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | full crash course + 3 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Full Crash Course (£60)
- Assessment of 3 Writing letters
- Estimated score, detailed correction, voice-note feedback
- Letters can be candidate-chosen or recall-recommended

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Full Crash Course + 5 Writing Assessments

| Field | Value |
|---|---|
| `product_id` | crash-5letters |
| `category_slug / category` | BUNDLE |
| `price_gbp` | £80 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | All disciplines |
| `duration` | 20+ hours |
| `format` | Recorded video + 5 letter assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Listening, Reading, Writing, Speaking, Materials Library, Writing Assessments, Recalls, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | full crash course + 5 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Full Crash Course (£60)
- Assessment of 5 Writing letters
- Estimated score, detailed correction, voice-note feedback
- Letters can be candidate-chosen or recall-recommended

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

---

## Separate Packages — pricing matrix

| Product | Category | Access | W | S | TB£32 | Price |
|---|---|---:|:---:|:---:|:---:|---:|
| 3 Writing Letter Assessments — Add-on | WRITING ADD-ON | 180 days | — | — | — | £30 (was £40) |
| 5 Writing Letter Assessments — Add-on | WRITING ADD-ON | 180 days | — | — | — | £45 (was £60) |
| 7 Writing Letter Assessments — Add-on | WRITING ADD-ON | 180 days | — | — | — | £60 (was £75) |
| 10 Writing Letter Assessments — Add-on | WRITING ADD-ON | 180 days | — | — | — | £85 (was £100) |
| Recorded Writing Crash Course | WRITING COURSE | 180 days | ✓ | — | ✓ | £35 (was £50) |
| Writing Crash Course + 2 Letter Assessments | WRITING BUNDLE | 180 days | ✓ | — | ✓ | £45 (was £55) |
| Writing Crash Course + 3 Letter Assessments | WRITING BUNDLE | 180 days | ✓ | — | ✓ | £55 (was £65) |
| Writing Crash Course + 5 Letter Assessments | WRITING BUNDLE | 180 days | ✓ | — | ✓ | £70 (was £85) |
| Writing Crash Course + 7 Letter Assessments | WRITING BUNDLE | 180 days | ✓ | — | ✓ | £90 (was £105) |
| Writing Crash Course + 10 Letter Assessments | WRITING BUNDLE | 180 days | ✓ | — | ✓ | £115 (was £135) |
| Recorded Speaking Crash Course | SPEAKING COURSE | 180 days | — | ✓ | ✓ | £30 |
| 1 Private Speaking Assessment Session | SPEAKING SESSION | 60 days | — | ✓ | — | £18 |
| 2 Private Speaking Assessment Sessions | SPEAKING SESSION | 60 days | — | ✓ | — | £34 |
| Double Special Package — Writing + Speaking | COMBO | 180 days | ✓ | ✓ | ✓ | £55 (was £70) |
| Mega Special Package | COMBO | 180 days | ✓ | ✓ | ✓ | £80 (was £120) |
| The Tutor Book — First Edition 2026 | BOOK | Permanent | — | — | — | £45 (was £60) |
| The Tutor Book — Add-on Price (Enrolled Students) | BOOK ADD-ON | Permanent | — | — | — | £32 (was £60) |


W = Writing letter assessment add-ons. S = Extra private Speaking session add-ons. TB£32 = eligible for discounted Tutor Book add-on.

---

## Separate Packages — product catalogue

### 3 Writing Letter Assessments — Add-on

| Field | Value |
|---|---|
| `product_id` | addon-3-letters |
| `category_slug / category` | WRITING ADD-ON |
| `price_gbp` | £30 (was £40) |
| `access_duration` | Tied to parent course access window |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | — |
| `format` | Manual assessment by Dr Ahmed (48–72h, Friday off) |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Writing Assessments (increment-only) |
| `entitlements` | writing_assessments += 3 on purchase |
| `bundle_components` | add-on only |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- 3 Writing letter assessments
- Estimated score per letter
- Detailed correction
- Voice-note feedback via WhatsApp
- Letters candidate-chosen or recall-recommended

**Developer implementation notes**

- Locate buyer active eligible enrolment and increment writing_assessments_remaining by 3.
- Block checkout if no eligible parent enrolment.
- Do not create standalone dashboard for this SKU.

### 5 Writing Letter Assessments — Add-on

| Field | Value |
|---|---|
| `product_id` | addon-5-letters |
| `category_slug / category` | WRITING ADD-ON |
| `price_gbp` | £45 (was £60) |
| `access_duration` | Tied to parent course access window |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | — |
| `format` | Manual assessment by Dr Ahmed (48–72h, Friday off) |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Writing Assessments (increment-only) |
| `entitlements` | writing_assessments += 5 on purchase |
| `bundle_components` | add-on only |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- 5 Writing letter assessments
- Estimated score per letter
- Detailed correction
- Voice-note feedback via WhatsApp

**Developer implementation notes**

- Locate buyer active eligible enrolment and increment writing_assessments_remaining by 5.
- Block checkout if no eligible parent enrolment.
- Do not create standalone dashboard; only conditional add-on inside eligible parent product.

### 7 Writing Letter Assessments — Add-on

| Field | Value |
|---|---|
| `product_id` | addon-7-letters |
| `category_slug / category` | WRITING ADD-ON |
| `price_gbp` | £60 (was £75) |
| `access_duration` | Tied to parent course access window |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | — |
| `format` | Manual assessment by Dr Ahmed |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Writing Assessments (increment-only) |
| `entitlements` | writing_assessments += 7 on purchase |
| `bundle_components` | add-on only |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- 7 Writing letter assessments
- Same assessment format as 3/5-letter packages

**Developer implementation notes**

- Locate buyer active eligible enrolment and increment writing_assessments_remaining by 7.
- Block checkout if no eligible parent enrolment.
- Do not create standalone dashboard; only conditional add-on inside eligible parent product.

### 10 Writing Letter Assessments — Add-on

| Field | Value |
|---|---|
| `product_id` | addon-10-letters |
| `category_slug / category` | WRITING ADD-ON |
| `price_gbp` | £85 (was £100) |
| `access_duration` | Tied to parent course access window |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | — |
| `format` | Manual assessment by Dr Ahmed |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Writing Assessments (increment-only) |
| `entitlements` | writing_assessments += 10 on purchase |
| `bundle_components` | add-on only |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- 10 Writing letter assessments
- Same assessment format as smaller packages

**Developer implementation notes**

- Locate buyer active eligible enrolment and increment writing_assessments_remaining by 10.
- Block checkout if no eligible parent enrolment.
- Do not create standalone dashboard; only conditional add-on inside eligible parent product.

### Recorded Writing Crash Course

| Field | Value |
|---|---|
| `product_id` | writing-crash |
| `category_slug / category` | WRITING COURSE |
| `price_gbp` | £35 (was £50) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours by profession |
| `format` | Recorded video + writing materials |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Materials Library, Add-ons |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | writing course only |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Full recorded Writing explanation A–Z
- Task, purpose, audience, case-note relevance
- Referral, discharge, transfer, update, complaint and profession-specific letters
- Grammar, sentence structure, clarity, conciseness
- Latest OET Writing assessment criteria
- Profession-specific examples + recall-based ideas

**Developer implementation notes**

- Five bundled SKUs exist (+2/+3/+5/+7/+10 letters).
- Add-on cards visible in dashboard for +N letter upsell.
- Tutor Book add-on at discounted £32 is available.
- Writing letter assessment add-ons increment writing_assessments_remaining.

### Writing Crash Course + 2 Letter Assessments

| Field | Value |
|---|---|
| `product_id` | writing-crash-2 |
| `category_slug / category` | WRITING BUNDLE |
| `price_gbp` | £45 (was £55) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours |
| `format` | Recorded video + 2 assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Writing Assessments, Materials Library, Add-ons |
| `entitlements` | writing_assessments=2 | ai_credits=0 |
| `bundle_components` | writing course + 2 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Writing Crash Course (£35)
- 2 Writing letter assessments

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Writing add-ons 3/5/7/10 are purchasable and increment writing_assessments_remaining.

### Writing Crash Course + 3 Letter Assessments

| Field | Value |
|---|---|
| `product_id` | writing-crash-3 |
| `category_slug / category` | WRITING BUNDLE |
| `price_gbp` | £55 (was £65) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours |
| `format` | Recorded video + 3 assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Writing Assessments, Materials Library, Add-ons |
| `entitlements` | writing_assessments=3 | ai_credits=0 |
| `bundle_components` | writing course + 3 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Writing Crash Course (£35)
- 3 Writing letter assessments

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Writing add-ons 3/5/7/10 are purchasable and increment writing_assessments_remaining.

### Writing Crash Course + 5 Letter Assessments

| Field | Value |
|---|---|
| `product_id` | writing-crash-5 |
| `category_slug / category` | WRITING BUNDLE |
| `price_gbp` | £70 (was £85) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours |
| `format` | Recorded video + 5 assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Writing Assessments, Materials Library, Add-ons |
| `entitlements` | writing_assessments=5 | ai_credits=0 |
| `bundle_components` | writing course + 5 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Writing Crash Course (£35)
- 5 Writing letter assessments

**Developer implementation notes**

- Recommended writing bundle.
- Tutor Book add-on at discounted £32 is available.
- Writing add-ons increment writing_assessments_remaining.

### Writing Crash Course + 7 Letter Assessments

| Field | Value |
|---|---|
| `product_id` | writing-crash-7 |
| `category_slug / category` | WRITING BUNDLE |
| `price_gbp` | £90 (was £105) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours |
| `format` | Recorded video + 7 assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Writing Assessments, Materials Library, Add-ons |
| `entitlements` | writing_assessments=7 | ai_credits=0 |
| `bundle_components` | writing course + 7 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Writing Crash Course (£35)
- 7 Writing letter assessments

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Writing add-ons increment writing_assessments_remaining.

### Writing Crash Course + 10 Letter Assessments

| Field | Value |
|---|---|
| `product_id` | writing-crash-10 |
| `category_slug / category` | WRITING BUNDLE |
| `price_gbp` | £115 (was £135) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 12–16+ hours |
| `format` | Recorded video + 10 assessments |
| `writing_addons` | TRUE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Writing Assessments, Materials Library, Add-ons |
| `entitlements` | writing_assessments=10 | ai_credits=0 |
| `bundle_components` | writing course + 10 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Everything in Writing Crash Course (£35)
- 10 Writing letter assessments

**Developer implementation notes**

- Heaviest writing focus.
- Tutor Book add-on at discounted £32 is available.
- Writing add-ons increment writing_assessments_remaining.

### Recorded Speaking Crash Course

| Field | Value |
|---|---|
| `product_id` | speaking-crash |
| `category_slug / category` | SPEAKING COURSE |
| `price_gbp` | £30 |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 8+ hours per profession |
| `format` | Recorded video |
| `writing_addons` | FALSE |
| `speaking_addons` | TRUE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Speaking Lessons, Speaking Cards, Useful Phrases, Role-play Practice, Materials Library |
| `entitlements` | speaking_sessions=0 (sessions sold separately as different SKU) |
| `bundle_components` | speaking course only |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Complete Speaking subtest and role-play structure explanation
- All major card types covered in detail
- Recall-based focus on most repeated card types
- Opening, information gathering, addressing concerns, closing safely
- Handling anxious, angry, confused, reluctant or non-compliant patients
- Empathy, reassurance, signposting, checking understanding

**Developer implementation notes**

- Extra private speaking sessions can be added as add-ons.
- Tutor Book add-on at discounted £32 is available.
- Use £18/session or £34/two-session SKUs; increment speaking_sessions_remaining.

### 1 Private Speaking Assessment Session

| Field | Value |
|---|---|
| `product_id` | speaking-1session |
| `category_slug / category` | SPEAKING SESSION |
| `price_gbp` | £18 |
| `access_duration` | Scheduled within candidate preparation window / 60 days |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | 1 live session |
| `format` | Live 1:1 session + detailed feedback |
| `writing_addons` | FALSE |
| `speaking_addons` | TRUE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Speaking Session Booking |
| `entitlements` | speaking_sessions += 1 |
| `bundle_components` | session only |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- Live 1:1 Speaking session
- Multiple cards covered
- Detailed performance feedback

**Developer implementation notes**

- Sold as standalone SKU and can increment on top of any active enrolment.
- Speaking session add-ons render on this product; writing add-ons do not.
- Use £18/session or £34/two-session SKUs; increment speaking_sessions_remaining.

### 2 Private Speaking Assessment Sessions

| Field | Value |
|---|---|
| `product_id` | speaking-2sessions |
| `category_slug / category` | SPEAKING SESSION |
| `price_gbp` | £34 |
| `access_duration` | Scheduled within candidate preparation window / 60 days |
| `profession` | Doctors, Nurses, Pharmacy |
| `duration` | 2 live sessions |
| `format` | Live 1:1 sessions + detailed feedback |
| `writing_addons` | FALSE |
| `speaking_addons` | TRUE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Speaking Session Booking |
| `entitlements` | speaking_sessions += 2 |
| `bundle_components` | 2 sessions |
| `recall_updates` | No |
| `extension_allowed` | No |

**What is included**

- 2 live 1:1 Speaking sessions
- Different cards each session
- Detailed performance feedback each session

**Developer implementation notes**

- Use £18/session or £34/two-session SKUs; increment speaking_sessions_remaining.

### Double Special Package — Writing + Speaking

| Field | Value |
|---|---|
| `product_id` | double-special |
| `category_slug / category` | COMBO |
| `price_gbp` | £55 (was £70) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | Writing 12–16+ hrs + Speaking 8+ hrs |
| `format` | Recorded video bundle |
| `writing_addons` | TRUE |
| `speaking_addons` | TRUE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Speaking Lessons, Speaking Cards, Useful Phrases, Materials Library |
| `entitlements` | writing_assessments=0 | speaking_sessions=0 | ai_credits=0 | tutor_book=false |
| `bundle_components` | writing course + speaking course |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Full recorded Writing course (latest criteria)
- Full recorded Speaking course A–Z

**Developer implementation notes**

- Mega Special (£80) is the next step up with 5 Writing assessments and 1 private Speaking session.
- Tutor Book add-on at discounted £32 is available.
- Speaking session add-ons increment speaking_sessions_remaining.
- Writing add-ons increment writing_assessments_remaining.

### Mega Special Package

| Field | Value |
|---|---|
| `product_id` | mega-special |
| `category_slug / category` | COMBO |
| `price_gbp` | £80 (was £120) |
| `access_duration` | 180 days / 6 months from purchase |
| `profession` | Medicine, Nursing, Pharmacy |
| `duration` | 18+ hours focused Writing + Speaking |
| `format` | Recorded video + assessments + 1 live session |
| `writing_addons` | TRUE |
| `speaking_addons` | TRUE |
| `tutor_book_discount` | TRUE |
| `dashboard_modules` | Writing Lessons, Model Letters, Writing Rules, Speaking Lessons, Speaking Cards, Useful Phrases, Writing Assessments, Speaking Session, Materials Library, Add-ons |
| `entitlements` | writing_assessments=5 | speaking_sessions=1 | ai_credits=0 | tutor_book=false |
| `bundle_components` | writing course + speaking course + 1 session + 5 assessments |
| `recall_updates` | Yes |
| `extension_allowed` | Yes — small extension fee |

**What is included**

- Full recorded Writing sessions (latest criteria)
- Full recorded Speaking course A–Z
- 1 Private Speaking session
- 5 Writing letter assessments
- 18+ hours focused Writing and Speaking preparation

**Developer implementation notes**

- Tutor Book add-on at discounted £32 is available.
- Speaking session add-ons increment speaking_sessions_remaining.
- Writing add-ons increment writing_assessments_remaining.

### The Tutor Book — First Edition 2026

| Field | Value |
|---|---|
| `product_id` | tutor-book |
| `category_slug / category` | BOOK |
| `price_gbp` | £45 (was £60) |
| `access_duration` | Permanent entitlement with content updates |
| `profession` | All disciplines |
| `duration` | — |
| `format` | Watermarked PDF + private Telegram channel |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Tutor Book Reader, Audio Scripts, Updates |
| `entitlements` | tutor_book=true |
| `bundle_components` | book + channel |
| `recall_updates` | Yes |
| `extension_allowed` | No |

**What is included**

- Listening: new recalls, full audio scripts, answers, justifications
- Reading: recall-based topics, vocab trends, Parts A/B/C strategies
- Writing: 8 full recall-based letters with model answers and structure guidance
- Speaking: 16 recall-based cards based on recent scenarios
- Private Telegram channel access for continuous updates

**Developer implementation notes**

- PDF is watermarked per buyer with buyer name + email.
- Output filename pattern: [email - THE TUTOR BOOK - First Edition 2026.pdf].

### The Tutor Book — Add-on Price (Enrolled Students)

| Field | Value |
|---|---|
| `product_id` | tutor-book-addon |
| `category_slug / category` | BOOK ADD-ON |
| `price_gbp` | £32 (was £60) |
| `access_duration` | Permanent entitlement |
| `profession` | All disciplines |
| `duration` | — |
| `format` | Watermarked PDF |
| `writing_addons` | FALSE |
| `speaking_addons` | FALSE |
| `tutor_book_discount` | FALSE |
| `dashboard_modules` | Tutor Book Reader |
| `entitlements` | tutor_book=true |
| `bundle_components` | book only |
| `recall_updates` | Yes |
| `extension_allowed` | No |

**What is included**

- The Tutor Book — First Edition 2026 (watermarked PDF)
- Private Telegram channel access

**Developer implementation notes**

- Only purchasable when buyer has active eligible enrolment with tutor_book_discount=TRUE.
- Block if no eligible parent enrolment.
- Block if buyer already owns Full Condensed + Tutor Book SKU (£135) or previous add-on.
- Not standalone dashboard; flips tutor_book=true on eligible parent enrolment and unlocks Tutor Book module there.

---

## Cross-cutting implementation rules

### Access control

- Before payment, only the product detail page is viewable.
- Lessons, materials, protected downloads, and dashboards are inaccessible before payment.
- After payment, a verified webhook writes an `enrolments` row with `start_date`, `expiry_date`, and status.
- Unlock only the purchased product dashboard.
- Other products remain locked behind their own product detail CTA.

### Bundle unlock

- Treat each bundle SKU as a single enrolment with multiple unlocked modules.
- `full-condensed-medicine-tbook` flips `tutor_book=true` on the same enrolment record.
- Block double purchases server-side, especially Tutor Book double charges.

### Add-on purchase rules

- Writing add-on purchase increments `writing_assessments_remaining` on the selected eligible enrolment.
- Speaking add-on purchase increments `speaking_sessions_remaining` on the selected eligible enrolment.
- Tutor Book £32 add-on flips `tutor_book=true` on the selected eligible enrolment.
- If a buyer has multiple eligible parent enrolments, show a selector at checkout to choose the target enrolment.
- If no eligible parent enrolment exists, block purchase and return a clear message with a route to buy an eligible course.

### Writing assessment lifecycle

State machine:

```text
not_submitted -> submitted -> under_review -> feedback_available
```

Rules:

- Decrement `writing_assessments_remaining` on submission.
- Turnaround is 48–72 hours, Friday off.
- Submission screen must clearly surface turnaround.
- Feedback delivery includes estimated score, detailed correction, and voice-note feedback via WhatsApp.

### Speaking session lifecycle

Flow:

```text
candidate picks slot -> confirmation -> live session -> marked completed
```

Rules:

- Decrement `speaking_sessions_remaining` on booking confirmation.
- Extra session add-ons are billed at £18/session or £34/two-session.
- Add-on purchases increment `speaking_sessions_remaining` on active eligible enrolment.

### AI credit logic

- Display remaining `ai_credits` in AI Practice module header.
- Each AI practice run decrements credits by 1.
- Keep `ai_credits` as a simple counter to allow future top-up SKUs without re-architecture.

### Expiry and extension

- Compare `now()` to `expiry_date` on every dashboard load.
- Past expiry: lock content and show `Extend Access` CTA where extension is allowed.
- Successful extension updates `expiry_date`.
- Permanent entitlements ignore expiry.

---

## Data model essentials

Implement or map to equivalent existing models:

```text
users (
  id,
  name,
  email,
  profession,
  whatsapp_number
)

products (
  id,
  slug,
  category_slug,
  price_gbp,
  access_days,
  writing_addons,
  speaking_addons,
  tutor_book_discount,
  dashboard_modules[],
  entitlement_template{}
)

enrolments (
  id,
  user_id,
  product_id,
  start_date,
  expiry_date,
  status
)

entitlements (
  id,
  enrolment_id,
  writing_assessments_remaining,
  speaking_sessions_remaining,
  ai_credits,
  tutor_book,
  basic_english
)

writing_submissions (
  id,
  enrolment_id,
  letter_topic,
  file_url,
  status,
  score,
  feedback_url,
  submitted_at,
  reviewed_at
)

speaking_bookings (
  id,
  enrolment_id,
  slot_at,
  priority,
  status
)

recalls (
  id,
  subtest,
  published_at,
  locked_until_enrolled bool
)

addon_purchases (
  id,
  user_id,
  parent_enrolment_id,
  addon_product_id,
  delta,
  applied_at
)
```

---

## Suggested seed-data shape

Use this shape or map it to the existing ORM schema:

```ts
type ProductSeed = {
  product_id: string;
  name: string;
  category_slug: string;
  price_gbp: number | null;
  public_status: 'active' | 'draft' | 'hidden';
  access_duration_days: number;
  writing_addons: boolean;
  speaking_addons: boolean;
  tutor_book_discount: boolean;
  dashboard_modules: string[];
  entitlement_template: {
    writing_assessments_remaining: number;
    speaking_sessions_remaining: number;
    ai_credits: number;
    tutor_book: boolean;
    basic_english?: boolean;
  };
  bundle_components: string[];
  recall_updates: boolean;
  extension_allowed: boolean;
};
```

---

## Frontend implementation contract

### Public catalogue

- Render product cards from `products` table or typed seed source.
- Show `Coming soon` and hide checkout for draft products only if explicitly allowed internally.
- Do not show `full-pharmacy` publicly until active.

### Product detail page

Must include:

- Hero section
- Profession
- Access duration
- Format
- Price and old price where applicable
- What is included
- Dashboard modules
- Conditional add-ons based on flags
- Payment CTA

### Candidate dashboard

- Dashboard modules must be driven from entitlement/product configuration.
- Locked modules must not expose protected content.
- Add-ons section must only show eligible add-ons.
- Expired course must lock course content but keep permanent Tutor Book access available.

### Admin dashboard

Admin must be able to:

- View all products and product status.
- View enrolments and entitlements.
- Manually adjust writing assessment count, speaking session count, AI credits, and Tutor Book access if needed.
- See add-on purchase history and target parent enrolment.
- Export product/enrolment data.

---

## Backend implementation contract

### Payment webhook

On verified successful payment:

1. Find product by `product_id`.
2. Create enrolment or apply add-on to eligible parent enrolment.
3. Write entitlement template.
4. Compute `expiry_date` from `access_duration_days` unless permanent entitlement.
5. Lock all unrelated products.
6. Send confirmation email/notification.

### Add-on checkout preflight

Before creating checkout session for any add-on:

1. Authenticate user.
2. Load active enrolments.
3. Match requested add-on type to parent flag.
4. If one eligible enrolment exists, apply there.
5. If many eligible enrolments exist, require user selection.
6. If none exist, reject checkout.

### Tutor Book PDF handling

- Watermark PDF at download time with buyer name/email.
- Prevent double charges if user already owns Tutor Book entitlement.
- Use consistent output filename: `[email - THE TUTOR BOOK - First Edition 2026.pdf]`.

---

## Design language

- Primary navy: `#0E2841`
- Mid blue: `#156082`
- Gold: `#D4A44F`
- Background neutrals: white and soft beige `#EAE9E6`
- Use rounded corners, soft shadows, generous whitespace, and medical education iconography.
- Desktop-first, responsive to tablet and mobile.

---

## Agent execution plan

When implementing this spec, follow these phases:

### Phase 1 — Audit current project

- Find existing product catalogue code, seed data, routes, dashboard unlock logic, checkout logic, and entitlement logic.
- Map current implementation to the product IDs in this file.
- Produce a gap report before editing.

### Phase 2 — Data model alignment

- Add/repair product, enrolment, entitlement, add-on purchase, writing submission, speaking booking, and recall models.
- Ensure migrations are safe and reversible.
- Preserve existing real users and purchases.

### Phase 3 — Product seed implementation

- Create/repair canonical product seed file.
- Use exactly the `product_id` values from this document.
- Mark `full-pharmacy` as `draft` / hidden from public UI.

### Phase 4 — Checkout and entitlement logic

- Implement product purchase flow.
- Implement add-on preflight checks.
- Implement entitlement counter changes.
- Implement double-purchase blocking.

### Phase 5 — Frontend catalogue and dashboards

- Build/repair public catalogue.
- Build/repair product detail pages.
- Drive dashboard modules from entitlements.
- Enforce flag-based add-on visibility.

### Phase 6 — QA and regression testing

- Test every SKU.
- Test every add-on type.
- Test ineligible add-on blocks.
- Test expired access.
- Test permanent Tutor Book access.
- Test draft Pharmacy visibility is hidden.

---

## Acceptance criteria

Implementation is complete only when all of these pass:

- Every product ID in this file exists exactly once in the database/seed layer.
- No unknown placeholder/demo SKU appears in production catalogue.
- Product prices match this spec.
- `full-pharmacy` is not public until activated.
- `writing_addons=FALSE` products show no writing add-on UI anywhere.
- `speaking_addons=FALSE` products show no speaking add-on UI anywhere.
- `tutor_book_discount=FALSE` products show no £32 Tutor Book add-on UI anywhere.
- Add-ons cannot be purchased without eligible parent enrolment.
- Add-ons never create standalone dashboards.
- Writing add-ons increment `writing_assessments_remaining`.
- Speaking add-ons increment `speaking_sessions_remaining`.
- Tutor Book add-on flips `tutor_book=true`.
- Payment webhook creates enrolment and entitlement records reliably.
- Expired courses lock content.
- Permanent Tutor Book entitlement remains available after course expiry.
- Admin can inspect and correct entitlements.
- Tests cover eligible/ineligible add-ons, bundles, expiry, and double-purchase prevention.

---

## Developer warning

Do not "simplify" the three independent flags into one generic `has_addons` flag. The product catalogue depends on separate eligibility for Writing add-ons, Speaking add-ons, and Tutor Book discount. Merging them will produce incorrect dashboards and wrong checkout options.
