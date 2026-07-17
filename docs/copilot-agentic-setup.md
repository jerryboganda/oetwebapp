# Copilot Agentic Setup

Last verified against official VS Code and GitHub Copilot documentation on
2026-05-26.

This workspace uses the already-installed user-level Oh My OpenAgent agents in
`~/.copilot/agents` plus a repo-local VS Code/GitHub Copilot/Codex customization
layer. The repo layer supplies OET-specific rules, prompts, and Codex skills; it
does not replace the selected `Oh My OpenAgent` agent in VS Code.

## Official Documentation Basis

- VS Code custom agents: `~/.copilot/agents` for user-level agents. The repo
  keeps only project-local OET agents under `.github/agents/*.agent.md`; generic
  vendored catalogs were removed.
- VS Code prompt files: `.github/prompts` for workspace slash prompts.
- VS Code custom instructions: root `AGENTS.md` and the repo's existing
  `.github/copilot-instructions.md` as always-on repo instructions, plus
  `.github/instructions/*.instructions.md` for targeted rules.
- Codex skills: `.codex/skills/oet-*` provide project-local specialist roles for
  Codex CLI.
- VS Code subagents: coordinator/worker patterns are supported through the
  `agent` tool and custom-agent `agents` frontmatter.
- VS Code MCP: workspace MCP lives in `.vscode/mcp.json`; user MCP lives in the
  VS Code user `mcp.json`.
- GitHub Copilot repository customization: `.github/instructions` is the
  supported repository customization location; `AGENTS.md` is supported for
  agent instructions.

## Active Surfaces

- `AGENTS.md`: the primary always-on OET project contract and highest-priority
  repo source of truth.
- `.github/copilot-instructions.md`: the tracked GitHub Copilot automation
  bridge. It must stay aligned with `AGENTS.md`; when they differ, `AGENTS.md`
  wins.
- `.github/instructions/*.instructions.md`: targeted guidance for agentic
  workflow, frontend, backend, tests, security/AI, validation, deployment, and
  admin Hallmark discipline.
- `.github/agents/*.agent.md`: OET-specific coordinator and specialist agents
  that can be used directly or as subagents from the installed OmO agent.
- `.codex/skills/oet-*`: Codex skill roles for OET exploration, planning,
  implementation, review, QA validation, security review, Ralph coordination,
  and OmO orchestration.
- `.github/prompts/*.prompt.md`: slash prompts for ultrawork, Ralph loop,
  planning, scouting, review, security review, build fixes, validation,
  handoff, and hyperplanning.
- `.vscode/settings.json`: enables discovery for `.github/*` customizations and
  the installed user-level `~/.copilot/agents` OmO agents.
- `.vscode/mcp.json`: keeps the existing Docker-backed MCP configuration. No
  tokened MCP services were added by this setup.
- `.github/ISSUE_TEMPLATE/copilot-task.md`: structured task intake for Copilot
  or OmO work.

## Installed User-Level OmO Agents

VS Code is configured with `chat.agentFilesLocations` pointing to
`~/.copilot/agents`. That folder currently contains the active OmO/Ralph agent
files shown in the Copilot agent picker, including `oh-my-openagent.agent.md`,
`omo-hephaestus.agent.md`, `omo-prometheus.agent.md`, `omo-oracle.agent.md`,
`omo-librarian.agent.md`, `omo-momus.agent.md`, `omo-visual-engineer.agent.md`,
and `ralph-copilot.agent.md`.

## How To Use

Select `Oh My OpenAgent` in the Copilot agent picker for the full user-level OmO
runtime. The workspace files then add OET-specific rules and helper agents.

Copilot should automatically load `AGENTS.md`, `.github/copilot-instructions.md`,
and any matching targeted instructions during coding, testing, review, and
commit-message generation. You do not need slash prompts for normal work. Ask
naturally, for example "fix this build error", "add this feature", "review this
diff", or "make this page work".

Slash prompts remain available only as optional shortcuts when you want to force
a specific workflow:

- `/ultrawork` for a full explore-plan-delegate-implement-review-verify loop
- `/ralph-loop` for PRD.md / PROGRESS.md filesystem-memory execution
- `/start-work` for an implementation plan
- `/scout` for read-only exploration
- `/code-review` or `/security-review` for reviews
- `/build-fix` for failures
- `/qa-validate` for validation selection
- `/handoff` for continuation notes
- `/hyperplan` for adversarial planning before risky work

Workspace custom agents are intentionally scoped to this project and should
prefer `AGENTS.md` and domain docs over generic framework advice. The main entry
point remains the installed `Oh My OpenAgent` agent unless you explicitly select
an OET workspace agent.

## Full-Power Settings

- chat.agentFilesLocations includes both .github/agents for workspace OET
  agents and ~/.copilot/agents for user-level OmO agents.
- `chat.subagents.allowInvocationsFromSubagents` is enabled for nested
  coordinator/worker workflows.
- `chat.autopilot.enabled` is enabled so the Autopilot permission level is
  available in the Chat UI.
- `chat.agent.maxRequests` is set to `300` for longer autonomous sessions.
- Tool approval level is still controlled by the Copilot Chat permission picker;
  use Bypass Approvals or Autopilot only when you accept the security tradeoff.

## Maintenance

- Keep instructions concise. Add detail to domain docs rather than duplicating
  large specs into Copilot files.
- Review `.vscode/mcp.json` before adding any MCP service that needs a token,
  account, browser login, external billing, or package execution outside the
  repo lockfile.
- Prefer lightweight, targeted checks on the Windows host. Heavy production
  builds run on GitHub Actions; the VPS only pulls prebuilt images.
- Use the Chat diagnostics/customizations UI to confirm which agents, prompts,
  and instructions are loaded.