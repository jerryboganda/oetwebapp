# Apple Platform Compatibility Audit

Companion to [`APPLE_COMPATIBILITY_MATRIX.md`](./APPLE_COMPATIBILITY_MATRIX.md) (the support contract) and `apple-compatibility.json` (the machine-readable source of truth).

Every statement below is traceable to a file, a command output, or a vendor document. Where something has not been executed, it says so — compilation is not execution, and a simulator pass is not a device pass.

---

## 1. Executive summary

The project's Apple surface is a **Tauri 2 macOS shell** and a **Capacitor 7 iOS/iPadOS app** that both render the deployed Next.js app inside the system WebView.

The central problem was not a missing feature. It was that **the declared Apple support floors were false**. Because the WebView engine ships with the operating system on Apple platforms, the real minimum OS is set by the web stack — and the stack (Next.js 16 + Tailwind CSS v4) requires **Safari 16.4**, while the app advertised **iOS 14.0**. The App Store would happily install it on devices that cannot render it.

Alongside that, the `arm64` half of the Universal macOS build had **never been executed** in CI (only cross-compiled), the macOS release shipped **unsigned and unnotarized** behind documentation that had been wrong since macOS 15, and **no architecture or distribution check existed anywhere** in the pipeline.

What changed: floors are now true and enforced by machine-checked guards; the Intel slice is built *and executed* natively on real Intel hardware; signing, notarization and stapling are wired and gated; and the class of drift that created the original gap can no longer recur silently.

What is not proven: no code has run on an actual iPhone or iPad, on iOS 16.4 specifically, or on macOS 12; and notarization has not yet executed because no Apple secrets are configured. Those are listed as **NOT VERIFIED** rather than implied by green builds.

---

## 2. Technology detected

Read from the repository, not assumed.

| Layer | Technology | Evidence |
| --- | --- | --- |
| Desktop framework | **Tauri 2.11.3** (Rust), remote-URL thin client | `src-tauri/Cargo.toml`, `src-tauri/tauri.conf.json` (`frontendDist: "splash"`) |
| Desktop bundle | macOS `.dmg` + Windows NSIS | `tauri.conf.json` → `bundle.targets` |
| Mobile framework | **Capacitor 7** (`@capacitor/ios` 7.6.5) | `capacitor.config.ts`, `ios/App/Podfile` |
| Web framework | **Next.js 16** + **React 19**, App Router | `package.json`, `app/` |
| Styling | **Tailwind CSS v4.1.11** + PostCSS + autoprefixer | `app/globals.css` (`@import "tailwindcss"`), `postcss.config.mjs` |
| Native iOS code | Swift: 2 app-target plugins + bridge VC | `ios/App/App/{SpeakingRecorderPlugin,PlaybackAttestationPlugin,OETBridgeViewController,AttestationSecret}.swift` |
| Native macOS code | Rust with `objc2` for `NSWindow.sharingType` | `src-tauri/Cargo.toml` (`[target.'cfg(target_os = "macos")'.dependencies]`) |
| Package managers | pnpm 10.33.0, CocoaPods | `package.json` (`packageManager`), `ios/App/Podfile` |
| Backend | .NET 10 (`OetLearner.Api`) — **not** bundled in the Apple apps | `global.json`, `backend/` |

**No framework migration was performed or needed.** The mission's "use the official compatibility mechanism for that technology" resolved to: Xcode build settings for iOS, and `tauri.conf.json` + entitlements for macOS.

### Correction to an early hypothesis

`src-tauri/entitlements.plist` contained the comment *"Hardened runtime allowances for the bundled Node + .NET sidecars"*, and an empty gitignored `desktop-backend-runtime/` directory exists. This looked like it might mean the desktop app bundled architecture-specific Node/.NET runtime binaries — which **would** have been the dominant blocker, since a self-contained .NET publish is single-architecture.

It does not. Investigation found **no sidecars at all**: no `externalBin`, no `tauri-plugin-shell`, no `Command::new`/process spawning anywhere in `src-tauri/src/`, and `docs/tauri-desktop-shell.md` states "No sidecars, no local SQLite, no bundled Node/.NET". The directory is empty; the comment was Electron-era residue. Had this been assumed rather than checked, the entire architecture audit would have been aimed at a phantom.

---

## 3. Current architecture

