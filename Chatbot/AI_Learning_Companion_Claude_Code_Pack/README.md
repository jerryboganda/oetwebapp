# AI Learning Companion — Claude Code Development Pack

This pack converts the full **AI Learning Companion Master Specification v3.0** (working persona: **Talk to Jana / Talk to Sami**) into a repository-ready Claude Code implementation system.

It is intentionally designed for **integration into an existing main project**, not for starting a disconnected greenfield application. Claude Code must first inspect the current repository, identify the existing stack and reusable modules, map the 184 source requirements to the codebase, then implement the program in staged vertical slices without silently dropping anything.

## What this pack guarantees

- All **184 feature IDs (F-001 through F-184)** are carried into a machine-readable and human-readable traceability matrix.
- All source areas are represented: learner profile, OET knowledge, RAG, tutoring, Writing, Speaking, Reading, Listening, grammar/vocabulary, plans, memory, analytics, multimodal, actions, monetisation, AI Credits, paywalls, growth, multi-exam, B2B, observability, security, privacy, accessibility, content operations, quality evaluation, legal gates, incident response, unit economics and beta gates.
- The source's **Stage 0 through Stage 5** release model is preserved.
- Every **TO VERIFY** item stays explicit. Claude Code is forbidden from inventing a legal, commercial, cost, voice-quality, calibration, policy or operational value just to make the build look complete.
- Existing application capabilities must be reused rather than duplicated. Authentication, learner data, course access, payments, AI credits, tests, CI/CD and deployment conventions in the main project remain the integration source of truth unless the repository audit proves they must be refactored.
- A feature is not “done” unless its backend/data/authorization/telemetry/tests/UX are complete as applicable and the traceability state is updated.

## Recommended install into the main repository

Copy the **contents of this pack** into the root of your main project so that `CLAUDE.md` is at the repository root and `.claude/commands/` is inside the repository.

Then open Claude Code in the repository root and use one of these two entry points:

1. Paste the entire prompt from `AI_LEARNING_COMPANION_CLAUDE_CODE_MASTER_PLAN.markdown.md` for a single end-to-end autonomous execution plan.
2. Or use the staged prompt files under `.claude/commands/`, beginning with `ai-companion-audit.md`, then Stage 0/1/2/3/4/5, and finally `ai-companion-production-verify.md`.

## Mandatory execution order

1. **Repository audit and gap map** — no stack assumptions and no duplicate subsystems.
2. **Stage 0 validation/foundations** — schemas, instrumentation, content inventory, external decision gates, feature flags, kill-switches and test harnesses.
3. **Stage 1 Monetisable OET Core** — safe monetisable foundation with one approved profession/corpus, entitlement-safe retrieval, navigator/deep links, basic profile/plan/memory, existing Writing assessment integration, Free/Plus/Pro commercial flows, counters/paywalls, privacy and cost telemetry.
4. **Stage 2 Learning Moat** — full OET grounding, Error DNA, adaptive plans, Reading/Listening/Writing tutor depth, video/timestamps, unified AI Credits and content-ops pipeline.
5. **Stage 3 Voice & Ultimate Mentor** — only after the Arabic/voice feasibility and economics gates pass.
6. **Stage 4 Multi-Exam** — IELTS/PTE/TOEFL as versioned knowledge/assessment packs without rebuilding the core.
7. **Stage 5 B2B** — tenant isolation, white-label, institution knowledge, seats/budgets/analytics, SSO/API/LMS and enterprise audit/support.
8. **Production verification** — regression, security, entitlement-leak testing, cost/credit reconciliation, performance, accessibility and rollback evidence.

## Files in this pack

