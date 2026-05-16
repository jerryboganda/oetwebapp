# Production Smoke Artifact Policy

Production deploys must leave enough evidence for a reviewer to understand exactly what shipped, what was tested, and how to roll back.

## Required Artifacts

- Git SHA and release tag.
- Frontend and backend build logs.
- Playwright smoke HTML report and screenshots.
- Production smoke output from `docs/PROD-SMOKE-RUNBOOK.md`.
- Reading/media smoke output from `scripts/deploy/reading-media-smoke.sh`.
- SBOM JSON and SCA JSON.
- Signed release evidence manifest (`checksums.sha256` + `checksums.sha256.asc`) and verification output proving `release-metadata.env git_sha` matches deployed `HEAD`.
- Immutable Docker image digest refs from `image-digests.env`.
- Active/previous-good release record from `.deploy/previous-good.env` and the
  active blue/green slot record from `.deploy/active-slot.env`.
- Desktop/mobile artifact checksums when those platforms are included.
- Deployment pre-flight backup ID and post-deploy verification output.

## Retention

- Keep release evidence for at least 90 days.
- Keep production database backup references according to the deployment retention policy.
- Keep signed desktop/mobile checksums for the lifetime of the released version.

## Failure Handling

A production smoke failure after deploy must produce one of these outcomes:

- Roll back using the recorded previous-good SHA, slot, evidence bundle, image
  digests, and pre-flight backup.
- Hold release and document an accepted risk with owner, mitigation, and expiry.
- Mark the release failed and keep artifacts for incident review.

## Artifact Safety

Do not upload secrets, raw tokens, cookies, private keys, keystores, provisioning profiles, `.env*` files, or unredacted provider responses. If a diagnostic file contains sensitive data, redact it before upload and note the redaction in the evidence checklist.
