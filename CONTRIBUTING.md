# Contributing to OET Prep Platform

Thank you for contributing. This repository is optimized for agent-assisted development (GitHub Copilot, Claude Code, Codex, and other AI IDEs).

## Quick start

1. Install dependencies:
   ```powershell
   pnpm install
   ```
2. Start the local stack:
   ```powershell
   .\start-dev.ps1
   ```
3. Run focused validation before committing:
   ```powershell
   pnpm exec tsc --noEmit
   pnpm run lint
   pnpm test
   pnpm run backend:build
   pnpm run backend:test
   ```

## Development model

- Read `AGENTS.md` and `.github/copilot-instructions.md` first — they contain the repo-wide rules.
- Load matching `.github/instructions/*.instructions.md` for the files you touch.
- Keep edits focused and preserve unrelated changes.
- Use the smallest validation command that covers your change.
- Update docs when behavior changes.
- Never commit secrets, `.env*` files, or credentials.

## AI-assisted contributions

- When using AI agents, share the relevant instruction files and docs as context.
- Review AI-generated changes for security, correctness, and compliance with `AGENTS.md`.
- AI changes must pass the same focused validation as manual changes.

## Reporting issues

Open a GitHub Issue with:
- A clear description and reproduction steps.
- The affected area (frontend, backend, desktop, mobile, infrastructure).
- Any relevant logs or screenshots.

## Security

See [SECURITY.md](./SECURITY.md) for vulnerability reporting and security practices.
