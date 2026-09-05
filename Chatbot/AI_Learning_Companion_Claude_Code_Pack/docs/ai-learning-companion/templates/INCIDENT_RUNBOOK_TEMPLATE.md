# Incident Runbook — AI Learning Companion

## Scope

Covers AI/model/provider failures, RAG/source-release regressions, entitlement leakage, credit-ledger/billing failures, voice failures, privacy/security incidents and severe quality regressions.

## Severity model

Numeric acknowledgement/resolution targets remain `TO VERIFY` until operationally approved.

| Severity | Example | Initial action | Named owner |
|---|---|---|---|
| Critical | entitlement/proprietary leak, credit corruption, serious privacy/security event | contain + kill switch + preserve evidence | TO VERIFY |
| High | widespread AI failure, wrong knowledge release, payment/voice failure | degrade/rollback + customer communication | TO VERIFY |
| Medium | localized quality or latency regression | mitigate + monitor | TO VERIFY |

## Available emergency controls

- disable/cap live voice without disabling text tutoring;
- freeze new AI Credit consumption while preserving balances;
- suspend a tier/action/provider route/heavy workload;
- roll back prompt/model/config or knowledge release;
- block affected entitlement/content source;
- restore credits for failed chargeable actions.

## Response checklist

1. Detect and assign severity.
2. Preserve request traces, ledger evidence, source/config/model versions and customer impact.
3. Activate the smallest safe kill switch.
4. Prevent new financial/content/privacy damage.
5. Communicate status through the approved channel.
6. Restore service from a known-good version.
7. Reconcile and restore credits where technical failures occurred.
8. Re-enable only with explicit approval and recovery criteria met.
9. Complete a post-incident review and feed corrective actions into tests/risk/evaluation registers.
