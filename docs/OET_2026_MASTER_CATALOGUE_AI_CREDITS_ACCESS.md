# OET 2026

# MASTER CATALOGUE, AI CREDITS & ACCESS SPECIFICATION

> **Single consolidated source of truth for catalogue products, AI packages, candidate balances, payment verification, profession-based access and entitlement enforcement.**

> **Conversion note:** This Markdown preserves the source specification content and wording while normalizing PDF-only layout. Repeated page headers/footers are omitted as non-requirement duplication. Tables are retained as layout-faithful text blocks where doing so best preserves exact cell wording.

### Governing terminology

Candidate-facing and per-user admin entitlement balances must use CREDITS / ATTEMPTS / UNLIMITED, never raw provider tokens. Provider/API token usage is platform-level operational data visible only in the separate global admin AI/API Usage & Billing view.

Consolidated and updated: 23 August 2026

OET with Dr. Ahmed Hesham • support@oetwithdrhesham.co.uk

## Document status and precedence

### MASTER SOURCE OF TRUTH

This document consolidates the supplied catalogue/package PDFs, Web App Access & Payment Requirements, Payment & Access Cycle, and the later 5 AI Credits gift rule. Duplicate copies are consolidated once. Where older PDFs conflict, the rules in the Master Rules section below take precedence.

## Contents

1. Master rules and terminology
2. AI credit architecture and candidate dashboard
3. Full-course 5-credit gift matrix
4. AI/practice/mock package balance matrix
5. Entitlement enforcement, subtest isolation & attempt persistence
6. Payment, billing and access workflow
7. Admin controls and profession-based access
8. Acceptance and regression tests
9. Complete product catalogue (Products 1-47)
10. Source documents consolidated

## 1. Master rules and terminology

### Rule A - Five free credits on every qualifying Full Course

Every eligible Full Course receives exactly 5 Gifted Shared AI Credits. A bundle that contains an eligible Full Course inherits the same 5-credit gift once; it must not receive another 5 merely because the course is nested inside the bundle.

### Rule B - Shared means universal

Shared Credits are a universal balance usable for Reading, Listening, Writing or Speaking. The 5 gifted credits are Shared Credits.

### Rule C - Credits are not access flags

Possessing AI credits must never unlock an entire Reading, Listening, Writing or Speaking library. Course/content entitlement and metered credit balance are separate controls and both must be enforced server-side.

### Rule D - Candidate visibility

The candidate dashboard must show the same balances the admin can see: Reading Credits, Listening Credits, Writing Credits, Speaking Credits and Shared Credits. Any extra restricted flexible pool (for Quick Check / Exam Prep Pro) and Full Mock attempts must also be visible when present.

### Rule E - Live balance updates

After every use, the candidate must immediately see what was used and what remains. Example: “1 Reading credit used - 2 Reading Credits remaining.” If Shared Credits are used, the message must say so explicitly.

### Credit types

```text
 Balance                         Type                                 Consumption                          Scope

                                                                      1 Reading exam attempt per
 Reading Credits                 Subtest-specific                                                          Reading only
                                                                      credit

                                                                      1 Listening exam attempt per
 Listening Credits               Subtest-specific                                                          Listening only
                                                                      credit

                                                                      1 AI-graded Writing submission
 Writing Credits                 Subtest-specific                                                          Writing only
                                                                      per credit

                                                                      1 AI-graded Speaking submission
 Speaking Credits                Subtest-specific                                                          Speaking only
                                                                      per credit

                                                                      Reading 1; Listening 1; Writing 2;
 Shared Credits                  Universal                                                                 Any of the four subtests
                                                                      Speaking 2

                                                                      1 AI-graded Writing
                                                                      submission or 1 AI-graded            Quick Check / Exam Prep
 Flexible W/S Credits            Restricted shared pool               Speaking card per credit;            Pro only
                                                                      Writing/Speaking only

                                                                      1 complete four-subtest mock per     Must not consume ordinary AI
 Full Mock Attempts              Separate allowance
                                                                      attempt                              credits

```

### Important implementation distinction

The separate Writing/Speaking packages are sold as a number of graded submissions/cards (for example Writing Starter = 3 letters). Their subtest-specific credit balance should therefore represent those attempts directly. Shared Credits remain universal units and use the 2-credit cost for Writing/Speaking stated in the 5-credit gift rule.

## 2. AI credit architecture and candidate dashboard

### Required candidate dashboard card

```text
 Candidate field                             Display value                                     Requirement

                                             Total / Used / Remaining (or Unlimited) +
 Reading Credits                             source package + valid from + expiry date +       Visible on Candidate + Admin user profile
                                             days left

                                             Total / Used / Remaining (or Unlimited) +
 Listening Credits                           source package + valid from + expiry date +       Visible on Candidate + Admin user profile
                                             days left

                                             Total / Used / Remaining (or Unlimited) +
 Writing Credits                             source package + valid from + expiry date +       Visible on Candidate + Admin user profile
                                             days left

                                             Total / Used / Remaining (or Unlimited) +
 Speaking Credits                            source package + valid from + expiry date +       Visible on Candidate + Admin user profile
                                             days left

                                             Total / Used / Remaining (or Unlimited) +
 Shared Credits                              source package + valid from + expiry date +       Visible on Candidate + Admin user profile
                                             days left

                                             If applicable: Total / Used / Remaining +
 Flexible W/S Credits                        source package + valid from + expiry date +       Conditional; Candidate + Admin user profile
                                             days left

 Full Mock Attempts                          If purchased: Total / Used / Remaining +          Conditional; Candidate + Admin user profile

 Candidate field                              Display value                                 Requirement

                                              source package + valid from + expiry date +
                                              days left
 All Subtest Activity                         Exact Reading/Listening exam or               Mandatory
                                              Writing/Speaking card name/ID,
                                              subtest, status, started date/time,
                                              balance source, credits used and
                                              remaining; Resume/Reopen/Review
                                              link

```

Do not display raw AI-provider “tokens” anywhere in the candidate interface. Candidate-facing balances must use Credits / Attempts / Unlimited only. The candidate and admin must read from the same authoritative balance ledger, not separate calculated copies. For each balance show source package, Total Granted/Purchased, Used, Remaining, valid-from/activation date, expiry date and days left. Remaining must be prominent; when multiple grants feed one bucket, show grant-level validity so the candidate can see what expires when. A transaction/history view must identify date/time, exact exam/card/activity name and ID, subtest, attempt status, credits used, balance source (Reading/Listening/Writing/Speaking/Shared/Flexible W/S/Mock), source package and remaining balance. For all four subtests, the candidate must be able to see exactly which Reading/Listening exams and Writing/Speaking cards have already been started/opened, and which attempts are in progress or completed.

### Candidate/user credits vs platform AI-provider tokens

Candidate-facing rule: raw AI-provider token counts must never appear anywhere in the candidate interface. Remove/replace displays such as “This month: 0 / 20,000 tokens” and “Credits available: 20,000 tokens”. Candidate balances are shown only as Credits / Attempts / Unlimited according to the purchased entitlement. Per-user admin rule: when the admin opens User Management > Candidate Profile, that individual user must also be represented only in Credits / Attempts / Unlimited. Do not show that candidate a token quota and do not show a token balance as if it were the candidate’s entitlement. Platform-admin exception: raw provider token usage is operational infrastructure data and must be visible only in a separate admin-level AI/API Usage & Billing area. This admin-only view should show the provider/API token quota or purchased capacity, tokens used (for example today / current billing period), tokens remaining, and enough billing/refill information for the admin to know when the provider account needs to be topped up or renewed. Provider tokens and candidate credits are different ledgers and must never be numerically equated. Candidate credits determine what the candidate may start; provider tokens measure the platform’s underlying AI/API consumption for services such as Writing and Speaking. Validity must be visible for every candidate entitlement. For each credit/attempt grant show source package, Total granted/purchased, Used, Remaining, activation/valid-from date, expiry date and days remaining. If more than one active grant contributes to the same bucket, show the grant-level breakdown as well as any aggregate headline balance. The 5 Gifted Shared AI Credits attached to an eligible Full Course inherit the validity/expiry of that qualifying Full Course unless an explicit package configuration states otherwise. Separate AI/practice/mock products use the validity defined in the package matrix (for example 30 days, 90 days or 6 months).

### Mandatory candidate activity history - all four subtests

The candidate panel must contain a clear attempt history for Reading, Listening, Writing and Speaking. Reading/Listening entries must show the exact exam title/ID; Writing entries the exact Writing letter/card title/ID; Speaking entries the exact Speaking card title/ID. Every entry must show subtest, date/time started, status (In progress / Completed), the balance/package that authorized it, credits deducted, remaining balance after the first start, and a Resume / Reopen / Review action where applicable. Merely browsing an exam information page must not count as an opened attempt. An exam is recorded as opened/started only when the backend creates the unique attempt ID and reserves/deducts the applicable allowance. Reopening, resuming or reviewing the same existing attempt must show the original attempt in history and must not create a duplicate attempt or consume another credit. This applies equally to Reading exams, Listening exams, Writing cards/letters and Speaking cards. Unlimited balances must display “Unlimited”; do not emulate unlimited access by assigning a very large number.

