# AI Usage Policy & Options Reference

> **Status:** authoritative. This document is the single source of truth for
> every configurable policy in the AI Usage Management subsystem. The code
> references these options by name; admin UI surfaces them using the same
> vocabulary. Defaults are picked to be **safe, legally defensible, and
> commercially sensible** for an OET exam-grading product. Alternatives are
> listed so the platform can be re-tuned without a code change.

---

## 0. Design principles

1. **Grounding is non-negotiable.** Every AI call routes through
   `AiGatewayService` (.NET) / `buildAiGroundedPrompt()` (TS). The gateway
   physically refuses ungrounded prompts. No policy below may weaken this.
2. **Scoring integrity over convenience.** Any feature whose output materially
   affects a learner's OET score prediction is treated as *scoring-critical*
   and is protected against credential-source drift by default.
3. **Every default is overridable by admins**, never by learners. Learners
   can only toggle preferences the admin allows.
4. **No option breaks the audit trail.** Every AI call is recorded regardless
   of credential source, provider, outcome, or feature.

---

## 1. Credential-source options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AiCredentialMode` (per user) | Which source the resolver should prefer | `auto` | `byok-only`, `platform-only`, `auto` |
| `AllowPlatformFallback` (per user) | If BYOK errors, may we transparently use platform credits? | `true` | `false` |
| `AllowByokOnScoringFeatures` (global) | Admin switch: allow BYOK on score-affecting calls at all | `false` | `true` |
| `AllowByokOnNonScoringFeatures` (global) | Admin switch: allow BYOK on practice/conversation/summarisation | `true` | `false` |
| `DefaultPlatformProviderId` (global) | Provider used when no BYOK applies | `digitalocean-serverless` | any registered `AiProvider` |

**Why `auto` + `AllowPlatformFallback=true` by default:** it gives learners the
cost benefit of their own key when it works and a clean experience when it
doesn't. Silent failure is worse than a small banner saying we fell back.

**Why `AllowByokOnScoringFeatures=false` by default:** a learner paying for a
score prediction trusts our grading. If their key silently routes through a
smaller model they self-selected, we still own the UX failure. Keep scoring
homogeneous.

---

## 2. Quota unit options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `QuotaUnit` (global) | What is metered | `tokens` | `requests`, `usd_estimate` |
| `TokensPerCreditDisplay` (global) | How many raw tokens equal one learner-visible "AI credit" | `1000` | any integer ≥ 100 |
| `CreditRoundingMode` (global) | Rounding for learner display | `ceil` | `floor`, `nearest` |

**Why tokens + 1k display unit:** tokens are the only unit that all providers
report consistently. Showing "3,412,771 tokens" to a learner is hostile;
showing "3,413 credits" is not.

---

## 3. Period & reset-policy options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `QuotaPeriod` (per plan) | Length of the billing window | `monthly` | `daily`, `weekly`, `rolling_30d`, `never_expire` |
| `QuotaResetAlignment` (per plan) | When the period ticks over | `calendar_month` | `subscription_anniversary`, `utc_midnight` |
| `RolloverPolicy` (per plan) | What happens to unused credits at reset | `expire` | `rollover_capped`, `rollover_full` |
| `RolloverCapPct` (per plan) | If `rollover_capped`, max carry-over as % of plan cap | `20` | 0–100 |
| `DailySafetyCapPct` (per plan) | Hard daily ceiling as % of monthly cap, to stop abuse | `25` | 0–100 (0 disables) |

**Why calendar-month + expire + 25% daily cap:** simple to explain, protects
the platform from a single learner burning a month's budget in a day, avoids
the engineering complexity of rolling windows unless you need them.

**Rolling-30d** is the most "fair" option but requires window-aware counters
rather than period counters. Available when you're ready for that complexity.

---

## 4. Overage policy options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `OveragePolicy` (per plan) | What happens when quota is exhausted | `deny` | `allow_with_charge`, `auto_upgrade`, `degrade_to_smaller_model` |
| `DenyMessage` (per plan) | Copy shown to learner on `deny` | localised default | any string |
| `OverageRatePerCredit` (per plan) | Price per credit when `allow_with_charge` | `null` | decimal ≥ 0 |
| `AutoUpgradeTargetPlan` (per plan) | Which plan to auto-bump to | `null` | any higher plan code |
| `DegradeModel` (per plan) | Which cheaper model to use on degrade | `null` | any allow-listed model |

**Why `deny` by default:** it's the only overage policy that can't surprise a
learner with a bill. `allow_with_charge` and `auto_upgrade` are valuable but
require the billing UX to show the consent flow. Ship `deny` first, enable
others per plan when billing consent is wired.

---

## 5. Feature-eligibility matrix (admin-editable)

Every feature the gateway serves is classified. Defaults:

| Feature code | Scoring-critical | BYOK default | Platform default | Notes |
|---|---|---|---|---|
| `writing.grade` | ✅ | ❌ | ✅ | Mock / practice grading |
| `writing.sample_score` | ✅ | ❌ | ✅ | Sample scoring |
| `speaking.grade` | ✅ | ❌ | ✅ | Speaking evaluation |
| `mock.full_grade` | ✅ | ❌ | ✅ | Full mock exam grading |
| `writing.coach.suggest` | ❌ | ✅ | ✅ | Inline suggestions |
| `writing.coach.explain` | ❌ | ✅ | ✅ | Why-is-this-wrong explanations |
| `conversation.reply` | ❌ | ✅ | ✅ | Practice conversation |
| `pronunciation.tip` | ❌ | ✅ | ✅ | Pronunciation feedback |
| `summarise.passage` | ❌ | ✅ | ✅ | Study-notes summarisation |
| `vocabulary.gloss` | ❌ | ✅ | ✅ | Word gloss |
| `admin.content_generation` | ❌ | ❌ | ✅ | Admin tooling, platform only |

