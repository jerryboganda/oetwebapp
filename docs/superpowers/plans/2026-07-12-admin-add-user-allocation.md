# Admin Add-User + Allocation — Implementation Plan

> **For agentic workers:** implement task-by-task. Backend build/tests run on **CI** (owner rule),
> not locally; frontend `tsc`/vitest run locally. Stage explicit paths, never `git add -A`. No
> `Co-Authored-By` trailer (attribution not enabled). Steps use `- [ ]` tracking.

**Goal:** An admin "Add User" flow that creates a learner and allocates ≥1 packages + add-ons,
per-user module toggles, per-user Materials-folder and Recall-set scoping, and a master login-expiry
gate — reusable for existing users.

**Architecture:** New per-user override tables + a `LearnerUser.AccessExpiresAt` column; the
entitlement resolver aggregates across multiple subscriptions and applies per-user module deny/allow;
Materials/Recalls services honor per-user allow-lists; the auth choke-point blocks expired logins; a
shared React allocation panel drives both Add-User and the user-detail page.

**Tech Stack:** .NET 10 minimal-API + EF Core (PostgreSQL), Next.js 15 app-router, React 19, TS.

## Global Constraints
- Module keys (exact): `Recalls`, `MaterialsLibrary`, `VideoLibrary`, `Mocks`.
- Expired error: HTTP **403**, code `subscription_expired`, message exactly
  `Your Subscription has expired. Please Renew Your subscription`.
- Renew CTA target: `/subscriptions`.
- RBAC scopes: create user = `AdminUsersWrite`; package/add-on = `AdminBillingSubscriptionWrite`;
  scope overrides = `AdminUsersWrite`.
- Single-subscription users' resolved entitlements MUST be byte-identical to today.
- Grants go through `SubscriptionBundleInitializer` (+ `AiCreditLedger`), never wallet-only path.

---

## Phase A — Backend data model

### Task A1: Per-user override entities + `AccessExpiresAt`
**Files:** Create `Domain/UserAccessEntities.cs` (UserModuleOverride, UserMaterialFolderAccess,
UserRecallSetAccess); Modify `Domain/Entities.cs` (LearnerUser: add `AccessExpiresAt`); Modify
`Data/LearnerDbContext.cs` (DbSets + `OnModelCreating` config: indexes, FKs, string lengths).
**Produces:** entities `UserModuleOverride{Id,UserId,ModuleKey,Enabled,UpdatedAt}`,
`UserMaterialFolderAccess{Id,UserId,FolderId,CreatedAt}`,
`UserRecallSetAccess{Id,UserId,RecallSetCode,CreatedAt}`; `LearnerUser.AccessExpiresAt DateTimeOffset?`.
Unique indexes `(UserId,ModuleKey)`, `(UserId,FolderId)`, `(UserId,RecallSetCode)`.
- [ ] Add entities + DbSets + config following existing patterns (Id prefixes `umo_`,`ufa_`,`ursa_`).
- [ ] Build check on CI.

### Task A2: EF migration
**Files:** Create `Data/Migrations/2026072712xxxx_AddPerUserAccessOverrides.cs` (+ Designer);
Modify `Data/Migrations/LearnerDbContextModelSnapshot.cs`.
- [ ] Prefer `dotnet ef migrations add AddPerUserAccessOverrides` (if API builds locally); else
  hand-author matching the 3 tables + `LearnerUser.AccessExpiresAt` + snapshot, mirroring
  `20260701090000_AddSubscriptionTimerFreezingAndManualPaymentFields` structure.
- [ ] Verify migration compiles + applies on CI.

---

## Phase B — Entitlement resolver aggregation + per-user module override

### Task B1: `DisabledModules` on snapshot + deny-aware `IsModuleEnabled`
**Files:** Modify `Services/Entitlements/EffectiveEntitlementResolver.cs`.
**Produces:** `EffectiveEntitlementSnapshot.DisabledModules: IReadOnlyList<string>` (default empty);
`IsModuleEnabled` subtracts `DisabledModules` **before** the fail-open empty-list branch.
- [ ] TDD: disabled key ⇒ false even when `EnabledModules` empty; unrelated key ⇒ fail-open true.

### Task B2: Multi-subscription aggregation
**Files:** Modify `Services/Entitlements/EffectiveEntitlementResolver.cs`.
- [ ] Replace `ResolveLatestSubscriptionAsync` single-pick with: load all subs, evaluate each with
  existing fail-low/expiry logic; `primary` = current latest ordering (back-compat for
  tier/plan/counters/addons); `HasEligibleSubscription` = any eligible non-expired; `EnabledModules`
  = union across eligible; counters summed; `ExpiresAt` = max across eligible.
- [ ] Apply `UserModuleOverride`: enabled=false ⇒ add to `DisabledModules`; enabled=true ⇒ union into
  `EnabledModules`.
- [ ] TDD: (a) single sub unchanged vs golden; (b) two plans union modules; (c) per-user disable of a
  plan-enabled module; (d) summed counters; (e) max expiry; (f) all-expired ⇒ not eligible.

---

## Phase C — Auth login-expiry gate

### Task C1: Block expired logins
**Files:** Modify `Services/AuthService.cs` (`EnsureAccountCanAuthenticateAsync`).
- [ ] After learner load, throw `ApiException.Forbidden("subscription_expired", <exact msg>)` when
  `AccessExpiresAt <= timeProvider.GetUtcNow()`. Placed so it also fires on `RefreshAsync`.