### Usage feedback examples

- Reading-specific balance used: “1 Reading Credit used. 2 Reading Credits remaining.”
- Shared balance used for Reading: “1 Shared Credit used for Reading. 4 Shared Credits remaining.”
- Shared balance used for Writing: “2 Shared Credits used for Writing. 3 Shared Credits remaining.”
- No balance available: “You do not have enough credits to start this activity. Purchase credits or use another active package.”

### Balance consumption priority

- Use the matching subtest-specific balance first when available.
- For Writing/Speaking, use any applicable restricted Flexible W/S pool next.
- Use Universal Shared Credits only when the specific/restricted balance is unavailable, or when the product/business rule explicitly chooses Shared.
- Never allow a balance to go below zero.
- Deduction/reservation must be atomic and idempotent so refreshes, duplicate requests or two concurrent devices cannot create free extra attempts or double-charge the same attempt.

## 3. Full-course 5-credit gift matrix

### Gift rule

Any qualifying Full Course = 5 Gifted Shared AI Credits. The gift is universal and can be spent across Reading, Listening, Writing and Speaking using the shared-credit consumption rates.

```text
#                                 Product                             Gifted AI balance                 Reason

                                  Full Condensed Recorded OET
1                                                                     5 Shared Credits                  Direct eligible Full Course
                                  Course - Medicine

2                                 Full Physiotherapy OET Course       5 Shared Credits                  Direct eligible Full Course

                                  Full Allied Health Profession OET
3                                                                     5 Shared Credits                  Direct eligible Full Course
                                  Course

                                  Full Condensed Recorded Course                                        Contains / inherits an eligible Full
4                                                                     5 Shared Credits
                                  + TutorBook                                                           Course

5                                 Full Nursing OET Course             5 Shared Credits                  Direct eligible Full Course

                                  Nursing Course + Assessment                                           Contains / inherits an eligible Full
6                                                                     5 Shared Credits
                                  Package                                                               Course

                                                                                                        Contains / inherits an eligible Full
7                                 Nursing Premium Bundle              5 Shared Credits
                                                                                                        Course

8                                 Full Pharmacy OET Course            5 Shared Credits                  Direct eligible Full Course

                                  Basic English Course -
9                                                                     0 gifted                          Not a Full Course
                                  Preparation for OET

10                                Full Crash Course - General OET     5 Shared Credits                  Direct eligible Full Course

                                  Full Crash Course + 3 Writing                                         Contains / inherits an eligible Full
11                                                                    5 Shared Credits
                                  Assessments                                                           Course

                                  Full Crash Course + 5 Writing                                         Contains / inherits an eligible Full
12                                                                    5 Shared Credits
                                  Assessments                                                           Course

```

### No double gifting in bundles

Example: Full Condensed Course + TutorBook receives 5 Shared Credits in total, not 10. Nursing Premium Bundle inherits the 5-credit gift from the Full Nursing course content; the same purchase must not be granted twice through

nested package inheritance.

### Idempotency requirement

The 5-credit gift is applied once per qualifying purchase/assignment event. Retrying payment webhooks, refreshing admin Save, or re-processing the same order must not grant the gift again.

## 4. AI, practice and mock package balance matrix

```text
#                               Package                               Candidate balance / allowance    Validity

30                              Quick Check                           R:3 | L:3 | Flexible W/S:5       30 days

31                              Exam Prep Pro                         R:6 | L:6 | Flexible W/S:15      90 days

                                                                      R:Unlimited | L:Unlimited |
32                              OET Mastery                                                            6 months
                                                                      W:Unlimited | S:Unlimited

33                              1 Full Mock                           1 Mock attempt (separate)        6 months

34                              3 Full Mocks                          3 Mock attempts (separate)       6 months

35                              5 Full Mocks                          5 Mock attempts (separate)       6 months

36                              Listening Starter                     L:3                              30 days

37                              Listening Standard                    L:6                              90 days

38                              Listening Pro                         L:Unlimited                      6 months

39                              Reading Starter                       R:3                              30 days

40                              Reading Standard                      R:6                              90 days

41                              Reading Pro                           R:Unlimited                      6 months

42                              Writing Starter                       W:3 submissions                  30 days

43                              Writing Standard                      W:8 submissions                  90 days

44                              Writing Pro                           W:15 submissions                 6 months

45                              Speaking Starter                      S:3 cards                        30 days

46                              Speaking Standard                     S:8 cards                        90 days

47                              Speaking Pro                          S:15 cards                       6 months

```

### Quick Check and Exam Prep Pro must remain restricted

The supplied catalogue defines their 5 / 15 flexible AI grading credits as usable for Writing or Speaking. Do not silently convert this restricted W/S pool into Universal Shared Credits, because that would also allow Reading/Listening and would change the product. If the backend currently has only one universal Shared field, add a restricted Flexible W/S balance or equivalent entitlement rule.

### Separate Reading/Listening packages

Their deterministic marking must not consume Universal Shared AI Credits. They use their own Reading/Listening package balances. Starter = 3, Standard = 6, Pro = Unlimited.

### Full mocks

Mock allowances are separate from all AI balances. A 3 Full Mocks package gives exactly 3 complete mock attempts and does not spend the candidate’s normal Reading, Listening, Writing, Speaking or Shared Credits.

## 5. Entitlement enforcement, subtest isolation & attempt persistence

### CRITICAL DEFECT TO FIX

A candidate account that has only 5 AI credits must not be able to open/start every Reading exam. Five credits are a balance, not an unlimited Reading entitlement. The same rule applies to Listening, Writing and Speaking.

### Required authorization model

- Separate content entitlement from credit balance. A Full Course can legitimately grant broad course-library access; a credit-only account cannot inherit that course-library entitlement.
- Every protected exam/activity endpoint must check the candidate’s active entitlement and remaining balance on the server. Client-side hiding alone is insufficient.
- For a metered Reading attempt: require Reading Credits >= 1, or Shared Credits >= 1, or an active unlimited Reading entitlement. Otherwise block the start request.
- For Listening: require Listening Credits >= 1, or Shared Credits >= 1, or active unlimited Listening.
- For Writing: require Writing Credits >= 1, or eligible Flexible W/S credit, or Shared Credits >= 2, or active unlimited Writing.
- For Speaking: require Speaking Credits >= 1, or eligible Flexible W/S credit, or Shared Credits >= 2, or active unlimited Speaking.
- A direct URL, bookmarked exam link, mobile app request or API call must not bypass the same authorization check.
- When balance reaches zero, the next start attempt must be blocked immediately and consistently across web, Android and iOS.

### Subtest isolation and exact package caps

A subtest-specific package grants access only to that subtest. A Reading-only package must not unlock Listening, Writing or Speaking. A Listening-only package must not unlock Reading, Writing or Speaking. The same isolation applies to Writing-only and Speaking-only packages. Example: Reading Starter grants exactly 3 Reading attempts. If the candidate has no other valid Reading entitlement or Universal Shared Credits, the 4th new Reading attempt must be blocked. Listening Starter grants exactly 3 Listening attempts; the 4th must be blocked under the same rule. No unused balance from one subtest may be silently converted into another subtest. A candidate with Reading Credits only and no other entitlement must not start a Listening exam. Universal Shared Credits are the explicit exception: if the candidate legitimately has Shared Credits, those may be used across Reading, Listening, Writing and Speaking according to the Shared Credit consumption rules. Restricted Flexible W/S Credits remain usable only for Writing or Speaking.

### When to deduct a credit

- Browsing the catalogue or viewing an exam information page should not consume a credit.
- Deduct/reserve the credit when a new attempt is actually started and a unique attempt ID is created. For Writing and Speaking, the first start/open of a new card/letter creates the attempt and consumes the applicable balance.
- Refreshing, resuming, reopening or reviewing the same existing attempt ID must not deduct again, including Writing/Speaking cards reopened later from the candidate dashboard.
- If the candidate deliberately selects Start New Attempt on the same Writing/Speaking card and the product rules allow
- another attempt, that is a new attempt ID and consumes a new applicable credit. Resume/Reopen/Review of the existing
- attempt remains free.
- A second simultaneous start request must be rejected or mapped to the same attempt so the user cannot overspend.
- If business rules allow an abandoned attempt to be restored/refunded, that reversal must be a ledger transaction; never edit history invisibly.

### Concrete expected behavior for the reported test

```text
 Scenario                                                                 Expected result

 Account has only 5 Shared Credits; no Reading package/course             May start at most 5 Reading attempts if all 5 Shared Credits are
 entitlement                                                              spent only on Reading. Sixth Reading attempt is blocked.

                                                                          Writing consumes 2 Shared Credits; 3 Shared Credits remain, so at
 Same account uses 1 Writing submission first
                                                                          most 3 Reading attempts remain.

                                                                          3 Reading-specific attempts available first, plus up to 5 more
 Account has Reading Starter + 5 Shared Credits                           Reading attempts from Shared if the candidate chooses/needs
                                                                          them.

                                                                          Reading is Unlimited for the package validity; Shared balance is
 Account has Reading Pro
                                                                          unaffected by ordinary Reading usage.

```

