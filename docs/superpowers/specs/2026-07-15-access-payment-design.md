# Web App Access & Payment Requirements — Design

Date: 2026-07-15
Source: owner PDF "Web App Access & Payment Requirements" (2 pages, 7 sections)
Status: APPROVED by owner — implementation authorised

## Owner decisions (locked)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Proof gate for card/online gateways | **Offline methods only.** Offline (bank transfer / Vodafone Cash / InstaPay / Fawry offline / Wise / other) = hard upload gate. Card gateways (Stripe/PayPal/EasyKash/Checkout.com/Paymob/PayTabs) = auto-generated system receipt proof row. Every order still has exactly one proof row; WhatsApp button still shows for every package. |
| 2 | Package → content model | **Subtest × profession + overrides.** Plan declares included subtests; buyer's registered profession supplies the other axis; optional per-plan include/exclude overrides. |
| 3 | Profession taxonomy | **Unify to one canonical list** (SignupProfessionCatalog = source of truth) + prod data migrations. |
| 4 | Content data | **Build ingest script; owner supplies missing video.** Plan+profession combos with zero content are blocked at checkout with a clear message. |
| 5 | Telegram delivery | **Admin-gated invite link.** Plan carries invite URL + instructions; order sits Pending Manual Fulfilment; admin marks fulfilled to reveal. No bot. |
| 6 | Existing 27 plans' delivery method | **All `automatic_web`**; owner sets exceptions in admin after deploy. |
| 7 | Profession change after purchase | **Hard lock after first completed purchase**; admin can change (audited); learner gets "Request a change" → WhatsApp. |
| 8 | Checkout pipelines | **Unify on express; repoint mobile bridge.** Cart code left in tree, untouched. |

### Implementer judgment calls (approved)

- **No entity rename.** `ManualPaymentRequest` / `ManualPaymentRequests` keeps its name even though it becomes the universal proof record. Production uses blue/green deploys: a table rename breaks the old container during rollover. **Additive columns only.** A `Kind` discriminator distinguishes `learner_upload` from `gateway_receipt`. Admin UI says "Payment Proof".
- **Fail-open threading.** Empty profession tag = intentionally neutral content, stays visible to all (the 28 untagged videos are 22 Reading/Listening + 6 general Speaking and are legitimately neutral). Malformed profession JSON now fails **closed**. A learner with no profession now fails **closed** on discipline-tagged Materials folders (matching video behaviour); untagged/neutral folders stay visible.

## Ground truth (from 56-agent recon, 2026-07-15)

**Already exists and is reused, not rebuilt:**
- `ManualPaymentRequest` — proof record with UserId, QuoteId, Amount/Currency, Method, Reference, ProofUrl, ProofHashHex (SHA-256), CandidateFullName/Email/WhatsApp, CourseName, CourseId, PaymentCategory, Status, SubmittedAt, ReviewedAt, ReviewedByAdminId, AdminNotes, AccessGrantedSubscriptionId.
- `ManualPaymentService` — SubmitAsync/ApproveAsync/RejectAsync/SetStatusAsync; magic-byte validation; 10 MB cap (`ManualPaymentProof.MaxProofBytes`); SHA-256 dedupe; `IFileStorage` write to `billing/manual-payments/{yyyy/MM}/{guid}-{hash12}.bin`.
- Learner upload page `app/billing/manual-payment/page.tsx` (ProofDropzone + WhatsApp button).
- Admin review page `app/admin/billing/manual-payments/page.tsx` (inline proof viewer + approve/reject).
- `lib/billing/whatsapp.ts` — `buildManualPaymentWhatsAppLink`, `PLATFORM_WHATSAPP = '447961725989'` (hardcoded).
- `UserAccessAllocationService` — GetAccessAsync / GrantPackageAsync / RemovePackageAsync / GrantAddonAsync / PutScopeAsync + 5 endpoints under `/v1/admin/users/{userId}/access/*`. Additive; Subscriptions are many-per-user; `EffectiveEntitlementResolver` aggregates additively.
- Admin user search by email (`AdminService.GetUserListAsync`, UI `app/admin/users/page.tsx:491`).
- `BillingPlan.AccessDurationDays` int = **180** already (`AdminEntities.cs:367`), seeded per-SKU as `accessDays`.
- Video Library gating is strong: `POST /v1/video-library/videos/{id}/playback-session` chains visibility → HMAC attestation → entitlement → concurrency → CDN signing. Requires module **explicitly** enabled (`VideoEntitlementService.cs:171-176`), so it does not rely on fail-open `IsModuleEnabled`.
- 117 videos live on Bunny library 696416 as 32 flat path-named collections, all with `SubtestCode` + `ProfessionIdsJson`, all published.
- RuntimeSettings: 36-section DB-over-env config, singleton `RuntimeSettingsRow` (Id="default"), `IRuntimeSettingsProvider` (~30s cache), admin UI `app/admin/settings/RuntimeSettingsClient.tsx` gated on `AdminSystemAdmin`.
- `SubscriptionStateMachine` exists. `MaterialFolderAudience` (TargetType="plan") binds folders to plans, authored folder-side.