- [ ] TDD: expired learner → Forbidden(subscription_expired); null/future → passes; suspended still
  wins first.

---

## Phase D — Admin services + endpoints

### Task D1: Create-user (password or invite)
**Files:** Modify `Services/AdminService.cs` (add `CreateUserAsync`), `Contracts/AdminRequests.cs`
(`AdminUserCreateRequest{Name,Email,Role,ProfessionId,MobileNumber?,Password?,SendInvite}`),
`Endpoints/AdminEndpoints.cs` (`MapPost("/users")` `.WithAdminWrite("AdminUsersWrite")`),
`lib/api.ts` (`createAdminUser`).
- [ ] Reuse `InviteUserAsync` core; if `Password` set → hash + `EmailVerifiedAt=now`, skip OTP; else
  keep invite/OTP. Write `MobileNumber` to `LearnerRegistrationProfile`. Validate profession vs
  signup catalog. TDD on validation + password vs invite branch.

### Task D2: Allocation service — packages, add-ons, scope
**Files:** Create `Services/Billing/UserAccessAllocationService.cs` (+ interface); Modify
`Endpoints/AdminEndpoints.cs` (access routes); `Contracts/AdminRequests.cs` (allocation DTOs);
`Services/AdminService.cs` (relax one-per-user guard in `CreateSubscriptionCoreAsync` behind a
`allowAdditional` param used only by this path).
**Produces:** `GET/POST/DELETE/PATCH .../access/*` per the spec; grants via
`SubscriptionBundleInitializer` + `AddonGrantProcessor` (idempotency ref
`admin_alloc:{userId}:{code}:{n}`); master expiry write + refresh-token revoke on past expiry.
- [ ] TDD: grant 2 packages ⇒ 2 subs, 1 primary; re-POST same package idempotent (no double credits);
  add-on grant idempotent; scope PUT overwrites overrides + master expiry; past expiry revokes tokens.

### Task D3: Access read model
**Files:** Modify `Services/Billing/UserAccessAllocationService.cs`, `lib/api.ts`, `lib/admin.ts`.
- [ ] `GET .../access` returns { subscriptions[], addOns[], moduleOverrides[], materialFolderIds[],
  recallSetCodes[], accessExpiresAt }. TDD DTO shape.

---

## Phase E — Content enforcement (Materials + Recalls)

### Task E1: Materials per-user folder restriction
**Files:** Modify `Services/Content/MaterialAccessService.cs` (`GetVisibleTreeAsync`,
`CanCandidateAccessMaterialFileAsync`).
- [ ] If user has `UserMaterialFolderAccess` rows, restrict tree to those folders (+ancestors nav,
  +descendants content); download check honors same. No rows ⇒ unchanged. TDD both.

### Task E2: Recalls per-user set restriction
**Files:** Modify `Services/VocabularyService.cs` (+ recall/vocab list endpoints as needed).
- [ ] If user has `UserRecallSetAccess` rows, filter terms to those set codes; no rows ⇒ unchanged.
  TDD both.

---

## Phase F — Frontend

### Task F1: API clients + types
**Files:** Modify `lib/api.ts`, `lib/materials-api.ts` (reuse), add `lib/user-access.ts`.
- [ ] `createAdminUser`, `fetchUserAccess`, `grantUserPackage`, `removeUserPackage`,
  `grantUserAddon`, `putUserAccessScope`; plan/add-on/recall-set catalog fetchers; folder tree reuse.

### Task F2: `ManageAccessPanel` (shared)
**Files:** Create `components/admin/user-access/manage-access-panel.tsx` (+ subcomponents:
`package-list.tsx`, `addon-picker.tsx`, `module-toggles.tsx`, `folder-scope-picker.tsx`,
`recall-set-picker.tsx`). Keep each < 500 lines.
- [ ] Controlled component: value = full allocation; emits change; used by F3 + F4.

### Task F3: `AddUserModal` + hub button
**Files:** Create `components/admin/user-access/add-user-modal.tsx`; Modify `app/admin/users/page.tsx`
(button in `actions`, modal state, submit orchestration: create → packages → addons → scope).
- [ ] TDD/vitest: renders, validates email+login-setup, sequences calls.

### Task F4: Detail-page allocation
**Files:** Modify `app/admin/users/[id]/page.tsx` (replace read-only subscription card with
`ManageAccessPanel` prefilled from `fetchUserAccess`).

### Task F5: Expired login popup
**Files:** Create `components/auth/expired-subscription-modal.tsx`; Modify
`components/auth/sign-in-form.tsx` (branch on `subscription_expired` code → show modal; Renew →
`/subscriptions`).
- [ ] vitest: given code, modal shown with exact copy + CTA href.

---

## Phase G — Verify & ship
- [ ] Frontend `tsc`/`npm run build` green locally.
- [ ] Push branch; watch `Build & Deploy (web + API)` + backend xUnit on CI (ignore QA-Smoke flake).
- [ ] Open one PR; squash-merge `--admin` on green; single prod deploy.

## Self-review coverage
Add-User ✔(D1,F3) · password-or-invite ✔(D1,F3) · phone ✔(D1) · multi-package ✔(A1,D2) ·
add-ons ✔(D2) · custom/master expiry + login block ✔(A1,C1,D2) · expired popup+CTA ✔(F5) ·
4 module toggles per-user ✔(A1,B1,B2,F2) · Materials folder scope ✔(A1,E1,F2) · Recall-set scope
✔(A1,E2,F2) · existing-user edit ✔(D2,D3,F4).
