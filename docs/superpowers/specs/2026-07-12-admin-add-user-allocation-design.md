# Admin "Add User" + Per-User Package / Module / Content Allocation — Design Spec

**Date:** 2026-07-12
**Branch:** `feat/admin-add-user-allocation`
**Status:** Approved (owner, 2026-07-12) — single-deploy, maximum scope.

## Goal

Give admins a manual provisioning system on the User Management screen: an **Add User**
button that creates a learner directly, allocates **one or more packages + add-ons** with a
custom expiry, sets **per-user module toggles** (Videos / Materials / Mocks / Recalls) and —
when a module is on — restricts to **specific Materials folders** and **specific Recall sets**.
After the user's master access date passes, **login is blocked** with the popup *"Your
Subscription has expired. Please Renew Your subscription"* (Renew button → `/subscriptions`).
The identical allocation surface is embedded in the existing user-detail page so **existing
users** can be allocated / edited too.

## Owner decisions (locked)

| Decision | Choice |
|---|---|
| Multi-package meaning | **Both** — multiple full packages **and** add-ons per user |
| New-user login setup | **Admin chooses per user** — type a password now, OR send invite email |
| Per-user content control | **Full** — module toggles + Materials folder picker + Recall-set picker |
| Delivery | **One big PR / single deploy** |
| Age field | **Skip entirely** (no age/DOB column) |
| Login-block trigger | **Master date**, auto-filled to latest package expiry, admin-overridable |
| Expired popup CTA | **Renew** → `/subscriptions` |

## Key codebase facts (from the 5-subsystem map)

- **Users** = 3 rows: `ApplicationUserAccount` (credential), `LearnerUser` (domain, has
  `AccountStatus`, `CurrentPlanId`, `ActiveProfessionId`), `LearnerRegistrationProfile`
  (FirstName/LastName/**MobileNumber**/ProfessionId/CountryTarget). Phone already exists.
- **Create** today is invite-only: `POST /v1/admin/users/invite` → `AdminService.InviteUserAsync`
  (random temp password + email OTP). Explicit password only via detail-page `Set Password`.
- **Subscriptions**: `Subscription` (Domain/Entities.cs) — scalar `PlanId`, `ExpiresAt` (null =
  permanent), inline counters, freeze fields. **One-subscription-per-user is enforced** in
  `AdminService.CreateSubscriptionCoreAsync` (rejects if the user already has any).
- **Add-ons** surface as `SubscriptionItem` rows (`ItemCode`, `Status`, `StartsAt`/`EndsAt`) —
  the resolver already reads them via `ResolveActiveAddOnCodesAsync`. `AddonGrantProcessor`
  applies grants idempotently (`IdempotencyRecord`), incl. `AiCreditLedger`.
- **Module gating**: per-**plan** only, `BillingPlan.DashboardModulesJson` → parsed by
  `EffectiveEntitlementResolver.ResolveAsync` into `EnabledModules`; `IsModuleEnabled` is
  **fail-open** (empty list ⇒ all enabled). No per-user override exists.
- **Materials**: a generic `MaterialFolder` tree (self-FK `ParentFolderId`, depth ≤ 8); **no**
  discipline/type/subtype columns — those are just folder *names*. Per-folder access =
  `MaterialFolderAudience` (plan/cohort/institution only). Enforced in `MaterialAccessService`.
- **Recall sets** = `RecallSetTag` rows (PK `Code`); term membership in
  `VocabularyTerm.RecallSetCodesJson`. No per-user or per-set gating today.
- **Login**: `AuthService.SignInAsync` → `EnsureAccountCanAuthenticateAsync` is the choke point
  (also runs on refresh/MFA). Suspension already blocks here with `account_suspended` / 403.
  Errors serialize to `{ code, message, ... }`; `sign-in-form.tsx` branches on `code`.
  **Note:** a 400 + `auth_request_failed` gets remapped to `invalid_credentials` in
  `auth-client.ts`, so the new error MUST be **403** with a snake_case code.

## Architecture

### Data model (one EF migration, applied on CI)
1. `LearnerUser.AccessExpiresAt` (`DateTimeOffset?`) — master login gate.
2. `UserModuleOverride(Id, UserId, ModuleKey, Enabled, UpdatedAt)` — per-user module state.
3. `UserMaterialFolderAccess(Id, UserId, FolderId, CreatedAt)` — per-user folder allow-list.
4. `UserRecallSetAccess(Id, UserId, RecallSetCode, CreatedAt)` — per-user recall-set allow-list.
5. **No new subscription table** — relax the one-per-user guard; each package = a `Subscription`
   row; one row flagged **primary** (drives `CurrentPlanId` / `ActiveProfessionId`). Add-ons =
   `SubscriptionItem` rows via the existing grant engine.

