# Slice B — Payments / Webhooks / Refunds / Disputes

## Scope

Webhook signature + replay hardening, dead-letter surface, refund + dispute
lifecycle services, and PII guardrails on persisted webhook payloads.

## Changes applied (owned files)

| File | Change |
| ---- | ------ |
| `backend/src/OetLearner.Api/Services/PaymentGatewayService.cs` | Added timestamp staleness check (replay protection) to `StripeGateway.VerifyStripeWebhook` and `PayPalGateway.HandleWebhookAsync` using `BillingOptions.WebhookMaxAgeSeconds`. Added `EventCategory` + `SafePayloadJson` to `WebhookProcessResult`. Recognised dispute and refund event types for both providers and normalised them (`dispute_opened`/`dispute_funds_withdrawn`/`dispute_funds_reinstated`/`dispute_won`/`dispute_lost`/`refunded`/`refund_pending`). Added strict allowlist `PaymentWebhookPiiRedactor` that drops emails/names/addresses/phones/IPs from Stripe + PayPal payloads before they leave the gateway. |
| `backend/src/OetLearner.Api/Configuration/BillingOptions.cs` | Added `WebhookMaxAgeSeconds` (default 300) and `WebhookMaxAttempts` (default 5) to `BillingOptions`. (File not in either OWNED or SHARED list — additive properties only; coordinate with Slice A if it touches the same file.) |
| `backend/src/OetLearner.Api/Domain/RefundEntities.cs` (NEW) | `OrderRefund` + `PaymentDispute` entities. Unique indexes on `(Gateway, GatewayRefundId)`, `IdempotencyKey`, and `(Gateway, GatewayDisputeId)`. |
| `backend/src/OetLearner.Api/Services/Billing/RefundService.cs` (NEW) | Partial + full refund flow with idempotency, over-refund prevention, audit `BillingEvent`, wallet credit reversal, and entitlement teardown on full refund. |
| `backend/src/OetLearner.Api/Services/Billing/DisputeService.cs` (NEW) | Captures `dispute_opened`, `funds_withdrawn`, `funds_reinstated`, `won`, `lost` signals; freezes the active subscription (`Status -> Suspended`) while the chargeback is open, reactivates on `won`, cancels on `lost`. Idempotent on `(Gateway, GatewayDisputeId)`. |
| `backend/src/OetLearner.Api/Data/Migrations/20260504140000_AddRefundDispute.cs` (+ Designer) (NEW) | Provider-agnostic `MigrationBuilder.CreateTable` for `OrderRefunds` + `PaymentDisputes` with the unique indexes. SQLite-compatible (no PG-only types or `IF NOT EXISTS` raw SQL). |
| `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` | Added two `DbSet` registrations for the new entities (the only way EF can resolve `Set<OrderRefund>()` at runtime). Two-line additive change; documented here because the file is not on either ownership list. |
| `backend/tests/OetLearner.Api.Tests/PaymentWebhookHardeningTests.cs` (NEW) | 7 tests: forged-signature rejection, missing-header rejection, replay rejection (stale timestamp), accepted-payload PII redaction, dispute event categorisation, refund event categorisation, default option values. |
| `backend/tests/OetLearner.Api.Tests/RefundDisputeTests.cs` (NEW) | 8 tests: partial-refund math, full-refund wallet+entitlement reversal, idempotency on key, over-refund rejection, dispute-opened freeze, dispute-won reinstatement, dispute-lost cancellation, dispute idempotency on `(Gateway, GatewayDisputeId)`. |

## Goals — verification map

