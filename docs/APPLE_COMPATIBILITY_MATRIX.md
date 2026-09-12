# Apple Compatibility Matrix

**Source of truth for what this project supports on Apple platforms.**
Machine-readable companion: [`apple-compatibility.json`](../apple-compatibility.json) (repo root), enforced by the guards in [`scripts/apple/`](../scripts/apple).

Scope of this document: the **macOS desktop app** (Tauri 2 shell, `src-tauri/`) and the **iOS/iPadOS app** (Capacitor 7, `ios/`). The marketing website (`OET Project Website`) has no Apple app target and is out of scope.

---

## 1. Supported platforms

| Platform | Architecture | Minimum OS | Basis | Verification |
| --- | --- | --- | --- | --- |
| macOS | `arm64` (Apple Silicon) | **12.0** | Tauri 2 supports macOS 10.13+; the WebView engine comes from Safari, which updates independently of the OS on 11+ | **NATIVELY EXECUTED** — GitHub `macos-latest` (macOS 26, arm64) |
| macOS | `x86_64` (Intel) | **12.0** | Native slice, never Rosetta | **NATIVELY EXECUTED** — GitHub `macos-15-intel` (x64) |
| macOS | Universal 2 (`arm64` + `x86_64`) | **12.0** | `--target universal-apple-darwin` | **PACKAGED** + recursive architecture gate |
| iOS / iPadOS | `arm64` | **16.4** | Next.js 16 and Tailwind CSS v4 both require Safari 16.4; on iOS the Safari version *is* the OS version | **COMPILED / ARCHIVED** |
| iOS Simulator | `arm64` | available runtimes only (26.x today) | iOS 26 simulators on hosted runners | **SIMULATOR TESTED** |
| iOS Simulator | `x86_64` | — | No x86_64 iOS simulator runtime exists for Xcode 26 | **N/A** |
| iPad app on Apple Silicon Mac ("Designed for iPad") | `arm64` | 11.0 | Implied by `TARGETED_DEVICE_FAMILY = "1,2"` | **NOT VERIFIED** |

### Deployment targets, build SDKs and toolchain

| | Value | Where declared |
| --- | --- | --- |
| iOS deployment target | `16.4` | `ios/App/App.xcodeproj/project.pbxproj` (×4), `ios/App/Podfile` |
| iPad support | `TARGETED_DEVICE_FAMILY = "1,2"` | `ios/App/App.xcodeproj/project.pbxproj` |
| macOS minimum system version | `12.0` | `src-tauri/tauri.conf.json` → `bundle.macOS.minimumSystemVersion` |
| Required Xcode | `26` or later (`macos-26` ships 26.6) | `.github/workflows/mobile-ci.yml`, `mobile-release.yml` assert this |
| Required iOS SDK | `26` or later | asserted in the same steps |
| WebView floor — content | Safari **16.4** | `apple-compatibility.json` → `webview.contentSafariMin` |
| WebView floor — Tauri splash | Safari **15.0** | `apple-compatibility.json` → `webview.shellSafariMin` |

The build SDK and the deployment target are deliberately different numbers. Building against the iOS 26 SDK is an **App Store Connect submission requirement** (in force since 2026-04-28); it does not raise the minimum OS users need.

### The two WebView floors, and why they differ

Both Apple apps are remote-URL WebView shells: they load the deployed Next.js app over HTTPS. On Apple platforms the rendering engine is supplied by the operating system, so the **real** minimum OS is set by the web stack, not by the native projects.

- **Content floor — Safari 16.4.** Next.js 16 officially supports Safari 16.4+, and Tailwind CSS v4's output depends on features up to `@property` (Safari 16.4). Measured in this repo's emitted CSS: **1912** `color-mix()`, **446** `oklch()`, **92** `@property`.
- **Shell floor — Safari 15.0.** The Tauri splash must be able to render its own "update Safari" guidance on the oldest engine we claim to support, so it may never use a feature newer than 15.0. Enforced separately by `assert-webview-floor.mjs`.

### Why macOS can stay at 12.0 but iOS cannot go below 16.4

