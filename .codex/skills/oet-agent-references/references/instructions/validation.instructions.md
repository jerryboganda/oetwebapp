---
name: "Local Validation On Host"
description: "Use when running builds, tests, installs, lint, type-checks, Playwright, dotnet, or scripts to validate changes in the OET repo. Defines where validation runs and the command ladder."
applyTo: "package.json,pnpm-lock.yaml,Dockerfile,docker-compose*.yml,backend/**,app/**,components/**,contexts/**,hooks/**,lib/**,tests/**,playwright*.config.ts,vitest.config.ts,scripts/**"
---

# Local Validation On Host

All local validation (installs, type-check, lint, unit tests, builds, Playwright, dotnet) runs
**directly on the Windows host** using PowerShell or `cmd`. Do not require Docker Desktop for
validation, and never run validation on the production VPS.

Host toolchain is installed and verified: Node 22.x, pnpm 10.33.0, .NET 10.x.

## Command ladder (run the smallest credible subset first)

```powershell
pnpm exec tsc --noEmit      # type-check frontend
pnpm run lint               # eslint
pnpm test                   # vitest unit tests
pnpm run build              # next build (heaviest frontend check)
pnpm run backend:build      # dotnet build
pnpm run backend:test       # dotnet test
pnpm run check:encoding     # encoding guard
pnpm run test:e2e:smoke     # Playwright smoke (when UI flows change)
```

If a script misbehaves under PowerShell quoting, fall back to `cmd /c "pnpm run <script>"`.

## Scope & safety

- Choose validation by risk: docs-only changes need no build; behavior changes need the matching
  type-check/test/build; broad refactors warrant the fuller ladder.
- Report exactly what ran, what did not run, and any remaining risk.
- The VPS is deploy-only. Storage persistence, protected volumes, and production container rules are a
  deployment/runtime invariant — see `deployment.instructions.md` — not a local validation concern.
- Take local, reversible actions freely. Get approval before destructive, networked, production, or
  credential-adjacent commands.