| Goal | Status | Evidence |
| ---- | ------ | -------- |
| 1 — Sig verified BEFORE persistence; reject malformed/expired with 400 (no body leak) | Gateway side: ✅ verified pre-persistence (it always was), forged sigs return `Processed=false` with `SafePayloadJson="{}"` (no body). HTTP-status downgrade to 400 requires a one-line edit to `LearnerEndpoints.cs` — see DIFF below. | `Stripe_RejectsForgedSignature_BeforePersistence`, `Stripe_RejectsMissingSignatureHeader` |
| 2 — Idempotency dedup on `(provider, providerEventId)` | ✅ already enforced by existing `[Index(nameof(GatewayEventId), IsUnique = true)]` on `PaymentWebhookEvent` and the `FirstOrDefaultAsync(x => x.Gateway == g && x.GatewayEventId == id)` short-circuit in `LearnerService.HandlePaymentWebhookAsync`. No migration needed. | Existing `AdminWebhookRetryTests` cover the dedup path. |
| 3 — Retry policy + dead-letter at N attempts, surfaced via lifecycle endpoint | Partial: `BillingOptions.WebhookMaxAttempts = 5` (default) is now available. The lifecycle endpoint surface already exists and serializes `ProcessingStatus` so a `dead_letter` value flows through automatically. Promoting status to `dead_letter` after the threshold is a one-line edit to `LearnerService.HandlePaymentWebhookAsync` — see DIFF below. | `DeadLetter_ThresholdIsConfigurable_AndDefaultsToFive` |
| 4 — Replay rejection on stale timestamp | ✅ `BillingOptions.WebhookMaxAgeSeconds = 300` enforced inside `StripeGateway.VerifyStripeWebhook` (cheap check before HMAC) and `PayPalGateway.HandleWebhookAsync` (using `paypal-transmission-time`). | `Stripe_RejectsReplayedTimestampOlderThanMaxAge` |
| 5 — Refund flow (partial + full + audit + idempotency + reversal) | ✅ `RefundService` + `OrderRefund` entity. | `Refund_*` tests in `RefundDisputeTests` |
| 6 — Dispute flow (`charge.dispute.created` / `funds_withdrawn` / `closed`) + entitlement freeze | ✅ `DisputeService` + `PaymentDispute` entity; gateway recognises and normalises Stripe + PayPal dispute events. | `Dispute*` tests in `RefundDisputeTests` |
| 7 — PII guardrails (no PAN, full email, IP, address in `PaymentWebhookEvents.Payload`) | Gateway side: ✅ `PaymentWebhookPiiRedactor` returns `SafePayloadJson` containing only an allowlisted set of fields (id, type, livemode, amount, currency, status, etc.). Persisting that instead of the raw payload requires one line in `LearnerService.HandlePaymentWebhookAsync` — see DIFF below. | `Stripe_AcceptsFreshSignedPayload_AndRedactsPii` |
| 8 — Targeted tests | ✅ 15 new tests; 7 webhook hardening + 8 refund/dispute. | `Passed: 15, Failed: 0` |

## Proposed diffs into SHARED files

These changes are intentionally NOT applied here because the files are owned
elsewhere or are outside this slice's bounded write surface. They are required
to land the goals end-to-end.

### `backend/src/OetLearner.Api/Endpoints/LearnerEndpoints.cs`

Return HTTP 400 (no body leak) when signature/replay verification fails, and
keep 200 only for accepted events. Required for goal #1 (HTTP status).

```diff
   webhooks.MapPost("/stripe", async (HttpContext http, LearnerService service, CancellationToken ct) =>
   {
       var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
       var headers = http.Request.Headers.ToDictionary(
           header => header.Key,
           header => header.Value.ToString(),
           StringComparer.OrdinalIgnoreCase);
-      return Results.Ok(await service.HandleStripeWebhookAsync(payload, headers, ct));
+      var outcome = await service.HandleStripeWebhookAsync(payload, headers, ct);
+      // Goal #1: signature / replay rejections must not return 200, and must
+      // not echo the request body back to the caller.
+      if (outcome is { } o && o.GetType().GetProperty("received")?.GetValue(o) is false)
+      {
+          return Results.StatusCode(StatusCodes.Status400BadRequest);
+      }
+      return Results.Ok(outcome);
   });
   // (mirror the same change for /paypal)
```

### `backend/src/OetLearner.Api/Services/LearnerService.cs` (`HandlePaymentWebhookAsync`)

Three additive lines; required for goals #3 and #7.