**Live defects to fix (spec §7 violations):**
1. `MaterialAccessService.CanCandidateAccessMaterialFileAsync` (:135-219) never checks `HasEligibleSubscription`; its only gate is fail-open `IsModuleEnabled` at :144. Expired subs normalise to `EnabledModules=[]` (`EffectiveEntitlementResolver.cs:328-336`) and empty ⇒ "enabled" (:85) ⇒ **expired subscriber downloads everything**.
2. `MaterialAccessService.cs:181-185` — `else if (files.Any(f => f.FolderId == null)) return true;` ⇒ **root-level files open to any authenticated learner**, before any audience/discipline/subscription check.
3. `VideoPlaybackSessionService.RenewAsync` (:125-145) re-signs a Bunny URL checking only session ownership/expiry — **no entitlement, profession, or attestation recheck**.
4. `ManualPaymentService.ResolveSubscriptionForApprovalAsync` (:373-417) falls back to `FirstOrDefaultAsync(s => s.UserId == row.UserId)` (:383) and **mutates that existing subscription in place** ⇒ violates §4 "granting a new package must not remove existing access".
5. `AdminService.CreateSubscriptionCoreAsync` (:5941-5946) throws `subscription_exists` ⇒ blocks multi-package grants.
6. Learner can PATCH `/v1/settings/profile` and write **any arbitrary unvalidated string** to `ActiveProfessionId` (`LearnerService.cs:654-663`) — no catalog check, no purchase check.
7. `other-allied-health` is registerable (`enrollment.ts:71`, `SeedData.cs:2205`) but **absent from the Professions reference table** (`SeedData.cs:934-942` seeds only 7) ⇒ discipline filter falls through ⇒ those users see every discipline's materials.
8. Malformed video `ProfessionIdsJson` fails **open** (`VideoLibraryLearnerService.cs:344-347`).
9. `ManualPaymentDto.ProofUrl` returns the raw internal storage key to clients (`BillingExpansionEndpoints.cs:365`).
10. Proof dedupe only rejects a hash reused by a **different** user (`ManualPaymentService.cs:117`).
11. `ManualPaymentService.SetStatusAsync` (:311-322) makes rejected/approved/paid terminal — a mis-clicked Reject is unrecoverable.
12. `lib/admin-permissions.ts` omits the backend `notifications` permission.

**Data reality:**
- Videos by (subtest, profession): listening+none 11, reading+none 11, speaking+none 6, speaking+medicine 6, speaking+nursing 5, speaking+pharmacy 3, writing+medicine 56, writing+nursing 11, writing+pharmacy 8. **Zero physiotherapy / dentistry / radiography videos.**
- Materials tree (622 files) **never ingested**; no ingest script exists. On-disk folder names (Medicine, Nursing, Pharmacy, Physiotherapy, Dentistry, Radiography) match seeded `ProfessionReference.Label` verbatim.
- `SubtestReference.SupportsProfessionSpecificContent` = true for writing/speaking, false for reading/listening.

## Design

### §1 — Universal proof of payment

Additive columns on `ManualPaymentRequest`:
- `Kind` string(24) NOT NULL default `'learner_upload'` — `learner_upload` | `gateway_receipt`
- `Gateway` string(32) NULL — stripe/paypal/easykash/... for `gateway_receipt`
- `PaymentTransactionId` Guid? NULL — FK to PaymentTransaction
- `ProfessionId` string(32) NULL — buyer's registered profession at purchase time
- `ProofWaivedByAdminId` Guid? NULL, `ProofWaivedAt` DateTimeOffset? NULL, `ProofWaiverReason` string(512) NULL