```
macOS desktop (Tauri 2)
  oet-desktop (Rust, universal-apple-darwin)
   ├─ bundled splash (src-tauri/splash/)  ← capability gate + offline card
   ├─ WKWebView → https://app.oetwithdrhesham.co.uk   (system engine)
   ├─ keyring (Keychain), minisign updater, deep link oet-prep://
   └─ resources: desktop-runtime-config.json

iOS / iPadOS (Capacitor 7)
  App.app (arm64, iPhone + iPad)
   ├─ WKWebView → server.url (app.oetwithdrhesham.co.uk)   (system engine)
   ├─ errorPath → capacitor-web/error.html                 (offline recovery)
   ├─ SpeakingRecorder + PlaybackAttestation (Swift, app target)
   └─ 19 CocoaPods, all source-built from node_modules
```

Both are **remote-URL shells**, which is what makes the OS-supplied WebView engine the binding constraint and makes `apple-compatibility.json` necessary.

---

## 4. Original compatibility gaps

| # | Class | Gap | Severity |
| --- | --- | --- | --- |
| 1 | OS VERSION | iOS deployment target `14.0` while the content requires Safari 16.4. The App Store offered the app to devices that cannot render it. | **Critical** |
| 2 | SIGNING / DISTRIBUTION | macOS `.dmg` built unsigned and unnotarized; `README.md` documented a "right-click → Open" bypass that no longer exists on macOS 15+. | **High** |
| 3 | ARCHITECTURE | Intel slice `x86_64` cross-compiled but never executed in any CI lane; `tauri-ci.yml` built only the host architecture. | **High** |
| 4 | TEST INFRASTRUCTURE | No architecture, signature or packaging assertion existed anywhere (no `lipo`, `codesign`, `spctl`, `stapler`, `otool` in `.github/`). | **High** |
| 5 | FRAMEWORK | Xcode unpinned/unasserted despite App Store Connect requiring Xcode 26 + iOS 26 SDK since 2026-04-28. | **Medium** |
| 6 | FRAMEWORK | `ExportOptions.plist` used `method: app-store`, renamed to `app-store-connect` in Xcode 15.4. | **Medium** |
| 7 | MEMORY / UX | iOS had **no offline fallback**: with `server.url` set the bundled `webDir` is bypassed, so an offline first launch showed a blank/WebKit error page. | **Medium** |
| 8 | PERMISSIONS | `UIRequiredDeviceCapabilities` declared `arm64` — redundant, and a capability declaration the app does not actually need. | **Low** |
| 9 | TEST INFRASTRUCTURE | `mobile-ci.yml` launched the simulator app with `com.oetwithdrhesham.app`, which is the **Android** applicationId. The iOS bundle ID is `com.oetprep.learner` and `docs/play-store-automation.md` requires it stay that way — so that step could not pass. | **Medium** |
| 10 | ACCESSIBILITY / FORM FACTOR | The a11y suite ran only at a 1366×900 desktop viewport; no iPhone or iPad coverage at all. | **Medium** |
| 11 | DATA MIGRATION | `ios/App/Podfile.lock` not committed; the transitive pod graph re-resolves every build. | **Medium** |
| 12 | TEST INFRASTRUCTURE | No shared Xcode scheme committed; `-scheme App` relied on Xcode synthesising an implicit scheme. | **Low** |
| 13 | FRAMEWORK | `tauri-ci.yml` piped `xcodebuild` through `xcpretty`, which is not in the current runner image manifest (`xcbeautify` is). | **Low** |

### Gaps investigated and found NOT to be real

Reporting these matters as much as the real ones — each would have justified churn that the evidence does not support.

| Claim | Finding |
| --- | --- |
| Background `setInterval` drains battery (≈60 sites, none pausing on `visibilitychange`) | **Not applicable on Apple platforms.** WKWebView suspends JavaScript execution when the app is backgrounded, so the OS already provides the pause. Rewriting ~60 timer sites would have been high-risk churn for no Apple-side benefit. The intervals are all cleared on unmount. |
| Fixed-pixel widths (`min-w-[540px]`, `min-w-[520px]`, `min-w-[420px]`) break small iPhones | **Already correct.** Every one sits inside an `overflow-x-auto` container — the right pattern for wide data tables, and the same pattern the plan would have recommended. |
| `min-h-screen`/`h-screen` used on error and standalone pages | **No occurrences** in `app/**`. |
| Missing `NSPhotoLibraryAddUsageDescription` is a crash risk | **No crash path.** `saveToGallery`/`PhotoLibrary` has zero usages repo-wide. |
| Heavy libraries (`pdfjs-dist`, `wavesurfer.js`, `hls.js`, `recharts`) load eagerly on mobile | **Already lazy.** All are behind `await import(...)`, `next/dynamic` or `dynamic-recharts`, and `tests/static/frontend-heavy-imports.test.ts` already enforces it. `pdfjs-dist` is imported via its `/legacy/` build, which is the broader-compatibility entry point. `@zoom/meetingsdk` is not imported in app code at all. |

