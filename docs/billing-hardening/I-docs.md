# Slice I — Docs, Observability, Incident Runbook

> Owner: docs subagent. Read [`README.md`](./README.md) first.

> **Status note (2026-05-13):** this is a historical slice report. The live
> billing reference is [`docs/BILLING.md`](../BILLING.md) and the operational
> runbook is [`docs/runbooks/billing-incident.md`](../runbooks/billing-incident.md).
> Previously planned refund/dispute, granular-permission, and webhook-retention
> items have now landed; the gap list below is preserved as audit history with
> updated closure notes.

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

> **2026-05-13 reconciliation:** later closure work reviewed each of the 9 gaps
> below against `main`; refund/dispute tables, granular billing permissions,
> subscription statuses, webhook dead-letter handling, and scheduled webhook
> payload retention are now represented in code and canonical docs.
> See [`J-final-integration.md`](./J-final-integration.md) for the per-gap
> resolution table with file/line evidence.

The following claims were the original Slice I handoff checklist. Current
status notes are included inline; this section should not be read as the live
launch blocker list.

1. **Refund / dispute tables** — resolved. Code uses `OrderRefunds` and
   `PaymentDisputes`; the canonical runbook now queries those table names.
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
6. **Webhook dead-letter** — resolved. Code promotes exhausted events to
   `ProcessingStatus = 'dead_letter'`; canonical docs now use that status.
7. **Granular billing permissions** — resolved 2026-05-13. The RBAC matrix in
   `docs/BILLING.md` §7 now lists `billing:refund_write`,
   `billing:catalog_write`, and `billing:subscription_write`; legacy
   `billing:write` remains a superset.
8. **Subscription status drift** — resolved in docs. The canonical enum is
   `Trial`, `Pending`, `Active`, `PastDue`, `Suspended`, `Cancelled`, `Expired`;
   `docs/BILLING.md` now mirrors those names instead of historical paused/failed labels.
9. **PII retention 180-day webhook payload nulling** — resolved 2026-05-13.
   `WebhookPiiRetentionWorker` schedules the aged-payload sweep and focused
   tests cover the retention cutoff behavior.

These gaps were **doc-truth** at the time of Slice I. They are deliberately
preserved so the closure trail remains auditable; use the inline status notes
and canonical billing docs for current implementation truth.

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
