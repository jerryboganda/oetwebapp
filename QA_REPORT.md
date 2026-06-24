# QA_REPORT.md — OET Prep Learner Mobile Production-Readiness

**Engagement:** Pre-release QA / production-readiness for the Capacitor mobile app, before shipping to internal testers.
**Branch:** `qa/production-readiness` · **Started:** 2026-06-25 · **Status:** in progress (Phase 2)
**Decisions:** Platforms = Android (delivered) + iOS (verify-only, no Apple account). Branch off `main`. Tester build URL = dedicated staging (URL pending). Push ships dormant.

> Evidence rule: every "done" below is backed by pasted real command output, file sizes, or screenshots. Claims without evidence are marked **TODO/unverified**.

---

## Phase 0 — Inventory & Findings (COMPLETE)

### App shape
- **Capacitor 7.6.5** hybrid; `appId com.oetprep.learner`. **Remote-URL architecture**: `server.url` → live site (`https://app.oetwithdrhesham.co.uk`), `webDir capacitor-web` is a redirect stub; **no bundled SPA**. Native shell loads the live site in a WebView.
- Web: Next.js 16.2.6 (App Router, `output: standalone`), React 19, TS 5.9 strict, Tailwind 4. Backend: .NET 10. PM: pnpm 10.33.0. Sentry (`@sentry/nextjs`) present.

### Native config
- **Android:** `minSdk 22`, `compile/targetSdk 35`, `versionCode 1` / `versionName 1.0`. Release: `minifyEnabled` + `shrinkResources` + ProGuard. Signing via git-ignored `keystore.properties` (no hardcoded creds). `allowBackup=false`, `singleTask`, `allowMixedContent=false`. Deep links: verified App Links (`autoVerify=true`) for `app.oetwithdrhesham.co.uk` + custom `oet-prep://open`. No `network_security_config.xml`.
- **iOS:** deployment target 13. Usage keys: Microphone, Camera, FaceID. `PrivacyInfo.xcprivacy` present & well-formed. `oet-prep` URL scheme, `remote-notification` background mode, `ITSAppUsesNonExemptEncryption=false`. ⚠️ `UIRequiredDeviceCapabilities=armv7` (legacy 32-bit; should be arm64).
- **Plugins (17):** app, browser, camera, device, filesystem, haptics, keyboard, network, preferences, push-notifications, share, splash-screen, status-bar + `@aparajita/capacitor-biometric-auth@7`, `@aparajita/capacitor-secure-storage@6`, `capacitor-voice-recorder@6`.

### Permissions vs plugins — least-privilege PASS (minor confirms pending)
Android: INTERNET, RECORD_AUDIO, VIBRATE, CAMERA, READ_MEDIA_AUDIO, POST_NOTIFICATIONS, USE_BIOMETRIC, USE_FINGERPRINT — all map to a plugin/feature. Confirm in Ph3: CAMERA actually exercised? `USE_FINGERPRINT` (pre-API-28) still needed? iOS usage keys all justified.

### Security baseline (strong)
- **CSP** nonce-based per-request in `middleware.ts` (intentionally not in `next.config.ts`); baseline meta also in `app/layout.tsx` (browser enforces intersection). `'unsafe-inline'` only in `style-src`; `'unsafe-eval'` dev-only. `object-src 'none'`, `base-uri/form-action 'self'`, `upgrade-insecure-requests` in prod. Static headers (XFO DENY, nosniff, Referrer-Policy, Permissions-Policy) in `next.config.ts`.
- **Auth:** backend issues short-lived access JWT (15m) + single-use rotating refresh (30d, HttpOnly cookie on web). Mobile stores tokens in **native Keychain/Keystore** (`lib/mobile/secure-storage.ts`, real `@aparajita/capacitor-secure-storage` dep); tokens not persisted to web storage in prod. CSRF double-submit + SameSite=strict.
- **Secrets:** none hardcoded; none committed (verified `git ls-files` → only `*.example` templates). `.gitignore` covers keystores, `keystore.properties`, `google-services.json`, `.env*`.

### Push — wired but DORMANT
Full JS impl (`lib/mobile/push-notifications.ts`, `components/mobile/mobile-runtime-bridge.tsx`): register → token→backend → billing/writing event routing → deep-link routing. Gradle applies google-services only if file present. `google-services.json` / `GoogleService-Info.plist` **absent** → no push until provisioned. Ships dormant by decision.

### Tests & CI (already substantial)
- Unit: Vitest (jsdom), ~380 test files; capacitor/haptics/secure-storage mocked in `vitest.setup.ts`. No coverage config.
- E2E: Playwright, 13 projects incl. mobile-emulated (Pixel 7, iPhone 14) + Sydney locale. Emulation only.
- CI (16 workflows): `mobile-ci.yml`, `mobile-release.yml` (signed AAB/APK + IPA via dispatch), `qa-smoke.yml`, `deploy.yml`, `sbom-sca.yml`, etc.

### Phase 0 risk table → see `TEST_PLAN.md` §2 (R1–R9).

---

## Phase 2 — Static analysis & hygiene
_Pending — outputs pasted here._

## Phase 3 — Security hardening
_Pending._

## Phase 4 — Automated tests
_Pending._

## Phase 5 — Manual / exploratory QA
_Pending — see `BUGLOG.md` for issues._

## Phase 6 — Production readiness
_Pending._

## Phase 7 — CI/CD
_Pending._

## Phase 8 — Signing
_Pending._

## Phase 9 — Build & deliver
_Pending._

## Phase 10 — Go / No-Go
_Pending._