### Candidate content cleanup - disable test/demo Reading and Listening items

- Reading: disable the candidate-facing “Other papers” series/folder shown in the supplied Reading screenshot. It was created only for testing and must not appear anywhere in the candidate Reading dashboard/catalogue.
- The “Other papers” content may remain in the backend/admin for development history if needed, but it must be disabled/hidden from candidates and must not be startable through a copied URL, API request, Android app or iOS app.
- Listening: apply the same rule to any Listening exam, folder or series that exists only for testing/demo/staging. Such test content must be disabled from the candidate Listening dashboard/catalogue and must not be startable through direct routes.
- Production filtering must be based on an explicit publish/visibility status (for example Published + Candidate Visible), not only on whether the frontend happens to list the item.

## 6. Payment, billing and access workflow

### Governing payment split

Regular / non-AI packages (Products 1-29) use a verification flow: self-service checkout, invoice/order record created, WhatsApp proof sent, admin approval, then access. AI / Practice / Mock packages (Products 30-47) use instant automatic access after successful server-confirmed payment: invoice/order record is created, but no proof upload, WhatsApp proof or admin approval is required. Pending or failed payment must never grant access.

### Flow A - Regular / non-AI packages (Products 1-29)

```text
 Step                                           Stage                                            Required behavior

                                                                                                 Candidate opens Subscription & Packages
 1                                              Subscription & Packages
                                                                                                 and selects the required regular package.

 2                                              Add to Cart                                      Candidate adds the package to cart.

                                                                                                 Candidate proceeds to checkout and pays
 3                                              Proceed to Payment                               using any currently enabled inside/outside
                                                                                                 payment method.

                                                                                                 The system creates one order/invoice
                                                                                                 record. A copy is visible in Candidate
                                                                                                 Dashboard > Billing & Invoices and the
 4                                              Invoice / order created
                                                                                                 same record is visible in Admin > Billing >
                                                                                                 Orders & Payments. Order status remains
                                                                                                 Pending Verification.

 Step                           Stage                                        Required behavior

                                                                             Candidate sends/uploads the payment
                                                                             invoice/receipt/proof to the configured
 5                              WhatsApp proof                               WhatsApp Business number. A visible
                                                                             Send on WhatsApp action must be
                                                                             available.

                                                                             Admin opens Admin > Billing > Orders &
                                                                             Payments, checks the candidate, package,
 6                              Admin review                                 amount, payment reference and
                                                                             invoice/proof, then clicks Approve/Accept or
                                                                             Reject.

                                                                             On Approve/Accept, the system
                                                                             automatically grants the exact purchased
                                                                             package and correct profession-specific
 7                              Automatic grant after approval
                                                                             content. No second manual assignment
                                                                             should be needed for standard web-app
                                                                             packages.
 8                              Manual/external exception                    If a product is explicitly configured for
                                                                             Telegram/manual material delivery,
                                                                             approval records the payment but fulfilment
                                                                             remains pending until the admin completes
                                                                             the configured manual delivery.

```

### Flow B - Instant AI / Practice / Mock access (Products 30-47)

```text
 Step                           Stage                                        Required behavior

                                                                             Candidate selects any Product 30-47:
                                                                             Quick Check, Exam Prep Pro, OET
 1                              Subscription & Packages                      Mastery, 1/3/5 Full Mocks, or any separate
                                                                             Listening/Reading/Writing/Speaking
                                                                             Starter/Standard/Pro package.

                                                                             Candidate adds the AI/practice/mock
 2                              Add to Cart
                                                                             product to cart.

                                                                             Candidate completes checkout through the
 3                              Proceed to Payment
                                                                             enabled payment method.

                                                                             The backend receives a successful
                                                                             provider confirmation/webhook. Pending,
 4                              Server confirms payment
                                                                             failed or cancelled payments must not grant
                                                                             any credits, attempts or entitlement.

                                                                             The system creates the invoice/order
                                                                             automatically in Candidate Dashboard >
 5                              Invoice / order created                      Billing & Invoices and Admin > Billing >
                                                                             Orders & Payments with Paid/Completed
                                                                             status.

                                                                             Immediately after successful payment
                                                                             confirmation, the system adds the exact
 6                              Instant automatic access                     purchased Reading, Listening, Writing,
                                                                             Speaking, Flexible W/S, Unlimited or Full
                                                                             Mock allowance to the candidate account.

                                                                             No proof upload, WhatsApp submission or
 7                              No proof / no approval                       manual admin approval is required for
                                                                             Products 30-47.
 8                              Dashboard updates                            Candidate Dashboard immediately shows
                                                                             the purchased package, validity, exact
                                                                             credits/allowances added, Used and
                                                                             Remaining balances, and all subsequent
                                                                             attempt/card activity. Duplicate provider
                                                                             callbacks must not duplicate the grant.

```

### Invoice / receipt storage and exact UI locations

```text
 Surface                                     Required location                           Must show / allow

                                                                                         Invoice/receipt, order ID, package name,
                                                                                         amount/currency, payment
 Candidate                                   Candidate Dashboard > Billing & Invoices
                                                                                         method/reference, payment status,
                                                                                         date/time, and View/Download.

                                                                                         Same invoice/order plus candidate
                                                                                         name/email, package, amount/currency,
 Admin - primary                             Admin > Billing > Orders & Payments         provider/reference, status, and for Products
                                                                                         1-29 an Approve/Accept and Reject action
                                                                                         with approval audit data.

                                                                                         Linked order/invoice history for that
                                             Admin > User Management > Candidate         candidate so billing, package access and
 Admin - candidate drill-down
                                             Profile > Billing / Orders                  candidate identity can be reviewed
                                                                                         together.

                                                                                         Purchased Product 30-47, exact
                                                                                         balances/allowances added, validity,
                                             Candidate Dashboard > AI Credits /
 AI / Practice / Mock balance view                                                       Used/Remaining, and links to the activity
                                             Practice & Mocks
                                                                                         history for Reading, Listening, Writing and
                                                                                         Speaking.
```

Products 1-29: the invoice/order record is mandatory, remains Pending Verification after payment, and the candidate must send the payment invoice/receipt/proof to WhatsApp before admin approval. Products 30-47: the invoice/order is still mandatory, but proof upload and WhatsApp submission are not required. Every order must store candidate/user ID, email, exact product, date/time, amount/currency, payment method, provider/order reference, invoice/receipt document or view, payment status, and resulting entitlement/credit grant. Regular-package approval must additionally store approved/rejected by and timestamp. For Products 1-29 use a canonical verification state such as Pending Verification, Approved/Verified or Rejected. For Products 30-47 use the provider payment state such as Pending, Paid/Completed, Failed/Cancelled; Paid/Completed triggers entitlement immediately. Show Send Proof on WhatsApp only for Products 1-29. It must not block or appear as a requirement for Products 30-47. Gateway/provider names remain configurable in admin and must not be hard-coded into the entitlement engine. Successful AI/practice/mock grants must be atomic and idempotent so webhook retries or refreshes cannot create duplicate credits or mock attempts.

## 7. Admin controls and profession-based access

### Manual admin access must always remain available

- Search a user by email and manually grant any course, package, section, video, material, recall or bundle.
- Choose the exact profession-specific product even when the purchase occurred outside the website.
- Set start and expiry date. Standard course access defaults to 180 days unless the product has another defined validity.
- Extend, shorten, suspend, restore or revoke access at any time.
- Manual access must work for automatic orders, manual orders, bank transfer, external methods, complimentary access and migrated users.
- Granting one new package must not remove other valid entitlements or balances.
- Admin must be able to grant/remove/adjust each credit bucket separately and every change must be written to the credit ledger.

### Admin visibility: per-user credits vs platform AI/API capacity

Admin > User Management > Candidate Profile must show the same authoritative candidate entitlement ledger as the candidate dashboard: Reading Credits, Listening Credits, Writing Credits, Speaking Credits, Shared Credits, Flexible W/S Credits when applicable, and Full Mock Attempts when applicable. For each user balance/grant, admin must see source package, Total granted/purchased, Used, Remaining, valid-from/activation date, expiry date and days remaining. This view must update immediately when a candidate starts a new metered activity, buys an AI package or receives/removes an admin grant.

Raw provider tokens must not be mixed into the per-user candidate profile. The per-user view is credits/attempts only. A separate Admin > AI/API Usage & Billing (or equivalent clearly named global admin page) must show the platform’s actual provider token consumption/capacity used by AI services such as Writing and Speaking, so the admin can monitor usage and top up/renew the provider account when required. This page is platform-level, not candidate-level.

### Profession mapping

