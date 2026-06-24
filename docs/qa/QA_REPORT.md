# OET Prep Desktop (Tauri 2) — Production-Readiness QA Report

**Branch:** `qa/production-readiness` · **Target:** Windows x64 (signed) + macOS dmg (CI, unsigned) ·
**Status:** IN PROGRESS · **Started:** 2026-06-25

This is the living evidence log. Each phase records the **actual** commands run and their output.
"Done" means it ran and the output is pasted here. Bugs are tracked in [BUGLOG.md](BUGLOG.md); the plan
is [TEST_PLAN.md](TEST_PLAN.md).

---

## Phase 0 — Recon (complete)

The app is a multi-target monorepo; the QA target is the **Tauri 2 desktop shell** (`src-tauri/`), an
in-progress Electron→Tauri migration (Phases 0–3 done & live-verified on Windows per
`docs/STATUS/MIGRATION-STATUS.md`). The shell spawns the .NET API + Next.js standalone as sidecars and
points WebView2 at loopback; `inject/desktop-bridge.js` reproduces the `window.desktopBridge` contract
(17 IPC commands). Full inventory in [TEST_PLAN.md](TEST_PLAN.md) §1.

**Toolchain verified (local):** rustc/cargo 1.96.0, tauri-cli 2.11.3, pnpm 10.33.0, Node 24.14.0,
dotnet 10.0.201. `src-tauri/Cargo.lock` committed. Repo **public** (`jerryboganda/oetwebapp`), no LICENSE.

---

## Phase 1 — Plan & gate (complete)

Plan approved. Decisions: branch from `main`, scope `src-tauri/**` + desktop CI + docs only; Windows x64
(build+self-sign+test) + macOS dmg (CI build, unsigned); NSIS only; self-signed Authenticode; updater
wired to the production HTTPS feed with a fresh production minisign key (private key in CI secret only).
Working in an isolated git worktree so the `main` checkout stays untouched.

---

## Phase 2 — Static analysis & hygiene (in progress)

### Rust formatting — `cargo fmt`
Found deviations (code was hand-written in a compact style). Added `src-tauri/rustfmt.toml`
(`edition=2021`, `max_width=100`), applied `cargo fmt`, then verified:

```
$ cargo fmt --check
FMT_CLEAN   (exit 0)
```

### Rust lint — `cargo clippy --all-targets --all-features -- -D warnings`
Initial run surfaced **4 warnings**, all in `src/lib.rs`:
- `unnecessary_mut_passed` at `lib.rs:55` (`set_extended_limit_info(&mut info)` → `&info`)
- `needless_borrows_for_generic_args` ×3 at `lib.rs:72,179,295` (`win.eval(&format!(…))` /
  `initialization_script(&bridge_script())` → drop the `&`)

All four fixed. Added `src-tauri/clippy.toml` (`msrv = "1.77.2"`). Re-run:

```
$ cargo clippy --all-targets --all-features -- -D warnings
Finished `dev` profile ... in 43.68s
CLIPPY_EXIT=0   (zero warnings)
```

### Rust release build — `cargo build --release`
```
Finished `release` profile [optimized] target(s) in 11m 39s   (RELEASE_EXIT=0)
```
Produces `src-tauri/target/release/oet-desktop.exe` (15.2 MB shell binary; the full installer adds the
bundled sidecars). This was a profile-compile check (LTO + codegen-units=1); the shipped installer is
rebuilt with the Phase 3 config in Phase 9.

### Supply chain — `cargo audit` (RustSec)
Installed `cargo-audit`; scanned **553 crate dependencies**:

```
$ cargo audit
Scanning Cargo.lock for vulnerabilities (553 crate dependencies)
... 0 vulnerabilities ...
warning: 17 allowed warnings found   (exit 0)
```

**0 vulnerabilities.** The 17 warnings are `unmaintained`/`unsound` advisories on **transitive** deps:
the `gtk-rs`/GTK3 family (`atk`, `atk-sys`, `gdk`, `gdk-sys`, `gtk`, …), `glib 0.18.5`
(RUSTSEC-2025-0098, *unsound*), `proc-macro-error` (build-time macro), and the `unic-*` family. The
GTK/`glib` crates are **Linux-only** transitive deps in `Cargo.lock` and are **not present in the
Windows/macOS binaries we ship**; the rest are build-time or upstream-Tauri transitive deps not
directly depended on by this crate. **No actionable fix on our side** — none are direct dependencies;
resolution would come from upstream Tauri version bumps. `cargo-deny` deliberately skipped (heavy
install; RustSec via `cargo audit` already covers the advisory gate; no OSS license-compliance need on
a single-owner repo).

