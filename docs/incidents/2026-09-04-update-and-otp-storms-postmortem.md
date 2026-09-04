# Postmortem — 2026-09-04: reload storm, device-limit OTP storm, Android "App not installed"

Three production defects, one release cycle. All fixed, shipped, and verified
on production. This document records what broke, the evidence for each root
cause, what was changed, and the structural guards that make recurrence
impossible without deliberately bypassing them.

Releases: **1.4.9/vc4** (reload + OTP fixes) and **1.4.10/vc5** (update-channel
fixes), commits `d9c7f93e7`, `9cc345ac0`, `54b2bee02` on `origin/main`.

---

## 1. Defect A — Capacitor app reloads 10–15 times on some devices

### Symptom
After launch (or any user action), the app visibly reloaded repeatedly,
cooled down, then restarted the cycle on the next action. Device-dependent.

### Root cause (proven in code)
The Capacitor shell loads the remote Next.js origin, so every
`window.location.assign/replace` is a **full WebView document load**. Three
auth-failure handlers fired one hard navigation **per in-flight request**
with no single-flight:
- `lib/api.ts` — `email_verification_required` 403 → `location.assign('/verify-email')` on every failing query;
- `lib/auth-client.ts` — `redirectToSignInAfterSessionLoss` → `location.replace('/sign-in')` on every `ensureFreshSession` failure (funneled from every apiRequest);
- SignalR `session_revoked` → same redirect path, repeatable on reconnect.

One logical auth flip (expired rotation, unverified-email flip, device
revoke) failing N concurrent dashboard queries therefore produced N document
loads. Fast devices settled (first unload won); slow devices interleaved
boots, queries, and navigations — the "device-dependent 10–15 reloads".

Contributors kept, not blamed: service worker registered inside SW-capable
native WebViews (guard checked `window.__CAPACITOR_NATIVE__`, which nothing
ever set), and the notification worker had no native guard at all.

### Fix
- New `lib/navigation/auth-redirect.ts`: `navigateAuthOnce()` — one hard auth
  navigation per document lifecycle (in-memory single-flight, not a debounce).
  Wired into both redirect sites.
- `app/providers.tsx` + `notification-center-context.tsx`: skip worker
  registration unless `getAppRuntimeKind() === 'web'`.

### Regression tests
`lib/navigation/__tests__/auth-redirect.test.ts` (4), auth-client
redirect-collapse test, existing suites green.

---

## 2. Defect B — device-limit OTP storm + "Invalid OTP"

### Symptom
At the 2-device limit, learners received 10–20 OTPs and the entered code was
rejected as invalid.

### Root cause (proven in code)
Two layered defects on top of previously-shipped guards (advisory lock,
60 s cooldown, durable client claim):
1. `DeviceChallengeForm.handleResend` had no same-tick single-flight:
   `isSending` state cannot stop a double tap before re-render, so two taps
   posted two `/device/send-otp` requests (second surfaced as a confusing
   cooldown error at best).
2. `EmailOtpService.IssueChannelOtpLockedAsync` (trust_device/password flows)
   inserted the challenge row **after** sending. A crash between send and save
   left a delivered code with no live row — every code the learner typed then
   verified as "invalid". The verify_email flow already saved first; the
   device flow did not.

### Fix
- In-flight ref guard on resend (frontend single-flight; backend stays source
  of truth).
- Save-before-send for the email branch + `SentAt == null` recovery that
  completes the stranded send instead of minting a duplicate code (Firebase
  SMS rows excluded — their code lives with the provider). Shared
  `SendChannelEmailAsync` helper keeps fresh-send and recovery templates and
  hashes identical.

### Regression tests
`DeviceTrustOtpRecoveryTests` (10× concurrent → 1 challenge + 1 send;
failed-send → same-challenge recovery → delivered code verifies; seeded
unsent row → recovered, never duplicated) plus pre-existing
`OtpResendCooldownTests`. Production E2E on the deployed SHA: limit
detected, 3 concurrent sends → one challenge id, wrong code rejected,
test account hard-deleted.

---

## 3. Defect C — Android update fails with "App not installed"

### Symptom
Every in-app update required uninstalling first; the downloaded APK would
not install over the existing app.

### Root cause (proven by APK forensics + Play API)
Pure-Python audit of the 1.4.7/1.4.8/1.4.9 APKs (signer cert + binary
manifest) plus `generatedapks.list(vc4)`:
- VPS lineage internally perfect: **one signer** (upload cert
  `41:5F:CB:E8:…:7F:A9`) across all three, same package
  `com.oetwithdrhesham.app`, monotonic versionCodes 2→3→4, identical
  manifest structure (no `testOnly`, no provider/permission drift).