---

## 5. Dependency findings

### iOS native dependencies

Every iOS pod is **source-built** — no `vendored_frameworks`, no `*.xcframework`, no prebuilt slices. That means no plugin can block an architecture, and no plugin raises the floor above the app's own.

| Dependency | iOS min (from podspec) | Binary? |
| --- | --- | --- |
| `@capacitor/ios` (Capacitor, CapacitorCordova) | 14.0 | Source |
| All 14 first-party `@capacitor/*` 7.x plugins | 14.0 | Source |
| `@capawesome/capacitor-app-update` 7.2.0 | 14.0 | Source |
| `capacitor-voice-recorder` 6.1.0 | 14.0 | Source |
| `@aparajita/capacitor-biometric-auth` 7.2.0 | 13.0 | Source |
| `@aparajita/capacitor-secure-storage` 6.0.1 | 13.0 | Source |

Highest pod minimum is **14.0**, below the new app floor of 16.4, so no pod is a constraint. `assertDeploymentTarget` in `@capacitor/ios/scripts/pods_helpers.rb` only *raises* pods below 14.0 and therefore cannot conflict with a 16.4 app target.

CocoaPods-sourced transitive pods (`KeychainSwift ~> 21.0`, `IONFilesystemLib ~> 1.1.1`, `GCDWebServer ~> 3.0`) are resolved at `pod install` time and are **UNKNOWN from the repository** — precisely because `Podfile.lock` is not committed (gap #11).

### Rust / macOS dependencies

`tauri 2.11.3`, `tauri-build 2.6.3`, six Tauri plugins, `keyring 3.6.3` (`apple-native`), `objc2 0.6.4` (macOS-only), `serde`, `base64`, `hmac`, `sha2`. All are pure Rust or Apple system frameworks; none ships an external architecture-specific binary, and `Cargo.lock` is committed. No portability hazards were found (no inline assembly, no arch intrinsics, no `target_arch` branching — `std::env::consts::OS` is used only for a platform string).

Version pins were **left alone**. No dependency was upgraded, because none was shown to increase architecture compatibility.

---

## 6. Binary architecture findings

**Before:** the release workflow produced a Universal 2 `.dmg` via `--target universal-apple-darwin` (`scripts/tauri-dist.cjs:45`) — and **nothing verified it**, and **nothing executed the Intel slice**.

The earlier hypothesis that an architecture-incompatible embedded dependency might undermine the Universal claim was investigated and dismissed: with no sidecars, no prebuilt frameworks and no checked-in binaries (`src-tauri/binaries/` does not exist; everything under `src-tauri/target/` is gitignored build output), the bundle's Mach-O set is the Rust executable plus Apple system frameworks.

**After:** `assert-macos-bundle-architectures.mjs` walks the entire `.app` and fails if **any** Mach-O lacks `arm64` or `x86_64` — main executable, every `.dylib`, framework and helper, following symlinks — and refuses to report success on an empty scan. It runs in CI and in the release pipeline.

---

## 7. Deployment target findings

| | Before | After | Justification |
| --- | --- | --- | --- |
| iOS / iPadOS | `14.0` (pbxproj ×4, Podfile) | **`16.4`** | Next.js 16 and Tailwind CSS v4 both require Safari 16.4; on iOS the engine version *is* the OS version, so there is no upgrade path. Verified in the emitted CSS: 92 `@property`, 1912 `color-mix()`, 446 `oklch()`. |
| macOS | `12.0` | **`12.0`** (unchanged) | Safari updates ship independently of macOS on 11+, and Safari 16.4 was released for Big Sur and Monterey — so a macOS 12 machine with an updated Safari is genuinely supported. Kept for maximum reach, with a runtime capability gate for the un-updated case. |
| `UIRequiredDeviceCapabilities` | `["arm64"]` | **`[]`** | Redundant (every 16.4+ device is arm64) and not a hardware capability the app requires. Not currently excluding any device — a correctness fix, not a reach gain. |
| `TARGETED_DEVICE_FAMILY` | `"1,2"` | unchanged | iPad support was already correct, including all four `~ipad` orientations. |

The framework floors were left at `SWIFT_VERSION = 5.0` and the legacy `objectVersion = 48` deliberately: raising them would be an unverifiable rewrite with no compatibility benefit. `ENABLE_USER_SCRIPT_SANDBOXING` was deliberately **not** added — its absence is why the CocoaPods script phases run.

### The runtime capability gate

`src-tauri/splash/` now probes the real requirements before navigating and shows an actionable "Update Safari to continue" card if they are unmet, with a *Check again* action (and a *Continue anyway* escape hatch so a misdetection cannot permanently brick the app). Detection covers `oklch()`, `color-mix(in oklab, …)`, `crypto.randomUUID`, `structuredClone`, `Array.prototype.at`, and `@property`.

The `@property` probe is a *behavioural* test — a registered typed custom property's `initial-value` must resolve through `var()` — because a stylesheet-presence check is worthless here: engines without support drop unknown at-rules silently, so a successful parse proves nothing. The probe is written in plain ES2017 so it cannot throw on the very engines it exists to catch, and the splash's own CSS was changed from `:focus-visible` to `:focus` so the page meets its own Safari 15.0 floor.

---

## 8. Changes implemented

**Declaration and correctness**

| File | Change |
| --- | --- |
| `apple-compatibility.json` | **New.** Single source of truth for iOS/macOS floors, architectures and the two WebView floors. |
| `ios/App/App.xcodeproj/project.pbxproj` | All four `IPHONEOS_DEPLOYMENT_TARGET` → `16.4`. |
| `ios/App/Podfile` | `platform :ios, '16.4'`. |
| `ios/App/App/Info.plist` | Removed redundant `UIRequiredDeviceCapabilities` (`arm64`). |
| `src-tauri/splash/index.html`, `splash.js` | WebKit capability gate with an actionable recovery card; `:focus` instead of `:focus-visible`. |
| `capacitor-web/error.html` | **New.** Self-contained, network-free offline/retry screen (safe-area aware, reduced-motion aware). |
| `capacitor.config.ts` | `server.errorPath: 'error.html'` — gives iOS the offline recovery the Tauri shell already had. |
| `src-tauri/entitlements.plist` | Removed the stale Node/.NET sidecar comment, `allow-unsigned-executable-memory` and `inherit`; documented what remains and why. |
| 9 × `app/*/error.tsx`, `components/reading/ReadingPlayer.tsx` | `calc(100vh - Nrem)` → `calc(var(--app-viewport-height,100dvh) - Nrem)`, so iOS containers size against the *visible* viewport rather than the large viewport that includes the space behind the collapsing toolbar. |

**Release and distribution**

| File | Change |
| --- | --- |
| `.github/workflows/tauri-desktop-release.yml` | Apple signing/notarization credentials wired (gated so unsigned builds still work, and the `APPLE_*` family is `unset` when no certificate is configured — the empty-value trap the old comment warned about); architecture gate; `codesign --verify --deep --strict`, `stapler validate`, `spctl --assess` on the `.app`; DMG signed/notarized/stapled on demand and validated. Gates run **before** checksums so hashes describe the shipped bytes. |
| `.github/workflows/mobile-release.yml` | Xcode 26 / iOS 26 SDK assertion; `ExportOptions` → `app-store-connect`; new **fail-closed** IPA guard before upload. |
| `.github/workflows/mobile-ci.yml`, `tauri-ci.yml` | See §10. |
| `ios/App/App.xcodeproj/xcshareddata/xcschemes/App.xcscheme` | **New.** Committed shared scheme so `-scheme App` is deterministic. |

**Verification tooling**

| File | Purpose |
| --- | --- |
| `scripts/apple/assert-apple-config-consistency.mjs` | Four files, three formats, one source of truth. `--self-test` (4 checks). |
| `scripts/apple/assert-webview-floor.mjs` | Emitted CSS + shell pages vs. declared floors; strips comments and string literals first so probe code does not fail the floor it enforces. `--self-test` (21 checks). |
| `scripts/apple/assert-macos-bundle-architectures.mjs` | Recursive Universal 2 gate. `--self-test` (4 checks). |
| `scripts/apple/assert-ios-ipa.mjs` | Inspects the exported IPA. `--self-test` (5 checks). |
| `scripts/apple/run-ios-simulator-smoke.mjs` | Representative device-class launch smoke + coverage report; derives the bundle ID from the built app. |
| `.github/workflows/apple-compatibility.yml` | **New.** Runs the guards (self-tests first) on relevant paths. |

---

## 9. Tests added

Guard self-tests (executed in CI *before* the real check, so a guard cannot silently stop guarding):

- **Consistency (4):** aligned config produces no violations; drifted `IPHONEOS_DEPLOYMENT_TARGET` is caught; an undeclared `UIRequiredDeviceCapabilities` entry is caught; a missing `dmg` target is caught.
- **WebView floor (21):** numeric-not-lexical version comparison (`16.4 > 15.4`, `9.0 < 10.0`); each of six features is detected below its requirement *and* permitted at it; CSS block comments, JS string literals, JS line comments and HTML comments are stripped; live usage adjacent to a stripped region is still caught.
- **Bundle architectures (4):** real directory walking, non-Mach-O files return null, an empty scan cannot pass.
- **IPA (5):** device-family normalisation tolerates absence and rejects a raw comma string; non-Mach-O returns null.
- **Simulator smoke (35):** `simctl` JSON parsing filters non-iOS runtimes (tvOS/watchOS) and unavailable devices, and normalises `iOS-26-5` → `26.5`; empty/null payloads are tolerated; device-class selection covers the lowest *and* highest available runtime, never picks one device twice, and reports a class it cannot resolve; and no device pattern carries the global regex flag — which would make `pattern.test()` stateful and silently skip devices, a heisenbug this assertion exists to prevent.

The simulator smoke's parsing and selection logic was pure-tested by extracting it from its `simctl` I/O (which cannot run off a Mac). It previously had **no** execution path available at all: it only runs on a macOS runner, and that lane has never yet run.

Plus the iOS simulator coverage report, which is a machine-readable record of what was and was not exercised (uploaded as a CI artifact).

**No iOS unit/UI test target exists** (the Xcode project ships none), so iOS verification remains build + launch smoke + a11y. Adding one is a real, separate piece of work and is not claimed here.

---

## 10. CI changes

| Workflow | Change |
| --- | --- |
| `tauri-ci.yml` | The single `runtime-macos` job became a **two-lane matrix**: `macos-latest` (macOS 26, arm64) and `macos-15-intel` (x64). Each lane asserts the runner's `uname -m` **and** its CPU brand string (`Apple`/`Intel`) — so "it started" cannot be mistaken for "it ran natively" — builds for the explicit target triple, asserts the binary's slices with `lipo`, then launches. |
| `mobile-ci.yml` | Asserts Xcode ≥ 26 and iOS SDK ≥ 26; records the resolved `Podfile.lock` and warns that it is uncommitted; logs `xcodebuild` to a file instead of piping through `xcpretty`; replaced the single arbitrarily-chosen simulator with the representative device-class matrix; derives the bundle ID from the built app (fixing the Android-ID mismatch); uploads the build log, coverage report and lock file as evidence. |
| `speaking-a11y.yml` | Installs **webkit** in addition to chromium — otherwise the new Apple device-descriptor projects fail at browser launch. |
| `apple-compatibility.yml` | **New.** |

**Deliberately not done:** no `continue-on-error`, no `|| true` on compatibility-critical checks, and no suppression of the Podfile.lock warning. An unsigned macOS release stays green but prints an explicit `::warning::` so it can never be mistaken for a signed one.

---

## 11. Performance findings

The two headline performance concerns did not survive scrutiny, and this section records why no churn was made in their name.

1. **Timers.** ~60 `setInterval` sites exist and none pause on `visibilitychange`. On Apple platforms this is not a defect: **WKWebView suspends JavaScript execution when the app is backgrounded**, so the OS already provides the pause. All intervals are cleared on unmount. Rewriting ~60 sites would have been high-risk churn for no Apple-side gain. The highest-frequency timers (160 ms / 500 ms in `app/speaking/task/[id]/page.tsx`) run only during an active speaking recording, where a live audio-level meter is the intended behaviour.

2. **Heavy imports.** Already lazy and already regression-protected: `pdfjs-dist` (via its `/legacy/` build), `wavesurfer.js`, `hls.js`, `@microsoft/signalr` and `recharts` are all behind dynamic boundaries, enforced by `tests/static/frontend-heavy-imports.test.ts`. `@zoom/meetingsdk` is not imported in app code at all.

**3. `calc(100vh - …)` on iOS — partially fixed.** `100vh` on iOS is the *large* viewport: it includes the space behind the collapsing toolbar, so containers sized against it are taller than the visible area. The app already had the correct convention (`--app-viewport-height`, a `dvh`-backed variable set from `visualViewport.height` in `lib/mobile/runtime.ts`, with a `100dvh` fallback — `app/globals.css`, `app/layout.tsx:161`); 12 places had not adopted it.

- **Fixed (10):** all nine route error boundaries (`app/*/error.tsx`) and the Reading player's passage scroll area (`components/reading/ReadingPlayer.tsx:221`), now `calc(var(--app-viewport-height,100dvh) - Nrem)`.
- **Deliberately left (2):** `app/reading/practice/[sessionId]/page.tsx:137` (a fixed `h-`) and `app/speaking/task/[id]/page.tsx:708` (`min-h-` with `overflow-hidden`). Both are interactive exam surfaces that react to the soft keyboard — and `--app-viewport-height` *tracks* `visualViewport.height`, so it shrinks when the keyboard opens. For an error page with no keyboard that behaviour is unambiguous; inside an active exam it is a behavioural change that cannot be verified without a device. Left rather than guessed.

This change was **measured before it was made**, not assumed. An earlier draft of this work deferred the fix on the grounds that Tailwind arbitrary-value math around a `var()` fallback might emit silently. A throwaway PostCSS run against the project's own `@tailwindcss/postcss@4.1.11` disproved that — Tailwind normalises the subtraction operator and emits `calc(var(--app-viewport-height,100dvh) - 9rem)`, which is valid CSS (the spec requires whitespace around `-`). The fix was then applied and the emitted declarations were re-verified with the same technique.

No performance numbers are claimed. No benchmarks were run, and none are invented.

---

## 12. Remaining constraints

Carried forward honestly; full table in the matrix document.

- iOS 16.4 execution — **NOT VERIFIED** (no such simulator runtime on hosted runners).
- Physical iPhone / iPad — **NOT VERIFIED** (no devices available).
- macOS 12 execution — **NOT VERIFIED** (no such runner image).
- Notarization / stapling — **NOT VERIFIED** (no Apple secrets configured yet; the pipeline exists and is gated).
- Entitlement minimality under the hardened runtime — **NOT VERIFIED** (safe today: unsigned builds do not enable the hardened runtime).
- "Designed for iPad" on Apple Silicon Macs — **NOT VERIFIED**.
- `Podfile.lock` uncommitted — CI records and warns; committing requires a macOS `pod install`.
- No iOS test target; no unit tests for the served web app at device viewports (a11y covers those viewports).
- Intel macOS runners retire **August 2027** — needs a self-hosted Intel runner or an explicit retirement decision before then.
- Small-screen *layout overflow* is not asserted by a dedicated test; it is covered indirectly by a11y runs at 375×667 / 430×932 / iPad WebKit viewports.

---

## 13. Proof of validation

### Executed locally (this repository, this session)

```
node scripts/apple/assert-apple-config-consistency.mjs --self-test   → passed (4/4 checks)
node scripts/apple/assert-webview-floor.mjs --self-test              → passed (21 checks)
node scripts/apple/assert-macos-bundle-architectures.mjs --self-test → passed (4 checks)
node scripts/apple/assert-ios-ipa.mjs --self-test                    → passed (5 checks)
node scripts/apple/run-ios-simulator-smoke.mjs --self-test           → passed (35 checks)
node scripts/apple/assert-apple-config-consistency.mjs               → Apple configuration is consistent:
                                                                       iOS 16.4, macOS 12.0, device family 1,2
node scripts/apple/assert-webview-floor.mjs --css <real compiled CSS> → OK content stays within Safari 16.4
                                                                        OK   shell stays within Safari 15.0
python -c "yaml.safe_load(<all 26 .github/workflows/*.yml>)"       → ALL WORKFLOWS PARSE OK
playwright-core device descriptors                                  → all 4 Apple descriptors resolve OK
postcss + @tailwindcss/postcss@4.1.11 (emission probe)              → OK   min-height: calc(var(--app-viewport-height,100dvh) - 9rem);
                                                                      OK   max-height: calc(var(--app-viewport-height,100dvh) - 13rem);
                                                                      ALL VALID
```

The WebView-floor run against the repo's real compiled stylesheet is what *proves* the 16.4 floor: the guard independently found `@property` (needs 16.4), 1912 `color-mix()` (16.2) and 446 `oklch()` (15.4) in the shipped CSS, which is the evidence behind the iOS deployment-target change. While building it, the guard correctly flagged two real problems in my own work — a `:focus-visible` above the splash's 15.0 floor, and probe strings being mistaken for live CSS — both fixed, and the comment/string stripper now carries its own self-tests.

The Tailwind emission probe is what turned the `100vh` fix from a guess into a verified change: it compiled the exact class strings used by the ten edited files through the project's own Tailwind version and confirmed the emitted declarations are valid CSS.

### Executed on GitHub Actions — real results

First run on `main` for merge commit `d7c0c67db` (PR #220). The repo has to be **public** for Actions jobs to start; runs while private die in ~4–10s with a billing/spending-limit annotation and no logs (AGENTS.md §"GitHub Actions visibility").

| Workflow / run | Result | Evidence |
| --- | --- | --- |
| **Apple Compatibility** `34680606776` | consistency **PASSED** (11s); CSS floor **FAILED → fixed** | See below |
| **Tauri Desktop CI** `34680606766` | ✅ **all 4 jobs success** | `macOS launch smoke — arm64 (Apple Silicon)` ✅ and `macOS launch smoke — x86_64 (Intel)` ✅ |
| **Build & Deploy (web + API)** `34680606769` | ✅ **success** | web + API + agent-gateway images tagged `d7c0c67db` |
| **SBOM and SCA** | ✅ success | — |
| **Mobile CI** `34695676768` | ✅ **all 5 jobs success** — `iOS Build Check` ran for the first time in the repo's history | An earlier run (`34680606780`) failed on a pre-existing ESLint error that kept `iOS Build Check` **skipped** (`needs: [lint, unit-tests]`). That one error — `app/videos/[id]/page.test.tsx:40:16 react/display-name`, caused by an `eslint-disable-next-line` sitting one line above the `forwardRef` it was meant to cover — is fixed. |

**The `x86_64` slice has now been executed natively for the first time.** Before this change it was cross-compiled and never run; `macos-15-intel` asserts the runner's CPU brand string and the binary's slices, then launches it.

**Live production verified** (`scripts/ship/watch-deploy.ps1 -Sha d7c0c67db…`):

```
SHIP-WATCH_DEPLOY_OK
LIVE web: 200 ok          LIVE api-ready: 200 (database/migrations/stuck_jobs/storage all ok)
LIVE api-live: 200 ok
oet-web-green / oet-api-green / oet-agent-gateway  HEALTH=healthy  IMAGE=…:d7c0c67dba33…
ROUTER_ACTIVE_SLOT=green
LIVE_SHA_OK d7c0c67dba33122f58cb9cfe185fb2d02593867b (serving slot: green)
```

Blue still carries the previous SHA (`69f9ee823`) as the rollback path.

### The CSS-floor failure was a real defect in this guard

The first CI run failed:

```
FAIL content requires a newer engine than the declared Safari 16.4:
  .next/static/chunks/17opldn84roqk.css
    - text-wrap: balance: needs Safari 17.5 (2 occurrence(s))
OK   shell stays within Safari 15.0.
```

Two things follow from it, and they point in opposite directions:

1. **The 16.4 floor is confirmed correct.** The guard scanned four real built CSS files, and `@property`, `color-mix()`, `oklch()`, `@layer` and `:has()` were **not** flagged — i.e. every load-bearing feature the floor was derived from is genuinely at or below Safari 16.4. The deployment-target change is now validated against the artifact, not just against documentation.
2. **The guard itself was wrong.** `text-wrap: balance` is a cosmetic typographic refinement: unsupported browsers ignore the declaration and text wraps normally. Classifying it as a hard floor requirement would have forced the entire app onto a newer OS for no user-visible benefit. The table now carries a `severity`:
   - `hard` — the app renders wrong without it (Tailwind v4's colour pipeline and cascade layers): `oklch()`, `color-mix()`, `@layer`, `:has()`, `@property`.
   - `advisory` — degrades harmlessly and is **reported but never gates**: `text-wrap: balance`, `@starting-style`, `field-sizing`, `@container`, `subgrid`, `dvh/svh/lvh`, `:focus-visible`.

   The self-test now asserts the split in both directions, so a future edit cannot silently promote a cosmetic feature to a floor requirement (or demote a load-bearing one).

### Verification levels

| Level | Status |
| --- | --- |
| STATICALLY VERIFIED | ✅ guards + 74 self-test checks, config consistency, 26 workflow files parse, device descriptors |
| COMPILED | ✅ Rust both arches (Tauri CI) **and the iOS app builds** for the simulator — deployment target 16.4, committed shared scheme, Xcode 26 / iOS 26 SDK assertion |
| PACKAGED | ⏳ Not yet — needs a desktop release tag / mobile release dispatch |
| SIMULATOR TESTED | ✅ **6 configurations** across 3 device classes on the newest runtime — see below |
| NATIVELY EXECUTED (macOS arm64 **and** x86_64) | ✅ **both lanes green on real Apple/Intel hardware** |
| DEPLOYED + LIVE (web) | ✅ verified on the serving slot |
| PHYSICAL DEVICE TESTED | ❌ No devices available |
| MACOS 12 / IOS 16.4 EXECUTED | ❌ No such runners/runtimes exist |
| SIGNING / NOTARIZATION | ❌ Not yet — needs `APPLE_*` secrets |

### iOS verification — now executed (Mobile CI `34695676768`, job 33m31s)

`iOS Build Check` ran for the first time. Every step passed: `Assert a current Xcode toolchain`, `Install CocoaPods`, `Record the resolved CocoaPods lock`, `Build iOS (no code sign)`, `Launch app on iOS simulators`.

It confirms the three iOS fixes directly:

- **The 16.4 deployment target compiles.** The project builds with `IPHONEOS_DEPLOYMENT_TARGET = 16.4` and `platform :ios, '16.4'`.
- **The bundle-ID fix is real.** The log reads `Bundle identifier (from the built app): com.oetprep.learner`. The previous CI step launched `com.oetwithdrhesham.app` — the *Android* applicationId — so it could never have passed.
- **The committed shared scheme works** with `-scheme App`, and the Xcode 26 / iOS 26 SDK assertion passes on the runner.

Simulator coverage, exactly as reported by the run — note that the gap is stated, not hidden:

```
Available simulator runtimes: 26.2, 26.4, 26.5
VERIFIED     iPhone 16e (iOS 26.2) — smallest supported iPhone
VERIFIED     iPhone 17 Pro Max (iOS 26.2) — largest supported iPhone
VERIFIED     iPad Pro 13-inch (M5) (iOS 26.2) — iPad
VERIFIED     iPhone 17e (iOS 26.5) — smallest supported iPhone
VERIFIED     iPhone 17 Pro Max (iOS 26.5) — largest supported iPhone
VERIFIED     iPad Pro 13-inch (M5) (iOS 26.5) — iPad
NOT VERIFIED iOS 16.4 (declared minimum)
6 simulator configuration(s) verified.
```

**iOS 16.4 itself remains NOT VERIFIED** — no such simulator runtime exists on hosted runners, so the declared minimum cannot be executed in CI.

**One correction made after this run:** the job took 33m31s against a 45-minute cap, 25 minutes of it in the simulator sweep, because the script booted every device class on *two* runtimes. Sweeping a second runtime re-boots every class without adding form-factor coverage, so it now sweeps the newest runtime only (3 configurations instead of 6), with the step bounded by `timeout-minutes: 20` so a slow runner cannot consume the job budget. Runtime spread belongs in a job matrix, not in serialised boots.

### Definition of done for this pass

The declared support matrix in `apple-compatibility.json` is now machine-enforced and internally consistent, and every excluded platform is documented with a technical reason. The work is **not** "verified for the declared matrix" until the CI lanes above have run green — in particular the first signed and notarized release, and the first native Intel smoke on `macos-15-intel`.