```text
Registered profession                       Generic purchased package                    Access granted

Medicine                                    Speaking Crash Course                        Medicine Speaking content

Nursing                                     Speaking Crash Course                        Nursing Speaking content

Pharmacy                                    Writing Crash Course                         Pharmacy Writing content

                                                                                         Physiotherapy Writing content when that
Physiotherapy                               Writing Crash Course
                                                                                         package is configured for Physiotherapy

```

The candidate must never receive another profession’s videos/materials by mistake. If a package is unavailable for the registered profession, block checkout and show a clear message. After first purchase, profession changes must be admin-controlled or trigger a review. All protected content must require valid backend authorization. Direct links must not bypass permissions.

## 8. Acceptance and regression tests

```text
ID                                          Test                                         Pass condition

                                                                                         User has 5 Shared Credits and no other
                                                                                         Reading entitlement. Start 5 Reading attempts
A01                                         5-credit-only Reading cap
                                                                                         successfully; 6th is blocked. PASS only if no
                                                                                         other Reading exam can be started.

                                                                                         Start 1 Writing using Shared (cost 2), then
A02                                         Mixed shared consumption                     Reading. Remaining Shared must be 3 after
                                                                                         Writing and 2 after one Reading.

                                                                                         User has Reading Credits and Shared Credits.
                                                                                         Reading uses Reading balance first; Shared
A03                                         Specific-before-shared
                                                                                         remains unchanged until Reading balance is
                                                                                         exhausted.

                                                                                         Exactly 3 Reading, 3 Listening and 5 flexible
A04                                         Quick Check limits
                                                                                         W/S credits. No unlimited library access.

                                                                                         Exactly 6 Reading, 6 Listening and 15 flexible
A05                                         Exam Prep Pro limits
                                                                                         W/S credits. No unlimited library access.

                                                                                         Reading, Listening, Writing and Speaking are
A06                                         OET Mastery unlimited                        Unlimited during validity and UI displays
                                                                                         Unlimited.

                                                                                         Exactly 3 AI-graded Writing submissions; 4th
A07                                         Writing Starter
                                                                                         is blocked unless another balance is available.

                                                                                         Exactly 3 AI-graded Speaking submissions;
A08                                         Speaking Starter                             4th is blocked unless another balance is
                                                                                         available.

                                                                                         3 Full Mocks gives 3 complete mocks and
A09                                         Mock separation
                                                                                         does not reduce ordinary AI balances.

                                                                                         Every qualifying Full Course grants 5 Shared
A10                                         Full-course gift
                                                                                         Credits once.

                                                                                         Reprocessing the same order/webhook/admin
A11                                         No duplicate gift
                                                                                         save does not add another 5 credits.

ID    Test                                          Pass condition

                                                    Full Course + TutorBook gets 5 total gifted
A12   Bundle inheritance
                                                    Shared Credits, not 10.

                                                    Candidate remaining balances exactly match
A13   Candidate/admin parity                        admin remaining balances after each
                                                    transaction.

                                                    Refresh/reopen of same attempt does not
A14   Refresh safety
                                                    double-deduct credits.

                                                    Two simultaneous start requests cannot spend
A15   Concurrency safety
                                                    the same final credit twice.

                                                    A user with zero balance cannot start a
A16   Direct URL protection                         protected exam through a copied/direct URL
                                                    or API request.

                                                    Expired package credits/allowances cannot be
A17   Expiry                                        consumed; still-active balances from other
                                                    packages remain available.

                                                    Medicine purchase maps to Medicine content;
A18   Profession isolation                          Nursing to Nursing; etc. No cross-profession
                                                    leakage.

                                                    For Products 1-29, payment creates an
                                                    invoice/order but access remains blocked
A19   Regular-package payment gating                while Pending Verification. After admin
                                                    Approve/Accept, the exact purchased package
                                                    is granted automatically.

                                                    Admin can grant, extend, suspend, restore
A20   Manual admin override                         and revoke without deleting unrelated valid
                                                    entitlements.

A21   Subtest isolation                             User owns Reading Starter only, with
                                                    no Shared Credits or other entitlement.
                                                    Reading is available only within its 3-
                                                    attempt allowance; every Listening,
                                                    Writing and Speaking start request is
                                                    blocked.
A22   Exact Reading/Listening cap                   Reading Starter allows exactly 3 new
                                                    Reading attempts and blocks the 4th;
                                                    Listening Starter allows exactly 3 new
                                                    Listening attempts and blocks the 4th,
                                                    unless another valid same-subtest or
                                                    Universal Shared balance exists.
A23   All-four-subtest activity history             After each Reading/Listening exam or
                                                    Writing/Speaking card is first started,
                                                    Candidate Dashboard shows the exact
                                                    item title/ID, subtest, start time, status,
                                                    authorization/balance source, credits
                                                    used and remaining.
                                                    Reopen/Resume/Review of the same
                                                    attempt shows the same history item
                                                    and does not deduct again.
A24   Writing/Speaking reopen persistence           Start a Writing or Speaking card once and
                                                    consume the applicable balance. Leave the
                                                    activity, then reopen/resume/review that exact
                                                    attempt ID. No second credit is deducted. A
                                                    deliberate Start New Attempt, if allowed,
                                                    creates a new attempt and consumes a new
                                                    credit.
A25   Invoice visibility parity                     Every successful or pending order has one
                                                    invoice/order record visible in Candidate
                                                    Dashboard > Billing & Invoices and the same

ID                                      Test                                             Pass condition
                                                                                         underlying record in Admin > Billing > Orders
                                                                                         & Payments with matching order ID, product,
                                                                                         amount/currency and payment
                                                                                         reference/status.
A26                                     Regular WhatsApp verification                    For Products 1-29, candidate is prompted to
                                                                                         send invoice/receipt/proof on WhatsApp, order
                                                                                         remains Pending Verification, and admin can
                                                                                         Approve/Accept or Reject from Admin > Billing
                                                                                         > Orders & Payments.
A27                                     Instant AI/practice/mock access                  For every Product 30-47, a successful server-
                                                                                         confirmed payment immediately grants the
                                                                                         exact purchased
                                                                                         credits/allowances/Unlimited/Mock entitlement
                                                                                         and updates Candidate Dashboard without
                                                                                         admin intervention.
A28                                     No proof for Products 30-47                      Quick Check, Exam Prep Pro, OET Mastery,
                                                                                         Full Mocks and all separate
                                                                                         Listening/Reading/Writing/Speaking packages
                                                                                         never require proof upload, WhatsApp
                                                                                         submission or admin approval after a
                                                                                         successful confirmed payment.
A29                                     AI purchase idempotency and failure safety       A pending/failed/cancelled Product 30-47
                                                                                         payment grants nothing. Replaying the same
                                                                                         successful webhook/order callback does not
                                                                                         duplicate credits, unlimited entitlement or Full
                                                                                         Mock attempts.
A30                                     No candidate token display                       Across web, Android and iOS, candidate
                                                                                         pages contain no raw provider token
                                                                                         quota/usage/remaining values. Candidate
                                                                                         balances show Credits / Attempts / Unlimited
                                                                                         only.
A31                                     Per-user admin uses credits only                 Admin > User Management > Candidate
                                                                                         Profile shows Reading, Listening, Writing,
                                                                                         Speaking and Shared balances (plus
                                                                                         conditional Flexible W/S and Mock balances)
                                                                                         as credits/attempts, with no raw provider token
                                                                                         quota attached to the user.
A32                                     Credit validity visibility                       For every active grant, candidate and per-user
                                                                                         admin views show source package, Total,
                                                                                         Used, Remaining, valid-from/activation date,
                                                                                         expiry date and days left. Gifted Full-Course
                                                                                         Shared Credits share the qualifying course
                                                                                         expiry unless explicitly configured otherwise.
A33                                     Global admin token monitoring                    A separate platform-level admin AI/API Usage
                                                                                         & Billing view shows provider token
                                                                                         quota/capacity, used and remaining values
                                                                                         needed for refill/renewal monitoring. These
                                                                                         values do not alter or replace candidate credit
                                                                                         balances.
A34                                     Reading test folder removed                      The Reading candidate dashboard/catalogue
                                                                                         does not show “Other papers”. Direct
                                                                                         URL/API/mobile access to that disabled test
                                                                                         series is denied.
A35                                     Listening test content removed                   Any Listening exam/folder/series flagged as
                                                                                         test/demo/staging is hidden from candidate
                                                                                         surfaces and cannot be started through direct
                                                                                         URL/API/mobile routes.

```

## 9. Complete product catalogue (Products 1-47)

### How to read this section

The descriptions below consolidate the supplied catalogue copy. Updated AI-credit rules are added where necessary. For qualifying Full Courses, the 5 Shared Credits rule overrides older descriptions that omitted the gift.

### 1. Full Condensed Recorded OET Course - Medicine • 5 Gifted Shared AI Credits

**Profession:** Medicine | Duration: 40+ hours | Category: Full recorded course

**Website description:** A complete, condensed and exam-focused recorded Medicine course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.

