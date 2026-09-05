# Claude Code Execution Checklist

## Before code
- [ ] Pack copied to repository root.
- [ ] Existing repo instructions read.
- [ ] `ai-companion-audit` completed.
- [ ] F-001…F-184 mapped to current repo.
- [ ] Duplicate user/entitlement/credit/payment truths explicitly avoided.
- [ ] TO VERIFY gates identified.
- [ ] Stage 0/1 scope fits approved budget/time.

## Every vertical slice
- [ ] F-ID(s) referenced.
- [ ] DB/domain contract handled.
- [ ] Backend authorization/entitlement complete.
- [ ] Frontend UX/state complete.
- [ ] Analytics/cost trace complete.
- [ ] Error/failure behavior complete.
- [ ] Accessibility/RTL considered.
- [ ] Unit/integration/E2E tests added.
- [ ] Migration/backfill/rollback documented.
- [ ] Feature flag used when risky.
- [ ] Traceability/status updated.

## AI/RAG changes
- [ ] Approved source/version only.
- [ ] Entitlement prefilter.
- [ ] Correct authority/profession/version.
- [ ] Citation/location.
- [ ] Unknown-answer behavior.
- [ ] Exfiltration/injection tests.
- [ ] Cache isolation.
- [ ] Cost/latency trace.
- [ ] Golden regression.

## Credit/payment changes
- [ ] Exact charge shown.
- [ ] Explicit confirmation.
- [ ] Idempotent/atomic.
- [ ] Concurrent retry tested.
- [ ] Technical failure no-charge/restore.
- [ ] Provenance/audit.
- [ ] Refund/reversal path.
- [ ] Webhook replay/out-of-order test.

## Release
- [ ] Traceability validator passes.
- [ ] Relevant golden/regression passes.
- [ ] No critical entitlement leak.
- [ ] No critical payment/ledger defect.
- [ ] Security red team passed/accepted.
- [ ] Accessibility core flows checked.
- [ ] Latency/cost telemetry live.
- [ ] Rollback/kill-switch tested.
- [ ] Unresolved TO VERIFY items safely gated.
- [ ] Production readiness report generated.
