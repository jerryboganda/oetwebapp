# Awesome Copilot Install

- Installed `github/awesome-copilot` clone from commit `9b74459`.
- Copied all 349 upstream skills into both `.github/skills/*` and `~/.copilot/skills/*`.
- Copied 214 upstream specialist agents into both `.github/agents/awesome-*.agent.md` and `~/.copilot/agents/awesome-*.agent.md` as hidden helpers (`user-invocable: false`) to avoid flooding the visible agent picker.
- Archived upstream instructions/plugins/workflows at `.github/awesome-copilot/` and `~/.copilot/awesome-copilot/` for on-demand loading.
- Visible wrappers: global `Awesome Copilot` at `~/.copilot/agents/awesome-copilot.agent.md`; project `OET Awesome Copilot` at `.github/agents/awesome-copilot-oet.agent.md`.
- Automatic routing instructions include Awesome Copilot; load only the relevant skill/instruction/specialist for a task rather than activating the entire collection.
- Marketplace plugin batch log: `%TEMP%\awesome-copilot-install-log.txt`; result JSON: `%TEMP%\awesome-copilot-install-results.json`.
- Installed 85/86 `awesome-copilot` marketplace plugins successfully. The `ai-ready@awesome-copilot` marketplace pin is stale (`johnpapa/ai-ready` SHA not present upstream), so `ai-ready` was installed via supported direct repo fallback `copilot plugin install johnpapa/ai-ready`; its skill is also present under both skill scopes.
