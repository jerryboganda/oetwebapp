# Slice A — Wallet hardening report

> May 2026 billing hardening pass. Owner: Slice A.
> File-ownership rules: see [`README.md`](README.md).

## What changed (owned files)

### `backend/src/OetLearner.Api/Services/WalletService.cs`

- `CreditAsync` and `DebitAsync` now:
  - Wrap the read-modify-write in `db.Database.BeginTransactionAsync` when the
    provider supports transactions (skipped for EF InMemory in unit tests).
  - Re-load the wallet inside the transaction so the configured concurrency
    token (`Wallet.LastUpdatedAt`, set in `LearnerDbContext.OnModelCreating`)
    detects racing writers via `DbUpdateConcurrencyException`.
  - Use `checked()` arithmetic on credit growth and an explicit
    `newBalance < 0` guard on debit to enforce the negative-balance invariant
    in code (with a DB-level CHECK as backstop, see migration).
  - Validate `amount` (must be `> 0` and `<= 1,000,000,000`) and
    `transactionType` (non-empty, ≤ 32 chars) before any DB read.
  - Emit one `AuditEvent` per mutation (`Action = "wallet.credit"` /
    `"wallet.debit"`, `ResourceType = "Wallet"`, `ResourceId = walletId`,
    details include `txId`, `type`, `amount`, `balanceAfter`).
  - Accept an optional `idempotencyKey`. When provided, the service writes a
    row in `IdempotencyRecords` keyed by `wallet-credit:{walletId}` /
    `wallet-debit:{walletId}` whose `ResponseJson` stores the original
    `WalletTransaction.Id`. A replay returns the cached transaction and
    applies no balance change.
- The original parameter overloads remain so existing call sites
  (`LearnerService`, mock-grant flows, etc.) compile unchanged; they delegate
  to the new overload with `idempotencyKey: null`.

### `backend/src/OetLearner.Api/Domain/WalletTopUpTierConfig.cs`

- New optional `Slug` (max 64). Stable, immutable kebab-case identifier so
  downstream catalogs / audit trails can reference a tier without depending
  on the auto-generated GUID. Backwards-compatible: existing rows tolerate
  `NULL` and the slug can be set once (then locked).

### `backend/src/OetLearner.Api/Services/AdminWalletTierService.cs`

- Added a strict `SlugRegex` (`^[a-z0-9]+(?:-[a-z0-9]+)*$`, length 2–64).
- `AdminWalletTierInput` gained a `Slug` field.
- `ReplaceAsync` now reads the existing rows BEFORE validation, so validation
  can enforce slug immutability per row.
- `ValidatePayload` (now also seeded with `existingRows`) rejects:
  - invalid slug format (when provided);
  - changing a slug that has already been persisted;
  - duplicate slugs across the payload (computed from the effective slug,
    falling back to the persisted value when the inbound is null);
  - existing duplicate-amount rule (preserved);
  - amounts > 1,000,000 and credits/bonus > 10,000,000 (overflow / typo
    guard);
  - active tiers whose `DisplayOrder` is not strictly ascending when sorted
    by `Amount` — catches "overlapping ranges" between active tiers in lieu
    of an explicit `MinAmount`/`MaxAmount` schema.
- `ListAsync` now projects `slug` for both `database` and `appsettings`
  sources (appsettings rows project a derived slug — see proposed diff).

### `backend/src/OetLearner.Api/Data/Migrations/20260504130000_HardenWallet.cs`

Additive PostgreSQL migration:

1. `ALTER TABLE "WalletTopUpTierConfigs" ADD COLUMN IF NOT EXISTS "Slug"`
   (`varchar(64) NULL`).
2. Partial unique index `IX_WalletTopUpTierConfigs_Slug` on `(Slug) WHERE
   Slug IS NOT NULL`, so legacy rows with `NULL` slugs do not collide.
3. Adds `CK_Wallets_CreditBalance_NonNegative CHECK (CreditBalance >= 0)`
   on `Wallets`, wrapped in a `DO $$ ... $$` block so re-running the
   migration is a no-op (PG < 15 has no `ADD CONSTRAINT IF NOT EXISTS`).
4. Adds a partial index
   `IX_IdempotencyRecords_Scope_Key_WalletPrefix` on `(Scope, Key) WHERE
   Scope LIKE 'wallet-%'` to keep the new wallet-credit/debit replay path
   cheap as `IdempotencyRecords` grows.

> Note: tests use EF Core `EnsureCreatedAsync` (not `Migrate`), so this
> migration is exercised in production / staging only. Slug-uniqueness and
> non-negative-balance are also enforced in service code, which is what the
> tests cover.
> No Designer file is included; the existing migration
> `20260413120000_AddSessionMetadataToRefreshTokens.cs` follows the same
> pattern, so this is consistent with the project convention.

