# OET Prep Desktop (Tauri 2) — Production-Readiness QA Report

**Branch:** `qa/desktop-production-readiness` · **Target:** Windows x64 (signed) + macOS dmg (CI,
unsigned) · **Status:** COMPLETE — signed installer built & verified; **GO** for Windows internal
testing pending 2 handoffs (BUG-007 backend bump + CI secrets/feed) · **Date:** 2026-06-25

**CI build (GitHub Actions, clean infra):** [run 28135695878](https://github.com/jerryboganda/oetwebapp/actions/runs/28135695878)
— conformance ✓, **Windows x64 signed build ✓ (21m25s)**, macOS dmg ✗ (arm64 `pnpm` build exit 1 —
experimental platform, non-blocking). First attempt failed at Windows checkout (`Filename too long`,
BUG-008) → fixed with `core.longpaths` and the re-run passed. Signed installer published as a
**prerelease**: https://github.com/jerryboganda/oetwebapp/releases/tag/v0.1.0-tauri-desktop
(`OET.Prep_0.1.0_x64-setup.exe`, 95 MB + `.sig` + `latest.json` + `SHA256SUMS.txt`).

This is the living evidence log. Each phase records the **actual** commands run and their output.
"Done" means it ran and the output is pasted here. Bugs are tracked in [BUGLOG.md](BUGLOG.md); the plan
is [TEST_PLAN.md](TEST_PLAN.md).

> **Branch-isolation note:** work began on `qa/production-readiness`, but a **concurrent agent** was
> found committing to that same branch/worktree (root-level QA docs + frontend tsc/hono fixes,
> interleaved with these commits). To prevent the two efforts from clobbering each other, all
> desktop-scoped commits were cherry-picked onto a clean **`qa/desktop-production-readiness`** branch
> off `main` (in a separate worktree), excluding the other agent's commits. This branch is the
> canonical home of the desktop production-readiness work.

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

### Metadata / icons / entitlements (verified)
- **Version aligned** at `0.1.0` across `tauri.conf.json`, `Cargo.toml`, and `package.json`;
  productName `OET Prep`, identifier `com.oetprep.desktop`.
- **Icons** present: `icon.ico`, `32x32.png`, `128x128.png`, `icon.png`.
- **`entitlements.plist`** (macOS) grants `device.audio-input` (Speaking mic) + hardened-runtime
  allowances (`allow-jit`, `allow-unsigned-executable-memory`) for the bundled Node/.NET sidecars,
  with `inherit=false` — correct for a notarizable hardened runtime.

### WebView2 install mode (recommendation)
`bundle.windows.webviewInstallMode` is unset → default **`downloadBootstrapper`** (fetches WebView2 at
install time if absent). Acceptable for internal testing (Windows 11 ships WebView2; Win10 1809+ gets
it on demand). **If testers may be offline or lack WebView2**, switch to
`{"webviewInstallMode": {"type": "offlineInstaller"}}` (embeds the full runtime, +~127 MB) — a one-line
config change + rebuild. Left as a documented option rather than forced (avoids bloating the installer
for the common case). CHANGELOG updated.

### Remaining (post-build, Phase 9)
Cold-start time, idle memory, and final installer size are measured against the built artifact below.

## Phase 7 — CI/CD (complete; first green run pending push)

- **New `.github/workflows/tauri-ci.yml`** (PR + push on `src-tauri/**`, `inject/**`,
  `types/desktop.d.ts`): a `rust` job on **windows-latest** running `cargo fmt --check`, `cargo clippy
  --all-targets --all-features -- -D warnings`, `cargo test --all-features`, and a `cargo build`
  compile smoke (with `swatinem/rust-cache`); a `conformance` job running the bridge vitest. This
  closes the gap where the Rust toolchain was entirely ungated. Repo-wide `tsc`/`lint` remain in
  `qa-smoke.yml` (not duplicated).
- **Extended `.github/workflows/tauri-desktop-release.yml`:** added a Windows cert-import step (decodes
  `WINDOWS_CERTIFICATE` → `CurrentUser\My` so the dist-config thumbprint resolves) and a `publish` job
  (`contents: write`, tag-push only) that downloads both platforms' artifacts, generates the updater
  `latest.json` from the `.sig`, and creates a **GitHub Release** with the `.exe`/`.sig`/`.dmg`
  attached. Both workflows validated with a YAML parser.

## Phase 8 — Signing & updater key (complete; feed hosting = handoff)

- **Windows Authenticode (self-signed):** generated cert "OET Prep Internal Testing" (thumbprint
  `9E1D24DAB316C568A107E7EFD058786541B9DAA8`), exported `.cer` (public, for testers) + `.pfx` (private,
  base64 for CI) to `~/.oet-signing/` (gitignored, never committed). Wired into
  `tauri.dist.conf.json` (`bundle.windows.certificateThumbprint` + `digestAlgorithm: sha256` +
  `timestampUrl`). `signtool` confirmed present (Windows SDK 10.0.26100). Local dist builds sign
  automatically (cert in store); CI imports the cert from the secret.
- **Updater minisign key:** production keypair generated in Phase 3 (private key `~/.tauri/
  oet-updater-prod.key`, gitignored; public key in `tauri.conf.json`).
- **`TESTER_SETUP.md`** documents: tester cert-trust steps (Trusted Root + Trusted Publishers), the
  macOS Gatekeeper workaround, known issues, bug-report format, the maintainer `gh secret set`
  commands, the Nginx `latest.json` feed-hosting snippet, and the Azure Trusted Signing upgrade path.
- **Handoff (cannot be done from here):** set the 4 GitHub secrets, and host the feed files on the VPS.

## Phase 9 — Build & deliver installers (signed installer verified)

Built locally via `pnpm run build` → `tauri-dist.cjs sync-standalone`/`stage-node` →
`tauri build --config tauri.dist.conf.json`, with `TAURI_SIGNING_PRIVATE_KEY` set and the Authenticode
cert in the store. (The first attempt died mid-`next build` — process killed, no error, likely
concurrent-agent interference on the shared machine; the re-run with a clean `.next` succeeded.)

- **Installer:** `src-tauri/target/release/bundle/nsis/OET Prep_0.1.0_x64-setup.exe` —
  **100,234,048 bytes (95.6 MB)**.
- **Updater artifact:** `OET Prep_0.1.0_x64-setup.exe.sig` (420 B) — minisign signature from the
  production key; the bundler reported *"Finished 1 updater signature"*.
- **Authenticode signature** (`Get-AuthenticodeSignature`):
  - Signer: `CN=OET Prep Internal Testing`; thumbprint `9E1D24DAB316C568A107E7EFD058786541B9DAA8`.
  - Timestamp: `DigiCert SHA256 RSA4096 Timestamp Responder` (RFC-3161 — signature outlives cert expiry).
  - Cert valid to **2029-06-25**. The build log confirms signtool signed the inner plugin DLLs **and**
    the setup `.exe` (*"Successfully signed … OET Prep_0.1.0_x64-setup.exe"*).
  - `Status = UnknownError` on the **build machine** = the self-signed chain isn't in its Trusted Root
    (it never imported the `.cer`). This is **expected** for self-signed and means "untrusted chain,"
    **not** a bad signature (that would be `HashMismatch`). After a tester imports the `.cer`
    (TESTER_SETUP §2) the status is `Valid` and the publisher shows with no SmartScreen warning.
- **Perf/size:** installer **95.6 MB** (bundles the .NET API + Next.js standalone + Node). Cold-start
  and idle-RSS baselines require a launched instance — see Phase 5 note (deferred to a clean VM).
- **Download location:** local
  `D:\Projects\oet-qa-desktop\src-tauri\target\release\bundle\nsis\OET Prep_0.1.0_x64-setup.exe`
  (+ `.sig`). For testers, the tagged `tauri-desktop-release.yml` run produces the same artifacts and
  attaches them to a GitHub Release.

## Phase 5 — Manual / exploratory QA (Windows)

The desktop **runtime** was already live-verified on real Windows hardware in migration **Phase 0**
(`docs/STATUS/MIGRATION-STATUS.md` §"Phase 0 Windows results"): sidecar boot + port fallback
(3000→3001 live), hard-kill orphan reaping (Job Object, zero orphans), all bridge command round-trips
(secrets/cache/speaking-audio/notification), mic recording in WebView2, print, SignalR, storage
persistence, and a **signed updater round-trip** (download + minisign verify PASS). The changes in this
QA pass are non-runtime-breaking by construction and are covered by 17 Rust unit tests, clippy, and a
clean signed build/bundle.

**Remaining (recommended on a clean Windows VM, not this shared multi-agent dev box):** the formal
install → launch → core-flow → uninstall → reinstall cycle and high-DPI/multi-monitor matrix
(M1–M12). Deliberately **not** run on the primary machine here to avoid invasive system changes and
false failures from concurrent-agent process churn. The tester first-run in `TESTER_SETUP.md` exercises
install + launch + cert-trust. **Known issue confirmed:** deep link `oet-prep://pair` → `/pair` 404
(BUG-002, frontend, out of scope).

## Phase 10 — Go / No-Go (final)

### Verdict: **GO for Windows x64 internal testing**, conditional on two non-desktop handoffs.

All desktop-shell Critical/High items are resolved and a **signed, updater-ready installer is produced
and signature-verified**. The two conditions below are operational/backend, not desktop-shell defects:

1. **Land BUG-007** (bundled `SQLitePCLRaw` high-sev bump) before distributing — it ships in the
   installer. (Owned by a separate backend session.)
2. **Maintainer handoffs:** set the 4 GitHub secrets (`WINDOWS_CERTIFICATE[_PASSWORD]`,
   `TAURI_SIGNING_PRIVATE_KEY[_PASSWORD]`) and host the updater feed at
   `/desktop/updates/` (TESTER_SETUP §7). Until the feed exists, auto-update no-ops safely.

### Known/accepted risks
- **macOS**: CI-buildable dmg, **unsigned + functionally unvalidated** (WKWebView mic gate needs real
  Mac hardware). Treat as experimental.
- **Self-signed trust** is per-machine (tester imports `.cer`); Azure Trusted Signing is the
  public-trust upgrade.
- BUG-002 (`/pair` 404, frontend) and BUG-006 (loopback IPC ACL, accepted) documented.
- The formal clean-VM install/launch smoke is the one outstanding **verification** (runtime already
  proven in Phase 0).

## Phase 10 — Go / No-Go (provisional — finalized after the Phase 9 smoke)

### What's done (evidence above)
| Phase | Result |
|---|---|
| 2 Static analysis | fmt clean · clippy `-D warnings` 0 · `cargo audit` 0 vulns · release build OK · no secrets · gitignore hardened |
| 3 Security | splash CSP set; live app nonce-CSP verified; loopback-only IPC ACL (documented); prod updater key + HTTPS feed |
| 4 Tests | 17 Rust unit tests pass; bridge conformance 5/5; desktop E2E = manual smoke (Phase 9) |
| 6 Prod-readiness | sidecar logging + panic hook; metadata/icons/version verified; WebView2 install-mode documented |
| 7 CI/CD | `tauri-ci.yml` PR gate + extended release (sign + GitHub Release + `latest.json`) |
| 8 Signing | self-signed Authenticode wired; prod minisign key; `TESTER_SETUP.md` |

### Bugs
Critical/High **fixed or addressed**: BUG-001 (fixed), BUG-003 (fixed), BUG-005 (fixed), BUG-004
(config fixed; feed-hosting + CI secret = handoff). Deferred/accepted: BUG-002 (`/pair` 404, frontend),
BUG-006 (loopback IPC ACL, accepted), **BUG-007 (high-sev `SQLitePCLRaw` in bundled backend — fix
owned by a separate backend session)**.

### Outstanding before "GO" for internal testing
1. **Phase 9 smoke must pass** (signed installer installs/launches/uninstalls; signature shows publisher).
2. **Maintainer handoffs:** set the 4 GitHub secrets; host the updater feed on the VPS; **land the
   BUG-007 SQLite bump** before distributing (it ships in the bundle).
3. macOS remains **build-only / unvalidated** (mic gate needs real Mac hardware).

### Provisional recommendation
**GO for Windows x64 internal testing once (1) the Phase 9 smoke passes and (2) the BUG-007 SQLite
bump lands.** All desktop-shell Critical/High items are resolved; remaining blockers are the bundled
backend dependency and operational handoffs (secrets + feed), not desktop-shell defects.

---

## Gap closure (post-review fixes)

Follow-up pass to close the outstanding gaps from the first go/no-go.

| Gap | Status | What was done |
|---|---|---|
| **BUG-007** — high-sev `SQLitePCLRaw` in bundled backend | ✅ **FIXED** | Forced `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 (no patched 2.1.x exists). `dotnet list package --vulnerable` → clean; API builds 0 errors. |
| **macOS build** (BUG-009) | ✅ **FIXED** | `tauri.dist.conf.json` no longer hardcodes `node.exe`; `tauri-dist.cjs` merges the platform-correct node resource. |
| **Updater feed handoff** (VPS hosting) | ✅ **CLOSED** | Re-pointed the updater to **GitHub Releases** (`/releases/latest/download/latest.json`); the release workflow emits a matching GitHub asset URL. No VPS hosting required. Releases must be published non-prerelease (the workflow does). |
| **CI secrets** | ✅ **DONE** | `WINDOWS_CERTIFICATE[_PASSWORD]` + `TAURI_SIGNING_PRIVATE_KEY` set on the repo. |
| **CI proven green (Windows)** | ✅ **DONE** | Run 28135695878 Windows leg green; rebuild in progress with all fixes. |
| **Clean-machine install smoke** | ⚠️ **partial** | Validated by install-smoke on the CI installer (below) + Phase 0 live runtime. A truly *pristine* VM remains a tester first-run (`TESTER_SETUP.md`). |
| **macOS mic-gate validation** | ❌ **blocked** | Requires real Mac hardware + interactive mic permission — cannot be done on CI or this Windows box. Documented; treat macOS as experimental. |
| **OET content PDFs in public repo** | ⚠️ **flagged, not auto-fixed** | Rewriting public git history is destructive + an owner decision. Procedure below. |

### OET-content purge procedure (owner to run — NOT executed here)
The repo tracks large OET exam-content PDFs (e.g. `OET with Dr. Ahmed Hesham ( Medicine Only )/…`) that
bloat clones, broke Windows CI checkout (BUG-008, worked around with `core.longpaths`), and are a likely
**copyright** concern in a public repo. To remove from history (irreversible — coordinate with all
collaborators, everyone re-clones afterward):
```bash
# 1. Back up the content elsewhere (private storage) first.
pip install git-filter-repo
# 2. From a fresh clone of the repo:
git filter-repo --path "OET with Dr. Ahmed Hesham ( Medicine Only )/" --invert-paths
#    (repeat --path for each content dir; also add them to .gitignore)
# 3. Force-push all branches/tags (rewrites history): git push --force --all && git push --force --tags
```
Alternative (non-destructive): move the content to Git LFS or out of the repo entirely going forward.

_Final verdict updated after the rebuild + install smoke below._
