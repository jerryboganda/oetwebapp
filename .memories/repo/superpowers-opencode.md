# Superpowers OpenCode Install

- Installed `obra/superpowers` v5.1.0 (same ref as the existing Copilot install at commit `6fd4507`).
- Opencode plugin entry added at `opencode.json:4` — `superpowers@git+https://github.com/obra/superpowers.git#v5.1.0` (project-scoped, version-controlled).
- Opencode plugin auto-registers 14 skills via its `config` hook and injects bootstrap via `experimental.chat.system.transform` — no `skills.paths` entry needed for Superpowers.
- Pre-existing `opencode.json` `skills.paths: [".claude/skills"]` and `permission.skill: "allow"` preserved; total skills after restart: 14 Superpowers + 4 design = 18.
- No stale symlink-based install found in `~/.config/opencode/` (only `.gitignore` and `opencode.jsonc`), so no migration cleanup was needed.
- Windows fallback (only if the `git+https` spec fails to resolve on this host — known upstream issue, per `.opencode/INSTALL.md`):
  1. `npm install superpowers@git+https://github.com/obra/superpowers.git#v5.1.0 --prefix "$HOME\.config\opencode"`
  2. Swap the plugin entry in `opencode.json` to `"~/.config/opencode/node_modules/superpowers"`.
  3. Restart opencode.
- Verify after restart, in order, stopping at the first green:
  1. `opencode run --print-logs "Tell me about your superpowers" 2>&1 | Select-String -Pattern superpowers` — expect a non-empty hit on the plugin loader line.
  2. From inside opencode: `use skill tool to list skills` — expect the 14 `superpowers/*` skills plus the 4 `.claude/skills` design skills.
  3. `use skill tool to load superpowers/using-superpowers` — confirm the bootstrap loads.
- Boundary: this opencode install is independent of the Copilot install at `~/.copilot/installed-plugins/superpowers-marketplace/superpowers/skills/*`; both share the same upstream ref so plan-doc references like `superpowers:subagent-driven-development` resolve identically.
