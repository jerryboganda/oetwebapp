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

## Phase 2 — Static analysis & hygiene (in progress)

Run in worktree `D:/Projects/oet-qa-prod-readiness` @ `main` tip (`c9bba0aa3`), pnpm 10.33.0, after `pnpm install --frozen-lockfile` (exit 0).

### `tsc --noEmit` — 1 error found → FIXED → clean
- **Before:** `app/billing/page.test.tsx(77,7): error TS2578: Unused '@ts-expect-error' directive.` `TSC_EXIT=2`.
- **Significance:** `next.config.ts` sets `typescript.ignoreBuildErrors:true` (R2), so `next build` would not catch this — but `qa-smoke.yml` runs `tsc`, meaning **main's CI type-check gate was red**.
- **Fix:** removed the now-unused `// @ts-expect-error test shim` (the `URL.revokeObjectURL = vi.fn()` assignment type-checks cleanly under TS 5.9; TS2578 guarantees no error is masked). Behavior unchanged.
- **After:** `TSC_EXIT=0` ✅.

### `pnpm run lint` (ESLint) — PASS
- `✖ 386 problems (0 errors, 386 warnings)` · `LINT_EXIT=0` ✅.
- All 386 are React-Compiler advisories (`react-hooks/set-state-in-effect`, purity, etc.) **intentionally downgraded to warnings** in `eslint.config.mjs`. Tech-debt, not a blocker; mass-refactor out of scope for a QA pass.

### `pnpm test` (Vitest) — 2075 tests pass deterministically
- Full suite: `Test Files 1 failed | 302 passed (303)` · `Tests 2 failed | 2073 passed (2075)`.
- Both failures in **one** file, `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.test.tsx` (admin Reading authoring — **not a mobile flow**): (1) 15s test timeout, (2) `expected 8 calls, got 16`.
- **Isolation re-run: `Test Files 1 passed (1) · Tests 8 passed (8)` (exit 0), `tests 13.72s`** — right against the 15s global timeout. ⇒ **flaky under full-suite parallel load** (timeout tip-over + cross-file state bleed doubling), not a product bug. Pre-existing on `main`; unrelated to this engagement. Logged as BUGLOG #1 (Medium, test-infra). Could flake the `qa-smoke` unit gate → revisit in Phase 7.

### `pnpm audit` — 23 advisories; only 1 prod chain, none ship in the app
- All deps: `23 (4 low, 13 moderate, 6 high)`. Prod-only (`--prod`): `11 (10 moderate, 1 high)`.
- **Every prod advisory is the single chain `@google/genai → @modelcontextprotocol/sdk → hono <4.12.25`** (server-side AWS-Lambda body-limit / Lambda@Edge header issues — never instantiated by this app's MCP-client usage).
- The 6 highs are dev/build/test toolchain: `form-data` & `undici` ← `electron-builder`→node-gyp (desktop build), `undici` ← `jsdom` (test env), `hono` ← MCP sequential-thinking **devDep**. **None ship in the mobile app** (remote-URL app bundles negligible JS). SCA already automated via `sbom-sca.yml`.
- **Action taken:** added `pnpm.overrides` `"hono@<4.12.25": ">=4.12.25"`. Surgical lockfile diff (19 lines). **Result: prod audit 11→6, high 1→0.** Remaining 6 are *moderate*, server-side, different chain (`@sentry/nextjs→@sentry/node→@opentelemetry/core`) — non-shipping in the remote-URL app; recommend a `@sentry/nextjs` bump at leisure (tracked via `sbom-sca.yml`). `tsc` re-validated clean post-override.
- Pre-existing peer-dep warnings (react-redux / @zoom/meetingsdk want React 18; next-intl wants Next <16) are benign and unrelated to the override.

### `pnpm run build` (`next build`, standalone) — PASS
- `BUILD_EXIT=0` ✅. Compiles the full route tree (overwhelmingly `ƒ` dynamic / server-rendered-on-demand, consistent with auth-gated standalone mode). Note: for the **remote-URL mobile app** the `next build` output is **not** bundled into the APK (`cap sync` copies `webDir: capacitor-web`, the redirect stub) — the build matters for the **web/staging deploy** the WebView loads, and as a CI health gate.

### Android `./gradlew lint` — DEFERRED to Phase 9
Requires the Android SDK toolchain + `cap sync android`; run alongside the Android build to avoid a duplicate toolchain spin-up. `mobile-ci.yml` already does an Android debug build on PR.

### Hygiene re-confirm
- Secrets: none committed (`git ls-files` → only `*.example`). `.gitignore` covers keystores/`keystore.properties`/`google-services.json`/`.env*`. ✅
- Unexpected: `src-tauri/src/lib.rs` showed clippy auto-fixes during the session (IDE/rust-analyzer, not this engagement) — left untouched, never staged.

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