### Bridge contract — conformance test
```
$ pnpm vitest run src-tauri/__tests__/desktop-bridge-conformance.test.ts
Test Files  1 passed (1) · Tests  5 passed (5)   (exit 0)
```

### Hygiene
- **`.gitignore`** hardened with code-signing/updater key patterns (`*.pfx *.p12 *.pem *.key *.cer
  *.crt *.snk`, `.tauri/`, `src-tauri/*.key`) — confirmed **no** such files are currently tracked.
- **Secret scan** (private-key blocks, Stripe live/test, AWS, Slack tokens) over the worktree: all 5
  hits are documentation examples (`AKIAIOSFODNN7EXAMPLE`, copilot `.md` samples) or a fake test
  placeholder (`sk_test_production_readiness`). **No real secrets committed.** The updater key in
  `tauri.conf.json` is a minisign **public** key (safe).
- Repo-wide `tsc --noEmit` / `pnpm lint` are existing project CI gates (qa-smoke etc.) and are not
  re-litigated here; the desktop contract is covered by the conformance test above.

**Phase 2 verdict so far:** Rust is fmt-clean and clippy-clean (`-D warnings`); 0 advisories;
no committed secrets; signing-artifact ignores in place. Release-build result pending.

---

## Phase 3 — Tauri 2 security hardening (in progress)

### Capabilities / IPC least-privilege (`remote-localhost.json`)
Audited the remote-origin capability. Findings:
- The `remote.urls` are **loopback-only** (`http://localhost`, `http://localhost:*`, `http://127.0.0.1`,
  `http://127.0.0.1:*`) — **no external origins** are granted IPC. The port wildcard is **required**
  because the renderer's port is chosen dynamically at runtime (`find_available_port`).
- All 17 `allow-*` grants correspond 1:1 to commands in the `window.desktopBridge` contract
  (`types/desktop.d.ts`) and are exercised/validated by the conformance test — none are dead grants.
- Input validation is present on every sensitive command: `sanitize_component` (alphanumeric + `._-`,
  length-capped) blocks path traversal on cache/secret/speaking keys; `open_external` accepts only
  `http`/`https`; `get_dropped_file_info` returns metadata only.
- **Residual risk (documented, accepted):** any content the webview loads from a loopback origin could
  call these commands. In practice the shell only ever navigates the webview to the trusted Next.js
  renderer it spawned, so this is low-risk. Logged as BUG-006 (accepted with rationale).
- `core:default` retained (used by window/tray/menu/webview/path/event); trimming it for marginal gain
  risks breaking the splash/tray and was avoided ("do no harm").

### CSP (the nuance done right)
- The **live app** is served from the loopback Next.js origin, which already enforces a strong
  **per-request nonce-based CSP** via `middleware.ts` (`default-src 'self'`, nonce `script-src`,
  scoped `connect-src`/`img-src`/`media-src`), set on every response (lines 78–85, 208/213). Phase 0
  verified all IPC command round-trips succeed under this CSP, so no Tauri-specific CSP additions are
  needed for the remote origin.
- `tauri.conf.json` `csp` was `null`, which only left the **bundled static splash** (`splash/index.html`)
  without a policy. Set a conservative splash CSP:
  `default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self';
  object-src 'none'; base-uri 'self'; frame-ancestors 'none'`. The splash has only an inline `<style>`
  block and no scripts, so this is non-breaking; the injected bridge runs as a webview
  initialization script (not subject to page CSP). To be verified live in Phase 5.

### Auto-updater (BUG-004)
- Generated a **fresh production minisign keypair** (`tauri signer generate`). Private key stored
  **outside the repo** at `~/.tauri/oet-updater-prod.key` (gitignored pattern in place); it must be
  added as the `TAURI_SIGNING_PRIVATE_KEY` GitHub secret (Phase 7/8) and **never** committed.
