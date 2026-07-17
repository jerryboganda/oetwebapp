# Scripts And Automation (`scripts/`)

Build, deployment, QA, diagnostic, and local-development automation for the OET Prep Platform.

## Layout

- **`deploy/`** — VPS and container deployment orchestration.
- **`qa/`** — Playwright matrix runner, accessibility sign-off helpers, local-stack assertions.
- **`repomix/`** — Repository packing hooks for AI context generation.
- **Root scripts** — One-off diagnostics, smoke tests, and dev helpers (PowerShell `*.ps1` and Node `*.mjs`).

## Rules

- PowerShell for Windows-host operations; Node.js (`*.mjs`) for cross-platform logic.
- No hard-coded secrets or production credentials.
- Destructive or production-affecting scripts require explicit approval.
- Keep scripts focused and compose larger workflows from smaller ones.

## Running scripts

```powershell
# PowerShell
powershell -ExecutionPolicy Bypass -File .\scripts\<name>.ps1

# Node
node ./scripts/<name>.mjs
```