**Access:** 6 months from purchase | Format: Recorded videos plus structured study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- 160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources
- 100+ Reading exams with answer keys and rationales
- 90+ Writing tasks covering the main OET letter types
- 100+ Speaking cards across different scenarios and card types
- Recent recall updates plus older recalls from 2023 onwards
- 5 Writing letter assessments with personalised correction
- 1 private Speaking session
- 5 AI practice credits
- Continuous Q&A support during the access period

**Best for:** Medicine candidates who want one comprehensive recorded course covering the full exam.

### 2. Full Physiotherapy OET Course • 5 Gifted Shared AI Credits

**Profession:** Physiotherapy | Duration: 40+ hours | Category: Full recorded course

**Website description:** A complete, condensed and exam-focused recorded Physiotherapy course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.

**Access:** 6 months from purchase | Format: Recorded videos plus structured study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- 160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources
- 100+ Reading exams with answer keys and rationales
- 90+ Writing tasks covering the main OET letter types
- 100+ Speaking cards across different scenarios and card types
- Recent recall updates plus older recalls from 2023 onwards
- 5 Writing letter assessments with personalised correction
- 1 private Speaking session
- 5 AI practice credits
- Continuous Q&A support during the access period

**Best for:** Physiotherapy candidates who want one comprehensive recorded course covering the full exam.

### 3. Full Allied Health Profession OET Course • 5 Gifted Shared AI Credits

**Profession:** Allied Health Profession | Duration: 40+ hours | Category: Full recorded course

**Website description:** A complete, condensed and exam-focused recorded Allied Health Profession course covering Listening, Reading, Writing and Speaking. It is designed for candidates who want a structured route through all four OET sub-tests with the freedom to repeat lessons throughout the access period.

**Access:** 6 months from purchase | Format: Recorded videos plus structured study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- 160+ Listening exams, including practice beyond the usual Jahshan and Benchmark resources
- 100+ Reading exams with answer keys and rationales
- 90+ Writing tasks covering the main OET letter types
- 100+ Speaking cards across different scenarios and card types
- Recent recall updates plus older recalls from 2023 onwards
- 5 Writing letter assessments with personalised correction
- 1 private Speaking session
- 5 AI practice credits
- Continuous Q&A support during the access period

**Best for:** Allied Health Profession candidates who want one comprehensive recorded course covering the full exam.

### 4. Full Condensed Recorded Course + TutorBook • 5 Gifted Shared AI Credits

**Profession:** Medicine | Duration: 40+ hours plus book | Category: Course and book bundle

**Website description:** The flagship recorded course bundled with TutorBook. TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall-based exams already included as add-ons.

**Access:** 6 months for the course plus permanent TutorBook access | Format: Recorded videos, structured materials and personalised watermarked PDF TutorBook

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Everything included in the Full Condensed Recorded Medicine Course
- TutorBook as a personalised watermarked PDF
- 8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking
- The main exam ideas and recall themes from 2026 across all four sub-tests
- New Reading dictionary including the vocabulary from the 2026 Reading recalls
- Model answers for Writing and relevant practice tasks
- Answer rationales and justifications to help candidates understand why each answer is correct
- Listening scripts for recall-based Listening practice
- Listening recall vocabulary and repeated words from recent exams
- Already-included add-on exams with more recent recall-based practice
- Private update channel access for new book updates and recall additions

**Best for:** Candidates who want the full recorded course together with complete 2026 recall-based TutorBook practice.

### 5. Full Nursing OET Course • 5 Gifted Shared AI Credits

**Profession:** Nursing | Duration: 30+ hours | Category: Full recorded course

**Website description:** A profession-specific OET course for nurses covering Listening, Reading, Writing and Speaking with nursing-focused examples, letters and role-play scenarios.

**Access:** 6 months from purchase | Format: Recorded videos plus nursing-specific study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Full Nursing OET preparation across Listening, Reading, Writing and Speaking
- Recall-based Nursing Writing letters and model answers
- Recall-based Nursing Speaking cards with expected ideas
- Listening and Reading practice library
- Continuous Q&A support during the access period

**Best for:** Nurses who need a complete recorded course without bundled assessments.

### 6. Nursing Course + Assessment Package • 5 Gifted Shared AI Credits

**Profession:** Nursing | Duration: 30+ hours | Category: Course and assessment bundle

**Website description:** The full Nursing OET course bundled with personalised Writing assessment support and AI practice credits. This option is designed for nurses who want structured learning plus direct feedback on their letters.

**Access:** 6 months from purchase | Format: Recorded videos, nursing materials and Writing assessments

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Everything included in the Full Nursing OET Course
- 5 Writing letter assessments
- Detailed correction and voice-note feedback via WhatsApp
- 5 AI credits for instant practice feedback

**Best for:** Nurses who want the full recorded course with personalised Writing feedback.

### 7. Nursing Premium Bundle • 5 Gifted Shared AI Credits

**Profession:** Nursing | Duration: 30+ hours plus 11+ hours foundation | Category: Premium bundle

**Website description:** The most complete Nursing package, combining the full Nursing OET course, Writing assessment support, AI practice credits and the Basic English foundation course for candidates who want to strengthen their English before or alongside OET preparation.

**Access:** 6 months from purchase | Format: Recorded videos, nursing materials, assessments and foundation English course

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Everything included in the Nursing Course + Assessment Package
- Basic English Course - Preparation for OET
- 11+ hours of foundation English training
- Grammar, vocabulary and sentence-formation support
- Course booklet for the Basic English module

**Best for:** Nursing candidates who want OET preparation plus extra English foundation support.

### 8. Full Pharmacy OET Course • 5 Gifted Shared AI Credits

**Profession:** Pharmacy | Duration: 25+ hours | Category: Full recorded course

**Website description:** A profession-specific OET preparation course for pharmacists covering all four sub-tests with pharmacy-focused Writing and Speaking content. The course includes pharmacy scenarios such as complaint handling, dosage issues, drug safety, expiry-date discrepancies, interactions and counselling.

**Access:** 6 months from purchase | Format: Recorded videos plus pharmacy-specific study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Full Pharmacy OET preparation across Listening, Reading, Writing and Speaking
- Pharmacy-specific Writing examples and model answers
- Pharmacy Speaking cards with expected ideas and useful language
- Recall-based practice resources
- Continuous Q&A support during the access period

**Best for:** Pharmacists who want profession-specific OET preparation rather than a general course.

### 9. Basic English Course - Preparation for OET

**Profession:** All disciplines | Duration: 11+ hours | Category: Foundation course

**Website description:** A preparatory English course for candidates at Beginner, A1, A2 or B1 level who need to build a stronger English base before intensive OET preparation. The course focuses on essential grammar, healthcare vocabulary, sentence formation, listening foundations and a practical study plan.

**Access:** 6 months from purchase | Format: Recorded videos plus downloadable course booklet

#### Included components

- Fully recorded preparatory English course
- Essential grammar explained from the basics and linked to OET production
- Medical and healthcare vocabulary foundation
- Sentence formation for OET-style communication
- Conversation and listening foundations for healthcare contexts
- Simplified course booklet
- Full study plan

**Best for:** Candidates who need stronger English foundations before starting full OET training.

### 10. Full Crash Course - General OET • 5 Gifted Shared AI Credits

**Profession:** All disciplines with Medicine focus | Duration: 20+ hours | Category: Full crash course

**Website description:** A condensed, high-impact OET course for candidates with limited time before the exam. It covers the four sub-tests in an exam-oriented format with high-yield strategies and recall-based guidance.

**Access:** 6 months from purchase | Format: Recorded videos plus selected study materials

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Condensed recorded preparation across Listening, Reading, Writing and Speaking
- High-yield exam strategies and practical techniques
- Recall-based guidance for recent exam trends
- Selected study materials and Listening recalls

**Best for:** Candidates who need fast, focused preparation across all four sub-tests.

### 11. Full Crash Course + 3 Writing Assessments • 5 Gifted Shared AI Credits

**Profession:** All disciplines | Duration: 20+ hours | Category: Crash course and assessment bundle

**Website description:** The Full Crash Course bundled with assessment of 3 Writing letters. It combines condensed preparation across the exam with personalised Writing correction.

**Access:** 6 months from purchase | Format: Recorded videos plus 3 Writing letter assessments

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Everything included in the Full Crash Course
- Assessment of 3 Writing letters
- Estimated score, detailed correction and voice-note feedback
- Letters may be candidate-chosen or recall-recommended

**Best for:** Crash-course candidates who want limited but focused Writing feedback.

### 12. Full Crash Course + 5 Writing Assessments • 5 Gifted Shared AI Credits

**Profession:** All disciplines | Duration: 20+ hours | Category: Crash course and assessment bundle

**Website description:** The Full Crash Course bundled with assessment of 5 Writing letters. It is the recommended crash-course bundle for candidates who want more personalised Writing feedback while studying the four sub-tests.

**Access:** 6 months from purchase | Format: Recorded videos plus 5 Writing letter assessments

