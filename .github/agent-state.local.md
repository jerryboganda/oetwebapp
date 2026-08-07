# Current Task - Production compute offload to GitHub Actions

Last updated: 2026-08-08

## Outcome

- Audited the OET release path and a read-only VPS snapshot. Web/API/backup
  image builds, CI validation, and EF migration-script generation are now
  explicitly owned by GitHub Actions; the VPS retains only data-local and
  runtime work.
- Added the Actions migration gate, a stdin-only PostgreSQL applicator,
  production startup-migration policy, image-only rollout guards, and the
  operator audit/limits document. The deploy job now streams only the small
  rollout/Compose/env-validator bundle and never syncs the source repository
  to the VPS.

## Validation

- `bash -n` passed for the new and touched deployment scripts.
- `bash scripts/deploy/verify-image-only-rollout.sh` passed.
- `bash scripts/deploy/verify-compute-offload.sh` passed, including the
  no-source-sync assertion.
- `git diff --check` passed for the implementation paths.
- The focused .NET migration-policy test was attempted but stalled locally
  before useful test output; no local backend pass is claimed. GitHub Actions
  remains authoritative for compilation and full backend/image gates.
- GitHub Actions run `31216996359` for commit `1df02d008817e2ee7b418d95e8b4044fe60a0080`
  passed all three image builds, off-box EF restore/build/idempotent SQL
  generation, migration application, and the artifact-only blue/green deploy.
- Post-deploy read-only checks returned `app_api_health_http=200` and
  `api_ready_http=200`; green API/web/backup containers are healthy and use
  the `1df02d008...` GHCR artifacts. No source checkout/sync occurred on the
  VPS rollout path.

## Next step

Preserve the unrelated `.codex/config.toml`, `.superpowers/`, and concurrent
product/UI/API changes. Backup restore parity/drill and authenticated learner
acceptance remain manual follow-ups; production runtime OCR/PDF/TTS/AI and
queue workloads remain data-local by design.
owner-side boundaries.

# Current Task - Critical Course Video Access Rule

Last updated: 2026-08-07

## Outcome

- Added a PostgreSQL-only corrective migration that applies the 18 canonical
  New/Old Crash Course Arabic Writing exclusions to every `full-%` plan and
  immutable plan-version mapping, while removing those IDs from explicit
  includes and preserving unrelated overrides.
- Added focused entitlement and migration-shape regression coverage plus the
  approved Superpowers design and execution plan.

## Validation

- Migration static check passed: 18 canonical IDs match the existing rule,
  both plan tables are targeted, and JSONB include/exclude merge operations are
  present.
- Focused entitlement tests passed 4/4; the migration-shape test compiled but
  needed a provider-guard correction, then the local clean rebuild stalled.
  CI run 31192330082 passed both API/web image builds and the deploy job.
- VPS is at `31bd84b61`; migration history contains
  `20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses`.
  Read-only SQL confirms every full-course plan/version has 0 blocked includes
  and 18 blocked excludes, all six Crash Course plan/version rows retain 18
  includes plus Listening/Reading/Speaking scope, all 18 videos remain tagged,
  and December/February writing content remains present outside the exclusion.
- API and web public health endpoints both returned HTTP 200; the repository
  helper's internal `oet-web:3000` probe is stale for the nginx proxy container.

## Next step

No further in-scope deployment action remains. Manual learner login/browser
acceptance with supplied test accounts remains an owner-side boundary; no
credentials or customer data were accessed.

# Current Task - Device exemption policy mismatch and admin list management

Last updated: 2026-08-07

## Outcome

- Auth sign-in, risk step-up, trusted-device checks, and stale device-OTP
  resend/verify now share the persisted exemption list and match the linked
  learner/expert profile email as well as the auth-account aliases.
- The prior hidden hard-coded exemption defaults were removed so deleting an
  address in the admin list genuinely revokes its exemption.
- The fourth supplied Gmail address is included in the seed and additive
  backfill migration; the admin runtime-settings field is now a searchable,
  add/delete table with staged Save All behavior.

## Validation

- `pnpm exec vitest run app/admin/settings/RuntimeSettingsClient.test.tsx --reporter=dot`: 11/11 passed.
- The focused backend test was added but could not complete locally because
  another agent's `CrashCourseVideoAccessRuleTests` process held shared .NET
  build artifacts; CI remains the backend compile/test gate.

## Next step

Run the final scoped verification, stage only the implementation/tests,
migration, and this state file, then commit and push `main`. Preserve the
unrelated `.codex/config.toml`, `.superpowers/`, and concurrent work.

# Current Task - Trusted-device cooldown false-positive incident

Last updated: 2026-08-07

## Outcome

- Web device identity now persists in both localStorage and an opaque
  first-party cookie, allowing privacy-oriented in-app-browser launches to
  recover the same identity instead of minting a new UUID each time.
- Device-change cooldown now counts only OTP-approved replacement identities;
  the initial bootstrap no longer consumes the learner's change budget.
- Added focused browser and backend regression coverage plus policy/runbook
  documentation for the corrected semantics.

## Validation

