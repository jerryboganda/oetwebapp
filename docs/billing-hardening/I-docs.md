# Slice I — Docs, Observability, Incident Runbook

> Owner: docs subagent. Read [`README.md`](./README.md) first.

## Scope

Per the slice ownership matrix, this slice owns:

- `docs/BILLING.md` — canonical billing module reference (created).
- `docs/runbooks/billing-incident.md` — first-responder incident runbook (created).
- `docs/billing-hardening/I-docs.md` — this report.
- Conservative augmentation of `instrumentation.ts` to add a billing-feature
  Sentry tag (added; existing lines preserved).

This slice does **not** modify any service, endpoint, or entity file.

## Files created

| Path | Purpose | Words |
| ---- | ------- | ----- |
| [`docs/BILLING.md`](../BILLING.md) | Architecture, source-of-truth, ER diagram, state machines, invariants, provider boundary, RBAC matrix, PII / retention. | ~1850 |
| [`docs/runbooks/billing-incident.md`](../runbooks/billing-incident.md) | Symptom→action map, PG 17 triage queries, six common scenarios, migration rollback policy, escalation. | ~1150 |
| [`docs/billing-hardening/I-docs.md`](./I-docs.md) | This slice report. | ~600 |

## Files modified

| Path | Change | Lines added | Existing lines preserved |
| ---- | ------ | ----------- | ------------------------ |
| [`instrumentation.ts`](../../instrumentation.ts) | Added a Sentry `addEventProcessor` block that tags any event whose request URL or transaction name matches a billing-related route with `feature: 'billing'`. Wrapped in `try/catch` and feature-detected against `addEventProcessor` so the change is a no-op when Sentry is absent or the SDK shape differs. | ~45 | All original lines (DSN-gated runtime delegation) intact, identical, in original order. |

## Behaviour-vs-doc gaps surfaced for owning slices

> **2026-05-12 reconciliation:** Slice J reviewed each of the 9 gaps below
> against `main` and confirmed 7 are now live. The 2 remaining items
> (granular billing permissions split, scheduled webhook-payload retention
> worker) are explicit v1.1 follow-ups, not v1 launch blockers.
> See [`J-final-integration.md`](./J-final-integration.md) for the per-gap
> resolution table with file/line evidence.

The following claims in `docs/BILLING.md` and the runbook depend on
behaviours that may not yet exist in code. Each is flagged here for the
owning slice; doc text already calls them out as planned / pending.

1. **`Refunds` / `Disputes` tables** — referenced by RBAC matrix, ER diagram,
   state machines (§4.4, §4.5), and runbook scenarios 3.2 / 3.5. Owned by
   slice B (`Services/Billing/RefundService.cs`, `DisputeService.cs`, new
   migration `*_AddRefundDispute*.cs`). Until those land, the runbook
   queries against `"Refunds"` / `"Disputes"` will fail; runbook explicitly
   says "table name when slice B lands; see I-docs.md".
2. **Wallet ledger invariants I7–I9** — assume `Wallet.CreditBalance` is a
   cached projection of `WalletTransaction.Amount`. Slice A must confirm
   the cached column is rebuildable and the write path takes a row-level
   lock or uses optimistic concurrency. If not, invariant **I8** is
   currently unsatisfied.
3. **Coupon over-redemption guard (I6)** — current schema enforces no
   transactional limit; the field `RedemptionCount` is incremented but
   there is no `SELECT … FOR UPDATE` shown in code. Slice C must confirm
   or add the concurrency guard.
4. **Quote immutability (I1, I3, I11)** — `BillingQuote.SnapshotJson` and
   version FKs are present, but no DB-level CHECK / trigger prevents an
   admin tool from mutating the row. Slice D should add an EF Core
   interceptor or a DB trigger to enforce immutability post-create.
