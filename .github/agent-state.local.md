# Agent State - Mass Rebrand to OET with Dr Hesham

Last updated: 2026-07-17

## Goal
Rename the entire project everywhere across web, desktop, mobile, backend, docs, and deployment configs to "OET with Dr Hesham", with contact number +44 7961 725989 and support email support@oetwithdrhesham.co.uk.

## Implemented This Run

- Rebased the rebrand onto the latest `origin/main` (which had diverged/force-pushed).
- Replaced old brand strings across ~1,900 files:
  - "OET Prep" / "OETPrep" / "OET with Dr Ahmed Hesham" → "OET with Dr Hesham"
  - "OetLearner" → "OetWithDrHesham"
  - "oet-prep" → "oet-with-dr-hesham"
  - "com.oetprep" → "com.oetwithdrhesham"
  - "oet-learner" / "oet_learner" / "OET Learner" → "oet-with-dr-hesham" equivalents
  - "support@edu80.app" → "support@oetwithdrhesham.co.uk"
- Renamed backend solution and project folders/files to `OetWithDrHesham.Api`.
- Renamed Android Java package folder to `com/oetwithdrhesham/learner`.
- Updated `capacitor.config.ts`, `src-tauri/tauri.conf.json`, iOS/Android manifests, Docker compose files, GitHub Actions, deployment docs, and environment examples.
- Updated support constants and added `+44 7961 725989` phone/WhatsApp support on `app/support/page.tsx` and `app/(auth)/privacy/page.tsx`.
- Restored grammar spec IDs (`oetpreposition-*`) that were mangled by substring replacement.
- Preserved real account holder names and generic OET-exam references where appropriate.

## Files Touched (Representative)

- `package.json`, `pnpm-lock.yaml`
- `capacitor.config.ts`, `src-tauri/tauri.conf.json`, `next.config.ts`
- `backend/OetWithDrHesham.sln` and `backend/src/OetWithDrHesham.Api/`
- `backend/tests/OetWithDrHesham.Api.Tests/`
- `android/app/src/main/java/com/oetwithdrhesham/learner/`
- `ios/App/App.xcodeproj/project.pbxproj`
- `lib/auth/support.ts`
- `app/support/page.tsx`, `app/(auth)/privacy/page.tsx`, `app/peer-review/page.tsx`
- `.github/workflows/*`, `.github/actions/setup-oet-stack/action.yml`
- `docker-compose*.yml`, `.env.production.example`
- `AGENTS.md`, `README.md`, `DEPLOYMENT.md`, `DEPLOY-MANUAL.md`
- `.github/agent-state.local.md`

## Validation

- `pnpm exec tsc --noEmit`: passed.
- `pnpm run lint`: passed (exit 0; pre-existing warnings only).
- `pnpm run backend:build`: succeeded (55 warnings, 0 errors).

## Blockers / Remaining Risk

- Full backend xUnit test suite and frontend unit tests were not run per the OET owner directive's lightweight-check guidance.
- iOS bundle ID and provisioning profile names were updated in code, but matching changes in the Apple Developer portal/certificates/provisioning profiles are manual external steps.
- Android signing keystore aliases and Google Play store listings remain external manual steps.
- `origin/main` had force-pushed/divergent history, so the branch was reset to `origin/main` and the rebrand reapplied as a single commit.

## Next Step

Commit and force-push the rebased rebrand branch, then merge PR #125 to `main`.
