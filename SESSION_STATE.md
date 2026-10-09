# SESSION STATE

Session: ai-pipeline-guide-and-cost-breakdown
Goal: Admin can run the AI pipelines without a developer and sees Writing/Speaking cost by stage, with Speaking grading on Opus 5.5 high
Mode: verify
Updated: 2026-10-10T00:00:00Z
Branch: main
HEAD: 2c67f80ed

<!--
The current run's working memory. Layer 2 of three: AGENTS.md (rules), this file + TASKS.json (run),
VERIFICATION.md / git / Actions runs (objective truth).
-->

## Objective

Owner request 2026-10-10: permanent in-page guide for /admin/ai-pipelines (flowcharts, tooltips, PDF),
Speaking Claude API grading must be Opus 5.5 High (not Sonnet), and Usage & Cost must split Writing
(grading + reviewer) and Speaking (live voice + grading + reviewer) with honest subscription/credit labels.

## Acceptance criteria

- [x] In-page guide with live flowcharts, worked priority example, glossary, (i) tooltips, print-to-PDF.
- [x] Speaking paid-API hop runs Opus 5.5 at effort high; blank anthropic model resolves to claude-opus-5-5.
- [x] Cost by stage: Writing grading/reviewer/total/avg per letter; Speaking live voice/grading/reviewer/total/avg per card and full mock; Today/7d/30d/all.
- [x] Subscription routes labelled "Subscription - $0 incremental API cost"; credits shown as gross/consumed/remaining/out-of-pocket.
- [x] Live voice metered in connected minutes x owner-set rate; an unset rate is a labelled assumption, never $0.
- [ ] Functional acceptance on live data: not tested - owner QA (production data and admin login are not reachable from this workstation).

## Decisions (do not revisit)

- D-1 Cost is re-priced at report time from tokens x model list price incl. cache buckets; the per-call stored estimate and budget enforcement are untouched - changing them could trip budget caps on candidates.
- D-2 Live voice has no server-side token telemetry (media goes browser to provider); minutes x rate is the method. The realtime hook was not changed to avoid risking the candidate path.
- D-3 Writing paid-API effort is reported, not changed (behaviour + 150 s hop budget change); Speaking moved max -> high on the owner's explicit "Opus 5.5 High".
- D-4 Keys & providers and Self-check sections were dropped by f98a28f0f; restored here.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Services/AiPipeline/AiUsageLedger.cs | new: model rate card + repricing ledger |
| backend/src/OetLearner.Api/Services/AiPipeline/AiCostBreakdownService.cs | new: cost breakdown, live voice minutes, promo accounting, runs |
| backend/src/OetLearner.Api/Services/AiPipeline/AiCreditGuard.cs | edit: spend via ledger |
| backend/src/OetLearner.Api/Services/AiPipeline/AiPipelineOverviewService.cs | edit: provider usage and credits via ledger |
| backend/src/OetLearner.Api/Services/AiPipeline/AiPipelineStore.cs | edit: blank anthropic model resolves to approved model |
| backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs | edit: Speaking effort high |
| backend/src/OetLearner.Api/Endpoints/AiPipelineAdminEndpoints.cs | edit: cost-breakdown, live-voice-rates, model check data |
| app/admin/ai-pipelines/page.tsx, lib/api/ai-pipelines.ts, components/domain/admin/ai-pipelines/* | edit/new: guide, panel, model check, benchmarks, restored sections |
| docs/ops/AI-PIPELINE-COST-BREAKDOWN.md | new |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| ship-gate | pnpm run ship:gate | local:ship:gate | PASS |
| compilation | build-images.yml | 38004523603 | PASS |
| deploy | production-deploy.yml | 38005218832 | PASS |
| functional acceptance | Owner manual QA | open /admin/ai-pipelines, Model check, Cost by stage, Live voice rate | NOT TESTED |

## Blockers

- None. Live 2c67f80ed on blue (web + API serving-image proof, ready/live HTTP 200). Functional acceptance remains owner QA.

## Next action

1. Owner QA on /admin/ai-pipelines: Model check on Speaking grading, Cost by stage, Live voice rate (replace the assumed rate), open the guide and Download PDF.
