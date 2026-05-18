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
- Accessibility: `release-evidence/accessibility-signoff.env` with automated axe counts and manual NVDA/VoiceOver signoff for the required launch flows.
- SBOM: `release-evidence/sbom.json`.
- SCA report: `release-evidence/sca.json`.
- Tool versions: `release-evidence/tool-versions.txt` with local scanner versions or version-tagged fallback images.
- Evidence checksums: `release-evidence/checksums.sha256` covering metadata, tool versions, SBOM, SCA, and accepted-risk files when applicable.
- Immutable image digests: `release-evidence/image-digests.env` with `WEB_IMAGE`, `API_IMAGE`, `DB_BACKUP_IMAGE`, and `ROUTER_IMAGE` pinned to `@sha256:<digest>`.
- Production evidence signature: detached GPG signature for `release-evidence/checksums.sha256` plus expected signer fingerprint from the protected environment when `EVIDENCE_ENV=production`.
- Production deploy provenance: `scripts/evidence-verify.sh` output with `EXPECTED_GIT_SHA` set to the deployed `HEAD`.
- Build artifacts: artifact names and SHA256 checksums.
- Desktop artifacts: Windows, macOS, and Linux installer/package names plus `npm run desktop:checksums:verify` output for `dist/desktop/desktop-checksums.sha256`.
- Desktop update metadata: `npm run desktop:update-metadata:verify` output for `latest*.yml`, proving HTTPS/non-loopback update references and matching local artifacts.
- Desktop remote API guard: public desktop builds must record `ELECTRON_REQUIRE_REMOTE_API=true` with a non-loopback HTTPS production API URL, certificate pins, and no bundled/local API fallback unless a signed accepted-risk record exists.
- Desktop packaged smoke: `npm run test:e2e:desktop:packaged` output for the signed package/executable on each target OS.
- Desktop signing/notarization: Windows Authenticode/timestamp evidence, macOS Developer ID/notarization ticket evidence, and Linux package install/smoke evidence.
- Desktop OAuth/deep links: proof that `oet-prep://` cold-start and second-instance callback links open the packaged app and route to the intended screen.
- Deployment pre-flight: `scripts/deploy/pre-flight.sh` output.
- Previous-good release record: `.deploy/previous-good.env` from the host after successful deploy.
- Active blue/green slot record: `.deploy/active-slot.env` from the host after successful deploy.
- Post-deploy smoke: `scripts/deploy/post-deploy-verify.sh` output.
- Observability smoke: `scripts/observability-smoke.sh` output.
- Rollback checkpoint: backup ID and rollback owner.
- Destructive migration approval package when applicable: maintenance window, verified backup ID, non-live restore drill ID, and owner approval.

## Accessibility Signoff Manifest

Production evidence verification fails closed unless `accessibility-signoff.env`
is present, checksummed, and all required launch flows are marked `pass`.
For the `Verify Release Artifacts` workflow, paste this file as the
`accessibility_signoff_base64` input after encoding it with
`base64 -w0 accessibility-signoff.env`.

```env
ACCESSIBILITY_AXE_CRITICAL=0
ACCESSIBILITY_AXE_SERIOUS=0
ACCESSIBILITY_NVDA_SIGNOFF=pass
ACCESSIBILITY_VOICEOVER_SIGNOFF=pass
ACCESSIBILITY_AUTH_SIGN_IN=pass
ACCESSIBILITY_LEARNER_DASHBOARD=pass
ACCESSIBILITY_LEARNER_BILLING=pass
ACCESSIBILITY_LEARNER_IMMERSIVE_FLOW=pass
ACCESSIBILITY_EXPERT_REVIEW_SUBMIT=pass
ACCESSIBILITY_ADMIN_AUDIT_LOGS=pass
ACCESSIBILITY_ADMIN_USER_CREDIT=pass
ACCESSIBILITY_REVIEWER=<name>
ACCESSIBILITY_REVIEWED_AT_UTC=<YYYY-MM-DDTHH:MM:SSZ>
ACCESSIBILITY_PLAYWRIGHT_REPORT=<artifact-or-run-url>
ACCESSIBILITY_MANUAL_EVIDENCE_URL=<recording-notes-or-checklist-url>
```

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