- `CLAUDE.md` — persistent repository instructions for Claude Code.
- `AI_LEARNING_COMPANION_CLAUDE_CODE_MASTER_PLAN.markdown.md` — one master prompt that can drive the whole program.
- `docs/ai-learning-companion/FULL_REQUIREMENTS_TO_IMPLEMENT.md` — complete implementation interpretation of the PDF.
- `docs/ai-learning-companion/ARCHITECTURE_AND_INTEGRATION.md` — target architecture and integration rules.
- `docs/ai-learning-companion/DATA_MODEL_CONTRACTS.md` — implementation-level entity contracts and invariants.
- `docs/ai-learning-companion/AI_RAG_MEMORY_AND_ROUTING.md` — RAG, source authority, memory, routing, caching and provider abstraction.
- `docs/ai-learning-companion/UX_SURFACES_PERSONA_ACTIONS.md` — roles, surfaces, context and action layer.
- `docs/ai-learning-companion/MONETIZATION_CREDITS_BILLING.md` — plans, allowances, credits, ledger, paywalls and margin controls.
- `docs/ai-learning-companion/SECURITY_PRIVACY_SAFETY_LEGAL_GATES.md` — trust, exfiltration defence, prompt injection, privacy, accessibility, clinical/distress boundaries and external sign-offs.
- `docs/ai-learning-companion/CONTENT_OPS_INGESTION.md` — source inventory through approval/publish/rollback.
- `docs/ai-learning-companion/QA_EVALUATION_AND_RELEASE_GATES.md` — golden sets, retrieval, hallucination, calibration, Arabic, red-team, load and release gates.
- `docs/ai-learning-companion/RELEASE_PLAN_WORKBREAKDOWN.md` — stage-by-stage engineering work plan.
- `docs/ai-learning-companion/TO_VERIFY_AND_DECISION_REGISTER.md` — external decisions and source inconsistencies that cannot be invented.
- `docs/ai-learning-companion/SOURCE_COVERAGE_INDEX.md` — section-by-section proof that the conversion covers the whole specification.
- `traceability/FEATURE_TRACEABILITY_MATRIX.md` — all 184 features.
- `traceability/features.json` and `traceability/features.csv` — machine-readable versions for scripting/issue generation.
- `.claude/commands/*.md` — ready-to-run Claude Code prompts.
- `scripts/validate_traceability.py` — verifies F-001…F-184 are present exactly once.

## Non-negotiable interpretation rule

The PDF deliberately permits phasing but forbids silent omission. Therefore a later-stage or non-goal feature remains in the program backlog even when it is not eligible for the first release. “Deferred” means deliberately deferred with an owner/gate, never forgotten.


## Quick-start and repository install helpers

- `START_HERE.md` — exact safe execution sequence for Claude Code.
- `INSTALL_INTO_MAIN_PROJECT.ps1` — Windows/PowerShell installer that preserves an existing project `CLAUDE.md` and installs this pack as an addendum instead of overwriting it.
- `INSTALL_INTO_MAIN_PROJECT.sh` — equivalent shell helper for macOS/Linux/WSL.
- `docs/ai-learning-companion/templates/REPO_GAP_ANALYSIS_TEMPLATE.md` — audit output template covering every feature.
- `docs/ai-learning-companion/templates/IMPLEMENTATION_PLAN_TEMPLATE.md` — staged implementation plan template.
- `docs/ai-learning-companion/templates/PRODUCTION_READINESS_REPORT_TEMPLATE.md` — evidence-based go/no-go release template.
- `docs/ai-learning-companion/templates/DECISION_RECORD_TEMPLATE.md` — formal resolution record for `TO VERIFY` and architecture decisions.
- `docs/ai-learning-companion/templates/INCIDENT_RUNBOOK_TEMPLATE.md` — starter incident/kill-switch runbook.
- `source/Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf` — original uploaded specification retained inside the pack for source traceability.

### Windows install example

From PowerShell, with this pack extracted somewhere outside the main repository:

```powershell
.\INSTALL_INTO_MAIN_PROJECT.ps1 -TargetRepo "D:\path\to\your\main-project"
```

Use `-WhatIf` first if you want PowerShell to show the planned copy operations without applying them.

If the main project already has a `CLAUDE.md`, the installer deliberately leaves it intact and writes this pack's rules as `CLAUDE_AI_COMPANION_ADDENDUM.md`. Merge the addendum into the existing project instructions manually or through a reviewed Claude Code change so existing repository-specific rules are not destroyed.