5. **Webhook payload integrity (I5)** — `PaymentWebhookEvent.PayloadSha256`
   exists, but no test confirms the hash is recomputed on retry and
   diff-rejected. Slice B / H to add the test.
6. **Webhook dead-letter** — runbook §3.1 references a "5+ failures →
   `ProcessingStatus = 'failed'`" policy and an admin "webhook backlog"
   view. Confirm both exist; if the threshold is different in code, update
   `docs/BILLING.md` §6.2 to match implementation.
7. **Granular billing permissions** — RBAC matrix in `docs/BILLING.md` §7
   notes that today only `billing:read` and `billing:write` exist. Slice C
   may want `billing:catalog_publish` and `billing:refund` as additions;
   `AdminPermissions.All` and the seed roles in
   `Endpoints/AdminEndpoints.cs` would need updating.
8. **`Subscription` lacks paused / past-due states in current enum** —
   state machine §4.1 lists `Pending / Active / PastDue / Paused / Cancelled / Failed`.
   Confirm `SubscriptionStatus` enum covers all values; slice D to add any
   missing ones via additive migration.
9. **PII retention 180-day webhook payload nulling** — runbook §8 of
   `docs/BILLING.md` claims `PaymentWebhookEvent.PayloadJson` is nulled
   after 180 days. No retention worker exists today. Slice B should
   schedule a `PaymentWebhookRetentionWorker` analogous to the
   `PronunciationAudioRetentionWorker` pattern.

These gaps are **doc-truth** (what the system should guarantee), and are
deliberately included so the owning slices have a checklist. Where a
behaviour is absent, the doc text already labels it as "planned" or names
the owning slice.

## Observability change rationale

The Sentry augmentation deliberately does **not**:

- Tag every event globally as billing (would corrupt Sentry filtering).
- Mutate `Sentry.configureScope` synchronously at module load (would race
  with the lazy DSN-gated init in `sentry.server.config.ts`).
- Add a hard dependency on a specific `@sentry/nextjs` version.

It **does**:

- Run after the runtime-specific config has imported, so the SDK is
  initialised (or proven absent) before we attach the processor.
- Use `addEventProcessor` (top-level API; available in `@sentry/nextjs`
  v8+) and feature-detect its presence.
- Match learner billing (`/billing/**`), admin billing
  (`/admin/billing/**`), wallet (`/wallet/**`), payments (`/payments/**`),
  and provider webhook routes (`/webhooks/stripe`, `/webhooks/paypal`).
- Preserve any pre-existing `event.tags.feature` value (no overwrite).

## Validation

| Check | Command | Result |
| ----- | ------- | ------ |
| Type-check `instrumentation.ts` | `get_errors` on the file | 0 errors |
| Doc files render valid markdown / mermaid | Visual review of fenced blocks | All `mermaid` blocks compile-shaped (flowchart, erDiagram, stateDiagram-v2). |
| No service / endpoint / entity files modified | `get_changed_files` (manual review of edits in this slice) | Only the three docs and `instrumentation.ts` changed by this slice. |
| Backend / unit tests | Not run — this slice changes only docs and a single instrumentation file with no business logic. Slice H owns billing tests. | n/a |

## Summary (6 lines)

- Created `docs/BILLING.md` (~1850 words): architecture, ER, state machines, invariants, provider boundary, RBAC, PII / retention.
- Created `docs/runbooks/billing-incident.md` (~1150 words): triage queries, six scenarios, migration rollback, escalation.
- Created `docs/billing-hardening/I-docs.md` (this report, ~600 words): change log + 9 cross-slice gaps.
- Augmented `instrumentation.ts` (+~45 lines, 0 lines removed): adds `feature: 'billing'` Sentry tag via `addEventProcessor`, feature-detected and try/catch-guarded.
- No service / endpoint / entity files touched; all behaviour gaps surfaced for owning slices A–E + H.
- Total documentation produced this slice: ~3600 words across three files; instrumentation change is opt-in via DSN.
