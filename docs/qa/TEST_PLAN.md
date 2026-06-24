# OET Prep Desktop (Tauri 2) — Production-Readiness Test Plan

**Scope:** the Tauri 2 desktop shell in `src-tauri/`, its build/CI/signing, the desktop-relevant
frontend seams (`src-tauri/inject/desktop-bridge.js`, `types/desktop.d.ts`, the bridge consumers),
and release artifacts — targeting a **signed Windows x64** and **unsigned macOS (CI-built)**
internal-testing release.

**Out of scope:** .NET backend & billing, the Electron shell, Capacitor mobile, the web production
deploy, and the broad frontend audit (covered by their own suites/reports).

**Branch:** `qa/production-readiness` (worktree off `main`). **Owner:** QA/release pass.
**Living docs:** this file, [QA_REPORT.md](QA_REPORT.md), [BUGLOG.md](BUGLOG.md),
[TESTER_SETUP.md](TESTER_SETUP.md); root `CHANGELOG.md` appended.

---

## 1. What we are testing (architecture recap)

`oet-desktop.exe` (Rust/Tauri 2) shows a bundled splash, spawns two sidecars — the bundled .NET API
(`OetLearner.Api.exe`, SQLite) and the Next.js standalone server (`node server.js`) — health-polls
both, then points the system WebView (WebView2 on Windows) at `http://127.0.0.1:{port}`. An injected
`desktop-bridge.js` reproduces the `window.desktopBridge` contract (17 IPC commands) so the frontend
runs unchanged.

## 2. Risk-based priorities (what must never break)

| Priority | Area | Why |
|---|---|---|
| P0 | App launches; both sidecars boot; webview reaches the renderer | If this fails the app is unusable |
| P0 | Hard-kill leaves no orphan sidecars (Windows Job Object) | Data/port leaks, zombie processes |
| P0 | Signed installer installs & launches on a clean machine | Trust + first-run |
| P1 | Secure secrets (OS keyring) round-trip | Auth/session integrity |
| P1 | Auto-updater detects + verifies a signed update | Safe delivery of fixes |
| P1 | IPC command input validation (path traversal, URL scheme) | Security |
| P2 | Window state, tray routes, deep links, notifications, offline cache | Feature parity w/ Electron |
| P2 | High-DPI, resize, multi-monitor, keyboard nav | Windows UX |

## 3. Test strategy & coverage targets

- **Rust static analysis (Phase 2):** `cargo fmt --check`, `cargo clippy --all-targets
  --all-features -- -D warnings`, `cargo build --release`, `cargo audit`. Target: **zero warnings**,
  zero actionable advisories.
- **Rust unit tests (Phase 4):** cover the pure, testable logic in `commands.rs` and `sidecar.rs`
  — input sanitization/traversal, URL validation, offline-cache round-trip, speaking-audio base64
  reassembly, port selection, runtime-config precedence, Electron-migration DB-exclusion. Target:
  **all critical-path pure functions covered, happy + error paths**. (Coverage measured by
  function/branch on the testable surface, not a global %, since much of the crate is Tauri runtime
  glue that requires an app handle.)
- **Bridge contract (Phase 2/4):** the existing `desktop-bridge-conformance.test.ts` must stay green
  (bridge ↔ `types/desktop.d.ts` parity).
- **Desktop E2E (Phase 4):** Playwright spawns the packaged Tauri exe and drives the loopback
  renderer through the P0 flow (launch → sidecars → webview → bridge present → sign-in renders →
  core route). Optional minimal `tauri-driver` smoke proving the native shell.
- **Manual/exploratory (Phase 5):** Windows matrix below.
- **Build & delivery (Phase 9):** signed NSIS install/launch/uninstall on a clean state; updater
  round-trip from the production feed.

## 4. Windows manual matrix (Phase 5)

| # | Case | Expected |
|---|---|---|
| M1 | Clean first-run (no prior app data) | Splash → sidecars boot → renderer loads; fresh SQLite seeded |
| M2 | Backend port in use | Port fallback within scan range; app still boots |
| M3 | Hard-kill (Task Manager → End Task on shell) | Both sidecars die (no orphans) |
| M4 | Window resize / minimize / restore / focus | `desktop:window-state-changed` fires; layout stable |
| M5 | High-DPI 125% / 150% / 175% | No clipping/blur; controls usable |
| M6 | Tray menu → Dashboard / Study Plan / Quit | Navigates / exits |
| M7 | Single-instance (launch twice) | Second launch focuses existing window |
| M8 | Deep link `oet-prep://dashboard` and `oet-prep://pair?code=...` | Routes; **/pair currently 404 — see BUGLOG** |
| M9 | Offline / backend-down | User-safe error surface, no raw stack trace |
| M10 | Open external link | Opens system browser (http/https only) |
| M11 | Secrets round-trip (set/get/delete) | Persists via Windows Credential Manager |
| M12 | Uninstall (NSIS) → reinstall | Clean removal; reinstall works |

## 5. macOS (deferred / CI build only)

CI (`macos-latest`) produces an **unsigned** dmg. The **WKWebView microphone-capture gate cannot be
validated on CI** (no audio hardware / permission prompts) and must be run **once manually on a real
Mac** (MIGRATION-STATUS §5.1). Until then macOS is "builds, not validated."

## 6. Exit criteria (go for internal testing)

- CI green on `windows-latest` (fmt + clippy + cargo test + tsc + lint + conformance + debug build).
- All Critical/High bugs fixed; others documented with severity in [BUGLOG.md](BUGLOG.md).
- Capabilities least-privilege; updater on HTTPS w/ production key (private key in CI secret only);
  no secrets in repo or bundle.
- Signed NSIS `.exe` installs/launches/uninstalls cleanly on a fresh Windows machine; publisher shows
  after the tester trusts the `.cer`.
- `QA_REPORT.md`, `TEST_PLAN.md`, `BUGLOG.md`, `TESTER_SETUP.md`, `CHANGELOG.md` complete & accurate.
