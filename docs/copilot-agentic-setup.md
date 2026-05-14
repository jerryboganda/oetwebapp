# Copilot Agentic Setup

This workspace has a project-local GitHub Copilot customization layer adapted
from the Copilot-compatible ideas in Everything Claude Code (ECC), inspected at
commit `3243a1c` on 2026-05-14.

## Installed Workspace Surfaces

- `.github/copilot-instructions.md`: always-on OET + ECC-inspired automation rules.
- `.github/instructions/*.instructions.md`: file-scoped guidance for frontend,
  backend, testing, security/AI grounding, deployment, and agentic workflow.
- `.github/prompts/*.prompt.md`: optional slash-prompt shortcuts for planning,
  ultrawork, TDD, code review, security review, build fixes, refactors, and
  validation. Normal Copilot chat should infer these workflows automatically
  from the always-on instructions.
- `.github/agents/*.agent.md`: custom OET Explorer, Planner, Implementer,
  Reviewer, Security Reviewer, and QA Validator agents.
- `.vscode/settings.json`: enables prompt files and attaches the core Copilot
  instruction files to code generation, test generation, reviews, and commits.
- `.vscode/mcp.json`: registers no-token MCP servers for Playwright and
  sequential thinking through lockfile-backed local `npx --no-install` binaries.
- `.github/ISSUE_TEMPLATE/copilot-task.md`: structured GitHub Copilot task issues.

## What Was Selected From ECC

ECC contains broad harness support for Claude Code, Codex, Cursor, OpenCode,
Gemini, Qwen, CodeBuddy, and more. Its managed installer does not expose a
GitHub Copilot target, but it does include a Copilot instruction file, prompt
files, and workflow concepts. This setup translates the useful Copilot-ready
parts into native VS Code/Copilot files for this repo.

Relevant ECC concepts adapted here:

- research first
- plan before broad edits
- test-first when risk is real
- security and prompt-defense baseline
- review before handoff
- validation loops
- specialist agent roles

## What Was Not Installed

- No Claude Code, Cursor, Codex, OpenCode, Qwen, Gemini, or CodeBuddy target
  folders were installed into this workspace.
- No tokened MCP services were added. GitHub, Exa, and other account-backed
  MCP services require explicit approval before registration.
- The no-token MCP packages are pinned as dev dependencies so VS Code does not
  fetch unpinned npm code when MCP starts.
- No production credentials, `.env*` files, or deployment secrets were changed.
- The full ECC repo was inspected in a temporary folder, not vendored into this
  app.

## How To Use

Copilot should automatically load the workspace instructions during coding,
testing, review, and commit-message generation. You do not need to manually use
slash commands for normal work. Ask naturally, for example "fix this build
error", "add this feature", "review this diff", or "make this page work", and
Copilot should choose the matching workflow automatically.

Slash prompts remain available only as optional shortcuts when you want to force
a specific workflow:

- `/ultrawork` for a full explore-plan-implement-review-verify loop
- `/plan` for an implementation plan
- `/tdd` for test-first work
- `/code-review` or `/security-review` for reviews
- `/build-fix` for failures
- `/qa-validate` for validation selection

Custom agents are available from the agent picker when the UI exposes workspace
agents. They are intentionally scoped to this project and should prefer
`AGENTS.md` and domain docs over generic framework advice.

## Maintenance

- Keep instructions concise. Add detail to domain docs rather than duplicating
  large specs into Copilot files.
- Review `.vscode/mcp.json` before adding any MCP service that needs a token,
  account, browser login, external billing, or package execution outside the
  repo lockfile.
- Optional BMad refresh, if you intentionally use that global workflow later:
  `cd $env:USERPROFILE; npx bmad-method install --yes --directory $env:USERPROFILE --modules bmm,bmb,cis,tea --tools github-copilot`
- If ECC adds an official `copilot` installer target later, dry-run it first and
  compare against this setup before applying.