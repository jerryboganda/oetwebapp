# Superpowers Copilot Install

- Installed `obra/superpowers` v5.1.0 commit `6fd4507` on 2026-05-31.
- Official Copilot CLI plugin installed: `copilot plugin install superpowers@superpowers-marketplace` (14 skills).
- Global VS Code Copilot agent: `~/.copilot/agents/superpowers.agent.md`.
- Global skills: `~/.copilot/skills/*` (14 Superpowers skills).
- Project VS Code Copilot agent: `.github/agents/superpowers-oet.agent.md`.
- Project skills: `.github/skills/*` (14 Superpowers skills).
- VS Code setting `chat.agentFilesLocations` already includes `~/.copilot/agents`; workspace `.github/agents` is auto-discovered.
- Smoke tests passed:
  - `copilot -s -p "Let's make a react todo list..."` routed to `brainstorming` first.
  - `copilot --agent "Superpowers" -s -p ...` confirmed the global Superpowers agent and routed a failing-test task to `systematic-debugging`.
  - `copilot --agent "OET Superpowers" -s -p ...` confirmed the project-level OET Superpowers agent and routed OET feature work to `brainstorming` while obeying `AGENTS.md`.
- Automatic routing was added after install: global user instruction `automatic-agent-routing.instructions.md` plus OET repo instruction updates tell agents to infer and use Superpowers, Copilot Plugins, Fabric agents, and relevant skills without the user naming them.
- Boundary: upstream official GitHub Copilot CLI plugin is separate from the VS Code custom-agent adapter. VS Code uses the installed `.agent.md` files plus `.github/skills` / `~/.copilot/skills`.
