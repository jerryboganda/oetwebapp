# SESSION STATE

Session: learner-personal-assistant
Goal: Learner-only OET Personal Ai Assistant, DeepSeek v4.1 Flash, repair operation ID overflow
Mode: verify
Updated: 2026-10-08
Branch: main
HEAD: 56c3322cf

## Objective

Remove learner Claude/UBAG choices and enforce one direct gateway model on every learner turn, including legacy pins. Repair the production recorder failure before inference.

## Acceptance criteria

- [x] Learner catalogue/selection and inference restricted in code; learner UI hides provider/model names.
- [x] Operation resource IDs fit varchar(64); request hash is SHA-256.
- [x] Learner-safe tool allowlist retained; study-only prompt applies including fallback prompt.
- [x] Image compilation and serving release confirmed.
- [ ] Functional acceptance: not tested—owner QA.

## Decisions (do not revisit)

- Learner model deepseek-v4.1-flash; shared encrypted direct gateway, no fallback. Admin/expert provider choices retained.
- Display OET Personal Ai Assistant; no learner model selector. Authorized study material only; no codebase, shell or deployment tools.
- Production logs confirm DirectAiCallRecorder insert failed with SQLSTATE 22001, varchar(64), matching overlong concatenated ResourceId.

## Touched files

| Path | Change |
| --- | --- |
| backend/src/OetLearner.Api/Endpoints/AiAssistantEndpoints.cs | Learner catalogue/selection |
| backend/src/OetLearner.Api/Services/AiAssistant | Routing, bounded operation identity, study-only prompt |
| components/domain/ai-assistant/AiAssistantPanel.tsx | Learner branding and selector removal |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| compilation | build-images.yml | 37692008180 | PASS |
| deployment | production-deploy.yml | 37692695925 | PASS |
| functional acceptance | Owner manual QA | Learner answer, model restrictions and study boundaries | NOT TESTED |

## Blockers

- None for implementation. Owner functional QA remains pending.

## Next action

1. Owner manual QA: learner reply, restricted providers, authorized study material and off-topic refusal; no automated functional acceptance claimed.

