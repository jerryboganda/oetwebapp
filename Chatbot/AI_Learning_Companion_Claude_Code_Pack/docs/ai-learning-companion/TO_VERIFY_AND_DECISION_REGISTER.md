# TO VERIFY and Product Decision Register

`TO VERIFY` means **do not guess**. Claude Code should build configuration, schemas, feature flags, measurement hooks and safe defaults, then keep production behavior gated until the named decision is approved.

| ID | Decision / unknown | Engineering preparation | Production gate / owner |
|---|---|---|---|
| TV-001 | Complete content inventory counts/hours/owners | Asset register + ingestion manifest | Content Ops + Dr Hesham |
| TV-002 | First OET profession for beta | Profession-scoped rollout flag | Product + Content Ops |
| TV-003 | Live beta cohort size | Cohort flag/telemetry | Product/Growth/AI QA |
| TV-004 | Retrieval recall/precision/citation thresholds | Evaluation runner | AI QA |
| TV-005 | Hallucination/quality thresholds | Sampling/eval dashboard | AI QA; critical entitlement/payment fabrication zero tolerance |
| TV-006 | Writing numeric score calibration | Feature flag + criterion-feedback fallback | Pedagogy/AI QA |
| TV-007 | Speaking numeric score calibration | Feature flag + criterion-feedback fallback | Pedagogy/AI QA |
| TV-008 | Egyptian Arabic ASR threshold | Voice spike suite | Product/Pedagogy |
| TV-009 | Arabic-English code-switch threshold | Voice/code-switch set | Product/Pedagogy |
| TV-010 | Live voice P50/P95 latency target | Latency tracing | Product/AI Engineering |
| TV-011 | Pronunciation-feedback scope/threshold | Granular pronunciation feature gate | Pedagogy |
| TV-012 | Actual fully loaded voice cost/minute | Cost trace | Finance/Product |
| TV-013 | Voice-to-credit conversion | Configurable credit tariff | Finance/Product after spike |
| TV-014 | Deep cross-subtest credit charge | Action price config | Finance/Product |
| TV-015 | Large PDF/extended audio credit charge | Size/duration tariff config | Finance/Product |
| TV-016 | Target fully loaded cost per AI Credit | Cost aggregation | Finance/Product |
| TV-017 | Non-credit usage reserve by tier | Tier cost policy | Finance/Product |
| TV-018 | Minimum net-revenue-per-credit multiple | Channel-floor validator | Finance/Product |
| TV-019 | Final included AI Credit wallets | Effective-dated grant config | Finance/Product after beta |
| TV-020 | Actual payment processor/fees | Provider/fee config | Finance |
| TV-021 | Apple/Google program enrollment/commission | Channel-fee config | Product/Finance/Legal |
| TV-022 | VAT/digital-service tax/entity treatment | Tax/provider integration | Accountant/Legal |
| TV-023 | Regional PPP discounts/country eligibility | Region pricebook/fraud controls | Growth/Finance |
| TV-024 | Local payment rails/fees | Payment adapter capability | Finance/Product |
| TV-025 | Partially consumed credit refund/chargeback treatment | Reversal primitives/state machine | Legal/Finance |
| TV-026 | UK subscription-contract commencement/entity scope | reminder/cancel/refund primitives | Legal |
| TV-027 | GDPR/DPIA applicability/lawful-basis map | Data inventory/privacy controls | Privacy/Legal |
| TV-028 | Voice recording retention/raw-audio policy | Configurable storage/retention | Privacy/Product |
| TV-029 | Subprocessor/DPA/vendor-training terms | Vendor register | Privacy/Legal |
| TV-030 | Jana/Sami trademark/domain/social/app-store clearance | Persona config | Legal/Brand |
| TV-031 | OET trademark/material licensing/disclaimer | Disclaimer/content metadata | Legal/IP |
| TV-032 | Store-specific AI/reporting/payment/steering behavior | Reporting + channel config | Product/Legal at release |
| TV-033 | Availability SLO | Uptime/latency monitoring | Engineering/Product |
| TV-034 | Incident severity/acknowledgement targets | Incident schema/runbook | Operations |
| TV-035 | Named on-call/delegated kill-switch owner | Role config/audit | Founder/Product |
| TV-036 | Learner-distress human escalation owner | Escalation route/runbook | Support/Product |
| TV-037 | Conversion/retention/churn targets | Funnel/cohort analytics | Growth |
| TV-038 | Permanent LTV:CAC/payback target | Cohort dashboard | Growth/Finance |
| TV-039 | Support deflection/tutor escalation quotas | Tracking/quota config | Operations |
| TV-040 | Refund/chargeback provision rate | P&L dashboard field | Finance |
| TV-041 | Human review sampling rate | Review queue | AI QA |
| TV-042 | Content maintenance SLA/cost | Queue timestamps/dashboard | Content Ops |
| TV-043 | Deduplicated owned-audience count/channel split behind stated 52k+ | Source/cohort tags | Growth |
| TV-044 | Warm conversion by WhatsApp/Telegram/Facebook/YouTube | Cohort tracking | Growth |
| TV-045 | Developer estimates/headcount/dates/stage budget | Repo gap analysis/WBS | Developer + Founder |
| TV-046 | ~US$4,000 quote coverage | Exact stage/feature coverage statement | Founder + Developer |

## Source-internal staging conflicts / safe resolutions

### DEC-001 — F-114…F-122 absent from Appendix A rows

The main F-001…F-184 inventory includes proactive F-114 through F-122, while Appendix A row extraction jumps from F-113 to F-123. **Do not drop them.** Keep traceability and treat as Stage 3/later proactive engagement pending Product phase confirmation.

### DEC-002 — F-138 Ultimate Stage 1 vs Stage 3 premium promise

Stage 1 can create tier schema/pricebook/disabled capability; do not market/activate full Ultimate continuous mentor until Stage 3 voice/premium/economics gates pass.

### DEC-003 — F-152 source-authority separation Stage 2 vs Stage 1 minimum

Implement baseline official-vs-methodology separation in Stage 1 because Stage 1 minimum scope requires it. Expand full profession/content conflict/version coverage in Stage 2.

### DEC-004 — F-057 Micro-lessons Stage 4-5

Although micro-learning is described earlier, preserve Appendix planning phase unless Product re-prioritizes it.

### DEC-005 — WCAG later feature vs product-wide target

Use accessible architecture/components from Stage 1 and complete formal WCAG 2.2 AA coverage/audit according to rollout. Do not intentionally create inaccessible core debt.

## Decision log template

```text
Decision ID:
Date:
Owner/approver:
Evidence:
Decision:
Effective environment/date:
Affected F-IDs/config:
Migration/rollout impact:
Tests/evaluation required:
Rollback/change procedure:
```