**AI credit rule:** Includes exactly 5 Gifted Shared AI Credits. Shared costs: Reading 1, Listening 1, Writing 2, Speaking 2. This gift is granted once for this qualifying purchase/assignment.

#### Included components

- Everything included in the Full Crash Course
- Assessment of 5 Writing letters
- Estimated score, detailed correction and voice-note feedback
- Letters may be candidate-chosen or recall-recommended

**Best for:** Crash-course candidates who want more extensive Writing assessment support.

### 13. 3 Writing Letter Assessments - Add-on

**Profession:** Doctors, Nurses and Pharmacy candidates | Category: Writing assessment add-on

**Website description:** A stackable Writing assessment add-on that gives candidates personalised feedback on 3 OET letters. It is intended for candidates already enrolled in an eligible course or package.

**Access:** Tied to the active parent course access window | Format: Manual assessment with 48-72h turnaround, excluding Friday

#### Included components

- 3 Writing letter assessments
- Estimated score for each letter
- Detailed correction
- Voice-note feedback via WhatsApp
- Candidate-chosen or recall-recommended letters

**Best for:** Candidates who need a small number of corrected letters before the exam.

### 14. 5 Writing Letter Assessments - Add-on

**Profession:** Doctors, Nurses and Pharmacy candidates | Category: Writing assessment add-on

**Website description:** A stackable Writing assessment add-on that gives candidates personalised feedback on 5 OET letters. It is intended for candidates already enrolled in an eligible course or package.

**Access:** Tied to the active parent course access window | Format: Manual assessment with 48-72h turnaround, excluding Friday

#### Included components

- 5 Writing letter assessments
- Estimated score for each letter
- Detailed correction
- Voice-note feedback via WhatsApp
- Candidate-chosen or recall-recommended letters

**Best for:** Candidates who want a balanced amount of Writing correction and score estimation.

### 15. 7 Writing Letter Assessments - Add-on

**Profession:** Doctors, Nurses and Pharmacy candidates | Category: Writing assessment add-on

**Website description:** A stackable Writing assessment add-on that gives candidates personalised feedback on 7 OET letters using the same assessment format as the smaller Writing packages.

**Access:** Tied to the active parent course access window | Format: Manual assessment by Dr Ahmed

#### Included components

- 7 Writing letter assessments
- Estimated score for each letter
- Detailed correction
- Voice-note feedback via WhatsApp
- Candidate-chosen or recall-recommended letters

**Best for:** Candidates who want repeated Writing practice with detailed feedback.

### 16. 10 Writing Letter Assessments - Add-on

**Profession:** Doctors, Nurses and Pharmacy candidates | Category: Writing assessment add-on

**Website description:** A stackable Writing assessment add-on that gives candidates personalised feedback on 10 OET letters. It provides the heaviest Writing correction option for candidates who want extensive practice.

**Access:** Tied to the active parent course access window | Format: Manual assessment by Dr Ahmed

#### Included components

- 10 Writing letter assessments
- Estimated score for each letter
- Detailed correction
- Voice-note feedback via WhatsApp
- Candidate-chosen or recall-recommended letters

**Best for:** Candidates who want intensive Writing correction and multiple opportunities to improve.

### 17. Recorded Writing Crash Course

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours by profession | Category: Writing course

**Website description:** A standalone recorded Writing course covering the OET Writing sub-test from A-Z. It explains task analysis, purpose, audience, case-note relevance, introductions, body paragraphs, closing paragraphs, all major letter types, assessment criteria and profession-specific examples.

**Access:** 6 months from purchase | Format: Recorded videos plus Writing materials

#### Included components

- Full recorded Writing explanation from A-Z
- Task, purpose, audience and case-note relevance
- Referral, discharge, transfer, update, complaint and profession-specific letters
- Grammar, sentence structure, clarity and conciseness
- Latest OET Writing assessment criteria
- Profession-specific examples and recall-based ideas

**Best for:** Candidates who mainly need structured Writing training.

### 18. Writing Crash Course + 2 Letter Assessments

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours | Category: Writing bundle

**Website description:** The Recorded Writing Crash Course bundled with 2 Writing letter assessments. It gives candidates the full Writing explanation plus a small amount of personalised feedback.

**Access:** 6 months from purchase | Format: Recorded videos plus 2 Writing assessments

#### Included components

- Everything included in the Recorded Writing Crash Course
- 2 Writing letter assessments
- Estimated score, detailed correction and voice-note feedback

**Best for:** Candidates who want Writing teaching plus limited correction.

### 19. Writing Crash Course + 3 Letter Assessments

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours | Category: Writing bundle

**Website description:** The Recorded Writing Crash Course bundled with 3 Writing letter assessments. It combines the full Writing explanation with personalised assessment support.

**Access:** 6 months from purchase | Format: Recorded videos plus 3 Writing assessments

#### Included components

- Everything included in the Recorded Writing Crash Course
- 3 Writing letter assessments
- Estimated score, detailed correction and voice-note feedback

**Best for:** Candidates who want a focused Writing bundle with several corrected letters.

### 20. Writing Crash Course + 5 Letter Assessments

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours | Category: Writing bundle

**Website description:** The Recorded Writing Crash Course bundled with 5 Writing letter assessments. It is the recommended Writing bundle for candidates who want structured lessons and a strong amount of personalised correction.

**Access:** 6 months from purchase | Format: Recorded videos plus 5 Writing assessments

#### Included components

- Everything included in the Recorded Writing Crash Course
- 5 Writing letter assessments
- Estimated score, detailed correction and voice-note feedback

**Best for:** Candidates who want the most balanced Writing course and feedback package.

### 21. Writing Crash Course + 7 Letter Assessments

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours | Category: Writing bundle

**Website description:** The Recorded Writing Crash Course bundled with 7 Writing letter assessments. It is designed for candidates who want deeper Writing practice and repeated feedback.

**Access:** 6 months from purchase | Format: Recorded videos plus 7 Writing assessments

#### Included components

- Everything included in the Recorded Writing Crash Course
- 7 Writing letter assessments
- Estimated score, detailed correction and voice-note feedback

**Best for:** Candidates who need more intensive Writing correction before the exam.

### 22. Writing Crash Course + 10 Letter Assessments

**Profession:** Medicine, Nursing and Pharmacy | Duration: 12-16+ hours | Category: Writing bundle

**Website description:** The Recorded Writing Crash Course bundled with 10 Writing letter assessments. This is the heaviest Writing-focused option and is designed for candidates who want maximum correction practice.

**Access:** 6 months from purchase | Format: Recorded videos plus 10 Writing assessments

#### Included components

- Everything included in the Recorded Writing Crash Course
- 10 Writing letter assessments
- Estimated score, detailed correction and voice-note feedback

**Best for:** Candidates who need intensive Writing support and repeated detailed feedback.

### 23. Recorded Speaking Crash Course

**Profession:** Medicine, Nursing and Pharmacy | Duration: 8+ hours per profession | Category: Speaking course

**Website description:** A standalone recorded Speaking course covering OET Speaking scenarios, card types, recent recall themes and exam performance for Medicine, Nursing and Pharmacy candidates.

**Access:** 6 months from purchase | Format: Recorded videos

#### Included components

- Complete explanation of the Speaking sub-test and role-play structure
- All major card types covered in detail
- Recall-based focus on repeated card types
- Opening, information gathering, addressing concerns and safe closing
- Handling anxious, angry, confused, reluctant or non-compliant patients
- Empathy, reassurance, signposting and checking understanding

**Best for:** Candidates who want a dedicated Speaking course without a full four-subtest package.

### 24. 1 Private Speaking Assessment Session

**Profession:** Doctors, Nurses and Pharmacy candidates | Duration: 1 live session | Category: Speaking session

**Website description:** A one-to-one live Speaking practice session using different OET cards with detailed performance feedback. It can be used as a top-up alongside any course or as a standalone speaking assessment session.

**Access:** Scheduled within the candidate preparation window | Format: Live 1:1 session plus detailed feedback

#### Included components

- 1 live 1:1 Speaking session
- Multiple cards covered
- Detailed performance feedback

**Best for:** Candidates who want one focused live Speaking practice session.

### 25. 2 Private Speaking Assessment Sessions

**Profession:** Doctors, Nurses and Pharmacy candidates | Duration: 2 live sessions | Category: Speaking session

**Website description:** Two one-to-one live Speaking assessment sessions using different cards with detailed feedback after each session.

**Access:** Scheduled within the candidate preparation window | Format: Live 1:1 sessions plus detailed feedback

#### Included components

- 2 live 1:1 Speaking sessions
- Different cards in each session
- Detailed performance feedback for each session

**Best for:** Candidates who want more than one live Speaking practice opportunity.

### 26. Double Special Package - Writing + Speaking

**Profession:** Medicine, Nursing and Pharmacy | Duration: Writing 12-16+ hours plus Speaking 8+ hours | Category: Combo package