Behaviour:
- **Offline methods** — hard gate, existing flow, generalised beyond `paymentCategory: 'inside_egypt'`. Checkout cannot complete without a proof row.
- **Card gateways** — `ApplyCheckoutCompletionAsync` auto-creates a proof row: `Kind='gateway_receipt'`, `Status='paid'`, `Gateway=<name>`, `Method=<gateway>`, `Reference=<GatewayTransactionId>`, amount/currency/email/course/date from the transaction, `ProofUrl=null`, `ProfessionId=<user.ActiveProfessionId>`. Idempotent on `PaymentTransactionId`.
- **Admin override** — endpoint to waive the upload requirement on a pending offline order; writes the `ProofWaived*` fields + AuditEvent.
- **Status vocabulary** — internal values unchanged (no status data migration). Admin UI maps: Pending ⇐ `pending`|`needs_review`; Verified ⇐ `approved`|`paid`; Rejected ⇐ `rejected`. Add a Reopen action: `rejected` → `pending` allowed.
- **Dedupe** — reject a hash reused by a different user (existing) **and** by the same user for a *different* order; allow resubmission against the same order.
- **DTO** — replace `ProofUrl` with `HasProof` bool (mirrors `PaymentMethodConfigDto.HasQrImage`).
- Base64 transport retained: 10 MB proof ⇒ ~13.3 MB encoded, within the 25 MB Kestrel cap.

### §1/§6.4 — WhatsApp

- RuntimeSettings section `support`: `whatsAppNumber` (seed `447961725989`), `whatsAppProofTemplate`.
- Public read endpoint exposing the number (no auth — it is a public support number).
- `lib/billing/whatsapp.ts` reads from settings, keeps the constant as fallback.
- New shared `components/billing/send-proof-whatsapp-button.tsx` rendered on: checkout review, checkout success, payment return, billing/orders, manual-payment page.

### §2/§5/§6.6 — Delivery method + fulfilment

`BillingPlan` (+ `BillingPlanVersion` parity) gains:
- `DeliveryMethod` string(32) NOT NULL default `'automatic_web'` — `automatic_web` | `manual_web` | `telegram` | `manual_material`
- `TelegramInviteUrl` string(512) NULL
- `DeliveryInstructions` string(2000) NULL
- `IncludedSubtestsJson` string(512) NULL — e.g. `["speaking"]`; null/`[]` ⇒ all subtests
- `ContentOverridesJson` text NULL — `{"videos":{"include":[],"exclude":[]},"materialFolders":{"include":[],"exclude":[]}}`

`Subscription` gains `FulfilmentStatus` string(24) NOT NULL default `'auto'` — `auto` | `pending_manual` | `fulfilled`.

Flow: `automatic_web` ⇒ `FulfilmentStatus='auto'`, Pending→Active on payment (unchanged). `manual_web`/`telegram`/`manual_material` ⇒ `FulfilmentStatus='pending_manual'`, Status **stays Pending**. The resolver already accepts only Active/Trial/FreezeRequested/Frozen, so Pending grants nothing — no new gating code required. Admin "Mark Fulfilled" ⇒ `FulfilmentStatus='fulfilled'`, Status→Active, Telegram invite revealed on the learner's order page.

Migration backfills every existing plan to `automatic_web` and every existing subscription to `auto`.

### §3 — Profession

**Canonical taxonomy:** `SignupProfessionCatalog` is the source of truth. New public `GET /v1/professions/catalog`. `lib/auth/enrollment.ts` fetches it instead of hardcoding. `Professions` reference table synced. `BillingPlan.Profession` validated against catalog ∪ `{'all'}`. Admin video picker (`lib/api/speaking-role-play-cards.ts` `PROFESSION_OPTIONS`) fetches it.

Data migrations: insert missing `other-allied-health` Professions row; map `BillingPlan.Profession='allied_health'` → `'other-allied-health'`; add `dentistry` to the billing list.

**Content resolution:** a video/material is accessible iff — module explicitly enabled **and** (`item.SubtestCode ∈ plan.IncludedSubtests` or IncludedSubtests empty) **and** profession-visible **and** not in `ContentOverrides.exclude`; or it is in `ContentOverrides.include`. `IncludedSubtests` unions across the learner's active packages. Existing `EntitlementsJson.video_library.subtests` migrates into `IncludedSubtestsJson`.