Apple ships Safari updates **independently of the OS** on macOS (Safari 16.4 was released for Big Sur and Monterey; macOS 14/15 can update Safari without updating macOS). A macOS 12 machine with an updated Safari therefore has an engine new enough for the content — so the desktop floor stays at 12.0 and a **runtime capability gate** in the splash handles the case where Safari was never updated.

That escape hatch does not exist on iOS: the WebView engine is the OS version. Hence 16.4 there.

---

## 2. Device support

### Supported iPhones and iPads

Everything that can run iOS/iPadOS 16.4 or later — iPhone 8 / 8 Plus / X and newer, and iPads whose final OS is 16.4 or later.

### Deliberately excluded, with reasons

| Excluded | Reason |
| --- | --- |
| iPhone 6s, 6s Plus, SE (1st gen), 7, 7 Plus | Capped at iOS 15.8, i.e. Safari 15.8. Tailwind v4's `@property` / `color-mix()` output has no polyfill — this is an upstream-unsupported configuration, not a configuration we chose to skip. |
| iPad Air 2, iPad mini 4 and older iPads | Capped below iOS 16.4 for the same reason. |
| macOS 11 and older | Cannot reach a Safari new enough and are outside Apple's security-update window. |
| macOS 27 and later on Intel | Apple has confirmed macOS 26 Tahoe is the last macOS release to support Intel Macs; macOS 27 is Apple Silicon only. |
| Rosetta as a support strategy | Both architectures are built and executed natively. Translation is acceptable only as an incidental fallback on OS versions where Apple still ships it, never as the compatibility claim. |
| `x86_64` iOS simulator | No such runtime exists for Xcode 26. |

`UIRequiredDeviceCapabilities` is intentionally **empty**. Requiring `arm64` there was redundant (every iOS 16.4+ device is arm64) and the key is an architecture statement rather than a hardware capability the app needs. Requiring only what is genuinely indispensable keeps devices in the supported set.

---

## 3. Known limitations and unverified claims

These are stated plainly rather than implied by a green build. See `APPLE_COMPATIBILITY_AUDIT.md` for evidence.

| Limitation | Why | Consequence |
| --- | --- | --- |
| iOS 16.4 execution is **NOT VERIFIED** | Hosted macOS runners carry only the newest few iOS runtimes (26.2/26.4/26.5 today); GitHub's policy is three `major.minor` platform versions per Xcode. | We prove the app *builds for* 16.4 and that the exported IPA declares it, but nothing has *run* on 16.4. Recorded automatically in the CI coverage report. |
| No home-button / sub-6" iPhone simulator is exercised | No `iPhone SE`-class device type is installed on the runner. | The smallest-screen class (375×667) is covered by **WebKit viewport emulation** in the a11y lane, not by a simulator. |
| macOS 12 execution is **NOT VERIFIED** | The oldest x86_64 runner image is macOS 15. No macOS 12 runner exists. | The 12.0 floor rests on Tauri's documented support plus the runtime capability gate. |
| Physical iPhone/iPad is **NOT VERIFIED** | No test devices available. | Simulator/emulator evidence only; device-only behaviours (real TCC prompts, microphone capture, push) remain unproven. |
| Notarization is **NOT VERIFIED** | Code exists and is gated on secrets; no signed run has happened yet. | The first release with `APPLE_*` secrets configured will exercise it. |
| Entitlement set under the hardened runtime is **NOT VERIFIED** | `allow-unsigned-executable-memory` and `inherit` were removed on the reasoning that they are unnecessary for a WKWebView host. | Safe today because unsigned builds do not enable the hardened runtime; the first signed run proves it. |
| `ios/App/Podfile.lock` — **now committed** | Previously never generated; produced by the first successful `iOS Build Check` run. | Pins `IONFilesystemLib 1.1.4` and `KeychainSwift 21.0.0` with checksums so pod resolution is reproducible. `GCDWebServer` is not in the build — it is only a transitive npm dependency of `capacitor-voice-recorder` and the `Podfile` never lists it. |
| No iOS test target exists | The Xcode project ships no unit/UI test bundle. | iOS verification is build + launch smoke + a11y, not unit tests. |
| `calc(100vh - …)` remains in 2 interactive exam surfaces | 12 offenders were found; 10 now use the app's `--app-viewport-height` (a `100dvh` fallback). The two left — `app/reading/practice/[sessionId]/page.tsx` and `app/speaking/task/[id]/page.tsx` — react to the soft keyboard, and `--app-viewport-height` tracks `visualViewport.height`, so the change is behavioural and cannot be verified without a device. | Minor: on iOS the collapsing toolbar makes those two containers slightly taller than the visible area. |
| Intel macOS runners retire **August 2027** | GitHub has announced `macos-15-intel` (and the x64 macOS 26 label) are the last x86_64 images. | Before then either add a self-hosted Intel Mac runner or retire the Intel claim explicitly. Do not let it silently become untested. |