### `backend/tests/OetLearner.Api.Tests/WalletHardeningTests.cs` (new)

12 unit tests, all passing:

| # | Test | Goal coverage |
| - | ---- | ------------- |
| 1 | `CreditAsync_AppendsTransactionAndAuditEvent` | Audit emission + atomic mutation |
| 2 | `DebitAsync_AppendsTransactionAndAuditEvent` | Audit emission |
| 3 | `DebitAsync_RejectsWhenBalanceInsufficient` | Negative-balance invariant |
| 4 | `CreditAsync_RejectsNonPositiveAmount(0)` | Strict input validation |
| 5 | `CreditAsync_RejectsNonPositiveAmount(-1)` | Strict input validation |
| 6 | `CreditAsync_WithSameIdempotencyKey_AppliesOnceAndReplaysSameTransaction` | Idempotency replay |
| 7 | `ConcurrentDebit_OnSameWallet_SecondWriterDetectsConcurrencyViolation` | Concurrent mutation race |
| 8 | `AdminWalletTierService_RejectsInvalidSlugFormat` | Tier slug format |
| 9 | `AdminWalletTierService_RejectsDuplicateSlugWithinPayload` | Tier slug uniqueness |
| 10 | `AdminWalletTierService_RejectsSlugChangeOnExistingTier` | Tier slug immutability |
| 11 | `AdminWalletTierService_RejectsActiveTiersWithNonAscendingDisplayOrder` | Tier overlap rejection |
| 12 | `AdminWalletTierService_RecordsAuditEventOnReplace` | Audit emission (admin path) |

## Proposed diffs for shared files (DIFF-ONLY — not applied)

These changes are required to surface the new functionality end-to-end but
live in files owned by other slices. Each diff is unified-format, anchored
on the current `main`.

### Shared: `backend/src/OetLearner.Api/Domain/Entities.cs`

Add a `RowVersion` byte[] concurrency token to `Wallet` for cross-DB
parity (PG already has `LastUpdatedAt` as the EF concurrency token; adding
`RowVersion` lets MSSQL/SQLite deployments enforce the same invariant
without depending on caller discipline around `LastUpdatedAt`).

```diff
--- a/backend/src/OetLearner.Api/Domain/Entities.cs
+++ b/backend/src/OetLearner.Api/Domain/Entities.cs
@@ public class Wallet
     public int CreditBalance { get; set; }
     public string LedgerSummaryJson { get; set; } = "[]";
     public DateTimeOffset LastUpdatedAt { get; set; }
+
+    /// <summary>
+    /// Cross-DB row-version concurrency token. PostgreSQL maps this to
+    /// <c>xmin</c> when the EF <c>Npgsql:ValueGenerationStrategy</c> is set,
+    /// SQLite uses an EF-managed shadow value. Pair with <c>[ConcurrencyCheck]</c>
+    /// in OnModelCreating: <c>Property(x =&gt; x.RowVersion).IsRowVersion();</c>
+    /// </summary>
+    [ConcurrencyCheck]
+    public byte[]? RowVersion { get; set; }
 }
```

### Shared: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`

```diff
--- a/backend/src/OetLearner.Api/Data/LearnerDbContext.cs
+++ b/backend/src/OetLearner.Api/Data/LearnerDbContext.cs
@@ modelBuilder.Entity<Wallet>().Property(x => x.LastUpdatedAt).IsConcurrencyToken();
+        modelBuilder.Entity<Wallet>()
+            .Property(x => x.RowVersion)
+            .IsRowVersion()
+            .IsConcurrencyToken();
+
+        // Slice A (May 2026 hardening): partial uniqueness on Slug so that
+        // legacy NULL rows do not collide.
+        modelBuilder.Entity<WalletTopUpTierConfig>()
+            .HasIndex(x => x.Slug)
+            .IsUnique()
+            .HasFilter("\"Slug\" IS NOT NULL");
```

### Shared: `backend/src/OetLearner.Api/Domain/BillingEntities.cs`

`WalletTransaction` should carry the idempotency key + a unique
`(WalletId, IdempotencyKey)` index so the replay path can be enforced at
the schema level rather than relying on the parallel
`IdempotencyRecords` table:

```diff
--- a/backend/src/OetLearner.Api/Domain/BillingEntities.cs
+++ b/backend/src/OetLearner.Api/Domain/BillingEntities.cs
@@ [Index(nameof(WalletId))]
+[Index(nameof(WalletId), nameof(IdempotencyKey), IsUnique = true)]
 public class WalletTransaction
 {
     [Key]
     public Guid Id { get; set; }
@@     public DateTimeOffset CreatedAt { get; set; }
+
+    /// <summary>
+    /// Caller-supplied idempotency key. When non-null, replays of the same
+    /// (WalletId, IdempotencyKey) MUST resolve to this row instead of
+    /// applying a new credit/debit. Enforced by the unique index.
+    /// </summary>
+    [MaxLength(64)]
+    public string? IdempotencyKey { get; set; }
 }
```

