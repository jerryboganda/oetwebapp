# Tauri Desktop Shell (`src-tauri/`)

Phase 1–3 implementation of the Electron→Tauri migration (ADR: `~/.claude/plans/as-this-project-have-velvety-hollerith.md`). The Electron pipeline (`electron/`, `electron-builder.config.cjs`, `scripts/desktop-dist.cjs`) stays frozen-but-shippable until Phase 6 cutover.

## Architecture

`oet-desktop.exe` (Rust, ~15 MB debug) orchestrates the same two processes Electron spawns today:

1. **Backend sidecar** — `desktop-backend-runtime/OetLearner.Api.exe`, env map ported from `electron/main.cjs getBundledBackendEnv` (SQLite under the Tauri app-data dir, port scan from 5198, polls `/health` — NOT `/health/ready`, which 503s forever on fresh SQLite; see flagged backend bug).
2. **Renderer sidecar** — `node .next/standalone/server.js`, env from `getStandaloneServerEnv` (port scan from 3000, polls `/api/health`). SSR/CSP-nonce middleware/CSRF all stay intact.

The WebView2/WKWebView window starts on a bundled splash, then navigates to `http://127.0.0.1:{port}`. A Windows **Job Object** (kill-on-close) guarantees no orphaned sidecars even on hard kill — verified, better than Electron today.

## Bridge (Phase 2)

`inject/desktop-bridge.js` (registered as `initialization_script`) implements `window.desktopBridge` with the exact `types/desktop.d.ts` shape — all 7 frontend consumers work unchanged. Verified live on the remote origin AND by `src-tauri/__tests__/desktop-bridge-conformance.test.ts` (5/5).

Implementation notes:
- App commands are ACL-gated for remote origins: `build.rs` declares them via `AppManifest::commands`, and `capabilities/remote-localhost.json` grants each `allow-*` to `http://localhost:*`/`http://127.0.0.1:*`.
- Window-state events are delivered as DOM `CustomEvent`s (no event-plugin capability needed).
- Speaking-audio blobs cross IPC as base64 (`chunksBase64`/`dataBase64`); the bridge rehydrates `ArrayBuffer`s so the outward contract is unchanged.
- Secrets use the OS keyring (`windows-credential-manager`/Keychain) — **Electron safeStorage vault contents are NOT migrated** (Chromium-specific ciphertext) → users re-login once.
- Data migration (`sidecar::migrate_from_electron`) copies `storage/` + `offline-content/` from `%APPDATA%\OET Prep\prod\user-data`, **deliberately not the SQLite DB**: the backend bootstraps SQLite via `EnsureCreatedAsync` (no-op on non-empty DBs), so an old-schema Electron DB crashes the new backend (`no such table: RuntimeSettings` — reproduced). Revisit when the backend adopts real EF migrations on SQLite.

## Dev & build

```powershell
# prerequisites (once): pnpm run build && node scripts/tauri-dist.cjs sync-standalone && node scripts/tauri-dist.cjs publish-backend
node scripts/tauri-dev.cjs          # cargo run with OET_REPO_ROOT set
node scripts/tauri-dist.cjs build   # full dist: backend publish + next build + stage node + tauri build (NSIS)
```

`tauri.dist.conf.json` holds `bundle.resources` (standalone, backend-runtime, node.exe) so plain `cargo build` doesn't require staged artifacts.

## Phase 4 — remaining packaging work

- **Windows signing**: wire Azure Trusted Signing via NSIS `signCommand` in `tauri.dist.conf.json` (reuse the env contract from `scripts/desktop-dist.cjs`).
- **Updater**: add `tauri-plugin-updater`; generate keys with `pnpm dlx @tauri-apps/cli signer generate`; host `latest.json` + artifacts under a new path next to `ELECTRON_UPDATES_URL`; port `scripts/desktop-update-metadata-verify.cjs` to the minisign manifest.
- **Cert pinning**: webview TLS is OS-owned — port pin enforcement into the Node proxy (undici Agent, SPKI-SHA256, reuse parsing from `electron/security/certificate-pinning.cjs`) and force all remote traffic through the proxy in Tauri builds.
- **WebView2 permission handler**: implement `PermissionRequested` on the Rust side so a user's one-time mic denial doesn't permanently brick recording (WebView2 persists denials in the profile `Preferences`; reproduced + wiped during the spike).
- **macOS**: `entitlements.plist` is in place (mic + sidecar allowances); needs icons (.icns), notarization, and the **WKWebView gate tests on real hardware** (mic recording mp4/aac, cookie persistence, print).

## Phase 5/6 — beta + cutover (operational)

- Beta dual-ship: separate identifier (e.g. `com.oetprep.desktop.beta`), Sentry tag `flavor: tauri` (bridge exposes `versions.flavor`).
- Cutover: final Electron "bridge" release downloads the Tauri installer (checksum-verified via `scripts/desktop-checksums.cjs` machinery), migrates userData, self-uninstalls; keep the Electron feed serving that bridge ≥6 months.

## Verified on Windows (2026-06-12)

| Item | Result |
|---|---|
| Sidecar boot + health + port fallback | PASS (renderer fell back 3000→3001 live) |
| Hard-kill orphan reaping (Job Object) | PASS |
| Bridge shape + all command round-trips from remote origin | PASS (runtime, secrets, cache, speaking-audio, notification, fileInfo) |
| Conformance vitest | 5/5 |
| Mic recording in WebView2 (spike, localhost origin) | PASS — 30s real-mic `audio/webm;codecs=opus`, decoded 29.94s/48kHz |
| Print dialog (`window.print()` → WebView2) | PASS — `edge://print/` preview target opened |
| Proxy health + SignalR negotiate through sidecar | PASS — `/api/health` 200; AI-assistant hub negotiate 403 (route resolved, auth-gated — transport OK) |
| Storage persistence (cookie/localStorage/IndexedDB across restart) | PASS |
| Updater wiring (plugin + check on boot) | PASS — checks feed on startup, parses/errors correctly; full download+install round-trip uses `updater-feed-server.mjs` against a signed release bundle |
| All of macOS | pending (hardware) |

## Updater round-trip test (spike c)

1. Build a signed release: `TAURI_SIGNING_PRIVATE_KEY=$(cat ~/.tauri/oet-updater-test.key) pnpm dlx @tauri-apps/cli@^2 build --config src-tauri/tauri.dist.conf.json` (produces `*-setup.exe` + `*-setup.exe.sig` + `createUpdaterArtifacts`).
2. Serve the feed: `node src-tauri/updater-feed-server.mjs <bundleDir> 8765` (advertises v0.1.1, signature from the real `.sig`).
3. Run the app with `OET_UPDATER_URL=http://127.0.0.1:8765/latest.json` → it checks, detects the newer version, downloads, and **verifies the minisign signature** before install. Pubkey is committed in `tauri.conf.json > plugins > updater > pubkey`; production swaps in the real key + an HTTPS endpoint next to `ELECTRON_UPDATES_URL`.
