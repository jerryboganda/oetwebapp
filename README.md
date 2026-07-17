# OET with Dr Hesham Platform

OET preparation platform with a Next.js 16 web app, ASP.NET Core 10 API, a Tauri 2 desktop shell (remote-only thin client), and a Capacitor 7 mobile shell.

## Stack

- Frontend: Next.js App Router, React 19, TypeScript 5.9, Tailwind CSS 4, motion v12
- Backend: ASP.NET Core 10, EF Core, PostgreSQL 17, SignalR
- Desktop: Tauri 2 (Rust core) — a remote-only thin client (see "Desktop app" below)
- Mobile: Capacitor 7 for iOS and Android

### Rust toolchain (desktop)

The desktop shell needs a stable Rust toolchain (`rustup`/`cargo`). On Windows the
Tauri build also needs the WebView2 runtime (preinstalled on Windows 11) and the
MSVC build tools; on macOS, Xcode command-line tools. See <https://v2.tauri.app/start/prerequisites/>.

## Local Baseline

- Frontend URL: `http://localhost:3000`
- Backend URL: `http://localhost:5198`
- .NET SDK: `10.0.300`

## Quick Start

```bash
pnpm install
pnpm run dev
pnpm run backend:run
```

Optional workflow commands:

```bash
pnpm run backend:watch
pnpm run desktop:dev
pnpm run mobile:sync
pnpm run mobile:run:android
pnpm run mobile:run:ios
```

## Desktop app (Tauri 2 — remote-only)

The desktop app is a **Tauri 2** shell that is a **remote-only thin client**: it
loads the live web app (`https://app.oetwithdrhesham.co.uk`) over HTTPS rather
than bundling the frontend. The Rust core is compiled; the bundled installer
contains only the Rust shell plus a tiny splash/offline screen.

```bash
pnpm run desktop:dev     # cargo run the shell (loads the production URL by default)
pnpm run desktop:dist    # tauri build → NSIS (Windows) + dmg (macOS)
```

To develop against a local web build, run `pnpm dev` (http://localhost:3000) and
set `OET_DESKTOP_WEB_URL=http://localhost:3000` before `desktop:dev`. The remote
URLs are configured in `src-tauri/desktop-runtime-config.json` (overridable by
the `OET_DESKTOP_WEB_URL` / `OET_DESKTOP_API_URL` env vars).

### What remote-only means (read this)

- **Frontend source is not in the installer.** The HTML/JS/CSS is served online,
  not shipped on disk, and the Rust core is compiled.
- **It is NOT a way to hide client code.** Any client can still read the served
  JS via the network or browser devtools. **Any logic that must stay secret has
  to run server-side behind the authenticated API** — never ship a secret in the
  page bundle and assume the desktop hides it.
- **The app requires an internet connection.** There is no bundled offline mode.
  If the network is down at launch (or the server is unreachable), the shell
  shows a clear "You're offline — Retry" screen instead of a blank window, and
  auto-retries when the OS reports the network is back.

### Security posture

- **HTTPS-only, origin-locked.** A navigation guard pins the window to the trusted
  origin; any other link opens in the system browser. A strict CSP governs the
  bundled splash.
- **DevTools are disabled** in release builds (Tauri default; the `devtools`
  Cargo feature is not enabled).
- **Least-privilege ACL.** The remote page is treated as semi-trusted. It is
  granted only the non-privileged `runtime_info` command (see
  `src-tauri/capabilities/app-remote.json`). The privileged commands (OS keyring
  secrets, offline cache, speaking-audio temp files, dropped-file probing,
  notifications, open-external) stay implemented but are **not** granted to the
  remote origin.

### Desktop signing

Installers are currently **unsigned** (no Authenticode / Apple Developer ID).

- **Updater artifacts** are still signed with a minisign key — set
  `TAURI_SIGNING_PRIVATE_KEY` / `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` in CI; the
  matching public key lives in `src-tauri/tauri.conf.json`.
- **Windows Authenticode:** add a `bundle.windows.certificateThumbprint` (or a
  `signCommand`) in `src-tauri/tauri.conf.json` and provide the cert via the
  `WINDOWS_CERTIFICATE` CI secret (the release workflow imports it).
- **macOS notarization:** add the `APPLE_*` secrets and map them in
  `.github/workflows/tauri-desktop-release.yml`. Until then, the macOS `.dmg` is
  unsigned (open it with right-click → Open the first time).

If PowerShell blocks scripts, run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

## Verification

```bash
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
pnpm run build
pnpm run backend:build
pnpm run backend:test
```

E2E coverage:

```bash
pnpm run test:e2e:install
pnpm run test:e2e:auth
pnpm run test:e2e
pnpm run test:e2e:smoke
pnpm run test:e2e:report
```

## Local API Truth

- The backend development host is `http://localhost:5198`
- `backend/src/OetWithDrHesham.Api/Properties/launchSettings.json` and `backend/src/OetWithDrHesham.Api/appsettings.Development.json` both agree on port `5198`
- `NEXT_PUBLIC_API_BASE_URL` should point to `http://localhost:5198` for direct local development

## Key Docs

- [AGENTS.md](./AGENTS.md) — always-on repo contract and source of truth
- [CLAUDE.md](./CLAUDE.md) — Claude Code copy of the repo contract
- [Copilot Agentic Setup](docs/copilot-agentic-setup.md) — workspace AI setup
- [Scoring](docs/SCORING.md)
- [Rulebooks](docs/RULEBOOKS.md)
- [AI Usage Policy](docs/AI-USAGE-POLICY.md)
- [Content Upload Plan](docs/CONTENT-UPLOAD-PLAN.md)
- [Result Card Spec](docs/OET-RESULT-CARD-SPEC.md)

## Working Model

- Use `AGENTS.md` as the first stop for any agentic work.
- Keep unrelated edits intact.
- Prefer isolated worktrees for multi-file changes.
- Prefer lightweight, targeted host checks; avoid heavy CI/build marathons unless explicitly requested.
- Keep shared-contract surfaces tight: scoring, rulebooks, auth, backend bootstrap, and the statement-of-results card.