**Website description:** A combined productive-skills package including the full recorded Writing course and the full recorded Speaking course. It is suitable for candidates who are confident in Listening and Reading and want to focus on Writing and Speaking.

**Access:** 6 months from purchase | Format: Recorded Writing and Speaking video bundle

#### Included components

- Full recorded Writing course with the latest criteria
- Full recorded Speaking course from A-Z
- Model letters, Writing rules, Speaking cards and useful phrases
- Materials for focused productive-skills preparation

**Best for:** Candidates who mainly need Writing and Speaking preparation.

### 27. Mega Special Package

**Profession:** Medicine, Nursing and Pharmacy | Duration: 18+ focused hours | Category: Premium combo package

**Website description:** A flagship Writing and Speaking combo package that includes the full recorded Writing course, full recorded Speaking course, one private Speaking session and 5 Writing letter assessments.

**Access:** 6 months from purchase | Format: Recorded videos plus Writing assessments and 1 live Speaking session

#### Included components

- Full recorded Writing sessions with the latest criteria
- Full recorded Speaking course from A-Z
- 1 private Speaking session
- 5 Writing letter assessments
- 18+ hours of focused Writing and Speaking preparation

**Best for:** Candidates who want the most complete package for the two productive sub-tests.

### 28. TutorBook - First Edition 2026

**Profession:** All disciplines | Category: 2026 recall-based OET book

**Website description:** TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall- based exams already included as add-ons.

**Access:** Permanent access with content updates | Format: Personalised watermarked PDF plus private update channel

#### Included components

- 8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking
- The main exam ideas and recall themes from 2026 across all four sub-tests
- New Reading dictionary including the vocabulary from the 2026 Reading recalls
- Model answers for Writing and relevant practice tasks
- Answer rationales and justifications to help candidates understand why each answer is correct
- Listening scripts for recall-based Listening practice
- Listening recall vocabulary and repeated words from recent exams
- Already-included add-on exams with more recent recall-based practice
- Private update channel access for new book updates and recall additions

**Best for:** Candidates who want complete 2026 recall-based practice for Listening, Reading, Writing and Speaking in one book.

### 29. TutorBook - Add-on for Enrolled Students

**Profession:** All disciplines | Category: Book add-on

**Website description:** A TutorBook add-on available to candidates with an eligible active enrolment. TutorBook is a 2026 recall-based OET preparation book built around 8 full exams covering Listening, Reading, Writing and Speaking. It brings together the main 2026 exam ideas and recall themes, with complete model answers, rationales, Listening scripts, Reading vocabulary support and additional recent recall-based exams already included as add-ons.

**Access:** Permanent access | Format: Personalised watermarked PDF plus private update channel

#### Included components

- 8 full 2026 recall-based OET exams covering Listening, Reading, Writing and Speaking
- The main exam ideas and recall themes from 2026 across all four sub-tests
- New Reading dictionary including the vocabulary from the 2026 Reading recalls
- Model answers for Writing and relevant practice tasks
- Answer rationales and justifications to help candidates understand why each answer is correct
- Listening scripts for recall-based Listening practice
- Listening recall vocabulary and repeated words from recent exams
- Already-included add-on exams with more recent recall-based practice
- Private update channel access for new book updates and recall additions

**Best for:** Already enrolled candidates who want to add complete 2026 recall-based TutorBook practice to their preparation.

### 30. Quick Check

**Profession:** All disciplines | Category: AI grading and practice starter package

**Website description:** A targeted one-off readiness package for candidates who want instant AI feedback on Writing or Speaking together with a small Listening and Reading practice allowance. This package includes 5 flexible AI grading credits for Writing letters or Speaking cards, plus 3 Listening exams and 3 Reading exams for focused practice.

**Access:** 30 days from purchase | Format: AI feedback reports plus Listening and Reading practice exams

**Candidate balance mapping:** Reading Credits 3; Listening Credits 3; Flexible W/S Credits 5. The W/S pool is restricted to Writing/Speaking, not universal Shared.

#### Included components

- 5 flexible AI grading credits for Writing or Speaking
- 3 Listening practice exams
- 3 Reading practice exams
- AI feedback reports for graded Writing or Speaking submissions
- 30-day validity

**Best for:** Candidates who want a quick readiness check before deciding whether they need a larger package.

### 31. Exam Prep Pro

**Profession:** All disciplines | Category: AI grading and exam preparation package

**Website description:** A larger one-off exam preparation package for candidates who need repeated AI grading and more Listening and Reading practice before the exam. It includes 15 flexible AI grading credits for Writing letters or Speaking cards, plus 6 Listening exams and 6 Reading exams.

**Access:** 90 days from purchase | Format: AI feedback reports plus Listening and Reading practice exams

**Candidate balance mapping:** Reading Credits 6; Listening Credits 6; Flexible W/S Credits 15. The W/S pool is restricted to Writing/Speaking, not universal Shared.

#### Included components

- 15 flexible AI grading credits for Writing or Speaking
- 6 Listening practice exams
- 6 Reading practice exams
- AI feedback reports for graded Writing or Speaking submissions
- 90-day validity

**Best for:** Candidates who want a balanced AI practice package covering Writing, Speaking, Listening and Reading.

### 32. OET Mastery

**Profession:** All disciplines | Category: Unlimited AI assessment and practice package

**Website description:** The highest AI practice package for candidates who want unlimited assessment during the access period. It includes unlimited AI assessment for Writing and Speaking, unlimited Listening and Reading practice, detailed AI feedback reports and priority queue access.

**Access:** 6 months from purchase | Format: Unlimited AI assessment, Listening and Reading practice, and priority access

**Candidate balance mapping:** Reading Unlimited; Listening Unlimited; Writing Unlimited; Speaking Unlimited for 6 months.

#### Included components

- Unlimited AI assessment for Writing letters and Speaking cards during the access period
- Unlimited Listening practice
- Unlimited Reading practice
- Detailed AI feedback reports
- Priority grading queue
- 6-month validity

**Best for:** Candidates who want the most complete AI-supported practice option with no fixed assessment limit during the access

period.

### 33. 1 Full Mock

**Profession:** All disciplines | Category: Full mock exam package

**Website description:** One complete OET mock exam covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.

**Access:** 6 months from purchase | Format: One full mock exam across all four sub-tests

**Candidate balance mapping:** Full Mock Attempts 1; separate from ordinary AI balances.

#### Included components

- 1 full mock exam covering Listening, Reading, Writing and Speaking
- Writing and Speaking AI-graded
- Listening and Reading auto-marked
- Mock allowance separate from AI credits
- 6-month validity

**Best for:** Candidates who want one complete exam-style practice run.

### 34. 3 Full Mocks

**Profession:** All disciplines | Category: Full mock exam package

**Website description:** Three complete OET mock exams covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.

**Access:** 6 months from purchase | Format: Three full mock exams across all four sub-tests

**Candidate balance mapping:** Full Mock Attempts 3; separate from ordinary AI balances.

#### Included components

- 3 full mock exams covering Listening, Reading, Writing and Speaking
- Writing and Speaking AI-graded
- Listening and Reading auto-marked
- Mock allowance separate from AI credits
- 6-month validity

**Best for:** Candidates who want several complete practice attempts before the real exam.

### 35. 5 Full Mocks

**Profession:** All disciplines | Category: Full mock exam package

**Website description:** Five complete OET mock exams covering all four sub-tests. Writing and Speaking are AI-graded, while Listening and Reading are auto-marked using answer-key marking. Mock exam allowances are separate from AI grading credits.

**Access:** 6 months from purchase | Format: Five full mock exams across all four sub-tests

**Candidate balance mapping:** Full Mock Attempts 5; separate from ordinary AI balances.

#### Included components

- 5 full mock exams covering Listening, Reading, Writing and Speaking
- Writing and Speaking AI-graded
- Listening and Reading auto-marked
- Mock allowance separate from AI credits
- 6-month validity

**Best for:** Candidates who want repeated full exam simulation and progress tracking.

### 36. Listening Starter

**Profession:** All disciplines | Category: Separate Listening practice package

**Website description:** A focused starter package for candidates who want a small set of Listening practice exams. It includes 3 Listening exams with deterministic answer-key marking.

**Access:** 30 days from purchase | Format: Listening practice exams with answer-key marking

**Candidate balance mapping:** Listening Credits 3.

#### Included components

- 3 Listening practice exams
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 30-day validity

**Best for:** Candidates who want a short Listening practice set before moving to a larger package.

### 37. Listening Standard

**Profession:** All disciplines | Category: Separate Listening practice package

**Website description:** A standard Listening practice package for candidates who want more focused Listening practice. It includes 6 Listening exams with deterministic answer-key marking.

**Access:** 90 days from purchase | Format: Listening practice exams with answer-key marking

**Candidate balance mapping:** Listening Credits 6.

#### Included components

- 6 Listening practice exams
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 90-day validity

**Best for:** Candidates who want a medium Listening practice set with a longer access window.

### 38. Listening Pro

**Profession:** All disciplines | Category: Unlimited Listening practice package

