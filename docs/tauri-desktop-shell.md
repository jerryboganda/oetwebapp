# Tauri Desktop Shell (`src-tauri/`)

The desktop app is a **Tauri 2** shell that is a **remote-only thin client**: it
loads the live web app (`https://app.oetwithdrhesham.co.uk`) over HTTPS. No
frontend source, .NET backend, or Node renderer is bundled — the installer ships
only the compiled Rust core plus a tiny splash/offline screen.

> Migrated from a prior Electron shell. The Electron pipeline has been fully
> removed; see the repo CHANGELOG / git history for the migration record.

## Architecture

```
oet-desktop (Rust core)
 ├─ WebviewWindow "main"
 │   ├─ loads bundled splash (src-tauri/splash/) → probes reachability
 │   └─ navigates to https://app.oetwithdrhesham.co.uk (the live web app)
 ├─ navigation guard  → HTTPS + trusted origin only; other links → system browser
 ├─ initialization_script → injects window.desktopBridge (inject/desktop-bridge.js)
 ├─ tray (Dashboard / Study Plan / Quit) · deep link (oet-prep://) · single-instance
 └─ updater (tauri-plugin-updater, minisign + GitHub latest.json)
```

- The remote URLs come from `src-tauri/desktop-runtime-config.json` (bundled as a
  resource), overridable by `OET_DESKTOP_WEB_URL` / `OET_DESKTOP_API_URL`.
- **Offline UX:** if the remote is unreachable at launch, the splash shows a
  "You're offline — Retry" screen and auto-retries on the OS `online` event.
- No sidecars, no local SQLite, no bundled Node/.NET → near-instant cold start
  and a small installer (Rust shell only).

## Bridge

`inject/desktop-bridge.js` (registered as `initialization_script`) implements
`window.desktopBridge` with the exact `types/desktop.d.ts` shape, so the frontend
consumers work unchanged. Verified by
`src-tauri/__tests__/desktop-bridge-conformance.test.ts`.

- Window-state changes are delivered as DOM `CustomEvent`s (no event-plugin
  capability needed by the remote page).
- Speaking-audio blobs cross IPC as base64 and are rehydrated to `ArrayBuffer`s.
- Secrets use the OS keyring (`keyring` crate: Credential Manager / Keychain).

## Security & capabilities (least privilege)

- **Origin lock:** `lib.rs::is_allowed_origin` permits only the bundled splash,
  the trusted HTTPS origin (and same-origin SPA routes), and — in dev builds —
  `localhost`. Everything else opens in the system browser.
- **CSP** for the bundled splash is set in `tauri.conf.json`
  (`app.security.csp`); `connect-src` allows only the app + API origins for the
  reachability probe.
- **DevTools** are disabled in release (the `devtools` Cargo feature is not on).
- **ACL:** the remote page is semi-trusted. `capabilities/app-remote.json` grants
  it only `core:default` + `allow-runtime-info`. The privileged commands (keyring
  secrets, offline cache, speaking-audio temp files, dropped-file probe,
  notifications, open-external) stay registered (`build.rs` declares them) but are
  **not** granted to the remote origin. `capabilities/dev-localhost.json` grants
  the same minimal set to the dev server.

## Dev & build

```bash
# Dev — loads the production URL by default. To target a local web build,
# run `pnpm dev` and set OET_DESKTOP_WEB_URL=http://localhost:3000 first.
pnpm run desktop:dev          # = node scripts/tauri-dev.cjs (cargo run)

# Production bundle — just `tauri build` (no backend/next/node staging).
pnpm run desktop:dist         # = node scripts/tauri-dist.cjs build  → NSIS + dmg
```

## Versions (pinned exact, verified June 2026)

`tauri 2.11.3` · `tauri-build 2.6.2` · `@tauri-apps/cli 2.11.3` · plugins:
`single-instance 2.4.2`, `deep-link 2.4.9`, `notification 2.3.3`, `opener 2.5.4`,
`dialog 2.7.1`, `updater 2.10.1`. The frontend uses the injected raw-JS bridge,
not `@tauri-apps/api`.

## Updater

Configured in `tauri.conf.json` (`plugins.updater`): minisign `pubkey` +
GitHub-releases `latest.json` endpoint. CI signs updater artifacts with
`TAURI_SIGNING_PRIVATE_KEY`. Round-trip test without installing:

```bash
OET_UPDATER_TEST=1 <run the built app>
# logs: "UPDATER-TEST: download+verify finished (signature valid)"
```

## CI

- `.github/workflows/tauri-ci.yml` — Rust gate (fmt, clippy `-D warnings`, test,
  build) on Windows + the bridge conformance test.
- `.github/workflows/tauri-desktop-release.yml` — Windows (NSIS) + macOS (dmg)
  build matrix, checksums, artifact upload, and (on tag) a GitHub Release with
  the updater `latest.json`. Installers are unsigned by default — see the README
  "Desktop signing" section to enable Authenticode / Apple notarization.
