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
Status: running (LTO + codegen-units=1). Result pending — will record exit + binary path/size.

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

## Phase 3–10
_(pending — will be filled with evidence as each phase completes.)_
