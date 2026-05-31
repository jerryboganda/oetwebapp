---
name: "OET Agentic Workflow"
description: "Use when: running Oh My OpenAgent, OmO, ultrawork, Ralph Loop, multi-agent orchestration, autonomous coding, planning, implementation, review, or handoff in this repository."
---

# OET Agentic Workflow

- Treat the user-level `~/.copilot/agents` Oh My OpenAgent installation as the active Copilot runtime when selected in VS Code.
- Treat root `AGENTS.md` as the always-on repository contract. Do not create a second repo-wide `.github/copilot-instructions.md` unless the user explicitly chooses to replace `AGENTS.md`.
- For non-trivial work, run the OmO loop: intent gate, fact-gathering research, decision-complete plan, todo list, focused implementation, targeted validation, independent review, concise handoff.
- For broad `ultrawork` tasks, plan before editing and then continue automatically into implementation; do not stop at the plan or rely on manual handoff buttons unless a genuine user decision blocks correctness.
- Use focused specialist lanes when useful: explorer, planner, implementer, reviewer, security, visual, QA, and Ralph coordinator.
- The user should not need to remember or request installed agents, skills, or plugins by name. Automatically route to Superpowers skills, OET Superpowers, official Copilot Plugins skills, OET Copilot Plugins, Awesome Copilot assets, OET Awesome Copilot, and Fabric specialist agents when the task domain matches.
- Superpowers routing: use brainstorming for new features/design, systematic-debugging for failures, test-driven-development for behavior changes, writing-plans for multi-step implementation, requesting-code-review for meaningful review, and verification-before-completion before claiming success.
- Official Copilot Plugins routing: use dependency-scanning or secret-scanning for security review, spark-app-template for GitHub Spark app scaffolding, workiq for WorkIQ tasks, and Fabric / Power BI skills or agents for Microsoft Fabric workflows.
- Awesome Copilot routing: use the installed Awesome skills, hidden `Awesome ...` specialists, and archived instructions for framework-specific implementation, modernization, testing, documentation, frontend/backend patterns, DevOps/cloud, architecture, and productivity workflows. Load the relevant asset only; do not flood a task with the whole collection.
- Use web search or official web docs for current framework/library/API/platform behavior when repo evidence is not enough, then reconcile external findings against `AGENTS.md` and local code.
- Ask questions only when a missing decision changes correctness. If a popup question tool is available, use it and include your recommended default option.
- Keep decisions conservative and repo-specific. Prefer existing code patterns, `AGENTS.md`, `docs/agent-operating-model.md`, and relevant domain docs over generic framework advice.
- Preserve user work. Inspect before editing, never reset or clean the tree, never overwrite unrelated changes, and serialize edits when multiple agents might touch the same file.
- Do not claim OpenCode-only features such as hashline edits, tmux panes, OpenCode hooks, provider fallback chains, or OmO MCPs are available inside Copilot unless the actual tool is present.