Admin can toggle the BYOK column per feature. `AllowByokOnScoringFeatures`
global switch gates the scoring-critical rows.

---

## 6. Failure & fallback options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `ByokErrorCooldownHours` (global) | After a 401/403 on a user key, how long before we retry it | `24` | any integer hours |
| `ByokTransientRetryCount` (global) | How many 429/5xx retries before falling through | `2` | 0–5 |
| `ProviderRetryPolicy` (per provider) | Polly policy preset | `exponential_2_30s` | `none`, `linear_3_10s`, `aggressive_5_60s` |
| `ProviderCircuitBreakerThreshold` (per provider) | Failures before breaker trips | `5` | any integer |
| `ProviderCircuitBreakerWindowSeconds` (per provider) | Rolling window for the counter | `30` | any integer |
| `FailoverOrder` (global) | Ordered list of provider IDs to try on circuit-breaker open | `[digitalocean-serverless]` | any subset of registered providers |

---

## 7. Global safety controls

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AiGlobalKillSwitch` | Hard-disable all AI calls platform-wide | `false` | `true` |
| `AiGlobalBudgetUsd` | Hard monthly USD cap across all platform-keyed calls | admin-supplied, no default | any decimal ≥ 0 |
| `AiGlobalBudgetSoftWarnPct` | Email admins at this % of budget | `80` | 0–100 |
| `AiGlobalBudgetHardKillPct` | Auto-engage kill switch at this % | `100` | 0–150 |
| `AiAnomalyDetectionEnabled` | Flag users whose daily spend ≥ `AnomalyMultiplier` × their 7-day median | `true` | `false` |
| `AnomalyMultiplierX` | Multiplier that triggers flagging | `10` | any decimal ≥ 2 |

Kill-switch semantics: when engaged, platform-keyed calls throw
`AiGloballyDisabledException`. **BYOK calls continue** — the switch protects
the platform budget, not learner sovereignty over their own key.

---

## 8. Custody & security options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `KeyEncryptionStrategy` | At-rest encryption for stored credentials | `aspnet_data_protection` | `aws_kms`, `azure_key_vault`, `hashicorp_vault` |
| `KeyRingPersistencePath` | Filesystem path for Data Protection key ring | `/var/lib/oet/keyring` | any writable path or blob URL |
| `RequireStepUpMfaForCredentialChange` | Force re-auth to add/rotate/revoke keys | `true` | `false` |
| `ValidateOnSave` | Ping provider before storing the key | `true` | `false` |
| `StoreResponseBodies` | Persist full AI response text in audit records | `false` | `true` (requires consent gate) |
| `ResponseSamplingPct` | If stored, % of responses sampled for QA | `0` | 0–100 |
| `CredentialValidateRateLimitPerMinute` | Per-IP limit on the validate endpoint | `5` | 1–60 |

**Why `StoreResponseBodies=false` by default:** audit completeness is served
by hashes + metadata + grounding version. Storing full bodies creates a
cross-user data-leakage surface, an unnecessary retention obligation, and a
larger PII footprint. Enable only with sampling + consent wiring.

---

## 9. Observability & alerting

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `UsageAggregationFrequency` | How often aggregation views refresh | `hourly` | `realtime`, `daily` |
| `AlertEmailRecipients` | Who receives budget/anomaly emails | admins with `SystemAdmin` | explicit list |
| `AlertChannels` | Where alerts go | `[email]` | `email`, `webhook`, `sms`, `slack` |
| `RetainUsageRecordsDays` | How long raw `AiUsageRecord` rows are kept | `395` | 30–3650 |
| `RetainAggregatesDays` | How long aggregate rows are kept | `3650` | 365–∞ |

**Why 395 days of raw retention:** covers a full annual reporting cycle plus
30 days of reconciliation. Long enough for finance, short enough for privacy.

---

## 10. Learner-visible presentation

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `ShowBYOKOption` (global) | Is BYOK visible in `/settings/ai`? | `true` | `false` (disables feature entirely) |
| `ShowExactTokenCounts` | Show raw tokens in learner UI | `false` | `true` |
| `ShowFallbackBanner` | Show banner when platform fallback engages | `true` | `false` (silent) |
| `CreditGaugeStyle` | Dashboard widget rendering | `dual_ring` | `single_bar`, `numeric_only` |
| `LowCreditWarningPct` | At what % remaining to warn the learner | `15` | 0–50 |

---

## 11. Option application order (decision log)

When two options conflict, precedence is:

1. `AiGlobalKillSwitch=true` → refuses everything (even BYOK for admin-flagged
   platform-maintenance cases; kill switch has a `scope` enum:
   `platform_keys_only` (default) or `all_calls`).
2. Feature classification → scoring-critical with
   `AllowByokOnScoringFeatures=false` → platform-only, no BYOK.
3. User `AiCredentialMode` → honoured within global policy bounds.
4. `AllowPlatformFallback` → honoured when upstream BYOK fails.
5. Quota state → `OveragePolicy` determines behaviour once exhausted.

Every decision is logged with its decisive rule in the `AiUsageRecord.policyTrace`
field so admin explorer can answer "why did this call use that credential?"

---

## 12. Non-configurable invariants (for clarity)

These are NOT options. Changing them requires a code change and a new major
version of this document:

- Grounding enforcement in `AiGatewayService` (refuses ungrounded prompts).
- Rulebook version header is always stamped on every call's audit row.
- Keys are never returned to the client after save.
- Every AI call produces exactly one `AiUsageRecord` row, regardless of
  outcome (success, provider error, quota denied, kill-switch denied).
- `AiCredentialMode=platform-only` cannot be overridden by a BYOK feature flag.