- `pnpm exec vitest run lib/device-id.test.ts lib/auth-storage.test.ts --reporter=dot`: 8/8 passed.
- `pnpm exec eslint lib/device-id.ts lib/device-id.test.ts --quiet`: passed.
- `cmd /c "pnpm exec tsc --noEmit --pretty false"`: passed.
- `git diff --check`: passed.
- Local `dotnet test ...TrustedDeviceServiceTests...` stalled before compiler/test
  output twice; no local backend pass is claimed. GitHub Actions remains the
  backend compile/test gate.
- Public production health was read-only checked: web `/api/health`, API
  `/health/live`, and `/health/ready` returned 200 before deployment.

## Next step

Review the scoped diff, stage only the incident implementation/tests/docs and
this state file, commit and push `main`, then watch Build & Deploy plus backend
CI and re-check production health. Existing unrelated working-tree files
(`.codex/config.toml`, `.superpowers/`, performance/test changes) must remain
unstaged.

# Agent State - Course platform security production completion

Last updated: 2026-08-05

## Current Task - Responsive app download strip

- Replaced the dark full-width sign-in app-download box with one compact,
  responsive strip outside the auth card.
- Added consistent desktop/mobile badges, actionable Google Play and iOS
  destinations, and responsive mobile layout/link assertions.
- Validation: focused Vitest 3/3, 390px Chromium Playwright sign-in smoke,
  TypeScript, scoped ESLint with 0 errors, and diff-check passed.
- Next step: stage only the implementation files, commit/push `main`, watch
  the production blue/green deployment, and report the live release evidence.

## Current Task - Mobile device verification back navigation

- Added a visible `Back to sign in` link to the shared device-verification
  screen used by Android and iOS Capacitor webviews.
- The link clears the pending challenge from AuthContext and persisted
  web/native storage before preserving the requested destination on `/sign-in`.
- Added focused component coverage, mobile Playwright coverage for Pixel/iPhone
  projects, and Mobile CI path triggers for shared auth changes.
- Validation: TypeScript, targeted ESLint, diff check, and 6 focused tests pass.
  Local mobile Playwright attempt was stopped after WebKit failed immediately
  and Chromium exceeded the local 120-second timeout; CI mobile builds remain
  the native Android/iOS verification gate.
- Next step: stage explicit files, commit/push `main`, watch Mobile CI and the
  production deployment, then report the authenticated-device limitation.

## Current Task - Admin package removal and primary package

- Implemented soft-revocation: cancelled packages are hidden from current
  Access & Allocation responses while subscription/audit history remains.
- Added primary-package API/UI behavior using `CurrentPlanId`; primary metadata
  is representative only and active package entitlements remain additive.
- Added parent-status checks for linked add-ons/AI access, idempotent admin
  included-credit reversal, save-order reconciliation, and focused regressions.
- Validation: focused PackageList Vitest 2/2 passed; targeted ESLint had 0
  errors with only existing React hook warnings; TypeScript and diff-check
  passed. Backend MSBuild stalled before compiler/test output and was stopped.
- Next step: inspect final diff, stage only implementation paths, commit/push
  `main`, and verify the GitHub blue/green deployment and live access/audit
  behavior.

## Goal

Implement and production-verify the controls in
`OET_Course_Platform_Security_Requirements (1).pdf`, preserving explicit
evidence for platform limitations and external provider blockers.

## Implemented

- Implemented the reference anti-sharing contract end to end: one approved
  client identity by default, bounded admin override (1-5), OTP approval for
  new identities, automatic replacement/revocation, family-wide sign-out,
  refresh/access-token liveness enforcement, playback termination, clear
  user-facing sign-out reasons, and SecurityEvent + AuditEvent evidence.
- Defined the exact cross-platform count: browser profile tabs/windows count
  once; each Android/iOS/Windows/macOS official app installation counts once;
  a browser profile and app on the same hardware count separately because the
  server cannot prove hardware equivalence. The exact policy is documented in
  `docs/SECURITY-DEVICE-POLICY.md` and surfaced in admin and learner UI.
- Added the learner limit migration and the SecurityEvents platform-width
  migration required for canonical `capacitor-android`/`capacitor-ios` values.
- Routed self-service family revocation through the central revocation service
  and made device/session security decisions resilient to request disconnects.

- Replaced learner-visible direct HLS URLs with 5-minute token-authenticated
  Bunny embed URLs, ready for MediaCage and never exposing the library key.
- Enabled the protected player on web using an authenticated, user-bound,
  single-use nonce; native clients retain shell-held HMAC attestation.
- Playback sessions persist the request device id and revoke if renewal is
  attempted from a different device.
- Auth and API requests await native secure-storage device-id initialization;
  enforced fresh sign-ins reject a missing device id instead of failing open.
- Added a sandboxed, origin-checked embed controller while keeping the moving
  forensic watermark above the provider player, including parent fullscreen.
- Native shells now fail closed when OS capture protection cannot engage.
  Web remains functional under watermark, token, session, and audit controls.
- Screenshot/capture/tamper signals flush immediately and pause/mute both
  direct and embedded playback before server-side revocation.
