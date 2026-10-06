# Per-process user-state cache (15 s)

Owner-approved trade-off, 2026-10-05: three per-user reads that ran on every request are held in
a per-process, short-lived cache. This page is the operator reference: what is cached, how it is
invalidated, the **worst-case staleness for every kind of change**, how to switch it off and how
to read its counters.

Code: `backend/src/OetLearner.Api/Services/Caching/` (`UserStateCache`,
`UserStateInvalidationInterceptor`, `UserStateCacheSwitchWorker`).

## What is cached

| Kind (`kind` metric tag) | Value | Read by | Subject |
| --- | --- | --- | --- |
| `jwt_account` | The account-liveness row: deleted, role, learner active, learner access expiry, expert active, refresh-token family alive | `OnTokenValidated` in `Program.cs` (every authenticated request) | auth account id |
| `entitlement` | `EffectiveEntitlementSnapshot` | `EffectiveEntitlementResolver.ResolveAsync` (about 25 services: content gates, videos, mocks, speaking, writing, recalls, companion, ...) | learner user id |
| `freeze_status` | The freeze-status DTO on `/v1/me`, `/v1/me/bootstrap`, `/v1/learner/dashboard` | `LearnerService.GetFreezeStatusForLoadedUserAsync` | learner user id |
| `freeze_gate` | The learner's current freeze record | `LearnerService.EnsureLearnerMutationAllowedAsync`, the write gate in front of about 40 learner mutations | learner user id |

Not cached, on purpose: the freeze request / confirm / cancel flows (they decide and write from
the record, so they read it directly), a denied JWT (a denial is always re-read, so a stale denial
can never lock a reinstated user out), anything while the request's own `DbContext` holds
uncommitted tracked changes (entitlement), and the "account not found" result.

## Safety rules

1. **TTL 15 s** (`Performance:UserStateCache:TtlSeconds`, clamped to 1..30). A hit is never older.
2. **End times cap the entry.** The entitlement entry stops at the earliest future subscription
   start / expiry, add-on item start / end or scheduled freeze start (`EffectiveEntitlementResolver.nextChangeAt`);
   a JWT entry stops at the learner access expiry. The access expiry is additionally compared with
   the live clock on every request, so a cached acceptance can never outlive it.
3. **Read-before-write token.** `BeginRead` is taken before the database read; `Set` refuses to
   store if the subject was invalidated in between. A value read before a concurrent write is never
   cached after that write's invalidation.
4. **Version stripes, not per-user state.** 4096 stripes plus one global version, compared on every
   read. A stripe collision only costs an extra miss. Nothing grows with the number of users except
   the entries themselves (bounded by `MaxEntries`, default 50 000, then swept and cleared).
5. **Wall clock moved backwards** (entry stored "in the future"): treated as a miss.

## Invalidation (in-process, immediate)

`UserStateInvalidationInterceptor` is attached to every `LearnerDbContext` (the `AddDbContext`
lambda in `Program.cs`). It collects the affected subjects in `SavingChanges` and applies them in
`SavedChanges`, i.e. **after a successful EF save**; a failed save evicts nothing. So every code
path in this process that changes the rows below is covered without each call site remembering to
evict. Cost: one `ChangeTracker.Entries()` pass (one `DetectChanges`) per save; per-type `Entries<T>()`
passes would each re-run `DetectChanges`, so they are not cheaper (see the `ponytail:` note in the
interceptor for the upgrade path if a bulk context is ever measured slow):

| Change (committed through EF in this process) | Entity / property that triggers it | Evicts |
| --- | --- | --- |
| Freeze request, approval, rejection, cancel, force-end, scheduled start/end | `AccountFreezeRecord`, `AccountFreezeEntitlement` (any change) | learner |
| Revoke a session, logout, logout-all, device replacement, refresh-token reuse, admin session removal | `RefreshTokenRecord.RevokedAt` / `ExpiresAt` modified, or deleted (a newly issued token does not evict) | auth account |
| Password change / reset | `ApplicationUserAccount.PasswordHash` | auth account |
| Role change, account deletion | `ApplicationUserAccount.Role` / `DeletedAt` | auth account |
| Suspend / reinstate / delete a learner | `LearnerUser.AccountStatus` | learner + auth account |
| Learner access expiry edit | `LearnerUser.AccessExpiresAt` | learner + auth account |
| Active profession / current plan change | `LearnerUser.ActiveProfessionId` / `CurrentPlanId` | learner + auth account |
| Auth account re-link | `LearnerUser.AuthAccountId` (old and new id) | both |
| Expert (de)activation | `ExpertUser.IsActive` / `AuthAccountId` | auth account |
| Subscription purchase, grant, renewal, expiry, credit change | `Subscription` (any change) | learner |
| Per-learner module override | `UserModuleOverride` (any change) | learner |
| Add-on item change | `SubscriptionItem` (any change) | everything (no user id on the row) |
| Plan, plan version, add-on or freeze-policy edit | `BillingPlan`, `BillingPlanVersion`, `BillingAddOn`, `AccountFreezePolicy` | everything |
| Sign-in bookkeeping, streak / activity counters | `LastLoginAt`, `FailedSignInCount`, `LastActiveAt`, ... | nothing (deliberately) |