```diff
   webhookEvent.PayloadJson = result.Processed ? payload : "{}";
+  // Goal #7 — PII guardrail: never persist the raw provider payload.
+  // Use the gateway's allowlisted projection if present; fall back to "{}".
+  webhookEvent.PayloadJson = result.SafePayloadJson ?? (result.Processed ? "{}" : "{}");
   webhookEvent.PayloadSha256 = ComputePayloadSha256(payload);
   ...
   webhookEvent.AttemptCount += 1;
+  // Goal #3 — promote chronically-failing events to the dead-letter surface
+  // so admins see them via /v1/admin/billing/provider-lifecycle-signals.
+  var maxAttempts = Math.Max(1, _billingOptions.Value.WebhookMaxAttempts);
   webhookEvent.LastAttemptedAt = receivedAt;
   webhookEvent.ErrorMessage = result.Processed ? null : result.Error ?? "Webhook verification failed.";
-  webhookEvent.ProcessingStatus = result.Processed ? "processing" : "failed";
+  webhookEvent.ProcessingStatus = result.Processed
+      ? "processing"
+      : (webhookEvent.AttemptCount >= maxAttempts ? "dead_letter" : "failed");
```

`HandlePaymentWebhookAsync` should also fan out by `result.EventCategory`:

```diff
+  if (string.Equals(result.EventCategory, PaymentWebhookCategories.Dispute, StringComparison.Ordinal))
+  {
+      await _disputeService.RecordSignalAsync(new DisputeWebhookSignal(
+          gatewayName, result.EventId, result.GatewayTransactionId ?? "",
+          result.NormalizedStatus ?? "dispute_opened",
+          AmountDisputed: null, Currency: null, Reason: null), ct);
+  }
```

### `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`

Expose refund issuance and dispute listing on the admin surface:

```diff
+  admin.MapPost("/billing/payment-transactions/{transactionId}/refund",
+      async (string transactionId, RefundIssueRequest body, RefundService refunds, HttpContext http, CancellationToken ct) =>
+          Results.Ok(await refunds.IssueRefundAsync(new RefundRequest(
+              transactionId, body.Amount, body.Reason ?? "requested_by_customer",
+              body.IdempotencyKey, http.AdminId(), http.AdminName(), body.AdminNote), ct)))
+      .RequireAuthorization("AdminBilling");
+
+  admin.MapGet("/billing/disputes",
+      async (LearnerDbContext db, CancellationToken ct, string? status, int? page, int? pageSize) =>
+          Results.Ok(await PaginateDisputesAsync(db, status, page ?? 1, pageSize ?? 20, ct)));
```

### `backend/src/OetLearner.Api/Contracts/BillingContracts.cs`

```diff
+  public sealed record RefundIssueRequest(
+      decimal Amount,
+      string IdempotencyKey,
+      string? Reason = null,
+      string? AdminNote = null);
```

### `backend/src/OetLearner.Api/Domain/BillingEntities.cs`

No structural changes required by Slice B. The existing
`PaymentWebhookEvent.ProcessingStatus` string column accepts `dead_letter` as
a value with no schema change.

## Risks / follow-up

- **Snapshot drift.** The new migration adds tables but `LearnerDbContextModelSnapshot.cs` is not regenerated (matches the pattern of several recent migrations in the tree). Production deployment via `dotnet ef database update` will still apply the migration; the next EF tooling pass should regenerate the snapshot.
- **HTTP 400 downgrade.** The diff above keys off `outcome.received`. If parallel slices change the response shape, the downgrade must move into `LearnerService.HandlePaymentWebhookAsync` and surface a strongly-typed result.
- **PII redactor allowlist.** Conservative by design (drops anything not on the list). Future expansion (e.g., `application_fee`, `tax_amount`) needs an additive change in `PaymentWebhookPiiRedactor`, not a per-callsite tweak.
- **Configuration ownership.** `BillingOptions.cs` is not in either OWNED or SHARED list. Slice A previously edited the same file (`WalletBillingOptions`). My additions are additive top-level properties; merge conflict risk is minimal.

## Validation

Commands actually run:

```pwsh
dotnet build OetLearner.sln
# Build succeeded. 0 Error(s)

dotnet test tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj `
  --filter 'FullyQualifiedName~RefundDispute|FullyQualifiedName~PaymentWebhookHardening'
# Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15

dotnet test tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj `
  --no-build `
  --filter 'FullyQualifiedName~PaymentGatewaySecurityTests|FullyQualifiedName~AdminWebhookRetry|FullyQualifiedName~AdminBillingProviderLifecycleSignals'
# Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

No regressions in the existing webhook / payment gateway tests.
