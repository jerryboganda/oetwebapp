# BUGLOG.md — OET Prep Learner Mobile QA

Every issue found during QA is logged here with reproduction steps and severity. **All Critical/High must be fixed before internal-testing release.** Lower-severity issues are documented and triaged.

**Severity scale:**
- **Critical** — crash, data loss, security exposure, payment/auth broken, blocks a critical flow with no workaround.
- **High** — a critical flow is significantly degraded; broken on a common device/OS; no easy workaround.
- **Medium** — feature works but with notable defects; workaround exists; affects less-common config.
- **Low** — cosmetic, minor polish, rare edge case.

**Status:** Open · In Progress · Fixed · Won't Fix · Deferred

| ID | Title | Severity | Area | Device/OS | Status | Repro / Notes | Resolution |
|----|-------|----------|------|-----------|--------|---------------|------------|
| 1 | `ReadingAnswerSheetBuilder.test.tsx` flaky under full-suite load | Medium | Admin Reading authoring (test infra) | CI/local | Open (deferred) | Full `pnpm test`: 2 failures (15s timeout + "8 vs 16 calls"). Isolated re-run: 8/8 pass, exit 0, `tests 13.72s` (vs 15s global timeout). ⇒ contention flake, not a product bug. Pre-existing on `main`; not mobile. | Defer to Phase 7 CI audit. Candidate fix: raise per-test timeout for this file and/or fix cross-file mock isolation. Not a mobile release blocker. |
| 2 | `tsc` error masked by `ignoreBuildErrors` (main CI type-gate red) | High | Build/CI (billing test) | n/a | Fixed | `app/billing/page.test.tsx:77` unused `@ts-expect-error` (TS2578). `next build` ignores type errors so it slipped past; `qa-smoke` `tsc` would fail. | Removed the unused directive; `tsc --noEmit` now exits 0. Behavior unchanged. |
| 3 | Prod dep `hono <4.12.25` advisories via `@google/genai` | Low | Supply chain | n/a | Fixed | `pnpm audit --prod`: 11 advisories, all `@google/genai→@modelcontextprotocol/sdk→hono` (server-on-Lambda issues, not exercised). Does not ship in remote-URL mobile app. | `pnpm.overrides` `"hono@<4.12.25":">=4.12.25"`. Prod audit 11→6, high 1→0; `tsc` clean. Remaining 6 moderate (Sentry/OTel, server-side) tracked. |
| 4 | Deep-link verification files are placeholders | Medium | Deep links (Android App Links / iOS Universal Links) | Android/iOS | Android Fixed; iOS blocked | `assetlinks.json` had placeholder SHA-256; `apple-app-site-association` has placeholder TEAM ID. ⇒ `https://` App Links won't auto-verify; custom `oet-prep://` scheme still works regardless. Served from web deploy (`public/`), so the **staging URL** must serve the real file. | Android: set `assetlinks.json` SHA-256 = generated keystore fingerprint `41:5F:…:7F:A9` (commit; deploy to staging to take effect). ⚠️ If distributing via Play Store (Play App Signing re-signs), also add Google's signing SHA-256 from Play Console. iOS: TEAM ID still blocked on Apple account. |

| 5 | GitHub PAT embedded in git remote URL | **Critical** | Secrets / repo credentials | n/a | Open (owner action) | `git remote -v` → `https://ghp_***(REDACTED)@github.com/jerryboganda/oetwebapp.git`. A live GitHub Personal Access Token sits in `.git/config` and is exposed to anyone with filesystem/log access (and now this session's logs). NOT in tracked history, but high blast radius (push/read per token scopes). | **1) Rotate/revoke the token now** (GitHub → Settings → Developer settings → PATs). **2) Strip it from the remote:** `git remote set-url origin https://github.com/jerryboganda/oetwebapp.git` and rely on `gh` keyring / Git Credential Manager (already `gh`-authed). I can do step 2 on request. |

| 6 | CI/release Android build broken — JDK 17 vs Capacitor 7 requires JDK 21 | High | CI / Android build | n/a | Fixed | Release run 28131781720 failed at `:capacitor-android:compileReleaseJavaWithJavac` → `error: invalid source release: 21`. Both workflows pinned `JAVA_VERSION: '17'`, but Capacitor 7's Android library compiles to Java 21 — so the Android build had never been green since the Cap-7 upgrade (local JDK 17 hits the same wall). | Bumped `JAVA_VERSION` 17→21 in `mobile-ci.yml` + `mobile-release.yml`; re-dispatched the release. |

| 7 | Android `minSdk 22` < Capacitor 7 filesystem requires 23 | High | CI / Android build (manifest merge) | n/a | Fixed | Release run 28132257139 (JDK 21) failed: `Manifest merger failed: uses-sdk:minSdkVersion 22 cannot be smaller than version 23 declared in library [io.ionic.libs:ionfilesystem-android:1.0.0]` (pulled by `@capacitor/filesystem@7`). Second "never green since Cap-7 upgrade" break. | Bumped `minSdkVersion` 22→23 in `android/variables.gradle` (Android 6.0; drops ~0.2% of devices). |

| 8 | Release build flake — `next/font` can't fetch Google Fonts in CI | Low | CI / Next.js build | n/a | Mitigated (retry) | Run 28132524973: `next/font: Failed to fetch Manrope/Montserrat from Google Fonts` during `pnpm run build`. Transient (other runs built fine); `next/font/google` fetches at build time. | Retried → green (run 28132736756). Hardening option: self-host fonts via `next/font/local` to remove the build-time network dependency. |

| 9 | iOS deployment target 13.0 < Capacitor 7 plugins require 14.0 | Medium | CI / iOS build (pod install) | n/a | Fixed | mobile-ci iOS Build Check failed at `cap sync ios`/`pod install`: `CapacitorVoiceRecorder … required a higher minimum deployment target` (podspec `ios.deployment_target = '14.0'`; 17 plugin pods need 14). Podfile + pbxproj were 13.0. Third "never green since Cap-7 upgrade" break (iOS side). | Bumped iOS deployment target 13.0→14.0 in `ios/App/Podfile` + `project.pbxproj` (4×). iOS 14 still covers iPhone 6s+. |

---

## Triage summary
- **Critical: 1 (open — embedded PAT, owner must rotate)** · High: 3 (fixed) · Medium: 3 (#9 iOS deploy target fixed; #4 deep-link Android-fixed/iOS-blocked; #1 deferred non-mobile flake) · Low: 2 (1 fixed, 1 mitigated) — final.
- **Signed Android build is green & signature-verified (run 28132736756).** No open mobile-flow-blocking bugs. The sole open item is the owner-action PAT revocation.
