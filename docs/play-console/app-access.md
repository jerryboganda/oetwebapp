# Play App Access — reviewer instructions (paste into Play Console > Policy > App content > App access)

## Principle
The Google review team must reach the real app experience without being blocked by subscription logic, OTP/device restrictions, or expired credentials. Use a PERMANENT review account, test it on a fresh phone/emulator with the exact release build before every submission, and make sure every restricted feature shown in screenshots/descriptions is reachable with this account.

## What to enter in Play Console
1. Create the reviewer account (example — replace with the real one before submission):
   - Username/email: `play-review@oetwithdrhesham.co.uk`
   - Password: `<set a permanent password, store in secrets manager, never commit it>`
   - Entitlement: active access to courses + Listening/Reading practice + any Writing/Speaking/AI feature claimed in the listing or visible in screenshots.
2. If sign-in uses email OTP / device verification:
   - Prefer: exempt this account from device-count enforcement via Admin > User security > approved-device limit override (the codebase already supports per-account `MaxDevicesOverride`; `TrustedDeviceService.DefaultMaxDevices = 2` would otherwise challenge a fresh reviewer device and a 3rd device forces replacement + OTP).
   - Otherwise provide EXACT steps + where the OTP arrives + a reliable path (e.g. which inbox, how long the code is valid, no manual approval, no geographic block, no one-time link that expires before review).
3. Navigation instructions (adjust to the submitted build):
   - Sign in with the reviewer account → land on Dashboard.
   - Courses/Materials via bottom nav → open a course → open Listening / Reading practice.
   - Writing / Speaking / AI tools ONLY if listed as live — state the exact path, or remove the claim.
   - Progress/Account via Settings → Privacy / Delete Account demonstrates account controls.
4. Constraints to verify before submission:
   - Account must NOT expire during review and must NOT be limited to an already-registered device.
   - No `3 changes / 7 days` device-change cooldown surprises (rapid reinstalls hit this) — use the override or a fresh dedicated account.
   - Country allow-list / risk `step_up`/`block` modes must be OFF or explicitly pass the reviewer (default is safe; do not enable geo fencing for the review window).
   - Firebase SMS OTP is OFF by default; email OTP via Brevo must actually deliver to the reviewer inbox.

## Repo evidence
- Device logic: `backend/src/OetLearner.Api/Security/TrustedDeviceService.cs` (`DefaultMaxDevices = 2`), per-user override `AdminSecurityService.SetCandidateDeviceLimitAsync`, `POST /v1/auth/account/delete`.
- Public legal routes: `/privacy`, `/terms`, `/support`, `/account-deletion` (all in `proxy.ts` PUBLIC_PATHS, `app/sitemap.ts`, `app/robots.ts`).
- Release build: signed AAB from `Mobile Release` workflow, `version` + `version_code` inputs (increment `versionCode` every release), package `com.oetwithdrhesham.app`, target API 36.
