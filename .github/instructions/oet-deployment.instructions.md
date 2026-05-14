---
description: "Use when editing Docker, CI/CD, release workflows, deployment docs, production/staging configuration, Electron packaging, or Capacitor build surfaces."
name: "OET Deployment"
applyTo: ["Dockerfile*", "docker-compose*.yml", ".github/workflows/*.yml", "scripts/deploy/**", "DEPLOYMENT.md", "electron/**", "capacitor.config.ts", "android/**", "ios/**"]
---
# OET Deployment Instructions

- Preserve production/staging separation. Do not point staging at production secrets or databases.
- Never delete or recreate `oetwebsite_oet_postgres_data` without explicit backup instructions and approval.
- Keep Docker health checks aligned with `GET /api/health` and the documented production topology.
- Production deploys are tag/manual controlled. Do not make normal pushes auto-deploy production.
- Staging GitHub deploys stay gated by `ENABLE_STAGING_DEPLOY=true`.
- For Electron, preserve standalone Next output assumptions and desktop backend runtime resources.
- For Capacitor, validate web build and sync assumptions before changing native config.
- Release workflow changes should include a rollback or failure-handling note.