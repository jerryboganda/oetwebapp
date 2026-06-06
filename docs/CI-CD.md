# CI/CD Pipeline Map

This repo is **cloud-only**: every build, lint, format, security scan, test, and deploy
runs on GitHub Actions, triggered automatically on push/PR. The owner's machine is used
strictly for writing code — nothing is built, linted, formatted, scanned, tested, or
deployed locally.

## Workflows

| Workflow file | Stage | Triggers | What it does | Blocking? |
| --- | --- | --- | --- | --- |
| `qa-smoke.yml` | Build + lint + test (full) | push to `main`/`master`, PR, manual | Backend `.NET` tests, frontend `tsc`/lint/`vitest`, Playwright e2e — the primary gate | Yes |
| `ci-fast.yml` | Build + lint + test (fast) | push to any branch **except** `main`/`master` | Per-push fast feedback: frontend `tsc` + lint + `check:encoding` + `vitest` + `next build`; backend `.NET` build + test | Yes |
| `sbom-sca.yml` | SBOM / SCA | PR, push to `main`, manual | Generates SBOM and runs grype vulnerability scan (threshold configurable on dispatch) | Yes (grype gate) |
| `security-sast.yml` | SAST | push to `main`, PR, weekly cron (Mon 05:00 UTC), manual | Semgrep scan (`p/default` + `p/security-audit`), uploads SARIF artifact | No (advisory) |
| `security-deps.yml` | Dependency vulns | push to `main`, PR, weekly cron (Mon 06:00 UTC), manual | `pnpm audit --audit-level=high` + `dotnet list package --vulnerable --include-transitive` | No (advisory) |
| `secret-scan.yml` | Secret scan | push, PR, manual | TruffleHog full git-history scan (`--results=verified,unknown`) | No (advisory) |
| `code-format.yml` | Format (cloud auto-fix) | PR, manual | `eslint --fix` + `dotnet format`, commits the result back to the PR branch | No (auto-fix) |
| `deploy.yml` | Deploy | push to `main`, manual | Builds web (Next.js) + API (.NET) images off-box, pushes to GHCR, SSH to prod VPS for health-gated blue/green redeploy | Yes (health gate; broken commit not promoted) |
| `mobile-ci.yml` | Mobile build/test | push to `main` + PR (mobile paths), manual | Capacitor/Android/iOS build + checks for mobile-related changes | Yes (on mobile changes) |
| `mobile-release.yml` | Mobile release | manual (platform + version inputs) | Builds Android/iOS release artifacts | N/A (manual) |
| `desktop-release.yml` | Desktop release | manual, tag push `v*.*.*-desktop` | Builds Windows Electron (NSIS) installer; publishing to Releases is opt-in | N/A (manual/tag) |
| `speaking-ci.yml` | Speaking module CI | PR + push (speaking paths) | Speaking-tagged backend tests + frontend lint for the speaking module | Yes (on speaking changes) |
| `speaking-e2e.yml` | Speaking e2e | nightly cron (03:00 UTC), manual | Playwright e2e against staging | No (scheduled) |
| `speaking-a11y.yml` | Speaking a11y | nightly cron (03:30 UTC), manual | axe accessibility scan against staging | No (scheduled) |
| `speaking-load.yml` | Speaking load | weekly cron (Mon 04:00 UTC), manual | k6 load tests against staging API | No (scheduled) |
| `speaking-content-batch.yml` | Speaking content | manual (profession/count/difficulty inputs) | Batch-generates speaking content cards | N/A (manual) |
| `elevenlabs-realtime-stt-live-smoke.yml` | STT smoke | manual (double-confirm inputs) | Live ElevenLabs realtime STT smoke test (consumes paid quota) | N/A (manual) |
| `oet-gapclosure-validation.yml` | Branch validation | push to `ci/oet-gapclosure-validation`, manual | Validates gap-closure work on clean runners; never touches `main` | Yes (on that branch) |
| `backend-security-remediation.yml` | Branch validation | push to `fix/backend-security-remediation`, manual | Backend-focused xUnit run for the remediation branch | Yes (on that branch) |

## How validation works

- **Everything runs in the cloud.** All building, linting, formatting, security-scanning,
  testing, and deploying happens on GitHub Actions — automatically on push/PR. Contributors
  only write code; no local toolchain run is required to land a change.
- **Formatting is auto-applied in the cloud.** `code-format.yml` runs `eslint --fix` and
  `dotnet format` on each PR and commits the result straight back to the PR branch, so nobody
  needs to run a formatter locally. (It skips forks, which have no write token.)
- **Security scans are advisory initially.** Semgrep (`security-sast.yml`), dependency audits
  (`security-deps.yml`), and TruffleHog (`secret-scan.yml`) report findings without failing the
  build, so pre-existing issues don't block work. Make any of them a blocking gate by:
  - removing the trailing `|| true`, and
  - adding `--error` (Semgrep) / `--fail` (TruffleHog), or dropping `|| true` on the audit step,
  once the existing findings are triaged / the history is clean.
- **GitHub Advanced Security (GHAS) note.** This is a private repo without GHAS, so the security
  workflows use runner-based tools (Semgrep, `pnpm audit`, `dotnet list --vulnerable`, TruffleHog)
  rather than GHAS-only features. If GHAS is enabled on the repo, you can additionally wire up
  **CodeQL** and **dependency-review-action** (and native code-scanning SARIF upload for the
  Semgrep results).
