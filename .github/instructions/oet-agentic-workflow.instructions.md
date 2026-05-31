---
description: "Use when planning, implementing, debugging, refactoring, or reviewing work in the OET Prep Platform. Enforces automatic research, planning, delegation, implementation, and verification."
name: "OET Agentic Workflow"
---
# OET Agentic Workflow

- Start from `AGENTS.md`, then inspect the relevant code and docs before editing.
- For multi-step work, keep a visible todo list and update it as work progresses.
- Use workspace custom agents when helpful: `OET Explorer`, `OET Planner`, `OET Implementer`, `OET Reviewer`, `OET Security Reviewer`, and `OET QA Validator`.
- The user should not have to request agents, skills, slash prompts, or plugin names. Automatically use installed Superpowers skills, `OET Superpowers`, official Copilot Plugins skills, `OET Copilot Plugins`, Awesome Copilot assets, `OET Awesome Copilot`, and Fabric specialist agents when their trigger domain fits the prompt.
- Route new feature/design prompts through `brainstorming`, failures through `systematic-debugging`, behavior changes through `test-driven-development` when practical, multi-step work through `writing-plans`, reviews through `requesting-code-review`, and completion claims through `verification-before-completion`.
- Route plugin-specific domains automatically: dependency or secret scanning to Advanced Security skills, GitHub Spark app tasks to `spark-app-template`, WorkIQ requests to `workiq`, and Microsoft Fabric / Power BI work to the Fabric skills and agents.
- Route Awesome Copilot domains automatically: framework-specific implementation, modernization, testing, docs, architecture, frontend/backend patterns, DevOps/cloud, data, and productivity workflows should load the relevant installed Awesome skill, hidden `Awesome ...` specialist, or archived instruction instead of requiring the user to name it.
- Research in parallel when searches are independent; serialize edits that touch the same file or contract.
- Fix root causes and preserve unrelated user changes.
- Prefer existing helpers, service boundaries, design tokens, and test conventions.
- Add tests proportional to risk; do not broaden scope just to satisfy a generic checklist.
- Run the lightest relevant validation first, then expand if the touched surface has wider blast radius.
- End with a concise report of changes, verification, skipped checks, and remaining risk.

## Autonomous Intent Router

The user should not have to remember slash prompts or agent names. Infer the
best workflow from the request and run it automatically.

| Request shape | Decide automatically |
| --- | --- |
| "add", "build", "implement", "make", "fix" | Explore -> plan if needed -> edit -> review -> verify. |
| Error output, failing command, broken test | Use build-fix/debug flow and rerun the failing command first. |
| Feature or bug touching behavior | Use TDD when practical and add focused regression coverage. |
| Auth, AI, scoring, rulebooks, uploads, storage, deployment | Load security/domain docs first and preserve invariants before editing. |
| "review", "audit", "check" | Use review mode and lead with findings. |
| Broad or ambiguous production-sensitive task | Create a short plan, ask only blocking questions, then continue when safe. |

Slash prompts in `.github/prompts/` are optional manual shortcuts. They do not
replace this automatic router.

## Default Task Flow

1. Classify the area and load matching instruction files.
2. Search for existing implementations and docs.
3. Identify invariants and risky files.
4. Plan only as much as needed to make the next edit safely.
5. Implement focused changes.
6. Review the diff for OET contracts, security, tests, and regressions.
7. Verify and document the outcome.

## Stop-And-Read Surfaces

Before editing any of these, read the corresponding docs and nearest tests:

- Scoring: `lib/scoring.ts`, `OetScoring`, `docs/SCORING.md`
- Rulebooks: `lib/rulebook/**`, backend rulebook services, `docs/RULEBOOKS.md`
- AI calls: `lib/rulebook/ai-prompt.ts`, `AiGatewayService`, `docs/AI-USAGE-POLICY.md`
- Content upload: content paper/upload endpoints and `docs/CONTENT-UPLOAD-PLAN.md`
- Result card: `components/domain/OetStatementOfResultsCard.tsx`, `docs/OET-RESULT-CARD-SPEC.md`
- Reading, grammar, pronunciation, conversation modules and their domain docs

## Execution Locality (MISSION CRITICAL)

The user's local machine is Windows, but heavy work must run inside local Docker
Desktop containers. Do this automatically and do not ask where to run builds,
tests, lint, installs, or broad validation.

- Use `docker exec oet-local-web <cmd>` for frontend type-check, lint, tests,
  builds, Playwright, and web-container npm scripts.
- Use `docker exec oet-local-api <cmd>` for backend restore, build, test,
  publish, EF, and API-container work.
- Use `docker compose -f docker-compose.local.yml --env-file .env.docker-local up`
  or `docker compose -f docker-compose.dev.yml --env-file .env.docker-local up`
  only to orchestrate local containers.
- Local-only: file editing, navigation, single-file reads, and git plumbing
  (`status`, `add`, `commit`, `push`, `pull`, `diff`, `log`).
- The VPS `oet-dev` is production deployment only. Never run build, test, lint,
  install, or exploratory validation commands there.
- If Docker Desktop is unavailable, stop and report — never fall back to running
  heavy work on the Windows host or the VPS.