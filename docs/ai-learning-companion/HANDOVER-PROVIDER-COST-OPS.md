# §15 Handover — Provider Routing, Cost Controls and Operations

Companion to `HANDOVER-ENV-BACKUP.md` (topology, secrets, backup/restore) and
`HANDOVER-TEST-SETS.md` (test assets). This document covers the configuration and
operational levers a new owner needs to run Sami, and **points at** the existing ops
runbooks rather than duplicating them.

> **Verify claims against source.** During the 2026-10-08 gate work, two
> remediations described in this documentation set were found never to have been
> implemented (the OpenCode length-retry ladder, and a "resolved" Pack 3 attachment
> defect that was actually a lost test harness). Treat prose here as a map, and the
> named file as the authority.

## 1. Provider routing (configuration, not code)

Model/provider choice is configuration. Swapping providers must not require rebuilding
product workflows, and it does not: every AI call routes through the coordinator.

| Layer | Where it lives | Notes |
| --- | --- | --- |
| Learner chat default route | `AiProviderRoutes` row for `ai_assistant.learner` | Owner decision D-005: `opencode` + `deepseek-v4.1-flash`, `reasoning_effort=max` |
| Provider rows + encrypted keys | `AiProviders` table, edited at `/admin/ai-providers` | Keys are encrypted with `DataProtection` purpose `AiProvider.PlatformKey.v1`, never env vars |
| Model allow-list | `OpenCodeProviderDefaults.CuratedChatModels` | A request for a model outside the allow-list is refused, not silently substituted |
| Model picker catalogue | `Services/AiAssistant/AssistantModelCatalog.cs` | Learner-visible list |

**Route switching is gated.** `AiProviderRouteApprovalService` refuses to move a feature
off Claude without a recorded passing benchmark. The benchmark is executed and recorded
via the admin record/list/compare endpoints — never hand-entered (`AiRouteBenchmarkRunner`
computes the metrics from real gateway calls). Non-scoring bar: schema_validity ≥ 98%,
evidence_grounding ≥ 95%, fabricated_source_claims = 0, cost_reduction ≥ 30%.

> **Known gap in the gate.** The benchmark measures schema validity, grounding,
> fabrication and cost — it does **not** measure whether a turn actually completes.
> That omission is why `reasoning_effort=max` shipped into a configuration whose hardest
> turns truncated their own output (D-SAMI-003). Adding a task-completion metric to the
> gate is an open recommendation, not yet implemented.

## 2. Output budget and the length-retry ladder

`RegistryBackedProvider` (`Services/Rulebook/AiProviderRegistry.cs`) enforces a minimum
output budget for OpenCode because `effort=max` reasoning can consume the whole budget
before the first answer token:

- Floor: `OpenCodeMinMaxTokens` = **16,384**
- Ladder on `finish_reason=length`: **16,384 → 32,768 → 65,536** (`OpenCodeLengthRetryCeilings`)
- Hard ceiling: `OpenCodeMaxMaxTokens` = **65,536** — the model's own documented output max
- At most **two** raises, then the honest failure. The retry is OpenCode-only, and the
  classifier (`IsOutputLengthFailure`) matches only our own truncation wording, so a quota,
  auth or rate-limit fault is never retried.

Cost/latency consequence: a truncated turn retries, so its wall time roughly doubles or
triples. Measured on Pack 2, busy-turn latency rose 105 s → ~230 s once the first raise
landed. This is a deliberate trade — a learner waiting is better than a refused turn.

## 3. Kill switches and emergency levers

| Lever | Scope | Effect |
| --- | --- | --- |
| Global kill switch (`AiGlobalPolicy.KillSwitchEnabled` + `KillSwitchScope`) | `AllCalls` blocks every AI call; `PlatformKeysOnly` blocks platform-key calls but still allows BYOK | Stops AI immediately; reason text is surfaced to the learner |
| Per-feature kill list | Blocks one feature code | `feature_disabled.<featureCode>`; leaves other features running |
| `ai_learning_companion` | Master switch for the learner companion surface | Off = the companion surface reports disabled |
| `companion_retrieval` | Knowledge retrieval | Off = answers without grounded sources rather than failing |
| `companion_actions` | Typed platform actions (open resource, add to plan, checkout) | Off = Sami explains but does not act |
| `companion_credits` | New AI Credit consumption | Off freezes **new** charges; balances are preserved |
| `companion_score_display` | Numeric Writing/Speaking band estimates | **Must stay OFF** until approved calibration exists (TV-006/TV-007) |

