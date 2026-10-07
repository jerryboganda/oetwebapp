# SESSION STATE

Session: staff-chatbot-access
Goal: Enable direct gateway chatbot for all admin and instructor/expert dashboards
Mode: execute
Updated: 2026-10-08
Branch: main
HEAD: 2849fb62f

## Objective

All admins already mount the role-authorized chatbot. Enable the missing expert launcher and direct OpenCode selection/inference for every expert account, retaining role-specific tool boundaries.

## Acceptance criteria

- [x] Admin and expert launchers mount without learner feature flag or owner-account allowlist.
- [x] Expert catalogue, model selection and inference admit the shared direct gateway.
- [x] Expert read-only tool permissions and learner-only model policy retained.
- [ ] Application release live with serving evidence.
- [ ] Functional acceptance: not tested—owner QA.

## Decisions (do not revisit)

- No change to owner-console privileges pending clarification: this task enables the role-authorized dashboard chatbot.
- Admin and expert defaults retained; direct gateway selectable through the existing encrypted provider.
- No automated QA; image builds and live release health provide deployment evidence only.

## Touched files

| Path | Change |
| --- | --- |
| components/providers/companion-mount.tsx | All expert dashboard chatbot mount |
| backend/src/OetLearner.Api/Endpoints/AiAssistantEndpoints.cs | Expert gateway catalogue/selection |
| backend/src/OetLearner.Api/Services/AiAssistant/AiAssistantGateway.cs | Expert inference role |

## Verification gates

| Gate | Command / workflow | Evidence | Result |
| --- | --- | --- | --- |
| compilation | build-images.yml | Awaiting release | NOT RUN |
| deployment | production-deploy.yml | Awaiting release | NOT RUN |
| functional acceptance | Owner manual QA | Admin and expert accounts, gateway selection and tool boundaries | NOT TESTED |

## Blockers

- Separate Owner Agent Console privilege expansion awaits user clarification; dashboard chatbot work proceeds.

## Next action

1. Commit scoped changes and ship via the existing release wrapper; record successful release and serving evidence.