`EffectiveEntitlementResolver.Invalidate(userId)` (the existing "I changed this behind the change
tracker" call) now also drops the shared entry for that user; the parameterless form that the
tracker hooks call on every save still only clears the request-scoped memo.

Bulk `ExecuteUpdate` / `ExecuteDelete` / raw SQL writes are invisible to an EF interceptor. Today
two exist and both are handled:

- `UserHardDeleteService.PurgeAsync` (the admin "hard delete user", reflection-driven
  `ExecuteDelete` over the whole schema) evicts the purged auth account and learner ids itself,
  right after its transaction commits.
- Billing plan / add-on hard deletes are refused while any subscription or item references them, so
  no cached snapshot can depend on a row they delete.

(`AuthDataRetentionWorker` bulk-deletes refresh tokens that were revoked long ago; a deleted revoked
token cannot make a family alive, so nothing to evict.) Anything added later must call
`UserStateCache.Invalidate...` itself or accept the TTL.

## Worst-case staleness

| Change | Made by | Worst-case latency before the active API slot reflects it |
| --- | --- | --- |
| Any row in the table above | a request handled by **this** process | next request (0 s) |
| Any of them | **another process**: the idle blue/green slot, the ai-worker, a deploy-time job | **15 s** (the TTL) |
| Logout / revoke / password reset / suspension / role change | this process | next request |
| The same, performed by a background sweep in another process | other process | 15 s |
| Refresh token reaches its natural `ExpiresAt` | time | 15 s (the access token itself still expires at its own 15 min) |
| Learner access expiry reached | time | **0 s** (compared with the live clock; entry capped at the instant) |
| Subscription / add-on item expiry or start reached | time | **0 s** (entry capped at the instant) |
| Scheduled freeze starts | time | write gate: 0 s (live clock); entitlement `IsFrozen`: 0 s (capped); freeze DTO shown on `/me`: 15 s |
| Plan or freeze-policy edit | admin on this process | next request |
| Admin hard delete of a user (bulk SQL) | this process | next request (explicit eviction) |
| Any other bulk SQL write to a watched table | anything | 15 s |
| A save inside an outer database transaction | this process | next request, except a request that re-fills the cache between the save and the commit (milliseconds): then up to 15 s |
| Kill-switch flag flipped (below) | admin | about 35 s (5 s startup delay, 30 s poll) |

Production note: with `KEEP_PREVIOUS_SLOT_RUNNING=false` (owner-approved) only one API slot and the
ai-worker run after a deploy, so "another process" means the ai-worker's background sweeps. The two
slots of a cutover each hold their own cache; each is bounded by its own TTL.

## Kill switches and knobs

| Switch | Effect | Latency |
| --- | --- | --- |
| Feature flag `user_state_cache`, **Enabled = false** (Admin > Feature Flags, create the flag if it does not exist) | cache off, all entries dropped. No row = ON (the approved default); the newest row for the key wins | about 35 s, no deploy |
| `Performance__UserStateCache__Enabled=false` | hard off | restart |
| `Performance__UserStateCache__TtlSeconds` (1..30) | shorter staleness bound | restart |
| `Performance__UserStateCache__MaxEntries` | memory bound | restart |

`UserStateCacheSwitchWorker` polls the flag; it is a hosted service, not a read on the request path,
so the JWT check stays a single database command. A failed read keeps the last value.

## Counters

- `GET /v1/admin/system/user-state-cache` (policy `AdminSystemAdmin`, the same as `/v1/admin/alerts`): per-kind hits / misses / sets / rejected sets,
  entry count, invalidations, TTL, config switch and runtime switch. Per process (it describes the
  slot that answered).
- `System.Diagnostics.Metrics` meter `OetLearner.UserStateCache`: counters
  `oet.user_state_cache.hits`, `.misses`, `.sets`, `.rejected_sets` (tag `kind`) and
  `.invalidations` (tag `scope` = `subject` | `all`). Zero cost without a listener; picked up by
  `dotnet-counters` or any OpenTelemetry exporter.

A healthy slot shows a hit ratio well above 90 % on `jwt_account`; a rising `rejected_sets` means
writes are landing during reads (normal under load, harmless: the fill is simply skipped).

## Manual test sources (not run by CI)

Not tested - owner QA (owner directive 2026-10-06: no automated QA anywhere). These files stay in
git as inert manual tools; no CI lane runs them and none has been run for this change. They describe
the intended behaviour:

`backend/tests/OetLearner.Api.Tests/Caching/` (hit / miss / expiry / clamp / invalidation / kill switch /
sweep; every row of the invalidation table; resolver, freeze DTO and write-gate consumers including
the "another process only shows within the TTL" and "never past the subscription expiry" cases) and
three JWT cases in `Auth/AuthQueryPerformanceTests.cs` (second request makes zero commands, an
in-process suspension evicts at once, a cached acceptance never survives the access expiry).