Flags live in the `FeatureFlags` table and are editable at `/admin/flags` without a deploy.
They **fail closed**: an unreadable flag, a missing row or an unreachable database resolve
to `false` (`CompanionFeatureFlags.IsFlagOnAsync`). That is correct for a surface reaching a
learner's history, entitlements and credit balance.

**Sami chat is not metered.** `AiAssistantGateway` skips the token-quota reserve for
`AiFeatureCodes.AiAssistantLearner` and `AiFeatureCodes.CompanionChat`
(`IsIncludedCompanionChat`), per SAMI §1.2/§9 and owner decision D-008. Cost control is not
removed: the kill switches above, per-user rate limiting and full `AiUsageRecord` telemetry
all still apply. Credit **debiting** (`ShouldDebitAiCredit`) is unchanged and still covers
only the separate assessment products.

## 4. Admin role and permission matrix

Admin endpoints are authorised by policy, not by UI hiding — check the policy on the
endpoint group before granting a role.

| Surface | Policy | Notes |
| --- | --- | --- |
| `/v1/admin/companion/access` | `AdminAiConfig` | Plan-level Sami access + per-user enable/disable with provenance |
| `/v1/admin/companion/knowledge` | `AdminAiConfig` | Knowledge corpus administration |
| `/v1/admin/ai-providers` | admin AI config | Provider rows, encrypted keys, model allow-lists, connection tests |
| `/v1/admin/flags` | admin | Kill switches and rollout flags |
| Admin dashboards | `app/admin/**` | Role-gated in `useAdminAuth`; learner requests to these routes are refused server-side |

Per-user Sami access (`CompanionAccessResolver`) is the **single** access decision, used by
the session endpoint, the orchestrator turn gate and the admin view, so there is no second
rule set to drift. Provenance values: `package_included`, `admin_enabled`, `promotional`,
`manually_disabled`, `expired`, `none`. Every write records an `AuditEvent`
(`CompanionUserAccessUpdated` / `CompanionUserAccessCleared`).

## 5. Observability

| Signal | Where | Notes |
| --- | --- | --- |
| Per-request usage | `AiUsageRecords` | One row per physical provider call: feature code, provider, model, tokens, latency, outcome, `ErrorCode`, sanitised `ErrorMessage`, policy trace |
| Turn-level failures | `AiUsageRecords` where `ErrorCode='provider_error'` | The learner sees one generic busy message; the specific class is only here — query this table, not the logs, to diagnose |
| Latency / health | `/health/live`, `/health/ready` | `ready` reports `database`, `migrations`, `stuck_jobs`, `storage` |
| Leak/security events | `AiAssistantOrchestrator` `LogError` + the withheld message body | A blocked answer is replaced in storage and the turn ends `OUTPUT_WITHHELD` |
| Entitlement changes | `AuditEvent` | Access toggles, plan changes |
| Credit ledger | Immutable ledger rows | Failed paid actions must be reversible; see `docs/ai-learning-companion/MONETIZATION_CREDITS_BILLING.md` |

Existing runbooks to use rather than reinvent: `docs/ops/incident-response-runbook.md`,
`docs/security/runbook-incident-response.md`, `docs/security/runbook-backup-restore.md`,
`docs/runbooks/billing-incident.md`, `docs/PROD-SMOKE-RUNBOOK.md`.

## 6. Rollback

| What | How |
| --- | --- |
| Application release | `gh workflow run production-deploy.yml -f sha=<previous-sha>` — images are already in GHCR, no rebuild |
| Database migration | Migrations are additive; `Down()` exists where a revert is safe. Production migration SQL is applied by the pipeline, not by hand |
| Knowledge release | Versioned releases with a changelog; roll back by republishing the prior release (no application rollback needed) |
| Prompt / config | Feature flags flip without a deploy; provider routing reverts via the route row |
| Provider failure | Failover is configured per route; `AiCircuitBreakerStore` opens circuits for failing providers — the Claude Max route is deliberately exempt (`IsAlwaysOn`) and must stay that way |

## 7. What this handover deliberately does NOT claim

- The 80 UAT scenarios are **not** all executed. See `SAMI-RUN-STATE.md` for exactly which
  packs ran on which build, and which six scenarios require an owner-run physical capture.
- The F-001..F-184 register is reconciled separately — see
  `traceability/FEATURE_TRACEABILITY_MATRIX.md` and `REGISTER-RECONCILIATION.md`.
- No test, lint or typecheck result is claimed anywhere: per repo policy there is no CI QA,
  and correctness is evidenced by compilation in `Build images`, the post-deploy health and
  serving-image proof, and the owner's own manual QA.
