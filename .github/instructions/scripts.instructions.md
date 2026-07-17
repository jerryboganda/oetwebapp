---
name: "Scripts And Automation"
description: "Use when editing build, deploy, QA, utility, or automation scripts under scripts/."
applyTo: "scripts/**"
---

# Scripts And Automation (`scripts/`)

This folder contains build, deployment, QA, diagnostic, and local-development automation.

## Responsibilities

- `scripts/deploy/**` — VPS and container deployment orchestration.
- `scripts/qa/**` — Playwright matrix runner, accessibility sign-off helpers, local-stack assertions.
- `scripts/repomix/**` — repository packing and context-generation hooks.
- Root-level `scripts/*.ps1` / `*.mjs` — one-off diagnostics, smoke tests, and dev helpers.

## Rules

- Prefer PowerShell for Windows-host operations and Node.js (`*.mjs`) for cross-platform logic.
- Scripts must never hard-code secrets or production credentials. Read from env or `IRuntimeSettingsProvider`.
- Destructive or production-affecting scripts require explicit user approval and backup awareness.
- Keep scripts focused; compose larger workflows by calling smaller scripts.
- Document usage and required env variables at the top of each script.

## Validation

- Run the script manually in a safe (dev/local) environment before committing changes.
- For PowerShell scripts, test with `powershell -ExecutionPolicy Bypass -File .\scripts\<name>.ps1`.
- For Node scripts, test with `node ./scripts/<name>.mjs`.
