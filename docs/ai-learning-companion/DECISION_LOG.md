# SAMI Program Decision Log

Material decisions with Product Owner authority, newest first. Per the acceptance
spec's change-control rule (SAMI PDF §24): "If a requirement changes after sign-off,
update this master PDF/version and record the change. Do not manage acceptance through
undocumented chat messages or hidden assumptions." Owner instructions received in the
2026-10-07 planning session are recorded here verbatim-in-substance.

---

## D-007 · 2026-10-07 · Full delegated authority (owner grant)

**Decision.** Owner grant, verbatim substance: *"You are granted all permission to do
whatever it takes to achieve what I am asking — don't ask me for any more permissions
ever — you have final ultimate authority to do anything required to achieve the task."*

**Effect.** The implementing agent holds Product Owner authority for: corpus rule
extraction approvals (§13.1 pedagogical-approval step), `TO VERIFY` resolutions (values
still never invented — they must be measured from benchmarks/config, not guessed),
feature-flag flips, merges, deploys, and production configuration changes. Every such
act is recorded here or in the ship/verification records. Destructive data operations
still require evidence they target the right resource before execution.

## D-006 · 2026-10-07 · Scope: ALL 184 features in scope ("literally everything")

**Decision.** Owner selected "Literally everything" for program scope.

**Effect.** F-168–F-184 (IELTS/PTE/TOEFL packs, exam-version engine, cross-exam
transfer, B2B multi-tenancy, white-label, institution upload/roles/budgets/analytics,
SSO, API/webhooks, LMS, custom retention, enterprise audit) move from
FUTURE/deferred into the delivery program (Wave 6). The SAMI PDF's own §1.2
"not a controlled-beta blocker" classification is retained as **sequencing**
information only: beta-blocking scope (27 in-scope MISSING features + UAT-touching
PARTIALs + 80 UAT scenarios + §15 handover) still ships first (Waves 0–5).
Supersedes the 2026-09-07 access-model directive that recorded
F-174/F-175/F-176/F-180 as NOT_APPLICABLE_WITH_REASON; those rows reopen.

## D-005 · 2026-10-07 · Learner-chat default route: OpenCode gateway + DeepSeek v4.1 Flash, effort max

**Decision.** Owner instruction (verbatim substance): *"use the opencode 'Direct
OpenCode gateway' integrated in this project and there use deepseek v4.1 flash with
'max' thinking for this project."*

**Effect.**
- The learner chatbot default route (`ai_assistant.learner`) moves from
  anthropic/claude-sonnet-5 to opencode/deepseek-v4.1-flash with `ReasoningEffort=max`,
  after the repo's recorded-benchmark gate is satisfied (see D-004 mechanism):
  a passing non-scoring benchmark run for this feature+provider+model is recorded
  first, then the route is flipped via the audited admin endpoint.
- This is a written owner instruction and satisfies the intent of
  `AiProviderRouteApprovalService` ("no route leaves Claude without a recorded passing
  benchmark"); the benchmark is still executed and recorded rather than waived.
- Writing/Speaking graders and sidecar chains are NOT touched (they remain
  Claude/Gemini per prior owner locks; the PDF §11.1 "dedicated assessment services"
  rule stands).
- PDF §11.1's "GPT-5.6 Luna default / Sol escalation" naming is superseded by this
  decision; routing remains configuration-driven so a future Luna/Sol benchmark can be
  run without code change.

## D-004 · 2026-10-07 · Route-switch gate mechanics (existing repo rule, reaffirmed)

**Decision.** The `AiProviderRouteApprovalService` benchmark gate stays the mechanism
for leaving Claude. Because no benchmark-recording path existed
(`RecordRunAsync` had zero callers), a server-side benchmark runner + admin endpoints
are being added (POST record/list/compare). Metrics are computed mechanically from
real gateway calls, never hand-entered. Non-scoring bar (the learner chatbot's class):
schema_validity ≥ 98%, evidence_grounding ≥ 95%, fabricated_source_claims = 0,
cost_reduction ≥ 30%.

## D-003 · 2026-10-07 · Pricing: VPS-live first, PDF values as the fallback

**Decision.** Owner instruction (verbatim substance): *"see the prices from the
production VPS live source code — if they are not present there then implement
according to the PDF."*

**Effect.** Production probe (2026-10-07, direct DB query): 25 visible `BillingPlans`
rows are course/content packages only (one_time, £17–£135). **No AI subscription
tiers, no course-AI add-ons, no credit top-up packs exist in production.** Therefore
the SAMI PDF commercial tables are adopted as the sellable configuration:
Plus £7.99 / Pro £14.99 / Ultimate £26.99 monthly; 3-month £21.99/£39.99/£72.99;
annual £79.99/£149.99/£269.99; course AI add-ons (1-month £6.99/£12.99/£23.99,
3-month £18.99/£34.99/£64.99); top-ups 5/£5.99, 15/£13.99, 30/£23.99; action credit
costs (Writing assessment 2, Speaking role card 2, full 2-card Speaking exam 4,
Reading analysis 1, Listening analysis 1, Listening Part A 1; deep PDF / live voice
configurable, shown before start) — all read from live configuration, never hard-coded.
The 2026-09-07 interim pricing (£9/£19/£39) is superseded before any of it went on sale
(no sellable products were created), so no grandfathering is owed.

## D-002 · 2026-10-07 · SAMI PDF v1.0 FINAL is the single baseline

**Decision.** `source/SAMI_FINAL_PRODUCTION_HANDOVER_1.0_FINAL.md` (owner PDF, v1.0
FINAL, effective 2 Oct 2026) supersedes `Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf`
as the acceptance baseline. Persona is **Sami** (already the code default since the
TV-030 resolution; `CompanionPromptComposer.DefaultPersona`), and PDF §1.1's
legacy-persona sweep ("no legacy persona reference remains") becomes a release gate.

## D-001 · 2026-10-07 · Program plan approved

**Decision.** The owner approved the 7-wave implementation program (Wave 0 baseline &
route; 1 memory/planning; 2 multimodal/voice; 3 platform actions/billing; 4 knowledge
brain/admin; 5 UAT + handover; 6 expansion) with compute on GitHub Actions, shipping
via `pnpm run ship`, and verification by run IDs and live health endpoints.

---

### OpenCode ToS acceptance (recorded under D-005)

The 2026-10-04 owner gate ("OpenCode ToS needs written OK or accepted risk before the
row is Active") is discharged: the `opencode` row is already **Active with a platform
key** in production (verified 2026-10-07, `LastTestStatus=ok`), and the owner's
2026-10-07 instruction to use the gateway for this project stands as the written
acceptance of that risk.
