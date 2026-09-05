# START HERE — Claude Code Integration Instructions

This pack is meant to be copied into the **root of the existing OET With Dr Hesham main project** and then executed by Claude Code from that repository root.

## Fastest safe workflow

1. Make a normal Git checkpoint/branch according to your project's existing rules.
2. Copy the contents of this pack into the repository root. Merge carefully if the project already has a `CLAUDE.md`; do not erase stronger existing repository rules.
3. Keep `source/Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf` as the original source reference. The implementation-ready conversion is under `docs/ai-learning-companion/`.
4. Start Claude Code in the repository root.
5. First run the repository audit prompt: `.claude/commands/ai-companion-audit.md`.
6. Review the generated `docs/ai-learning-companion/REPO_GAP_ANALYSIS.md`, especially existing modules that must be reused and every `TO VERIFY` blocker.
7. Execute Stage 0, then Stage 1. Do not jump to Stage 3 voice/Ultimate unless the source-defined gates pass.
8. Continue Stage 2 → Stage 3 → Stage 4 → Stage 5 only as explicitly approved.
9. Finish with `.claude/commands/ai-companion-production-verify.md`.
10. Run `python scripts/validate_traceability.py` whenever the traceability files are edited.

## One-shot alternative

Paste the full contents of:

`AI_LEARNING_COMPANION_CLAUDE_CODE_MASTER_PLAN.markdown.md`

into Claude Code while it is open at the main project's root. That prompt instructs Claude to audit first, map the 184 requirements to the real codebase, create a dependency-ordered implementation plan, implement in vertical slices, test, update traceability, and stop at external decision gates rather than guessing.

## What Claude Code must create in the real project during the audit

Use the templates in `docs/ai-learning-companion/templates/` and create these repository-specific working files:

- `REPO_GAP_ANALYSIS.md`
- `IMPLEMENTATION_PLAN.md`
- `PRODUCTION_READINESS_REPORT.md`
- decision records for material `TO VERIFY` resolutions
- incident/runbook material appropriate to the actual infrastructure

## Critical operating rules

- Do not treat this as a new standalone chatbot.
- Do not create parallel auth, user, course, entitlement, payment, AI-credit, assessment or analytics truths when the project already has them.
- Entitlement checks happen server-side before retrieval and actions.
- Keep one user-facing currency: AI Credits.
- Preserve every F-001…F-184 requirement, even when deliberately deferred.
- Never convert `TO VERIFY` into an invented value.
- Do not claim Writing/Speaking numeric scores are official or guaranteed.
- Do not provide patient-specific clinical diagnosis/treatment/decision support.
- Do not ship full voice/Ultimate before the source-defined voice, cost, calibration and safety gates pass.
- Keep accessibility, privacy, security, auditability, rollback and cost telemetry in the definition of done rather than as post-launch cleanup.

## Recommended first Claude Code instruction

> Read the repository's existing instructions first, then read `CLAUDE.md`, `START_HERE.md`, the complete `docs/ai-learning-companion/` pack, and `traceability/FEATURE_TRACEABILITY_MATRIX.md`. Perform the repository audit only. Do not implement yet. Map every F-001 through F-184 requirement to the existing codebase and create `docs/ai-learning-companion/REPO_GAP_ANALYSIS.md` plus a dependency-ordered Stage 0/1 plan. Reuse existing systems, preserve all `TO VERIFY` gates, and report any contradictions before editing production code.