### Out of scope for this matrix

`externalBin`/sidecars: the desktop shell bundles **no** Node or .NET runtime — it is a remote-only thin client, so there are no architecture-specific sidecar binaries to audit. (`src-tauri/entitlements.plist` previously carried a stale comment claiming otherwise; it has been corrected.)

---

## 4. How this is enforced

| Guard | What it proves |
| --- | --- |
| `scripts/apple/assert-apple-config-consistency.mjs` | `project.pbxproj`, `Podfile`, `Info.plist` and `tauri.conf.json` all agree with `apple-compatibility.json`, and `UIRequiredDeviceCapabilities` stays within the allowlist. |
| `scripts/apple/assert-webview-floor.mjs` | The emitted CSS and shell pages contain no **rendering-breaking** CSS feature newer than their declared floor. Comments and probe strings are stripped first, so naming a feature in detection code does not fail the check. Cosmetic features that degrade harmlessly (e.g. `text-wrap: balance`) are reported as warnings, never as gates — an earlier revision failed CI on one of those, which would have raised the whole app's floor for a typographic nicety. |
| `scripts/apple/assert-macos-bundle-architectures.mjs` | **Every** Mach-O in the packaged `.app` — executable, dylibs, frameworks, helpers — contains both `arm64` and `x86_64`. |
| `scripts/apple/assert-ios-ipa.mjs` | The exported IPA declares the documented `MinimumOSVersion`, supports iPhone and iPad, is `arm64` only, carries no undeclared required capabilities, and (in release) verifies under `codesign`. |
| `scripts/apple/run-ios-simulator-smoke.mjs` | Launches the built app across representative device classes on the newest available runtime and writes a coverage report that names what it could **not** cover. Reads the bundle ID from the built app rather than accepting one, because the iOS bundle ID and the Android applicationId are deliberately different. One runtime only: booting is the dominant cost (~4 min per configuration), and a second runtime re-boots every class without adding form-factor coverage. |

All five run in CI. The first four also carry `--self-test`, which is executed before the real check so the guards cannot silently stop guarding.

| Workflow | Coverage |
| --- | --- |
| `apple-compatibility.yml` | Consistency + emitted-CSS floor (builds the web app). |
| `tauri-ci.yml` | Per-architecture Rust builds; **native** launch smoke on real Apple Silicon **and** real Intel; binary slice assertion. |
| `tauri-desktop-release.yml` | Universal build; architecture gate; `codesign --verify --deep --strict`, `spctl --assess`, `xcrun stapler validate` on `.app` and `.dmg` when Apple secrets are present. |
| `mobile-ci.yml` | Xcode 26 / iOS 26 SDK assertion; pod resolution record; simulator build; multi-device launch smoke; a11y at Apple form factors. |
| `mobile-release.yml` | Archive → IPA export → IPA guard (fail-closed) before upload. |

---

## 5. Changing this matrix

1. Edit `apple-compatibility.json` — it is the single source of truth.
2. Update the real declaration (`project.pbxproj` / `Podfile` / `tauri.conf.json` / `Info.plist`) to match.
3. Run the guards (they each support `--self-test`).
4. Raising a floor is a product decision and must be recorded in this document with its justification — the `@property`/Safari 16.4 dependency is the reason the iOS floor is what it is, and it will not move on its own.
