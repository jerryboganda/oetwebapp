# BMad Method Auto-Routing

BMad Method v6 is installed globally at `%USERPROFILE%\_bmad\` and exposes 66
skills under `%USERPROFILE%\.agents\skills\bmad-*` (modules: **bmm**, **bmb**,
**tea**, **cis**, plus **core**). Detailed routing rules live in the global
user instructions file `bmad-routing.instructions.md` (under
`%APPDATA%\Code\User\prompts\`) and apply to every workspace.

In this repository:

- For every prompt, evaluate which `bmad-*` skill(s) apply (use the skill
  `description` field) and run them
  **in parallel** with the active mode's specialists (e.g. agency-* subagents)
  whenever the work is independent.
- Repository instructions in this `AGENTS.md` and `.github/` tree always
  **win** over BMad defaults when they conflict — BMad augments, it does not
  override mission-critical OET rulebook / scoring / AI-gateway / content
  upload / reading-authoring / grammar / pronunciation / conversation
  invariants documented in `AGENTS.md`.
- Quick refresh / update of BMad: `cd $env:USERPROFILE; npx bmad-method install
  --yes --directory $env:USERPROFILE --modules bmm,bmb,cis,tea --tools
  github-copilot`