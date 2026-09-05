# Release Plan and Dependency-Ordered Work Breakdown

This plan preserves the source Stage 0–5 sequencing. Exact dates/headcount/estimates are generated **after repository audit**.

## Stage 0 — Validation / Architecture Foundations

### S0.1 Repository audit and traceability
Map F-001…F-184 to existing code; identify reuse/gaps; architecture decision record; source/repo conflict register; scope/budget coverage statement.

### S0.2 Core schema contracts
Profile/journey, append-only events, memory/provenance, entitlement, credit ledger, source metadata/release, AI trace/cost, flags/incident/audit.

### S0.3 Provider/RAG foundations
Provider abstraction, authority model, entitlement prefilter, hybrid retrieval skeleton, citations/location and trace hooks.

### S0.4 Content asset register
Inventory format/tool, first-profession readiness, approval owner workflow and ingestion manifest.

### S0.5 Commercial foundations
Effective-dated tier/price/allowance policy, AI Credit compatibility/migration plan, cost attribution and kill switches.

### S0.6 Evaluation foundations
Golden-case schema/runner, retrieval/security/ledger test harness, live-beta telemetry and voice-spike harness/design.

### S0 exit
All launch blockers have owners; schemas/instrumentation are ready; source inventory has started; developer quote/stage coverage is mapped. If approved budget cannot reach Stage 1, reduce Stage 1 or approve more budget **before** coding an unfinishable scope.

---

## Stage 1 — Monetisable OET Core

### S1.1 Identity/onboarding vertical slice
F-001…F-012 relevant subset: anonymous/free, profession/exam/date/target, availability/preferences, result-capture readiness and historical journey schema.

### S1.2 One-profession grounded assistant
Approved first corpus; official-vs-methodology baseline; entitlement-safe RAG; source labels; anti-hallucination; support/platform map.

### S1.3 Global companion + navigator
Shared session/context shell, floating/full tutor surfaces as current UI permits, deep links, action registry and current-page context.

### S1.4 Basic plan/NBA/memory
Basic weekly/next action, learning-memory provenance, exam-date replanning and user controls.

### S1.5 Existing Writing assessment re-plumb
Integrate existing assessment with unified trace/entitlement/credit; preserve working behavior.

### S1.6 Commercial core
Free/Plus/Pro, Ultimate scaffold only, message cap, tier policy, AI Credits/top-ups, usage, paywalls, checkout continuation and subscription lifecycle.

### S1.7 Trust/ops
Privacy, exfiltration/injection, audit, rate limits, cost dashboard/alerts, kill switches, incident baseline and accessible/RTL-safe core.

### S1.8 Legal-ready billing primitives
Key terms data, renewal reminder capability, cancellation and refund/cooling-off state machine awaiting exact legal sign-off.

### S1 exit
Paid learner can onboard, ask eligible grounded OET questions, navigate/start resource, receive plan/next action, use existing Writing assessment with correct credits, upgrade through contextual paywall, and admins can see cost/quality/support signals. Critical RAG/ledger/security/E2E gates pass.

---

## Stage 2 — Learning Moat

### S2.1 Full OET content coverage
F-013…F-029: all approved source classes, versioning/overrides/conflicts and content pipeline.

### S2.2 Error DNA / mastery / adaptive planning
F-030…F-047 except fingerprint Stage 3; multiple plan horizons, replan and spaced reinforcement.

### S2.3 Tutor modes and drills
F-048…F-064 plus Reading/Listening/grammar/vocab with score-calibration rules.

### S2.4 Reading/Listening analytics
Part-level analysis, timing/distractors/confidence, targeted drills and score-change evidence.

### S2.5 Mock/readiness/analytics
F-072…F-085 with cohort benchmarking explicitly deferred until reliable/privacy-safe.

### S2.6 Multimodal/context
F-086…F-097 as provider/cost policy allows; sensitive-upload guardrails.

### S2.7 Action layer completion
F-098…F-112: timestamp, save/add/remind/support/handoff/report/balance/purchase-resume.

### S2.8 Admin/tutor/content ops
F-123…F-134: quality/content/teaching gaps, rule approval, revenue/cost, promotional credits.

### S2 exit
Full approved OET learning journey persists correctly; retrieval/regression passes; Error DNA/adaptive plans/tutors/video/content ops work with unified credits and traceable quality.

---

## Stage 3 — Voice & Ultimate Mentor

### Gate first
Two-week Arabic/voice feasibility and unit-economics approval.

### S3.1 Voice infrastructure
STT/TTS/live voice adapter, session lifecycle, transcript, 12-min cap, idle/silence, reconnect, no-charge failure and voice credits.

### S3.2 Speaking simulator
Role cards/personalities/difficult modes/exam integrity/pause-and-coach/replay/weakness generation.

### S3.3 Learning Fingerprint
F-045 evidence-driven derived model.

### S3.4 Proactive coaching
F-113…F-122: countdown, inactivity/weak-subtest/new content, calendar, summaries, quiet hours, milestones; gamification/referrals only as approved.

### S3.5 Ultimate behavior
Continuous next-best action/replanning, priority routing, multimodal depth and tutor collaboration.

### S3 exit
Voice quality/cost gates pass and Ultimate unit economics meet contribution floor.

---

## Stage 4 — Multi-Exam

### S4.1 Generalize exam pack
F-168…F-173: version engine, modules, task rules, track/profession, content packs and scoring adapter.

### S4.2 Add packs
IELTS/PTE/TOEFL prioritized by Product. Import sources through same authority/entitlement/content-ops/evaluation/billing/memory/action architecture.

### S4.3 Cross-exam skill transfer
Preserve transferable skill evidence without carrying incompatible task strategy.

### S4 exit
A representative second exam pack can be added without core rebuild.

---

## Stage 5 — B2B / Enterprise

### S5.1 Tenant boundary
F-174: source/index/cache/data separation and tenant-aware auth.

### S5.2 White label / institution knowledge
F-175…F-177.

### S5.3 Seats/budgets/analytics
F-178…F-179 with per-tenant cost/revenue.

### S5.4 Enterprise integrations
F-180…F-182 SSO/API/webhooks/LMS.

### S5.5 Enterprise governance
F-183…F-184 custom retention, audit/support and residency where required.

### S5 exit
Cross-tenant security tests pass and first real contract requirements are validated.

---

# First-12-month non-goal enforcement

Do not allow full B2B, full multi-exam production, dedicated Windows/macOS native when PWA is adequate, low-value gamification, early cohort benchmarking, SSO/API/LMS, fully autonomous proactive coaching, official score/pass claims, unlimited expensive usage, clinical decision support or counselling/pass prediction to delay the core monetisable OET program unless Product explicitly changes scope.

# Estimation rules for Claude Code

After repo audit, estimate by **vertical slice**, including DB, backend, frontend, tests, migration, observability and rollout. External dependencies are separate. The source references a roughly **US$4,000 development spend**; the developer must explicitly state which stages/features that amount can cover instead of assuming all 184 items fit it.