- Replaced the throwaway test pubkey in `tauri.conf.json` with the production public key
  (`…EC23D1888C42D62F`), and changed the endpoint from `http://127.0.0.1:8765/latest.json` to
  **`https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`** (HTTPS, required for non-loopback
  updater endpoints). The `OET_UPDATER_URL` env override in `lib.rs` still allows pointing at a local
  feed for testing.
- **Remaining (handoff):** host the feed (`latest.json` + signed `.exe` + `.sig`) at that path on the
  prod server (nginx/route snippet to be delivered in Phase 8), and set the CI signing secret. Until
  then the updater check fails gracefully (logged, no crash) — strictly better than the localhost
  placeholder.

### Secrets-in-bundle
- `withGlobalTauri: false` confirmed. The Rust sources contain no baked tokens (only the public updater
  key + public URLs). A grep of the **built** `.next/standalone` + binary for baked secrets is deferred
  to Phase 9 (artifacts not yet built in this worktree).

## Phase 4 — Automated tests (Rust complete; E2E pending build)

### Rust unit tests
Added behavior-preserving extractions (`validate_external_url`, `decode_base64_chunks`) to make the
core IPC logic testable without side effects, plus 17 unit tests across `commands.rs` and `sidecar.rs`:

```
$ cargo test
running 17 tests
test commands::tests::sanitize_rejects_empty_and_whitespace ... ok
test commands::tests::sanitize_neutralizes_path_traversal_and_separators ... ok
test commands::tests::sanitize_preserves_allowed_chars ... ok
test commands::tests::sanitize_caps_length ... ok
test commands::tests::validate_external_url_accepts_http_and_https_and_trims ... ok
test commands::tests::validate_external_url_rejects_empty_and_other_schemes ... ok
test commands::tests::decode_base64_chunks_joins_in_order ... ok
test commands::tests::decode_base64_chunks_empty_is_empty ... ok
test commands::tests::decode_base64_chunks_rejects_invalid ... ok
test sidecar::tests::find_available_port_returns_bindable_in_range ... ok
test sidecar::tests::find_available_port_skips_occupied ... ok
test sidecar::tests::renderer_env_sets_expected_keys ... ok
test sidecar::tests::backend_env_toggles_packaged_vs_dev ... ok
test sidecar::tests::load_runtime_config_reads_file_and_strips_bom ... ok
test sidecar::tests::load_runtime_config_user_data_overrides_resource ... ok
test sidecar::tests::migrate_from_electron_is_noop_when_marker_exists ... ok
test sidecar::tests::sidecar_log_pipes_creates_log_and_rotates ... ok
test result: ok. 17 passed; 0 failed
```
Coverage focuses on the security-relevant pure logic (input sanitization/traversal, URL scheme,
base64 reassembly) and runtime setup (port selection, env construction, config precedence/BOM,
migration idempotency, log rotation). The remaining crate surface is Tauri runtime glue requiring a
live app handle (exercised by E2E + manual QA, not unit-testable).

### Bridge conformance
`pnpm vitest run src-tauri/__tests__/desktop-bridge-conformance.test.ts` → 5/5 (recorded in Phase 2).

### Desktop E2E
Harness wired in Phase 7/9 — Playwright spawns the packaged exe and drives the loopback renderer.
Execution deferred until the signed installer is built (Phase 9), where results are recorded.

## Phase 6 — Production readiness (logging done; perf/metadata in Phase 9)

### Sidecar logging + panic capture (BUG-003)
Both sidecars previously ran with `Stdio::null()`, discarding all output. Now:
- `sidecar_log_pipes()` routes each sidecar's combined stdout+stderr to
  `<app_data>/logs/backend.log` / `renderer.log`, keeping the prior session as `*.log.old`
  (one-generation rotation — bounded growth). Falls back to `null` if the file can't be created, so
  **logging never blocks a sidecar from starting** ("do no harm").
- A panic hook writes Rust panics to `<app_data>/logs/desktop.log` (packaged builds run
  `windows_subsystem="windows"` with no console, so panics would otherwise vanish).
- Covered by `sidecar_log_pipes_creates_log_and_rotates` unit test.

Remaining Phase 6 items (perf/size baselines, metadata/icons, `webviewInstallMode`, README/CHANGELOG)
are completed alongside the Phase 9 build.

## Phase 5, 7–10
_(pending — filled as each completes.)_
