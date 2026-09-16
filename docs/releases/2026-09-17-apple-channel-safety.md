# Release Note — 17 Sep 2026: Apple channel safety + macOS/iOS release enablement

Handover: "OET WITH DR AHMED HESHAM | FINAL APPLE RELEASE HANDOVER" (17 Sep 2026).
Evidence pack: `docs/APPLE-RELEASE-EVIDENCE-PACK.md` · Device QA: `docs/apple/`.

## Platform versions deployed (one release = every applicable platform)

| Platform | Exact version | Channel | State after this release |
| --- | --- | --- | --- |
| Website | `57e1ab3` | oetwithdrhesham.co.uk | Live; pricing-page Mac card → `/get-app` "Use Web App" |
| Web App | `490bc319` + iOS-gate commit | app.oetwithdrhesham.co.uk | Live; Mac download disabled (kill-switch), iOS purchase gate live for all shells |
| Android | 1.4.14 / versionCode 9 | Google Play | Live, untouched (picks up gate + download states from the shared deploy) |
| Windows desktop | 0.7.9 | Direct download + Tauri auto-update | **Live, untouched** (handover rule) |
| macOS desktop | public 0.7.9 download disabled; 0.7.10 held | — | 0.7.10 built unsigned in CI dry-run (`35160582821`): Universal arch gate green, `.app.tar.gz`+`.sig` updater artifacts produced; publish skipped |
| iOS | none public | TestFlight → App Store | Release pipeline ready (ASC cloud signing + TestFlight upload, dormant); App Store metadata ready; blocked on owner ASC inputs |

## Changes shipped

1. **Mac download disabled** (handover §3 first action — protected playback has
   never passed on a real Mac and the public DMG is unsigned):
   `NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED=1`; `/api/download/mac` 503; feed strips
   `downloads.mac`; `/get-app` + marketing site point Mac candidates to the Web
   App. Windows/Android untouched.
2. **macOS auto-update enablement**: `"app"` bundle target emits updater
   artifacts; feed assembler publishes darwin entries **only for signed builds**
   (unsigned runs strip mac entries — Gatekeeper would break a self-updated
   unsigned app). ASC API-key notarization wired alongside the Apple-ID path.
3. **iOS release pipeline**: ASC API-key cloud signing + TestFlight upload
   (dormant until owner ASC inputs); raw-IPA VPS publish demoted to
   internal-only; preflight validates the new secret set (fails closed on AASA
   Team-ID placeholders).
4. **No-in-app-purchase model**: iOS shell renders an "enrol on our website"
   notice on /cart, /pricing, /catalog, /ai-packages, /checkout/review
   (`IosPurchaseGate`); web/Android/desktop unchanged; documented in
   `docs/IOS-PURCHASE-COMPLIANCE.md`.
5. **App Store metadata**: en-US listing (within all character limits),
   privacy answers (matching the existing `PrivacyInfo.xcprivacy`), age rating
   4+ path, review-notes template with demo-account placeholder,
   `APP-STORE-LISTING-NOTES.md`.
6. **Evidence + QA packs**: `docs/APPLE-RELEASE-EVIDENCE-PACK.md`,
   `docs/apple/MAC-QA-CHECKLIST.md`, `docs/apple/IOS-QA-CHECKLIST.md`.

## Explicitly not complete (handover §7)

macOS COMPLETE and iOS COMPLETE are **not** claimed: signing/notarization,
TestFlight, App Store submission and every real-device gate are owner-blocked
(ASC Team Keys 401 + Team ID + Developer ID certificate + Mac/iPhone hardware).
The checklists above are the closure path once unblocked.
