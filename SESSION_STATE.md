# SESSION STATE

Session: direct-opencode-gateway
Goal: Direct OpenCode gateway across admin chat, learner chat and owner console; shared encrypted provider; remove native inference
Mode: execute
Updated: 2026-10-08T01:32:00+05:00
Branch: work/direct-opencode-gateway
HEAD: 8956f7f489aee1aa2c4b78b9f3b9bc9b9bc784f5

## Objective

Implement the approved direct gateway on all three surfaces. Preserve defaults, history and guarded console tools; remove native OpenCode inference and console credential input.

## Acceptance criteria

- [x] Backend compatibility and additive encrypted-state migration live before replacement.
- [ ] Application and console replacement releases live with serving-image evidence.
- [ ] Production readiness and absence of native inference paths verified.
- [ ] Functional acceptance: not tested—owner QA (manual matrix in docs/ops/DIRECT-OPENCODE-GATEWAY.md).

## Decisions (do not revisit)

- Shared encrypted provider managed only in /admin/ai-providers; no OpenCode inference CLI, SDK, server or container.
- Keep current defaults; learner DeepSeek v4.1 Flash/max. No provider fallback or automatic replay of interrupted tools.
- Existing Guard, approvals, snapshots and UID 10002 tool runner remain authoritative.
- Image builds compile; no automated QA locally or CI; functional acceptance belongs to owner.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api | Shared gateway, role selection, encrypted state, session controls |
| agent-console | Native replacement, guarded tools, retention and packaging |
| app/admin and components/admin/agent-console | Direct gateway labels and shared settings |
| docs/ops/DIRECT-OPENCODE-GATEWAY.md | Manual acceptance and rollout record |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| compatibility compilation | build-images.yml | 37678854057 | PASS |
| compatibility deployment | production-deploy.yml | 37679851932; exact serving SHA 8956f7f489 | PASS |
| migration/readiness | Production read-only inspection | AddAssistantProviderState applied; private learner-api status HTTP 200 ready | PASS |
| replacement build/deploy | build-images.yml and agent-console.yml | Awaiting release | NOT RUN |
| functional acceptance | Owner manual QA | docs/ops/DIRECT-OPENCODE-GATEWAY.md | NOT TESTED |

## Blockers

- None for implementation/release. Functional owner QA remains unperformed.

## Next action

1. Ship replacement via existing workflows, verify serving images and native runtime removal, then record deployment evidence. Owner performs manual acceptance.