**Checkout block:** `BuildBillingQuoteAsync` (live def at `LearnerService.cs:8228` — **not** the `#if false` dead copy at :7178) and `CreateCheckoutSessionAsync` (:3826) throw `ApiException("profession_mismatch")` when `plan.Profession != 'all' && plan.Profession != user.ActiveProfessionId`. Storefront marks mismatched plans unavailable.

**Content availability:** new `PlanContentAvailabilityService` — given (plan, profession) returns per-module item counts, 5-min cached. Checkout blocks with `content_unavailable_for_profession` when a content module is enabled but yields zero items. This is what cleanly handles Physiotherapy having no videos.

**Profession lock:** `LearnerService.cs:654-663` validates against the catalog and rejects with `profession_locked` once the user has ≥1 completed purchase. Admin can change from the user detail page (audited). Settings UI shows locked state + "Request a change" → WhatsApp.

### §4 — Admin manual access

- `AdminUserAccessPackageRequest` gains `StartsAt`, `OverrideProfessionMismatch`.
- `GrantPackageAsync`: `StartedAt = request.StartsAt ?? now`; `ExpiresAt = request.ExpiresAt ?? StartedAt + plan.AccessDurationDays` (180 default); syncs `learner.AccessExpiresAt` to the furthest package expiry; validates plan.Profession vs learner.ActiveProfessionId unless overridden (audited either way).
- New `SuspendPackageAsync` / `RestorePackageAsync` via `SubscriptionStateMachine`. `RemovePackageAsync` must go through the state machine instead of hard-setting `Cancelled`.
- Fix `ResolveSubscriptionForApprovalAsync` to create a new subscription rather than mutate an arbitrary existing one.
- Allow multiple subscriptions in `CreateSubscriptionCoreAsync`.
- `UserAccessSubscriptionDto` surfaces `StartedAt`, `FulfilmentStatus`.

### §7 — Security fixes

1. `MaterialAccessService.CanCandidateAccessMaterialFileAsync` — add `HasEligibleSubscription` conjunct at :144.
2. Remove the root-level blanket `return true` (:181-185); require subscription + module + discipline.
3. `VideoPlaybackSessionService.RenewAsync` — re-check entitlement + profession before re-signing.
4. Malformed profession JSON ⇒ fail closed (`VideoLibraryLearnerService.cs:344-347`).
5. Materials `IsDisciplineVisible` — learner with no profession fails closed on discipline-tagged folders; neutral folders stay visible.

### Data + mobile

- `scripts/materials/ingest-materials.mjs` — resumable, checkpointed, rate-limited to 30 writes/min, maps top-level folder names to disciplines. Owner runs it.
- `lib/native/billing-bridge.ts:276` repointed at `/v1/billing/checkout-sessions` (express) with the correct payload.
- `lib/admin-permissions.ts` — add the missing `notifications` permission.

## Migrations (hand-authored, future-dated, inline `[Migration]`, ModelSnapshot untouched)

Latest existing: `20260728091000`. New:
1. `20260729090000_AddPaymentProofAndDeliveryMethod.cs` — all additive columns on ManualPaymentRequest / BillingPlan / BillingPlanVersion / Subscription + backfills.
2. `20260729091000_UnifyProfessionTaxonomy.cs` — insert `other-allied-health`; map `allied_health` → `other-allied-health`; migrate `EntitlementsJson.video_library.subtests` → `IncludedSubtestsJson`.
3. `20260729092000_AddSupportWhatsAppRuntimeSettings.cs` — support section columns + seed.

## Acceptance criteria → implementation map

| §7 criterion | Satisfied by |
|---|---|
| Proof compulsory + visible to admin for every order | Offline hard gate + auto gateway receipt ⇒ one proof row per order; unified admin dashboard |
| WhatsApp button on every package | Shared `SendProofOnWhatsAppButton` on all checkout/success/billing surfaces |
| Auto access follows package AND profession | Subtest × profession resolution in the entitlement resolver |
| Medicine+Speaking ⇒ Medicine Speaking only | Profession-visible filter + fail-closed on malformed/no-profession |
| Manual admin access always available | UserAccessAllocationService + ManageAccessPanel (extended) |
| 180 days, auto-expires | `ExpiresAt = StartsAt + AccessDurationDays`; AccessExpiresAt sync; existing 403 gate |
| Telegram/manual clearly marked, stay pending | `DeliveryMethod` + `FulfilmentStatus='pending_manual'` + Subscription stays Pending |
| Direct links must not bypass | Security fixes 1-5 |
