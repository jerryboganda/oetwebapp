# Electron + Capacitor → Tauri 2 Migration — Master Status

**Last updated:** 2026-06-12
**Decision record:** `~/.claude/plans/as-this-project-have-velvety-hollerith.md` (approved ADR)
**Desktop shell branch:** `feat/tauri-desktop-shell` (worktree `D:\Projects\oet-tauri-desktop`)
**Backend SQLite fixes:** uncommitted in main checkout `D:\Projects\NEW OET WEB APP` (see §4)

---

## 1. The decision (recap)

Replace **Electron** (desktop) with **Tauri 2**; **keep Capacitor** for iOS/Android.
**Wails** and **GRIT** were evaluated and ruled out.

- **Mobile stays Capacitor** — it already uses the OS system webview (same engine Tauri mobile would use), so Tauri mobile offers no size/perf win while costing a rewrite of the working Kotlin/Swift `SpeakingRecorderPlugin`, biometric, secure-storage and push integrations.
- **Wails ruled out** — v3 still alpha (Jun 2026), Go-only backend (ours is .NET).
- **GRIT ruled out** — `gritframework.dev` is a Go+React meta-framework wrapping Wails (desktop) + Expo/React Native (mobile); adopting it means rewriting the .NET backend in Go and the UI in React Native.

Why desktop-only: Electron's penalty (bundled Chromium ~100 MB, 200–300 MB RAM) exists **only on desktop**. Mobile shells are already thin.

---

## 2. Architecture

The Tauri shell (`oet-desktop.exe`, Rust) orchestrates the **same two sidecars Electron spawns today**:
1. Bundled `.NET` API (`OetLearner.Api.exe`) on a loopback port (SQLite for offline).
2. `node .next/standalone/server.js` (Next.js SSR — middleware/CSP/CSRF intact).

The WebView2 (Win) / WKWebView (mac) window points at `http://127.0.0.1:{port}`. The injected `inject/desktop-bridge.js` reproduces the exact `window.desktopBridge` contract (`types/desktop.d.ts`) the Electron preload exposes — **the frontend needs zero changes**.

Full technical detail: [docs/tauri-desktop-shell.md](docs/tauri-desktop-shell.md).

---

## 3. Phase status

| Phase | Scope | Status |
|---|---|---|
| **Decision/ADR** | Tauri vs Wails vs GRIT; hybrid with Capacitor | ✅ Done |
| **0 — Spike gate (Windows)** | mic recording, sidecar lifecycle, kill-safety, ports, storage, print, SignalR, signed-updater round-trip | ✅ **All PASS** |
| **0 — Spike gate (macOS)** | WKWebView mic recording, cookies, print | ⛔ **PENDING — needs Mac hardware** (the one remaining make-or-break unknown) |
| **1 — Scaffold + sidecar runtime** | Rust orchestrator, ports/env/health/restart/Job-Object, Electron-data migration | ✅ Done, committed `37be29b26` |
| **2 — Bridge seam** | injected `window.desktopBridge`, conformance vitest | ✅ Done (5/5), committed |
| **3 — Feature parity** | all 18 IPC commands, tray, deep links, single instance, keyring secrets | ✅ Done, live-verified, committed |
| **4 — Packaging/updater/signing** | NSIS+dmg bundle, `tauri-plugin-updater`, signed round-trip, CI workflow | 🟡 **Mostly done** — built+signed NSIS, proven update round-trip with a **test** key; CI workflow committed `9089c6659`. **Pending:** production updater key + Azure Authenticode secrets |
| **5 — Beta dual-ship** | opt-in Tauri build alongside Electron, 2+ weeks soak | 📋 Not started (calendar-bound; blocked on Phase 0-mac + Phase 4 signing) |
| **6 — Cutover** | Electron "bridge" release downloads+installs Tauri, self-uninstalls | 📋 Not started (blocked on Phase 5) |

### Phase 0 Windows results (all verified on real hardware)

