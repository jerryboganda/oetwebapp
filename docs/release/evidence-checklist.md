# Release Evidence Checklist

Use this checklist for every staging or production release. Attach the filled checklist, generated artifacts, and signoff notes to the release record before marking any launch-gate item complete.

## Release Metadata

- Release version:
- Git SHA (must match `release-evidence/release-metadata.env git_sha`):
- Branch/tag:
- Environment: staging | production
- Release owner:
- Technical approver:
- Product approver:
- Deployment window UTC:

## Required Evidence

For each item, record status, owner, evidence file or link, and notes.

- TypeScript check: `npx tsc --noEmit` output or CI run.
- ESLint: `npm run lint` output or CI run.
- Unit tests: full Vitest report.
- Backend tests: `dotnet test backend/OetLearner.sln` report.
- Production build: `npm run build` report.
- Backend build: `npm run backend:build` or `dotnet build` report.
- Playwright smoke: HTML report artifact.
- Prod smoke: `docs/PROD-SMOKE-RUNBOOK.md` run output.
- Reading/media smoke: `scripts/deploy/reading-media-smoke.sh` output for disabled paper mode, entitled PDF access, protected media denial, and legacy route 410 checks.
- Accessibility: automated axe plus manual signoff.
- SBOM: `release-evidence/sbom.json`.
- SCA report: `release-evidence/sca.json`.
- Tool versions: `release-evidence/tool-versions.txt` with local scanner versions or version-tagged fallback images.
- Evidence checksums: `release-evidence/checksums.sha256` covering metadata, tool versions, SBOM, SCA, and accepted-risk files when applicable.
- Production evidence signature: detached GPG signature for `release-evidence/checksums.sha256` plus expected signer fingerprint from the protected environment when `EVIDENCE_ENV=production`.
- Production deploy provenance: `scripts/evidence-verify.sh` output with `EXPECTED_GIT_SHA` set to the deployed `HEAD`.
- Build artifacts: artifact names and SHA256 checksums.
- Deployment pre-flight: `scripts/deploy/pre-flight.sh` output.
- Post-deploy smoke: `scripts/deploy/post-deploy-verify.sh` output.
- Observability smoke: `scripts/observability-smoke.sh` output.
- Rollback checkpoint: backup ID and rollback owner.

## Secret Safety Checks

- No plaintext provider, signing, database, OAuth, or payment secrets appear in logs.
- Admin UI provider configuration stores only secret references or masked status.
- Secret-bearing changes include a redaction/no-plaintext review note.
- CI artifacts exclude `.env*`, cookies, tokens, private keys, keystores, and provisioning profiles.

## Closeout

- Open P0 release blockers:
- Accepted risks with owner and expiry:
- Rollback threshold:
- Rollback approver:
- Final decision: Go | Hold | Monitor