- The production migration enables single-session, risk enforcement,
  trusted-device OTP binding, verified-email gating, rooted/emulator blocks,
  capture revocation, and a 300-second playback token TTL. Legacy device-unbound
  refresh tokens and active playback sessions are revoked.
- Added encrypted runtime configuration and a fail-open IPinfo
  Core/Plus/Max integration for VPN/proxy/Tor/hosting sign-in signals.
- Updated the security matrix, acceptance report, and admin runbook with the
  actual implementation state and remaining external/manual evidence.

## Validation

- Focused Vitest (attestation + embed-origin controller): 11/11 passed.
- Focused ESLint across every touched frontend/test file: passed.
- `pnpm exec tsc --noEmit --pretty false`: first run reached only four
  pre-existing unrelated errors in `lib/__tests__/api.test.ts` (lines 458,
  459, 480, 481); the post-change rerun hit the host timeout.
- Focused .NET test/build attempts hit the repository's known MSBuild host
  stall and were terminated; the GitHub production build is the compile gate.
- A focused ESLint retry also hit the host command timeout; no lint result was
  claimed.
- Final targeted ESLint checks completed with 0 errors (existing React hook
  warnings only) for the admin security/user pages, sessions page, auth and
  notification paths, device-id client, and API type helpers.
- `git diff --check` passed. The bounded API build emitted package/vulnerability
  warnings and then produced no compiler output for over two minutes, so it
  was stopped; no green backend build is claimed from this host.

## External Blockers / Residual Evidence

- Bunny MediaCage Basic or Enterprise DRM must be enabled and verified in the
  authenticated Bunny account; provider dashboard access is unavailable here.
- A signed iOS IPA requires Apple team id, distribution certificate,
  provisioning profile, and a non-placeholder associated-domain file.
- Android 1.4.1 and Windows/macOS desktop 0.7.0 are signed and published, but
  both tags predate the final security commits. The installed Windows 0.7.0
  app remained capturable in a foreground `CopyFromScreen` test, so corrected
  releases must be published from the post-deploy `main` SHA.
- IPinfo code is complete; production activation still needs a paid provider
  token entered through Admin → Settings → Security.
- Hardware OBS/RDP/mirroring/screenshot tests remain manual acceptance work.

## Next Step

Stage only the explicit implementation paths (preserving `.codex/config.toml`
and `.superpowers/`), commit, push `main`, and verify the blue/green production
workflow. The owner still needs real-device/manual acceptance for same-hardware
browser/app counting and cross-platform sign-out behavior.

# Current Task - Permanent admin user purge and Actions-only production compute

Last updated: 2026-08-08

## Outcome

- Admin Delete now permanently purges learner/expert profiles, auth accounts,
  sessions, attempts, billing rows, audit references, and other model-discovered
  user-linked rows; the normalized email row is removed so the address can be
  registered again. Legacy soft-deleted profiles remain purgeable from the
  detail page.
- The admin UI uses one explicit permanent-delete action with an accessible
  exact-email confirmation modal and returns to the user list after success.
- The system-admin hard-delete compatibility route delegates to the same purge
  service without retaining the target user id in the new audit ResourceId.
- Production deploys now build web, API, and backup-sidecar images in GitHub
  Actions; the active VPS rollout pulls per-commit image tags and starts
  containers with `--no-build`. Legacy source-build scripts refuse to run
  unless an owner-approved emergency override is explicitly supplied.

## Validation

- `vitest run app/admin/users/[id]/page.test.tsx --reporter=dot`: 1 file,
  8/8 tests passed, including exact-email purge confirmation.
- `git diff --check`: passed.
- Backend filtered build/test commands stalled on the shared Windows host
  before compiler/test output; no local backend pass is claimed. GitHub Actions
  remains the backend compile/test and production image gate.

## Next step

Stage only the explicit implementation/tests/workflow/deploy paths, commit and
push `main`, then verify the GitHub blue/green deployment and production health.
The two already-soft-deleted accounts must be purged once from the deployed
admin UI; no direct production database deletion was performed here.

# Current Task - Forward-compatible per-user video access

Last updated: 2026-08-08

## Outcome

- Preserved explicit per-user video allocations for existing content while
  automatically including videos first published after the initial video scope.
- Applied the same rule to learner catalog/detail visibility and the playback
  entitlement gate, so a newly uploaded and published video does not require
  ticking every registered learner.
- Preserved the original scope timestamp when an admin later edits the user's
  selected video ids, preventing unrelated saves from hiding new content.

## Validation

- Targeted ESLint passed for `components/admin/user-access/video-scope-picker.tsx`
  and `lib/user-access.ts`.
- `git diff --check` passed.
- Focused `dotnet test` and direct API `dotnet build` both stalled on the shared
  Windows MSBuild host before compiler/test output; no local backend pass is
  claimed. GitHub Actions remains the backend compile/test gate.

## Next step

Stage only the explicit video-access implementation, tests, docs, and this
state file; commit and push `main`, then verify the GitHub production workflow
and health gates. Preserve unrelated `.codex/config.toml` and `.superpowers/`.