**Website description:** An unlimited Listening practice package for candidates who want open Listening practice throughout the access period. Listening is auto-marked with deterministic answer-key marking and does not use AI credits.

**Access:** 6 months from purchase | Format: Unlimited Listening practice with answer-key marking

**Candidate balance mapping:** Listening Unlimited.

#### Included components

- Unlimited Listening practice
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 6-month validity

**Best for:** Candidates who want unlimited Listening practice during their preparation period.

### 39. Reading Starter

**Profession:** All disciplines | Category: Separate Reading practice package

**Website description:** A focused starter package for candidates who want a small set of Reading practice exams. It includes 3 Reading exams with deterministic answer-key marking.

**Access:** 30 days from purchase | Format: Reading practice exams with answer-key marking

**Candidate balance mapping:** Reading Credits 3.

#### Included components

- 3 Reading practice exams
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 30-day validity

**Best for:** Candidates who want a short Reading practice set before moving to a larger package.

### 40. Reading Standard

**Profession:** All disciplines | Category: Separate Reading practice package

**Website description:** A standard Reading practice package for candidates who want more focused Reading practice. It includes 6 Reading exams with deterministic answer-key marking.

**Access:** 90 days from purchase | Format: Reading practice exams with answer-key marking

**Candidate balance mapping:** Reading Credits 6.

#### Included components

- 6 Reading practice exams
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 90-day validity

**Best for:** Candidates who want a medium Reading practice set with a longer access window.

### 41. Reading Pro

**Profession:** All disciplines | Category: Unlimited Reading practice package

**Website description:** An unlimited Reading practice package for candidates who want open Reading practice throughout the access period. Reading is auto-marked with deterministic answer-key marking and does not use AI credits.

**Access:** 6 months from purchase | Format: Unlimited Reading practice with answer-key marking

**Candidate balance mapping:** Reading Unlimited.

#### Included components

- Unlimited Reading practice
- Deterministic answer-key marking
- Always free to grade - no AI credits used
- 6-month validity

**Best for:** Candidates who want unlimited Reading practice during their preparation period.

### 42. Writing Starter

**Profession:** All disciplines | Category: Separate Writing AI grading package

**Website description:** A starter Writing AI grading package sized for focused practice. It includes 3 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.

**Access:** 30 days from purchase | Format: AI-graded Writing letters

**Candidate balance mapping:** Writing Credits 3 graded submissions.

#### Included components

- 3 AI-graded Writing letters
- Instant Claude feedback on every letter
- Detailed per-criterion feedback
- 30-day validity

**Best for:** Candidates who want a small number of instant Writing assessments.

### 43. Writing Standard

**Profession:** All disciplines | Category: Separate Writing AI grading package

**Website description:** A standard Writing AI grading package for candidates who want more letter practice. It includes 8 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.

**Access:** 90 days from purchase | Format: AI-graded Writing letters

**Candidate balance mapping:** Writing Credits 8 graded submissions.

#### Included components

- 8 AI-graded Writing letters
- Instant Claude feedback on every letter
- Detailed per-criterion feedback
- 90-day validity

**Best for:** Candidates who want repeated Writing practice with instant feedback.

### 44. Writing Pro

**Profession:** All disciplines | Category: Separate Writing AI grading package

**Website description:** A larger Writing AI grading package for candidates who want intensive letter practice. It includes 15 AI-graded Writing letters with instant Claude feedback and detailed criterion-based comments.

**Access:** 6 months from purchase | Format: AI-graded Writing letters

**Candidate balance mapping:** Writing Credits 15 graded submissions.

#### Included components

- 15 AI-graded Writing letters
- Instant Claude feedback on every letter
- Detailed per-criterion feedback
- 6-month validity

**Best for:** Candidates who want an intensive Writing-only AI grading option.

### 45. Speaking Starter

**Profession:** All disciplines | Category: Separate Speaking AI grading package

**Website description:** A starter Speaking AI grading package sized for focused practice. It includes 3 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.

**Access:** 30 days from purchase | Format: AI-graded Speaking cards with transcription

**Candidate balance mapping:** Speaking Credits 3 graded cards.

#### Included components

- 3 AI-graded Speaking cards
- Whisper transcription plus Claude assessment
- Rule-cited transcript markers
- 30-day validity

**Best for:** Candidates who want a small number of instant Speaking assessments.

### 46. Speaking Standard

**Profession:** All disciplines | Category: Separate Speaking AI grading package

**Website description:** A standard Speaking AI grading package for candidates who want more role-play practice. It includes 8 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.

**Access:** 90 days from purchase | Format: AI-graded Speaking cards with transcription

**Candidate balance mapping:** Speaking Credits 8 graded cards.

#### Included components

- 8 AI-graded Speaking cards
- Whisper transcription plus Claude assessment
- Rule-cited transcript markers
- 90-day validity

**Best for:** Candidates who want repeated Speaking practice with instant transcript-based feedback.

### 47. Speaking Pro

**Profession:** All disciplines | Category: Separate Speaking AI grading package

**Website description:** A larger Speaking AI grading package for candidates who want intensive role-play practice. It includes 15 AI-graded Speaking cards using Whisper transcription and Claude assessment with rule-cited transcript markers.

**Access:** 6 months from purchase | Format: AI-graded Speaking cards with transcription

**Candidate balance mapping:** Speaking Credits 15 graded cards.

#### Included components

- 15 AI-graded Speaking cards
- Whisper transcription plus Claude assessment
- Rule-cited transcript markers
- 6-month validity

**Best for:** Candidates who want an intensive Speaking-only AI grading option.

## 10. Source documents consolidated

### Duplicate handling

Several uploaded PDFs are duplicate/alternate copies of the same catalogue and access specifications. They are intentionally represented once in this master document so developers have one unambiguous source.

- DOC-20260625-WA0023.(1).pdf - complete catalogue plus AI, practice and mock package addendum (Products 1-47).

- OET_2026_Website_Course_Package_Descriptions_UPDATED(1).pdf and duplicate variants - catalogue descriptions (Products 1-29).

- Web_App_Access_and_Payment_Requirements(1).pdf and duplicate copy - proof of payment, automatic/manual delivery, profession mapping, manual admin access and minimum acceptance criteria.

- DOC-20260812-WA0008(2).pdf and duplicate copy - Payment & Access Cycle and admin approval flow.

- DOC-20260819-WA0002(3).pdf - latest 5 Gifted AI Credits rule and shared-credit consumption rates.

- User instructions dated 23 August 2026 - candidate and per-user admin surfaces must show Credits/Attempts/Unlimited rather than raw provider Tokens; balances must be split by Reading, Listening, Writing, Speaking and Shared (plus conditional Flexible W/S and Mock allowances) with source package, Total/Used/Remaining and full validity/expiry information. Raw AI-provider token capacity is visible only in a separate platform-level admin AI/API usage/billing view. Candidate activity history covers all four subtests and reopening the same attempt does not consume another credit. Payment flow is split: Products 1-29 require invoice/WhatsApp verification/admin approval, while Products 30-47 receive instant automatic access after successful confirmed payment. Candidate-facing test content is disabled, including the Reading “Other papers” series and any Listening test/demo/staging items.

### Final developer sign-off checklist

- [ ] All qualifying Full Courses/bundles grant 5 Shared Credits exactly once.
- [ ] Candidate UI says Credits, never Tokens.
- [ ] Candidate sees Reading, Listening, Writing, Speaking and Shared balances; restricted W/S and Mock balances are shown when applicable.
- [ ] Used and remaining balances update immediately after every activity.
- [ ] A credit-only account cannot open/start more metered exams than its available balance permits.
- [ ] Direct URLs and API calls cannot bypass entitlements.
- [ ] Admin and candidate balances are identical and backed by one ledger.
- [ ] Candidate activity history covers Reading, Listening, Writing and Speaking; existing attempts/cards can be Resume/Reopen/Review without another deduction.
- [ ] Products 1-29 create candidate/admin invoices, require WhatsApp proof and remain Pending Verification until Admin > Billing > Orders & Payments approves them.
- [ ] Products 30-47 create candidate/admin invoices but require no proof upload, WhatsApp submission or admin approval; successful confirmed payment grants access instantly.
- [ ] Candidate and admin invoice/order views reference the same underlying order ID and payment record.
- [ ] Candidate UI and Admin > User Management > Candidate Profile show Credits / Attempts / Unlimited only; raw provider Tokens are not shown on candidate-level surfaces.
- [ ] Every candidate credit/attempt grant shows source package, Total, Used, Remaining, activation/valid-from date, expiry date and days left; gifted Full-Course Shared Credits inherit the course expiry unless explicitly configured otherwise.
- [ ] Admin has a separate global AI/API Usage & Billing view for real provider token usage/capacity and refill/renewal monitoring.
- [ ] Reading “Other papers” is disabled from all candidate surfaces and direct routes; any Listening test/demo/staging content is disabled the same way.
- [ ] All A01-A35 acceptance tests pass on web, Android and iOS before production sign-off.
