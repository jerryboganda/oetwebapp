# Mobile Store Listings — OET with Dr Ahmed Hesham
Source of truth for Google Play Main store listing. Developer Action Brief dated 03 September 2026.
Package: `com.oetwithdrhesham.app`. Same application record is used for testing and production — do NOT create a second app.

## 0. Listing identity (Play Console > Grow users > Store presence > Main store listing + Store settings)

| Field | Exact value / action |
|---|---|
| App name | `OET with Dr Ahmed Hesham` (24 chars, within 30-char limit, matches brand artwork) |
| Default language | English (United Kingdom) or the account's existing English default. No extra locales unless professionally translated. |
| App type | App (not a game) |
| Category | Education (exam-preparation / learning application) |
| Short description (80-char limit) | `OET preparation for healthcare professionals with courses, practice and AI tools.` |
| Website (Store listing contact details) | `https://www.oetwithdrhesham.co.uk` |
| Support email (actively monitored) | `support@oetwithdrhesham.co.uk` |
| Phone | Optional — official support/business number only if the team intends to publish it (visible publicly). |
| Developer name | Prefer `OET with Dr Ahmed Hesham` if the verified Play developer account allows this display name. Legal/verification info remains the verified account information. |
| Package name | `com.oetwithdrhesham.app` — do NOT change for this app; production continues from this same record. |

Do NOT publish generic metadata: no package name as visible title, no default Android icon, no placeholder descriptions or placeholder support details.

## 1. Short description (exact)

```text
OET preparation for healthcare professionals with courses, practice and AI tools.
```

## 2. Full description (exact default English — final pass against live build before submission)

```text
Prepare for OET with Dr Ahmed Hesham through a structured digital learning platform designed for healthcare professionals.

The application gives enrolled learners convenient access to OET preparation content and practice resources from their mobile devices. Learners can access the courses, materials and practice tools included in their account or purchased package.

Key features may include:
- OET preparation courses and structured learning materials.
- Computer-based Listening and Reading practice.
- Writing and Speaking preparation resources.
- Profession-specific learning content for eligible healthcare professions.
- Recalls, revision resources and exam-practice materials where included in the learner's package.
- AI-powered practice and assessment tools where available in the current version.
- Account-based access so learners can continue using the content assigned to their subscription or purchase.

Some courses, tests, credits, recalls or other learning features require an active account, eligible access, or a separate purchase. Availability depends on the learner's profession and package.

Independent preparation platform: OET with Dr Ahmed Hesham is an independent exam-preparation service. It is not the official OET test provider and is not presented as being operated by the official OET organisation.
```

### Metadata accuracy rule (blocking)
Do NOT mention a feature because it is planned. Google may reject when the listing promises functionality the submitted build does not provide — including AI Writing/Speaking, payment functions, specific professions, recalls, or any feature still under development. Remove/adjust any bullet that is not live in the submitted build before submission.

## 3. Branding and graphic assets
- Visual identity: OET with Dr Ahmed Hesham (purple brand, crest/book + caduceus + graduation cap). Final uploads must be exported from original source artwork, never from a phone screenshot.
- TWO SEPARATE ICON JOBS (updating one does not update the other):
  1. Play Console Store icon: `512 x 512 px, 32-bit PNG with alpha, max 1024 KB`, clean high-resolution source export.
  2. Android launcher icon embedded in the AAB (`android/app/src/main/res/mipmap-*`, adaptive + round). Source: `public/brand/oet-square-logo.png`.
- Feature graphic: `1024 x 500 px`, JPEG or 24-bit PNG without alpha. Brand palette + name `OET with Dr Ahmed Hesham`. Minimal readable text. NO claims such as #1, best, guaranteed pass, discounts, prices, rankings unless separately verified and policy-safe. Do NOT build from a buggy/unfinished screen — clean brand artwork + simple learning-experience representation.
- Generated artefacts in this repo: `public/brand/play-store-icon-512.png`, `public/brand/feature-graphic-1024x500.png` (or `.jpg`), `fastlane/metadata/android/en-GB/*`.

## 4. Screenshot plan (capture ONLY from the final corrected release build)
| # | Screen | Capture requirement |
|---|---|---|
| 1 | Dashboard | Main learner dashboard after login. No loading state, errors, clipped icons, unfinished widgets. |
| 2 | Courses / Materials | Course/material navigation demonstrating the learning library. |
| 3 | Reading Practice | Computer-based Reading experience, clean readable layout. |
| 4 | Listening Practice | Listening exam/practice interface with correct headings and layout. |
| 5 | Writing Preparation | ONLY if the exact feature is live and stable in this build. |
| 6 | Speaking Preparation | ONLY if the exact feature is live and stable in this build. |
| 7 | AI / Practice Tools | ONLY if the displayed AI function is available to users in the submitted version. |
| 8 | Progress / Account | Optional: progress, account or learning-history screen if it adds value. |