| Check | Result |
|---|---|
| Mic recording in WebView2 from localhost | ✅ 30s real-mic `audio/webm;codecs=opus`, decoded 29.94s/48kHz |
| Sidecar boot + health + port fallback | ✅ renderer fell back 3000→3001 live |
| Hard-kill orphan reaping (Job Object) | ✅ zero orphans (better than Electron today) |
| Bridge shape + all command round-trips from remote origin | ✅ runtime, secrets (Credential Manager), cache, speaking-audio, notification, fileInfo |
| Print dialog (`window.print()` → WebView2) | ✅ `edge://print/` preview opened |
| SignalR negotiate + proxy health through sidecar | ✅ `/api/health` 200; AI-hub negotiate 403 (route resolved, auth-gated) |
| Storage persistence across restart | ✅ cookie/localStorage/IndexedDB survive |
| Signed updater round-trip | ✅ 99.7 MB NSIS built+signed; running build detected 0.1.0→0.1.1, downloaded, **minisign verify passed** |
| Conformance vitest | ✅ 5/5 |

---

## 4. Backend SQLite fixes (REQUIRED for the desktop backend to work)

The bundled desktop backend runs on **SQLite**, which exposed three bugs that also affect the **current Electron** desktop. These are in the **main checkout working tree** (`feat/writing-voice-note-feedback`), tangled with an unrelated parallel force-delete session — **commit path-scoped**, do not bundle the two.

| Fix | File | Verification |
|---|---|---|
| `/health/ready` skips the pending-migrations probe on SQLite (EnsureCreated never writes `__EFMigrationsHistory`, so it 503'd forever and timed out desktop startup) | `Program.cs` ~1985 + new `HealthReadySqliteTests.cs` | ✅ test passes; live fresh-DB boot → 200 |
| `SubscriptionExpiryWorker` + `SpeakingAudioRetentionWorker` `IsSqlite` client-eval fallbacks | both worker files | ✅ targeted tests 2/2 |
| **Systemic**: `LearnerDbContext.OnModelCreating` applies a model-wide `DateTimeOffset → UTC-ticks` `ValueConverter` when `IsSqlite()`, so all timestamp comparisons translate (fixed ~8 workers at once) | `LearnerDbContext.cs` | ✅ live fresh-DB boot → **zero "could not be translated"** |

**Data-format note:** the converter changes SQLite timestamp columns from TEXT to INTEGER — applies to **fresh** desktop DBs only, which matches the contract (desktop shells never carry old SQLite DBs across schema generations; see `migrate_from_electron` in `src-tauri/src/sidecar.rs`, which deliberately does NOT copy the Electron DB).

A follow-up chip (`task_7b249ebb`) was filed but is **superseded** by the systemic converter.

---

## 5. What remains — and who owns it

**Blocked on user (cannot be done on the Windows dev machine):**
1. **macOS Phase 0 gate** — clone branch on a Mac, `cargo build`, run the mic/cookie/print checks. This is the last make-or-break unknown; if WKWebView mic capture fails, the documented fallback is native AVFoundation capture behind the existing `speaking:audio:*` IPC seam.
2. **Production code-signing** — Azure Authenticode secrets into the CI workflow; generate + vault a **production** updater keypair (the committed `tauri.conf.json` pubkey is a throwaway test key at `~/.tauri/oet-updater-test.key`).

**Blocked on calendar (by design):**
3. **Phase 5 beta** — opt-in Tauri build, separate app identifier, Sentry `flavor: tauri` tag, ≥2 weeks soak.
4. **Phase 6 cutover** — Electron bridge release; keep the Electron feed serving it ≥6 months.

**Explicitly NOT done and NOT a regression:** the Tauri shell has never run on macOS or Linux; production deployment of the desktop app happens through **installer + updater channels**, NOT the web production deploy.

---

## 6. CI

`.github/workflows/tauri-desktop-release.yml` (committed `9089c6659`): bridge-conformance gate → Windows NSIS + **macOS dmg** matrix with Rust caching, updater signing from `TAURI_SIGNING_PRIVATE_KEY` secret, checksums, artifact upload. This gives reproducible macOS dmg builds **without owning a Mac** — but a green macOS build still does not discharge the manual WKWebView mic gate (§5.1).