### Entitlement resolution (core change)
`EffectiveEntitlementResolver.ResolveAsync` becomes **multi-subscription aggregate**:
- Load all of the user's subscriptions; compute per-sub eligibility with the *existing* fail-low
  logic; keep a **primary** (latest, for tier/plan/counters back-compat) but:
  - `HasEligibleSubscription` = any eligible & non-expired sub.
  - `EnabledModules` = **union** of every eligible sub's plan modules, THEN apply
    `UserModuleOverride` as an explicit **deny/allow set** (disable wins; enable adds). Deny is
    modeled as a real subtraction that short-circuits the fail-open `IsModuleEnabled`.
  - Counters (writing/speaking/AI) summed across eligible subs; snapshot `ExpiresAt` = max.
- **Single-sub users must be provably unchanged** (union of one = itself). Guarded by tests.
- To keep `IsModuleEnabled`'s fail-open contract intact while honoring per-user *disable*, the
  snapshot carries an explicit `DisabledModules` set that `IsModuleEnabled` subtracts first.

### Content scoping (restriction-within-plan)
- **Materials**: if the user has `UserMaterialFolderAccess` rows, `MaterialAccessService`
  restricts the visible tree to those folders (+ ancestors for nav, + descendants for content)
  and the download check likewise; no rows ⇒ inherit plan/audience behavior. Fail-closed only
  when a selection exists.
- **Recalls**: if the user has `UserRecallSetAccess` rows, recall/vocab queries filter
  `RecallSetCodesJson` to those codes; no rows ⇒ inherit.

### Auth login gate
Inside `EnsureAccountCanAuthenticateAsync`, after loading the learner, mirror the suspend block:
`if (learner.AccessExpiresAt is { } exp && exp <= timeProvider.GetUtcNow()) throw
ApiException.Forbidden("subscription_expired", "Your Subscription has expired. Please Renew Your
subscription");`. Setting/updating a past expiry revokes refresh tokens (kills live sessions).

### Backend endpoints (reuse existing RBAC scopes)
- `POST /v1/admin/users` — create-with-password OR invite (`AdminUsersWrite`).
- `GET /v1/admin/users/{id}/access` — current allocation snapshot.
- `POST /v1/admin/users/{id}/access/packages` — grant a plan (new Subscription row, full bundle
  init via `SubscriptionBundleInitializer`, custom expiry); `DELETE .../packages/{subId}`;
  `PATCH .../packages/{subId}` (expiry/primary). (`AdminBillingSubscriptionWrite`)
- `POST /v1/admin/users/{id}/access/addons` — idempotent add-on grant on a target sub.
- `PUT /v1/admin/users/{id}/access/scope` — declarative overwrite of module overrides,
  folder allow-list, recall-set allow-list, and master expiry. (`AdminUsersWrite`)

### Frontend
- `AddUserModal` (new) on `app/admin/users/page.tsx`, beside Invite / Bulk Import.
- `ManageAccessPanel` (new, shared) — packages list (add/replace/remove + expiry), add-on
  multiselect, 4 module switches, folder-tree picker, recall-set picker, master expiry. Mounted
  in `AddUserModal` and in `app/admin/users/[id]/page.tsx` (replaces the read-only sub card).
- `ExpiredSubscriptionModal` (new) shown by `sign-in-form.tsx` on `subscription_expired`.
- New `lib/api.ts` clients + reuse folder-tree / audience-picker / recall-set-tags catalogs.

## Risks
1. **Multi-subscription ripple** (highest): every "the user's subscription" singular lookup must
   be audited. Mitigation: resolver aggregation + xUnit; primary-flag preserves back-compat
   fields; keep `ResolveSubscriptionForApprovalAsync` fallback intact.
2. **Fail-open module trap**: per-user *disable* cannot be modeled by shrinking `EnabledModules`.
   Mitigation: explicit `DisabledModules` deny-set subtracted before the fail-open check.
3. **EF migration on Windows/CI**: generate via `dotnet ef` locally if it builds; else hand-author
   migration + snapshot and verify on CI (`Build & Deploy`), not QA-Smoke.
4. **Direct-password create** bypasses email verification — set `EmailVerifiedAt` deliberately and
   revoke nothing; document the security posture.

## Test strategy
Backend xUnit (run on CI): resolver aggregation (union modules, deny-set, summed counters, max
expiry, single-sub unchanged), login-expiry gate, idempotent package/add-on grants, content
restriction. Frontend: `tsc`/`npm run build` + targeted vitest on new components.