### Screenshot QA rules
- Minimum Play requirement: 2 screenshots. For this app provide 6–8 strong phone screenshots.
- Recommended: `1080 x 1920` portrait (9:16) JPEG or 24-bit PNG without alpha.
- No device-frame mockups unless intentionally designed and policy-compliant.
- Do NOT reuse screenshots from before bug fixes. Large-phone top-bar/icon alignment and dashboard performance must be corrected first (see `docs/mobile-performance/`).
- Check the final set on a small Android phone AND a large phone such as S24 Ultra.
- Tablets: test tablet rendering first; only then add tablet screenshots. Do NOT market tablet quality before verification.
- Naming: `01_Dashboard, 02_Courses, 03_Reading, 04_Listening, 05_Writing, 06_Speaking, 07_AI, 08_Progress`.

## 5. Support / Privacy / App content (release-blocking, not presentation details)
- [ ] Actively monitored support email under Store listing contact details: `support@oetwithdrhesham.co.uk`.
- [ ] Website: `https://www.oetwithdrhesham.co.uk`.
- [ ] Valid Privacy Policy URL in Play Console + accessible inside the app (`/privacy`, `/support`, Settings). Must be a PUBLIC WEB PAGE, not a PDF. Must identify app/developer, data collected, use/sharing, security, retention/deletion, privacy contact.
- [ ] Data safety completed from the ACTUAL app, backend and every SDK used. Do NOT copy answers from another app. See `docs/play-console/data-safety.md`.
- [ ] Account deletion BOTH inside the app (`Settings > Delete Account`, `POST /v1/auth/account/delete`, 30-day grace) AND through an external web page (`/account-deletion`), URL entered in Play Console. Disabling/freezing ≠ deletion.
- [ ] App access: provide Google reviewers working credentials + navigation instructions for restricted content. See `docs/play-console/app-access.md`.
- [ ] Ads: declare accurately. No third-party advertising SDK → select No. Purchases/subscriptions ≠ third-party ads.
- [ ] Target audience + content: healthcare professionals/adults (16+). Do NOT select child audiences unless genuinely designed for them + Families requirements met.
- [ ] IARC content rating questionnaire completed with accurate answers for the current build.
- [ ] Any additional Play declarations for permissions, financial features, health-related data, or other restricted functionality actually used.

## 6. Reviewer access + technical release (September 2026)
- Permanent Google Play review account with active access to enough content to inspect the core app. Must not expire during review; must not be limited to an already-registered device. Avoid device-count blocks, OTP loops, geographic restrictions, manual approval, one-time links. If OTP/special auth is unavoidable, provide exact instructions + reliable review path in Policy > App content > App access. Test credentials on a fresh phone/emulator with the exact release build before every submission. Restricted features shown in screenshots/descriptions must be reachable with the supplied test account.
- Android release checks: target Android 16 / API 36+ (mandatory for submissions after 31 Aug 2026) — repo: `compileSdk 36 / targetSdk 36` in `android/variables.gradle`; signed AAB + Play App Signing (same app, `mobile-release.yml`); increment `versionCode` every release + clear `versionName`; keep `com.oetwithdrhesham.app`; branded launcher icon embedded; fix large-screen top-bar/icon alignment (S24 Ultra) before final release; fix slow dashboard + smoothness before production (screenshots from corrected build); test tablets before claiming/showcasing tablet support.

## 7. Testing → production
Store listing is shared across testing tracks. Branding + metadata completed now travel with the same app when promoted to Production — keep the existing application/package, do NOT create a new Play Store app. Test opt-in URL is for joining a testing track, not the long-term public marketing link. After production release, promote the public Play listing. Test users cannot leave public reviews (feedback is private, no rating impact). Do NOT ask testers for public reviews during test; ask only after production + on the production version. Allow time for review/propagation delays.

## 8. Final developer completion checklist (all MUST)
- [ ] Visible app name is `OET with Dr Ahmed Hesham`.
- [ ] Play icon is the branded 512 x 512 asset — no default Android placeholder.
- [ ] Installed launcher icon is also branded in the AAB.
- [ ] Feature graphic 1024 x 500 uploaded.
- [ ] 6–8 clean phone screenshots from the corrected build uploaded.
- [ ] Short + full descriptions added and match the build.
- [ ] Category is Education; tags selected without keyword stuffing.
- [ ] Support email + website published.
- [ ] Privacy Policy + account-deletion requirements satisfied.
- [ ] Data safety, App access, Ads, Target audience, Content rating completed accurately.
- [ ] Reviewer account works on a fresh device, not blocked by OTP/device limits.
- [ ] AAB targets API 36+ and uses existing package `com.oetwithdrhesham.app`.
- [ ] Large-phone UI + dashboard performance corrected before production.
- [ ] Final listing previewed on Google Play and checked on at least one small and one large Android phone.

## Console pointers
- Main store listing: Play Console > Grow users > Store presence > Main store listing.
- Contacts: Store listing contact details (website + support email).
- Privacy + deletion URLs, Data safety, App access, Ads, Target audience, Content rating: Play Console > Policy > App content (+ Store settings where surfaced).
- Releases: signed AAB via `Mobile Release` workflow (`version` + `version_code` inputs, increment every release).
