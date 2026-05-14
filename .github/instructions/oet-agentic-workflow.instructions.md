---
description: "Use when planning, implementing, debugging, refactoring, or reviewing work in the OET Prep Platform. Enforces automatic research, planning, delegation, implementation, and verification."
name: "OET Agentic Workflow"
---
# OET Agentic Workflow

- Start from `AGENTS.md`, then inspect the relevant code and docs before editing.
- For multi-step work, keep a visible todo list and update it as work progresses.
- Use workspace custom agents when helpful: `OET Explorer`, `OET Planner`, `OET Implementer`, `OET Reviewer`, `OET Security Reviewer`, and `OET QA Validator`.
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