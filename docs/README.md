# Docs index

The canonical entry points. Anything not listed here is supporting detail,
a module sub-doc, or a dated record; check its date before trusting it.

## Start here

- [AGENTS.md](../AGENTS.md) — rules for agents: compute runs on GitHub Actions only, the ship/deploy loop, domain invariants.
- [ARCHITECTURE.md](ARCHITECTURE.md) — repository map: where each concern lives and where new code goes.
- [README.md](../README.md) — stack, local URLs, desktop shell notes.
- [CONTEXT.md](../CONTEXT.md) — domain glossary: canonical terms and the words to avoid.
- [adr/](adr/) — decisions: hand-authored EF migrations (0001), scoring via canonical helpers (0002), AI via the grounded gateway (0003), media via `IFileStorage` (0004).

## Continuity & verification

Where agent working memory lives. Layers have exclusive ownership — no file has two jobs.

- [SESSION_STATE.md](../SESSION_STATE.md) — the current run: goal, decisions, verification gates, next action. Schema: [scripts/agent/session-state.template.md](../scripts/agent/session-state.template.md).
- [TASKS.json](../TASKS.json) — the execution queue; `pnpm run ax:next` picks the next ready task.
- [VERIFICATION.md](../VERIFICATION.md) — machine-written evidence index, one row per GitHub Actions run. Never hand-edit.
- [PROGRESS.md](../PROGRESS.md) — compact durable checkpoint ledger; history in [PROGRESS-ARCHIVE-2026.md](PROGRESS-ARCHIVE-2026.md).
- [scripts/agent/README.md](../scripts/agent/README.md) — the `ax:*` commands and the compute-locality rule for that script.
- [dev/lessons-learned.md](dev/lessons-learned.md) — session-proven gotchas, Mistake → Lesson → Action.

## Domain rules (do not deviate)

- [SCORING.md](SCORING.md) — canonical OET scoring (`lib/scoring.ts`, `OetScoring.cs`).
- [RULEBOOKS.md](RULEBOOKS.md) — Writing/Speaking rulebooks and the grounded-AI stack; registry in [canonical-rules/README.md](canonical-rules/README.md).
- [AI-USAGE-POLICY.md](AI-USAGE-POLICY.md) — AI gateway, providers, quotas and options.
- [OET-RESULT-CARD-SPEC.md](OET-RESULT-CARD-SPEC.md) — the statement-of-results card.
- [WRITING-MODEL-ANSWER-RULES.md](WRITING-MODEL-ANSWER-RULES.md) — owner directives for Writing model answers.
- [READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md](READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md) and [READING-MODULE-SAVE-AND-UPLOAD.md](READING-MODULE-SAVE-AND-UPLOAD.md) — Reading content upload.
- [CONTENT-UPLOAD-PLAN.md](CONTENT-UPLOAD-PLAN.md) and [PRODUCTION-DATA-PERSISTENCE.md](PRODUCTION-DATA-PERSISTENCE.md) — content model and where production data lives.

## Modules

- [LISTENING.md](LISTENING.md), [speaking/README.md](speaking/README.md), [mocks/PRD.md](mocks/PRD.md), [GRAMMAR-MODULE.md](GRAMMAR-MODULE.md), [PRONUNCIATION.md](PRONUNCIATION.md), [CONVERSATION.md](CONVERSATION.md) — module specs.
- [product-manual/README.md](product-manual/README.md) — product manual for every role.

## Commerce

- [OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md](OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md) — packages, AI credits and access (current pricing).
- [BILLING.md](BILLING.md) — billing module reference; incidents in [runbooks/billing-incident.md](runbooks/billing-incident.md).

## Operations

- [DEPLOY-MANUAL.md](../DEPLOY-MANUAL.md) — how production deploys (`build-images.yml` + `production-deploy.yml` → GHCR → blue/green); [DEPLOYMENT.md](../DEPLOYMENT.md) for env, compose files and disaster recovery.
- [ops/production-compute-offload.md](ops/production-compute-offload.md) — what runs on Actions vs the VPS; [PRIVATE-CI-SELF-HOSTED-RUNNER.md](PRIVATE-CI-SELF-HOSTED-RUNNER.md) for the optional private runner.
- [ADMIN-RUNTIME-SETTINGS.md](ADMIN-RUNTIME-SETTINGS.md) — secrets and settings managed from `/admin/settings`.
- [ops/deploy-gate.md](ops/deploy-gate.md) and [ops/incident-response-runbook.md](ops/incident-response-runbook.md) — deploy approval, rollback and incidents.
- [security/README.md](security/README.md) — security evidence pack and runbooks.

## Releases

- [app-release-playbook.md](app-release-playbook.md) — compulsory procedure for Android, iOS and desktop releases; see also [play-store-automation.md](play-store-automation.md), [releases/RELEASE-LEDGER.md](releases/RELEASE-LEDGER.md) and [tauri-desktop-shell.md](tauri-desktop-shell.md).
- [desktop/NATIVE-CAPABILITIES-PLAN.md](desktop/NATIVE-CAPABILITIES-PLAN.md) — plan for putting the desktop shell's 16 registered-but-ungranted Tauri commands to use: security model, phases, flags and owner questions.

## Frontend and QA

- [DESIGN.md](../DESIGN.md) and [PRODUCT.md](PRODUCT.md) — design system and product context for UI work.
- [qa/test-coverage-map.md](qa/test-coverage-map.md) — which suites cover what.

Historical packs are labelled in place, e.g. [product-strategy/](product-strategy/README.md).