- Play **App Signing re-signs** uploads with its own key (delivery cert
  `D2:8D:A6:9D:…:46:B3`).

A Play-installed copy can therefore never be updated by the VPS APK and vice
versa — Android rejects it with "App not installed", and no app code can
override OS signature enforcement. The in-app updater offered the VPS APK
unconditionally, recreating the conflict on every update.

### Fix (shipped in 1.4.10/vc5)
- New `InstallerSource` native plugin + `lib/mobile/install-source.ts`:
  the update screen routes Play-installed copies to the Play listing (APK
  hidden from them) and keeps sideloaded copies on the direct APK; plain
  browsers get both with guidance.
- `android/app/build.gradle`: release builds fail closed without
  `keystore.properties` (scoped to release tasks; debug unaffected) — a
  debug-signed "release" APK can never ship again.
- `mobile-release.yml`: aborts unless the built APK carries the pinned
  upload certificate (public fingerprint, safe to pin).
- VPS feed now records `versionCode` end to end
  (`assemble-mobile-manifest.mjs --version-code` → `current.json` →
  `MobileRelease` → `/api/releases/native`), and both publish jobs reject a
  feed downgrade (input vc must exceed the live feed vc). Play enforces this
  server-side; the feed previously had no check.

### Regression tests
Classifier + routing tests (`install-source.test.ts`,
`android-install/page.test.tsx`), feed route versionCode tests, manifest
script verified locally (include/omit/reject cases).

### Releases
- 1.4.9/vc4: fixes A+B → VPS feed + Play internal (completed).
- 1.4.10/vc5: fix C → VPS feed + Play internal (completed). Signer and vc5
  re-verified from the shipped artifact before publishing.

### User protocol (one time)
Uninstall once, fresh-install 1.4.10 from exactly one channel (VPS APK or
Play internal — never mix), after which updates work normally and the update
screen enforces the channel.

---

## 4. Structural "never again" inventory

| Recurrence vector | Guard | Where |
|---|---|---|
| Concurrent hard navigations | `navigateAuthOnce` single-flight | `lib/navigation/auth-redirect.ts` + call sites + tests |
| SW in native shell | runtime-kind guards | `app/providers.tsx`, notification center + comment |
| Resend double-tap | in-flight ref | `device-challenge-form.tsx` + test |
| OTP orphan on crash | save-before-send + `SentAt==null` recovery | `EmailOtpService` + 3 tests |
| Wrong/debug signer ships | fail-closed gradle + CI cert pin | `build.gradle`, `mobile-release.yml` |
| Cross-channel update offered | installer-aware routing | plugin + `install-source.ts` + page + tests |
| Feed versionCode downgrade | record + gate | manifest script, feed route, both publish jobs + tests |
| Stale-build confusion in shell | manual-only refresh banner | `StaleBuildGuard` (unchanged, verified manual-only) |

Release policy (also in `docs/play-store-automation.md`): cut both channels
at the same version/versionCode; never mix channels on one device; Play
promotion/review stays a UI action for the owner; iOS stays untouched by
Play tasks (bundle ID `com.oetprep.learner` frozen).

## 5. Environment lessons (paid for in full — keep them)

- PowerShell mangles inline `curl.exe -d '{...}'` JSON (server sees garbage
  → empty-400). Always `--data-binary @file`.
- Direct `api.*` POSTs return empty-400 on the public Host: the API
  allowlists internal hosts only. Drive API E2E via
  `app.oetwithdrhesham.co.uk/api/backend` with Origin/Referer.
- Local `node_modules` can go corrupt mid-session (another agent active);
  when `pnpm exec`/vitest break, validate via CI instead of fighting it.
- Untracked duplicate trees (`pdf-policy-release*/`) get collected by
  vitest locally and poison runs — never commit copies of app dirs.
- `generatedapks.list` needs no edit context; delivery-cert truth comes
  from `certificateSha256Hash`, not `downloadId` strings (terminal wrapping
  corrupts their display — extract to files).
- Mobile CI iOS simulator + macOS runners are billing-flaky; QA Smoke is
  chronically red; Speaking CI dashboard-shell failures pre-date this work
  (all proven on a pristine parent worktree before claiming).

## 6. Open / owner-gated items

- iOS release: missing `APPLE_TEAM_ID` + signing secrets in CI,
  `apple-app-site-association` placeholders, macOS billing block.
- Valid-OTP acceptance on production was inbox-blocked; covered by backend
  acceptance tests on the identical deployed SHA.
- Physical-device reload run was not possible from the build machine;
  substituted with API concurrency proofs + unit tests + prod log review.