### Shared: `backend/src/OetLearner.Api/Contracts/BillingContracts.cs`

```diff
--- a/backend/src/OetLearner.Api/Contracts/BillingContracts.cs
+++ b/backend/src/OetLearner.Api/Contracts/BillingContracts.cs
@@ public sealed record WalletTopUpRequest(
     int Amount,
     string Gateway,
     string? IdempotencyKey
 );
+
+/// <summary>
+/// Admin payload for spending wallet credits with idempotency
+/// (used by <c>/v1/admin/wallets/{userId}/spend</c>). Slice A consumers
+/// pass <c>IdempotencyKey</c> through to <c>WalletService.DebitAsync</c>.
+/// </summary>
+public sealed record AdminWalletSpendRequest(
+    int Amount,
+    string TransactionType,
+    string? ReferenceType,
+    string? ReferenceId,
+    string? Description,
+    string? IdempotencyKey);
```

### Shared: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`

The existing admin wallet-tier endpoints already accept the
`AdminWalletTierInput.Slug` field through model-binding (no endpoint change
needed). Slug is automatically surfaced in the response envelope via
`AdminWalletTierService.ListAsync`.

```diff
--- a/backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs
+++ b/backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs
@@ // Wallet tier admin (existing) — no signature change required for Slice A.
+
+// Optional: explicit admin wallet spend endpoint that exercises the new
+// idempotency-key path of WalletService.DebitAsync. Useful for manual ops
+// (refunds, comp credits) and for the test surface in slice H.
+admin.MapPost("/wallets/{walletId}/spend",
+    async (HttpContext http, string walletId, AdminWalletSpendRequest req,
+           WalletService wallet, CancellationToken ct) =>
+    {
+        var tx = await wallet.DebitAsync(
+            walletId, req.Amount, req.TransactionType,
+            req.ReferenceType, req.ReferenceId, req.Description,
+            createdBy: http.UserId(),
+            idempotencyKey: req.IdempotencyKey,
+            ct);
+        return Results.Ok(new { transactionId = tx.Id, balanceAfter = tx.BalanceAfter });
+    })
+    .RequireAuthorization(p => p.RequireAdminPermission(AdminPermission.ManageBilling));
```

### Shared: `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`

No mandatory change. The existing `/v1/billing/wallet/top-up` endpoint
already routes through `LearnerService.CreateWalletTopUpAsync`, which
already participates in the existing payment-idempotency pipeline. Slice A
introduces no new learner-facing endpoint.

## Risks / follow-ups

1. **Snapshot drift.** The model snapshot
   `LearnerDbContextModelSnapshot.cs` is owned by the EF tooling and is a
   shared file. Adding `Slug` to the entity but not updating the snapshot
   means the next `dotnet ef migrations add` will re-emit a Slug column.
   This is harmless at runtime (additive `IF NOT EXISTS` guard) but should
   be reconciled when the next migration is generated.
2. **InMemory provider semantics.** The new `BeginTransactionAsync` call
   is a no-op for EF InMemory (suppressed via `Ignore(InMemoryEventId.TransactionIgnoredWarning)`
   in the test scaffold). Production hardening relies on PostgreSQL
   transactional semantics — verify in staging with a multi-replica deploy.
3. **CHECK constraint adoption.** `CK_Wallets_CreditBalance_NonNegative`
   only takes effect after the migration has run. Any pre-existing
   negative-balance rows would block the migration; staging-deploy MUST run
   `SELECT * FROM "Wallets" WHERE "CreditBalance" < 0;` and remediate
   before the migration ships.

## Validation

Commands run from repository root:

```powershell
dotnet build backend/OetLearner.sln                                  # Build succeeded. 0 Error(s), 4 unrelated nullability warnings.
dotnet test  backend/OetLearner.sln --no-build `
    --filter "FullyQualifiedName~WalletHardeningTests"               # Passed: 12, Failed: 0, Total: 12
dotnet test  backend/OetLearner.sln --no-build `
    --filter "FullyQualifiedName~AdminWalletTier|`
              FullyQualifiedName~WalletConcurrency|`
              FullyQualifiedName~BillingTopUpIdempotency|`
              FullyQualifiedName~BillingTopUpTiersEndpoint|`
              FullyQualifiedName~BillingWalletTopUpAuth"              # Passed: 29, Failed: 0, Total: 29
```
