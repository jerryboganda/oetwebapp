# Google Play Release Checklist — OET with Dr Ahmed Hesham (03 Sep 2026 brief, 100%)

Package `com.oetwithdrhesham.app`. Keep the SAME application record for testing → production. Do NOT create a second app.

## A. Store listing (Play Console > Grow users > Store presence > Main store listing)
- [ ] App name = `OET with Dr Ahmed Hesham` (exact capitalization, no dot).
- [ ] Language = English (United Kingdom) or existing English default. No extra locales unless professionally translated.
- [ ] App type = App. Category = Education. Tags relevant, no keyword stuffing.
- [ ] Short description (exact, ≤80 chars): `OET preparation for healthcare professionals with courses, practice and AI tools.`
- [ ] Full description = `fastlane/metadata/android/en-GB/full_description.txt` (exact brief copy). FINAL PASS: remove any bullet not live in the submitted build (AI Writing/Speaking, payments, specific professions, recalls).
- [ ] Website = `https://www.oetwithdrhesham.co.uk`. Support email = `support@oetwithdrhesham.co.uk` (monitored inbox).
- [ ] Developer name preference = `OET with Dr Ahmed Hesham` (legal info stays verified).
- [ ] No generic metadata: no package-as-title, no default icon, no placeholder text.

## B. Graphics (two separate icon jobs — both required)
- [ ] Play Store icon: `public/brand/play-store/play-store-icon-512.png` (512×512, 32-bit PNG+alpha, 189 KB ≤1024 KB) uploaded in Console.
- [ ] AAB launcher icon: branded mipmap set regenerated from `public/brand/oet-square-logo.png` (mdpi 48 → xxxhdpi 192 + adaptive foreground + round). Installed icon verified on device — matches Store icon.
- [ ] Feature graphic: `public/brand/play-store/feature-graphic-1024x500.png` + `.jpg` (1024×500, no alpha, brand palette + name, minimal text, no #1/best/guaranteed/pass/price claims) uploaded.
- [ ] Screenshots 6–8 from the CORRECTED build only (`docs/play-console/screenshot-plan.md`, `01_Dashboard…08_Progress`, 1080×1920 portrait, JPEG/PNG no alpha, no frames, no pre-fix shots). Verified on small + large (S24 Ultra) phones. Tablets only if tested.

## C. Support / Privacy / App content (Policy > App content + Store settings)
- [ ] Support email + website published.
- [ ] Privacy Policy URL in Console + in-app (`/privacy` ← `/support`, Settings, Terms). Public web page, not PDF. Identifies app/developer, data/use/sharing/security/retention/deletion, contact (`dpo@` + `support@` + ICO). In-app: `app/(auth)/privacy/page.tsx` (updated 03 Sep 2026).
- [ ] Data safety from ACTUAL app/backend/SDKs (`docs/play-console/data-safety.md`). Justify RECORD_AUDIO/CAMERA/READ_MEDIA_AUDIO/POST_NOTIFICATIONS/biometric. No copy-paste from another app.
- [ ] Account deletion BOTH in-app (`Settings > Delete Account`, 30-day grace) AND web (`/account-deletion`, URL in Console). Disabling ≠ deletion.
- [ ] App access with WORKING reviewer credentials + navigation (`docs/play-console/app-access.md`). Reviewer reaches every screenshotted/described restricted feature.
- [ ] Ads = No (no third-party ads SDK; purchases ≠ ads). Audience = healthcare professionals/adults 16+, NOT children. IARC rating accurate. Permissions/financial/health declarations completed.

## D. Reviewer access
- [ ] Permanent account `play-review@oetwithdrhesham.co.uk` (or current equivalent) with active entitlements, no expiry, not single-device-locked.
- [ ] Device-limit/OTP/geo blocks removed for reviewer (per-account `MaxDevicesOverride`, country allow-list OFF, Brevo email OTP delivers). Exact OTP steps documented if unavoidable.
- [ ] Credentials tested on a FRESH phone/emulator with the EXACT release AAB before every submission.

## E. Technical release (September 2026 rules)
- [ ] Target API 36+ (repo: `compileSdk 36 / targetSdk 36` in `android/variables.gradle`).
- [ ] Signed AAB + Play App Signing via `Mobile Release` workflow (same app). `versionCode` incremented every release, clear `versionName`.
- [ ] Package stays `com.oetwithdrhesham.app` (Android + assetlinks + `validate-mobile-release-inputs.mjs` + CI + tests aligned; iOS bundle also `com.oetwithdrhesham.app`).
- [ ] Firebase: `google-services.json` was removed during the rename (FCM registration disabled via `FCM_REGISTRATION_ENABLED=false` to stop fresh-install crash). Before re-enabling push: create the Firebase Android app for `com.oetwithdrhesham.app`, download the new `google-services.json`, re-enable registration, re-declare in Data Safety.
- [ ] `apple-app-site-association` still contains `TEAM_ID` placeholder — replace with the real Apple Team ID at release (preflight `validate-mobile-release-inputs.mjs` blocks while placeholders remain).
- [ ] Large-phone top-bar/icon alignment fixed (edge-to-edge `WindowInsetsCompat` bridge in `MainActivity`, safe-area split in `top-nav.tsx`, `globals.css` vars — see `docs/mobile-performance/`). Verified on S24 Ultra-class device before production.
- [ ] Dashboard slowness fixed (TanStack dedupe, `staleTime` with explicit invalidation — see `docs/mobile-performance/04*`). Screenshots from corrected build.
- [ ] Tablets tested before claiming/showcasing tablet support (`configChanges` covers resize; exam pages already advise tablet/desktop + headphones).

## F. Testing → production
- [ ] Branding/metadata completed NOW on the testing record — travels to Production on promotion.
- [ ] Test opt-in URL ≠ marketing link. After production, promote the PUBLIC listing.
- [ ] No public-review asks during test (test feedback is private, no rating impact). Ask only post-production on the production version.
- [ ] Allow review/propagation delays. Preview final listing on Play + on ≥1 small and ≥1 large Android phone.

## G. Verification performed in this change
- `pnpm exec tsc --noEmit` + `pnpm run lint` + targeted unit tests for touched areas (share, auth-guard public paths, sitemap/robots) + `node scripts/qa/validate-mobile-release-inputs.mjs --platform=android` structure checks (requires release secrets for full pass — CI `Mobile Release` is the gate).
- Asset checks: Play icon 512×512 RGBA <1024 KB; feature graphic 1024×500 RGB no-alpha; mipmap densities regenerated; PWA icons refreshed.
- Manual Console steps that code CANNOT do (require a human in Play Console): upload Store icon / feature graphic / screenshots, paste listing copy, set category/language/audience/rating, complete Data safety + App access + deletion URL, create/verify reviewer account, promote testing → production.
