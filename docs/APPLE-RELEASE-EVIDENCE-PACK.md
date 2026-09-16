# Apple Release Evidence Pack — 17 Sep 2026

Status of every gate in the 17 Sep 2026 Apple handover. Anything not proven on
the exact production artifact is marked **PENDING** and is never reported as
complete (handover §7 acceptance rule).

## 0. Channel status (handover §2 table)

| Channel | Status | Evidence |
| --- | --- | --- |
| Windows 0.7.9 | **LIVE — untouched** | Updater feed `windows-x86_64` unchanged: EXE `https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.9/…x64-setup.exe`, sha256 `514d320c…89b3624d6`. No commit in the 0.7.9 release artifacts. |
| macOS public download | **DISABLED 17 Sep 2026** per handover rule (protected playback never passed on a real Mac; public DMG unsigned) | `NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED=1` (deploy.yml build arg + compose runtime env); `/api/download/mac` → 503; `/get-app` + marketing pricing page show "Use Web App"; updater feed strips `downloads.mac` |
| macOS 0.7.10 | **ON HOLD** — built and gated in CI, unsigned | Dry-run run `35160582821` (allow_unsigned=true, publish skipped): conformance ✅, Windows ✅, macOS Universal ✅ (arch gate arm64+x86_64), updater artifacts produced. SHA-256 (dry-run build): DMG `1a907073…e98d0dd2`, `.app.tar.gz` `5155b2a4…7747c53` |
| iOS | **NOT PUBLIC** — TestFlight path ready, blocked on owner ASC inputs | No store listing; `/get-app` shows non-clickable "coming soon"; raw-IPA VPS publish demoted to internal-only input (default skip) |

## 1. Completed and verified (CI or prod artifact)

| Gate | Result | Evidence |
| --- | --- | --- |
| Mac download disabled, candidates redirected to Web App | ✅ | Deploy of `490bc319…` on main (`deploy.yml`, blue/green health-gated); tests: route 503, feed strip, get-app disabled card |
| Windows channel untouched | ✅ | Feed JSON unchanged (version 0.7.9, windows-x86_64 only); desktop publish leg skipped in dry-run |
| macOS Universal build (arm64 + x86_64) | ✅ | Dry-run `35160582821` macOS leg green incl. `assert-macos-bundle-architectures` |
| macOS auto-update artifacts | ✅ (shape) | `OET with Dr. Hesham.app.tar.gz` + `.sig` now produced (`"app"` bundle target); feed assembler publishes `darwin-aarch64`/`darwin-x86_64` — but only when signed (see rails) |
| Unsigned-mac safety rails | ✅ | Publish leg strips darwin updater entries + downloads.mac when `APPLE_CERTIFICATE` absent |
| iOS signing/TestFlight workflow | ✅ (dormant) | `mobile-release.yml` uses ASC API-key cloud signing (`-allowProvisioningUpdates -authenticationKey*`) + `altool --upload-app`; fails closed when secrets absent |
| Raw-IPA removal from candidate path | ✅ | `publish_vps_ipa` dispatch input, default `false`, labelled internal-only |
| App Store metadata (en-US) | ✅ (ready, unsubmitted) | `fastlane/metadata/ios/en-US/*` (name/subtitle/keywords/promo all within limits), review notes template, `APP-STORE-LISTING-NOTES.md` (privacy answers, age rating 4+, export compliance) |
| Privacy manifest | ✅ (pre-existing) | `ios/App/App/PrivacyInfo.xcprivacy` (email/deviceID/audioData, UserDefaults CA92.1, no tracking) |
| No-in-app-purchase model | ✅ | `IosPurchaseGate` + route layouts on /cart, /pricing, /catalog, /ai-packages, /checkout/review; unit tests; `docs/IOS-PURCHASE-COMPLIANCE.md` |
| Account deletion reachable in-app | ✅ (web flow; iOS device run PENDING) | `app/(auth)/account-deletion` + backend soft-delete migration; documented for review |
| Apple-compatibility guards | ✅ | `apple-compatibility.yml`, `tauri-ci.yml`, `mobile-ci.yml` green on main after billing-wall fix |

## 2. Owner-blocked (cannot be closed from this workspace)

| Blocker | What it gates | Unblock |
| --- | --- | --- |
| ASC API 401 (skipped per owner) — fresh Team Keys or correct per-tab Issuer ID + **Team ID** | All ASC operations: certificate creation, TestFlight upload, notarization via API key, App Store submission | Owner: re-download/recreate keys in Integrations → Team Keys; supply 10-char Team ID; update `.tools-state/apple-api-keys/appstoreconnect-api-keys.json` + GitHub secrets (`ASC_ISSUER_ID`, `ASC_KEY_ID_IOS`, `ASC_PRIVATE_KEY_IOS`, `ASC_KEY_ID_CI`, `ASC_PRIVATE_KEY_CI`, `APPLE_TEAM_ID`) |
| `public/.well-known/apple-app-site-association` still has `TEAM_ID` placeholders | iOS preflight fails closed (by design) | Same Team ID; then replace placeholders and redeploy |
| Developer ID Application certificate | macOS signing + notarization + stapled 0.7.10 + mac auto-update feed entries + Mac download re-enable | Created via ASC API once it authenticates (Developer-role key), then `APPLE_CERTIFICATE*` secrets |
| Real Mac (Apple Silicon) | Clean-download Gatekeeper test, 3-video protected playback (incl. after notarization), capture/PiP, update-over-existing, mic/speaking, relaunch→Dashboard | Run `docs/apple/MAC-QA-CHECKLIST.md` |
| Real iPhone/iPad | OTP/keyboard/AutoFill, safe areas, spelling, credits, deletion, TestFlight upgrade | Run `docs/apple/IOS-QA-CHECKLIST.md` |

## 3. Handover §7 status table (filled honestly)

| Platform | Fields | Status |
| --- | --- | --- |
| Windows | 0.7.9 public/live, untouched | ☑ CONFIRMED |
| macOS | Public version 0.7.9 with download **disabled**; protected playback PENDING; 0.7.10 QA PENDING; signing PENDING (no Developer ID cert); Hardened Runtime PENDING; notarization PENDING; staple PENDING; Gatekeeper PENDING; capture/PiP PENDING; auto-update built+gated, install proof PENDING; public URL — disabled until QA passes | ☐ COMPLETE (explicitly not claimed) |
| iOS | Version/build — workflow ready, none built; TestFlight PENDING; full QA PENDING; min iOS 16.4 declared+guarded, device run PENDING; metadata/privacy ready; account deletion implemented; purchase compliance implemented+documented; App Review PENDING; App Store URL — none | ☐ COMPLETE (explicitly not claimed) |

## 4. Cross-platform version matrix (handover §6 "one release note")

| Platform | Version | Channel | State |
| --- | --- | --- | --- |
| Website | build of 57e1ab3 | oetwithdrhesham.co.uk | Live |
| Web App | deploy of 490bc319+ | app.oetwithdrhesham.co.uk | Live |
| Android | 1.4.14 (versionCode 9) | Google Play | Live |
| Windows | 0.7.9 | Direct download + auto-update | Live, untouched |
| macOS | 0.7.9 public build / 0.7.10 held | Download disabled (kill-switch) | Pending signed release |
| iOS | — | TestFlight → App Store | Blocked on owner ASC inputs |

Every code change in this round (mac disable, updater rails, iOS purchase
gating, download-page states) was applied to the shared web app, so web,
desktop shells, Android and the future iOS shell all pick it up from the same
deploy — that is the parity mechanism.
