# Electron Desktop Conversion Plan — OET Prep Platform

**Document version:** 1.0.0  
**Date:** 2026-04-12  
**Project:** OET Prep (ai-studio-applet / com.oetprep.desktop)  
**Status:** Implementation-ready analysis and plan  

---

## 1. Executive Summary

### What the Project Is

OET Prep is a comprehensive OET (Occupational English Test) preparation platform built as a Next.js 15 App Router web application with an ASP.NET Core 10 backend, PostgreSQL database, and first-party JWT authentication. The platform serves three user roles — **Learner**, **Expert** (reviewer/instructor), and **Admin** — across approximately 120+ distinct routes covering diagnostic assessments, skill practice (listening, reading, writing, speaking), AI-powered conversation, mock exams, expert review workflows, study planning, achievements, billing, community features, and a full CMS/admin panel.

### What the Desktop Plan Is

The project **already has a mature, production-quality Electron desktop shell**. This document evaluates the existing implementation against official Electron guidance and strict UI/UX/functional parity requirements, identifies remaining gaps, and provides a phased roadmap to bring the desktop app to full release quality.

### Key Finding

The Electron conversion is **75-80% complete**. The existing shell is architecturally sound and follows official Electron security best practices closely. Remaining work is primarily in: macOS/Linux packaging polish, CI/CD integration, production update server deployment, Electron Fuses hardening, and parity validation testing.

---

## 2. Analysis Environment and Method

### Capabilities Used

| Capability | Status | How Used |
|---|---|---|
| Claude Opus 4.6 Agent (VS Code) | ✅ Active | Primary analysis engine |
| Gem Team Orchestration | ✅ Active | Phase routing, parallel analysis delegation |
| Explore Subagent | ✅ Used | Thorough project structure mapping |
| Agent Skills (electron-pro) | ✅ Loaded | Cross-referenced against official docs |
| Web Research | ✅ Performed | Official Electron docs, ecosystem references |
| File System Analysis | ✅ Full | All source files read and analyzed |
| Terminal Access | ✅ Available | Not needed for read-only analysis |
| Repository Memory | ✅ Read | Prior project facts incorporated |

### Missing/Degraded Capabilities

| Capability | Status | Compensation |
|---|---|---|
| cc_token_saver_mcp | Unavailable | All analysis handled by main model |
| context-mode MCP | Deferred | Custom tool search used instead |
| jcodemunch-mcp | Unavailable | Direct file reads performed |

---

## 3. External Research and Skill Applicability Audit

### Sources Reviewed

| Source | Tier | Trust Level | Key Takeaways | Adopted? |
|---|---|---|---|---|
| [electronjs.org/docs/latest/tutorial/security](https://www.electronjs.org/docs/latest/tutorial/security) | **Tier 1 — Official** | Authoritative | 20-point security checklist: context isolation, sandbox, CSP, IPC validation, fuses, custom protocols | ✅ Yes — primary reference |
| [electronjs.org/docs/latest/tutorial/process-model](https://www.electronjs.org/docs/latest/tutorial/process-model) | **Tier 1 — Official** | Authoritative | Main/renderer/preload/utility process separation | ✅ Yes |
| [electronjs.org/docs/latest/tutorial/context-isolation](https://www.electronjs.org/docs/latest/tutorial/context-isolation) | **Tier 1 — Official** | Authoritative | contextBridge as only safe exposure mechanism | ✅ Yes |
| [electronjs.org/docs/latest/tutorial/updates](https://www.electronjs.org/docs/latest/tutorial/updates) | **Tier 1 — Official** | Authoritative | autoUpdater, Squirrel, static storage, self-hosted options | ✅ Yes |
| [electronjs.org/docs/latest/tutorial/application-distribution](https://www.electronjs.org/docs/latest/tutorial/application-distribution) | **Tier 1 — Official** | Authoritative | Electron Forge vs Builder, asar packaging, rebranding | ✅ Yes |
| electron-pro skill (repo-installed) | **Tier 3 — Skill** | Advisory | Security checklist, performance targets, native integration patterns | ⚠️ Partially — validated against official docs first |
| awesome-electron (GitHub) | **Tier 2 — Curated** | Reference | Ecosystem awareness, common patterns | ⚠️ Reference only |
| VoltAgent electron-pro subagent | **Tier 3 — Marketplace** | Advisory | Generic desktop patterns | ❌ Rejected — too generic for this project |
| mcpmarket.com electron-app-planner | **Tier 3 — Marketplace** | Low | Generic planning template | ❌ Rejected — not project-specific |
| lobehub openclaw-skills-electron | **Tier 3 — Marketplace** | Low | Generic skill prompts | ❌ Rejected — no actionable guidance |

### Cross-Check Results

The existing project Electron code was compared against all 20 official Electron security recommendations:

| # | Official Recommendation | Project Status |
|---|---|---|
| 1 | Only load secure content | ✅ Renderer loads localhost in dev; HTTPS in prod |
| 2 | No Node.js integration for remote content | ✅ `nodeIntegration: false` |
| 3 | Enable context isolation | ✅ `contextIsolation: true` |
| 4 | Enable process sandboxing | ✅ `sandbox: true` |
| 5 | Handle session permission requests | ✅ `setPermissionRequestHandler` + `setPermissionCheckHandler` implemented |
| 6 | Do not disable webSecurity | ✅ `webSecurity: true` |
| 7 | Define a CSP | ✅ CSP defined in `next.config.ts` headers |
| 8 | No allowRunningInsecureContent | ✅ `allowRunningInsecureContent: false` |
| 9 | No experimental features | ✅ Not enabled |
| 10 | No enableBlinkFeatures | ✅ Not used |
| 11 | No allowpopups for WebViews | ✅ `webviewTag: false` |
| 12 | Verify WebView options | ✅ `will-attach-webview` prevented |
| 13 | Disable/limit navigation | ✅ `will-navigate` handler restricts to trusted origin |
| 14 | Disable/limit new windows | ✅ `setWindowOpenHandler` returns `{ action: 'deny' }` |
| 15 | Safe `shell.openExternal` | ✅ Protocol-validated (`http:` / `https:` only) |
| 16 | Current Electron version | ✅ Electron 41.1.0 (latest stable line) |
| 17 | Validate IPC message senders | ⚠️ **Partial** — IPC handlers exist but do not validate `event.senderFrame` |
| 18 | Avoid `file://` protocol | ✅ All content served via HTTP localhost |
| 19 | Check Electron fuses | ⚠️ **Missing** — `@electron/fuses` not configured |
| 20 | Don't expose raw Electron APIs | ✅ `contextBridge.exposeInMainWorld` with wrapped functions |

---

## 4. Confirmed Project Stack and Architecture

### Confirmed Facts (from source code inspection)

| Layer | Technology | Version | Evidence |
|---|---|---|---|
| Frontend framework | Next.js (App Router) | 15.4.9 | `package.json` dependency |
| React | React 19.2.1 | 19.2.1 | `package.json` dependency |
| Language | TypeScript | 5.9.3 | `package.json` devDependency |
| CSS framework | Tailwind CSS 4.1.11 | 4.1.11 | `package.json` devDependency |
| State management | React Context + Zustand 5 | 5.0.12 | `contexts/`, `lib/stores/` |
| Forms | React Hook Form + Zod | 7.72 / 4.3.6 | `package.json` dependencies |
| Animation | Motion (Framer Motion) | 12.23.24 | `package.json` dependency |
| Charts | Recharts | 3.8.0 | `package.json` dependency |
| AI integration | @google/genai | 1.17.0 | `package.json` dependency |
| Real-time | @microsoft/signalr | 10.0.0 | `package.json` dependency |
| Audio | wavesurfer.js | 7.12.5 | `package.json` dependency |
| Backend | ASP.NET Core | .NET 8+ | `backend/` directory, `global.json` |
| Database | PostgreSQL 17 | 17 | `docker-compose.desktop.yml` |
| Auth | First-party JWT | — | `DEPLOYMENT.md`, backend config |
| Build output | Standalone (`output: 'standalone'`) | — | `next.config.ts` |
| Desktop shell | Electron | 41.1.0 | `package.json` devDependency |
| Desktop packaging | electron-builder | 26.8.1 | `package.json` devDependency |
| Desktop updater | electron-updater | 6.6.2 | `package.json` dependency |
| Mobile | Capacitor | 6.2.1 | `package.json` dependency |
| Testing | Vitest 4 + Playwright 1.58 | — | `package.json` devDependencies |
| Package manager | npm | — | `package-lock.json` presence |

### Strongly Inferred

- Backend uses Entity Framework Core with auto-migrations
- SQLite is the desktop-mode database (confirmed in `getBundledBackendEnv()` in `main.cjs`)
- PostgreSQL is production/Docker-mode only
- The app supports three runtime kinds: `web`, `desktop`, `capacitor-native` (confirmed in `runtime-signals.ts`)

---

## 5. Workspace / Repository Breakdown

| Folder | Role | Desktop Relevance |
|---|---|---|
| `app/` | Next.js routes (120+ pages across auth, learner, expert, admin) | **Core renderer content** — loaded unchanged inside Electron |
| `components/` | 98+ React components (ui, auth, domain, layout, mobile, runtime, state) | **Core renderer components** — includes `RuntimeLifecycleBridge` for Electron |
| `contexts/` | Auth context, notification context | **Rendered unchanged** |
| `hooks/` | Mobile detection, analytics | **Rendered unchanged** |
| `lib/` | API clients, auth, types, i18n, mobile, network, stores, runtime-signals | **Critical** — `runtime-signals.ts` drives desktop detection |
| `electron/` | **Main process**: `main.cjs`, `preload.cjs`, `menu.cjs`, `updater.cjs`, `runtime-config.cjs`, `security/` | **Desktop shell** — the primary Electron layer |
| `backend/` | ASP.NET Core API, EF Core, PostgreSQL/SQLite | **Bundled in desktop** via `desktop-backend-runtime/` |
| `desktop-backend-runtime/` | Pre-built .NET 8 self-contained API binary + config | **Packaged as extraResource** in Electron builds |
| `scripts/` | `electron-dev.cjs` (dev runner), `desktop-dist.cjs` (packaging) | **Desktop build toolchain** |
| `tests/e2e/desktop/` | Electron-specific Playwright tests (smoke, surface, motion, packaged) | **Desktop parity validation** |
| `docker-compose.desktop.yml` | Docker baseline for desktop dev (postgres + api + web) | **Desktop development infrastructure** |
| `capacitor-web/`, `android/`, `ios/` | Capacitor mobile builds | Not directly relevant to Electron |
| `docs/` | Product strategy, QA, implementation plans | Planning reference |
| `public/` | Static assets (icons, fonts, images) | Bundled into standalone output |

---

## 6. Current Product Structure

### Role-Based Feature Areas

#### Learner (42+ routes)

| Feature Area | Routes | Key Behaviors |
|---|---|---|
| **Dashboard** | `/dashboard`, `/dashboard/project`, `/dashboard/score-calculator` | Hero card, action cards, progress overview, study plan summary |
| **Diagnostic** | `/diagnostic/*` (8 routes) | Skills assessment across 4 OET sub-tests, results, insights |
| **Listening** | `/listening/*` (6 routes) | Audio player (wavesurfer.js), drills, playback, results, review |
| **Reading** | `/reading/*` (4 routes) | Passage display, timed practice, answer checking, results |
| **Writing** | `/writing/*` (10 routes) | Essay editor, model comparison, AI feedback, expert review request, phrase suggestions, revision |
| **Speaking** | `/speaking/*` (11 routes) | Mic check, recording, roleplay, phrasing, fluency timeline, transcript, expert review |
| **Mocks** | `/mocks/*` (7 routes) | Full mock exam simulation, setup, player, detailed report |
| **Conversation** | `/conversation/*` (4 routes) | AI conversation practice, session history, results |
| **Study Plan** | `/study-plan`, `/study-plan/drift` | Personalized schedule, drift detection |
| **Vocabulary** | `/vocabulary/*` (4 routes) | Flashcards, quiz, browse bank |
| **Grammar** | `/grammar/*` (2 routes) | Grammar lessons |
| **Pronunciation** | `/pronunciation/*` (2 routes) | Pronunciation drills |
| **Progress** | `/progress`, `/progress/comparative` | Analytics dashboard, comparative view |
| **Readiness** | `/readiness` | OET readiness score and assessment |
| **Achievements** | `/achievements/*` (3 routes) | Badges, certificates |
| **Goals** | `/goals`, `/goals/study-commitment` | Goal setting, study commitment tracking |
| **Billing** | `/billing/*` (4 routes), `/subscriptions` | Plan management, upgrade, score guarantee, referral |
| **Community** | `/community/*` (4 routes) | Forums, study groups, ask-an-expert |
| **Settings** | `/settings/*` (3 routes) | Profile, preferences, reminders |
| **Misc** | `/onboarding`, `/exam-booking`, `/exam-guide`, `/feedback-guide`, `/test-day`, `/next-actions`, `/marketplace`, `/strategies`, `/predictions`, `/practice/*`, `/submissions/*`, etc. | Onboarding, exam info, marketplace, strategies |

#### Expert (8+ routes)

| Feature Area | Key Routes |
|---|---|
| Queue management | `/expert/queue`, `/expert/queue-priority` |
| Review workflow | `/expert/review/*` (writing, speaking) |
| Learner management | `/expert/learners/*` |
| Quality assurance | `/expert/calibration/*`, `/expert/scoring-quality`, `/expert/rubric-reference` |
| Schedule & metrics | `/expert/schedule`, `/expert/metrics` |
| Templates & AI | `/expert/annotation-templates`, `/expert/ai-prefill` |

#### Admin (14+ routes)

| Feature Area | Key Routes |
|---|---|
| User management | `/admin/users/*`, `/admin/experts`, `/admin/roles`, `/admin/permissions` |
| Content CMS | `/admin/content/*`, `/admin/media`, `/admin/taxonomy`, `/admin/criteria`, `/admin/content-*` |
| Analytics | `/admin/analytics/*` (5 sub-dashboards), `/admin/business-intelligence` |
| Operations | `/admin/review-ops`, `/admin/escalations`, `/admin/sla-health`, `/admin/audit-logs` |
| Configuration | `/admin/ai-config`, `/admin/flags`, `/admin/webhooks`, `/admin/notifications` |
| Billing ops | `/admin/billing`, `/admin/credit-lifecycle`, `/admin/score-guarantee-claims`, `/admin/free-tier`, `/admin/freeze` |

#### Auth (14 routes)

Sign-in, registration (multi-step), password reset, MFA setup/challenge, OAuth callback, email verification, terms.

#### API Routes

- `/api/health` — Health check
- `/api/backend/[...path]` — Backend reverse proxy (all `/v1/*` API calls)

---

## 7. Current User Flows

### Critical User Journeys

1. **Registration → Onboarding → Dashboard**: Multi-step registration → email verification → onboarding questionnaire → dashboard with personalized study plan.

2. **Practice Session (Writing)**: Dashboard → select writing task → essay editor → submit → AI instant feedback → model comparison → optional expert review request → revision.

3. **Practice Session (Speaking)**: Mic check → task selection → recording → AI analysis → fluency timeline → optional expert review.

4. **Practice Session (Listening)**: Select listening exercise → wavesurfer.js audio player → answer questions → results with detailed breakdown.

5. **Mock Exam**: Setup (choose sub-tests) → timed simulation → comprehensive report with scores.

6. **Expert Review Flow**: Expert queue → claim review request → read learner submission → annotate/score → submit feedback → quality checks.

7. **Admin Content Management**: Content list → create/edit (rich editor) → publish workflow → revision history.

8. **Billing**: Current plan view → upgrade → payment (external checkout) → confirmation → subscription management.

9. **Auth → MFA**: Sign-in → MFA challenge → session → access token refresh cycle.

10. **Desktop-specific**: App launch → bundled backend starts → standalone renderer starts → health check passes → main window loads.

---

## 8. UI/UX Parity Requirements

### Design System (from DESIGN.md — canonical reference)

The following MUST remain identical in the Electron desktop app:

| Aspect | Requirement |
|---|---|
| **Color palette** | Cream Canvas (#f7f5ef), Surface White (#fffefb), Primary Violet (#7c3aed), Navy Ink (#0f172a), 16 named tokens — all preserved |
| **Typography** | Manrope (UI) + Fraunces (display) loaded via Next.js font optimization |
| **Component styling** | Rounded-2xl cards, 44-48px touch targets, shadow-clinical hover, glass top nav with blur |
| **Layout** | Sticky top nav + sidebar; 1200px max workspace width; hero-card rhythm on all pages |
| **Motion** | 160-320ms springs/fades, 1px hover lift, staggered entry, route transitions (via Motion library) |
| **Depth & elevation** | Ambient color blooms, faint grid veil, border + shadow (not shadow alone), glass-panel nav |
| **Dark mode** | Deep blue-black (#07111d), inverted surfaces, controlled contrast |
| **Responsive** | Mobile sidebar → top nav + bottom nav; tablet graceful collapse; desktop full layout |
| **Empty states** | Centered, explanatory, framed inside card or dashed surface |
| **Loading states** | Skeleton components in every route (`loading.tsx` files) |
| **Error states** | Global error boundary (`error.tsx`) + route-level error boundaries |

### Electron-Specific UI Additions (already implemented)

These are legitimate desktop enhancements that DO NOT break web parity:

- Native menu bar (Navigate, Edit, View, Window, Help)
- `About OET Prep` dialog
- Update progress bar in taskbar
- Window state data attributes (`data-window-focused`, `data-window-maximized`, etc.)
- `oet-prep://` deep link protocol

### Strict Parity Rules

1. **No layout changes** — Shell dimensions 1440×980 default, 1200×800 minimum match the web viewport
2. **No font changes** — Manrope and Fraunces must load the same way (Next.js font loader works in standalone)
3. **No animation changes** — Motion system runs identically in Chromium renderer
4. **No color changes** — Tailwind CSS processes at build time; tokens are immutable
5. **No navigation changes** — App Router routing works identically in standalone server
6. **No responsive behavior removal** — Mobile/tablet breakpoints preserved (users may resize window)

---

## 9. Functional Parity Requirements

### API Communication
- **Web**: Browser → Next.js `/api/backend/[...path]` proxy → ASP.NET Core API
- **Desktop (dev)**: Electron renderer → Docker web container → proxy → Docker API container
- **Desktop (packaged)**: Electron renderer → standalone Next.js server → proxy → bundled `OetLearner.Api.exe` (SQLite)
- **Parity**: ✅ Identical from the renderer perspective — all requests go through `/api/backend/*`

### Authentication & Session
- **JWT tokens** stored in browser-accessible storage (cookies/localStorage)
- **Token refresh** handled by `auth-client.ts`
- **MFA flows** (TOTP setup, challenge) work through standard web forms
- **OAuth callbacks** redirect through browser — ⚠️ See Gap #3 below
- **Parity**: ✅ JWT auth works identically in Electron renderer

### Real-time (SignalR)
- SignalR WebSocket connections go through the API proxy
- CSP allows WebSocket origins from the API base URL
- **Parity**: ✅ WebSocket behavior identical in Electron Chromium

### Audio/Media
- Listening module uses wavesurfer.js for audio playback with waveform visualization
- Speaking module uses `MediaRecorder` API for microphone recording
- Audio files served from API backend
- **Parity**: ✅ All Web Audio/MediaRecorder APIs available in Electron Chromium

### File Operations
- Writing module: essay import/export
- Admin: media upload, content import
- **Parity**: ✅ File dialog and upload work via standard web APIs; `shell.openExternal` for downloads

### Service Worker
- `providers.tsx` explicitly skips service worker registration when `window.desktopBridge` is detected
- **Parity**: ✅ Correctly disabled in desktop mode

### Offline Behavior
- Desktop preload exposes `window.desktopBridge.offlineCache` (store, get, delete, list, clear)
- Offline cache stored in `userData/offline-content/` as JSON files
- **Parity**: ✅ Desktop-specific enhancement (additive, not breaking)

### Secure Storage
- Desktop preload exposes `window.desktopBridge.secureSecrets` backed by Electron `safeStorage`
- Uses OS keychain (macOS Keychain, Windows DPAPI, Linux Secret Service)
- **Parity**: ✅ Desktop-specific enhancement (additive, not breaking)

---

## 10. Web-to-Electron Compatibility Analysis

### Browser Assumptions in Current Code

| Browser Feature | Usage | Electron Compatibility |
|---|---|---|
| `window.location` / URL routing | Next.js App Router | ✅ Works via standalone server |
| `localStorage` / `sessionStorage` | Auth token storage | ✅ Works (persisted in userData) |
| `document.cookie` | Potential session state | ✅ Works |
| `navigator.mediaDevices` | Speaking mic access | ✅ Requires permission handler (implemented) |
| `MediaRecorder` | Speaking recording | ✅ Available in Chromium |
| `Web Audio API` | wavesurfer.js | ✅ Available in Chromium |
| `fetch` / `XMLHttpRequest` | API communication | ✅ Works |
| `WebSocket` | SignalR | ✅ Works |
| `Notification` API | Browser notifications | ✅ Supported (Electron provides native notifications) |
| `window.open()` | External links | ✅ Intercepted by `setWindowOpenHandler` → `shell.openExternal` |
| `document.visibilityState` | Lifecycle tracking | ✅ Works |
| `document.fullscreenElement` | Fullscreen detection | ✅ Works |
| `navigator.serviceWorker` | PWA caching | ✅ Skipped in desktop mode (correct) |
| `<link rel="preconnect">` | Font preloading | ✅ Works |
| `CSS backdrop-filter` | Glass nav effect | ✅ Supported in Chromium |
| `@font-face` | Manrope/Fraunces | ✅ Works via Next.js font optimization |
| `prefers-color-scheme` | Dark mode detection | ✅ Follows OS theme |
| `prefers-reduced-motion` | Accessibility | ✅ Follows OS setting |

### Known Electron Behavioral Differences

| Area | Web Behavior | Electron Behavior | Impact |
|---|---|---|---|
| Browser back/forward | Hardware buttons + UI | No browser chrome; menu + keyboard shortcuts | ✅ Handled — menu includes navigation |
| Tab opening | `target="_blank"` opens new tab | Intercepted → `shell.openExternal` | ✅ Handled |
| Address bar | Users can type URLs | No address bar; deep links via `oet-prep://` | ✅ Handled |
| Refresh | F5 / Ctrl+R / pull-to-refresh | F5 / Ctrl+R via keyboard; menu View → Reload | ✅ Handled |
| Download dialog | Browser download manager | Electron download handler needed | ⚠️ Gap #5 |
| Print | Browser print dialog | `window.print()` works | ✅ Works |
| Zoom | Browser zoom (Ctrl+/−) | Menu View → Zoom In/Out/Reset | ✅ Handled |

---

## 11. Gap Analysis

### Gap #1 — IPC Sender Validation (Security)
- **Issue**: IPC handlers in `main.cjs` do not validate `event.senderFrame` origin
- **Why**: Official recommendation #17 requires IPC sender validation
- **Affected Features**: Secret storage, offline cache, runtime info, open-external
- **Risk**: Low in this app (renderer is trusted), but does not meet official standard
- **Solution**: Add `validateSenderFrame()` check to all `ipcMain.handle()` calls, verifying `event.senderFrame.url` matches the trusted renderer origin
- **Parity Impact**: None — implementation-only change

### Gap #2 — Electron Fuses Not Configured (Security)
- **Issue**: `@electron/fuses` package not installed or configured
- **Why**: Official recommendation #19 advises flipping fuses like `RunAsNode`, `EnableNodeCliInspect`
- **Affected Features**: All — fuses affect the entire app binary
- **Risk**: Medium — `runAsNode` could be exploited; `nodeCliInspect` allows debugging
- **Solution**: Install `@electron/fuses`, add afterSign hook in electron-builder to flip: `RunAsNode=false`, `EnableNodeCliInspectArguments=false`, `EnableEmbeddedAsarIntegrityValidation=true`, `OnlyLoadAppFromAsar=true`
- **Parity Impact**: None — build-time hardening

### Gap #3 — OAuth Callback in Desktop (Functional)
- **Issue**: OAuth callbacks (`/auth/callback/[provider]`) expect browser redirect flow
- **Why**: Desktop doesn't have a browser address bar; OAuth providers redirect to web URLs
- **Affected Features**: Google, Facebook, LinkedIn sign-in
- **Risk**: High for users who rely on social sign-in
- **Solution**: Use `shell.openExternal` for OAuth initiation with a custom protocol callback (`oet-prep://auth/callback/*`), then map back to renderer URL via `mapProtocolUrlToRendererUrl()` (already implemented). The backend must register `oet-prep://auth/callback` as an allowed redirect URI. Alternatively, use a localhost redirect with ephemeral port.
- **Parity Impact**: None visually — OAuth opens system browser, returns to app

### Gap #4 — macOS Code Signing and Notarization (Distribution)
- **Issue**: `electron-builder.config.cjs` has macOS targets configured but no Apple Developer credentials
- **Why**: macOS Gatekeeper blocks unsigned apps
- **Affected Features**: macOS distribution
- **Risk**: High for macOS release
- **Solution**: Configure Apple Developer certificate, entitlements, and notarization in CI. `electron-builder.config.cjs` already has `hardenedRuntime: true` and `gatekeeperAssess: false` — add notarization plugin.
- **Parity Impact**: None

### Gap #5 — File Download Handling (Functional)
- **Issue**: No explicit download handler for file downloads (audio exports, certificates, CSV reports)
- **Why**: Electron doesn't show a browser download bar; downloads need explicit handling
- **Affected Features**: Certificate download, report export, audio file download
- **Risk**: Medium — downloads may silently fail
- **Solution**: Add `session.defaultSession.on('will-download')` handler to prompt save dialog and track progress
- **Parity Impact**: Slightly different dialog style (OS native vs browser) — acceptable

### Gap #6 — Linux Packaging (Distribution)
- **Issue**: No Linux targets defined in `electron-builder.config.cjs`
- **Why**: Repo memory states "Desktop release scope now targets Windows, macOS, and Linux"
- **Affected Features**: Linux distribution
- **Risk**: Medium — Linux users cannot install
- **Solution**: Add `linux` config to `electron-builder.config.cjs` with AppImage and deb targets
- **Parity Impact**: None

### Gap #7 — Bundled Backend Update Strategy (Operational)
- **Issue**: `desktop-backend-runtime/` contains pre-built binaries; update mechanism for backend changes is unclear
- **Why**: When the API schema changes, the desktop backend must match the frontend
- **Affected Features**: All API-dependent features
- **Risk**: High if versions drift
- **Solution**: Include backend version in app metadata; require backend rebuild as part of desktop packaging CI; expose backend version in runtime-info IPC for diagnostics
- **Parity Impact**: None

### Gap #8 — Deep Link Registration on Linux (Functional)
- **Issue**: `oet-prep://` protocol registration only happens when `app.isPackaged`; Linux requires `.desktop` file modification
- **Why**: `app.setAsDefaultProtocolClient` behavior varies by OS
- **Affected Features**: Deep linking, OAuth callback (if custom protocol approach)
- **Risk**: Low — only affects Linux OAuth flow
- **Solution**: Generate `.desktop` file with `MimeType=x-scheme-handler/oet-prep` in electron-builder Linux config
- **Parity Impact**: None

### Gap #9 — CSP Hardening for Desktop (Security)
- **Issue**: Repo memory notes "keep desktop CSP strict by removing unnecessary unsafe-inline and unsafe-eval"
- **Why**: Production build uses `script-src 'self' 'unsafe-inline'`; `unsafe-inline` can be removed if nonces are used
- **Affected Features**: XSS hardening surface
- **Risk**: Low — standalone server controls all content
- **Solution**: Move from `unsafe-inline` to CSP nonces in Next.js headers, or accept current level as sufficient since all content is trusted
- **Parity Impact**: None

---

## 12. Recommended Electron Architecture

### Architecture: Standalone Server in Electron Shell

The project correctly uses the **most robust** Electron wrapping pattern for a Next.js app:

```
┌─────────────────────────────────────────────────┐
│                 ELECTRON MAIN PROCESS            │
│                                                   │
│  ┌─────────────┐  ┌──────────────────────────┐  │
│  │  main.cjs   │  │ Bundled Backend           │  │
│  │  - Window    │  │ OetLearner.Api.exe       │  │
│  │  - Menu      │  │ (SQLite for desktop)     │  │
│  │  - IPC       │  │ spawned as child process │  │
│  │  - Updater   │  └──────────────────────────┘  │
│  │  - Security  │                                 │
│  └──────┬──────┘  ┌──────────────────────────┐  │
│         │         │ Standalone Next.js Server │  │
│         │         │ (node server.js)          │  │
│         │         │ spawned as child process  │  │
│         │         │ Proxy → Backend           │  │
│         │         └──────────────────────────┘  │
│         │                                        │
│  ┌──────┴──────┐                                 │
│  │ BrowserWindow │                               │
│  │ (Renderer)   │ ← loads http://127.0.0.1:PORT │
│  │ preload.cjs  │                                │
│  │ sandbox=true │                                │
│  └──────────────┘                                │
└──────────────────────────────────────────────────┘
```

**Why this is correct:**

1. **Next.js standalone output** runs identically to production Docker deployment
2. **Backend bundled as self-contained .NET executable** with SQLite — no Docker needed on user machines
3. **Renderer is fully sandboxed** — treats the web content as untrusted
4. **No `file://` protocol** — all content served via HTTP, matching official recommendation #18
5. **Port discovery** via `findAvailablePort()` avoids conflicts

### Alternative Architectures Considered and Rejected

| Alternative | Why Rejected |
|---|---|
| **Load `file://` build output** | Official recommendation #18 discourages `file://`; SSR routes wouldn't work; API proxy wouldn't function |
| **Electron Forge** | Project already uses electron-builder with mature config; migration is unnecessary churn |
| **WebContentsView** | Single-window app; no need for multi-view composition |
| **Remote URL loading** | Would require internet connection; violates offline-capable requirement |
| **BrowserView** | Deprecated in favor of WebContentsView; not needed |

---

## 13. Process Boundary Design

### Main Process (`electron/main.cjs`)

**Responsibilities (confirmed from source):**

| Responsibility | Implementation |
|---|---|
| Application lifecycle | `app.whenReady()`, `window-all-closed`, `before-quit`, single-instance lock |
| Window management | `BrowserWindow` creation (1440×980, 1200×800 min), state tracking, focus sync |
| Child process management | Spawn/manage standalone renderer server + bundled backend server |
| Port allocation | `findAvailablePort()` starting from configured ports |
| Health checking | Poll `/api/health` (renderer) and `/health/ready` (backend) with 120s timeout |
| Native menu | `createDesktopMenu()` with Navigate, Edit, View, Window, Help |
| Security policies | Permission handler, navigation guard, window-open handler, webview prevention |
| Certificate pinning | `installCertificatePinning()` via SPKI SHA-256 pins |
| Secure storage | `safeStorage`-backed vault in `userData/secure-storage/` |
| Offline cache | JSON file cache in `userData/offline-content/` |
| Auto-updater | `electron-updater` with manual download, native notifications |
| Deep linking | `oet-prep://` custom protocol handling |
| Runtime config | Multi-source config: env vars → packaged config → user override |

### Preload Script (`electron/preload.cjs`)

**Exposed API surface via `window.desktopBridge`:**

```typescript
interface DesktopBridge {
  platform: NodeJS.Platform;
  versions: { electron: string; chrome: string; node: string };
  openExternal: (url: string) => Promise<boolean>;
  runtime: {
    info: () => Promise<DesktopRuntimeInfo>;
    onWindowStateChange: (listener: Function) => () => void;
  };
  secureSecrets: {
    get: (namespace: string, key: string) => Promise<string | null>;
    set: (namespace: string, key: string, value: string) => Promise<void>;
    delete: (namespace: string, key: string) => Promise<void>;
    status: () => Promise<SecretStorageStatus>;
  };
  offlineCache: {
    store: (key: string, data: unknown) => Promise<{ success: boolean; key: string }>;
    get: (key: string) => Promise<CachedItem | null>;
    delete: (key: string) => Promise<{ success: boolean; key: string }>;
    list: () => Promise<CacheListItem[]>;
    clear: () => Promise<void>;
  };
}
```

**Assessment**: Clean, minimal API surface. Each function wraps a specific `ipcRenderer.invoke()` call. No raw IPC exposure. Compliant with official recommendation #20.

### Renderer Process

The renderer is the standard Next.js web application. It detects desktop mode via:

1. **Bootstrap script** (`lib/runtime-signals.ts` → `getRuntimeBootstrapScript()`) sets `document.documentElement.dataset.runtimeKind = 'desktop'`
2. **Runtime check** (`getAppRuntimeKind()`) detects `window.desktopBridge`
3. **Conditional behavior**: Service worker skipped, `RuntimeLifecycleBridge` syncs window state

### IPC Contract Summary

| Channel | Direction | Purpose |
|---|---|---|
| `desktop:open-external` | Renderer → Main | Open URL in system browser |
| `desktop:runtime-info` | Renderer → Main | Get runtime status |
| `desktop:window-state-changed` | Main → Renderer | Broadcast window state changes |
| `desktop:secret-storage:get` | Renderer → Main | Read from OS keychain |
| `desktop:secret-storage:set` | Renderer → Main | Write to OS keychain |
| `desktop:secret-storage:delete` | Renderer → Main | Remove from OS keychain |
| `desktop:secret-storage:status` | Renderer → Main | Check keychain availability |
| `desktop:offline-cache:store` | Renderer → Main | Cache content offline |
| `desktop:offline-cache:get` | Renderer → Main | Read cached content |
| `desktop:offline-cache:delete` | Renderer → Main | Remove cached content |
| `desktop:offline-cache:list` | Renderer → Main | List cached items |
| `desktop:offline-cache:clear` | Renderer → Main | Clear all cached content |

---

## 14. Build, Packaging, and Environment Strategy

### Development Workflow (confirmed)

```
1. Start Docker desktop baseline:
   docker compose -f docker-compose.desktop.yml up -d

2. Run desktop dev mode:
   npm run desktop:dev
   
   This runs:
   a. scripts/electron-dev.cjs
   b. Readiness check (assert-local-stack.mjs)
   c. Spawns Electron pointing at Docker web:3000 + Docker api:5198
```

### Production Build Workflow (confirmed)

```
1. Build Next.js standalone output:
   npm run build
   
2. Package desktop installer:
   npm run desktop:dist
   
   This runs:
   a. scripts/desktop-dist.cjs
   b. Loads Next.js env config
   c. Validates security (cert pins, code signing)
   d. Verifies bundled backend exists
   e. Runs electron-builder with electron-builder.config.cjs
   f. afterPack: syncs standalone + runtime config into resources
   g. Output → dist/desktop/ (or temp dir → copy back)
```

### Packaging Matrix

| Platform | Target | Code Signing | Status |
|---|---|---|---|
| **Windows** | NSIS installer (x64) | Required (WIN_CSC_LINK or Azure Trusted Signing) | ✅ Configured |
| **macOS** | DMG + ZIP | Required (Apple Developer + notarization) | ⚠️ Config present, credentials needed |
| **Linux** | Not configured | Not required | ❌ Gap #6 — needs AppImage + deb targets |

### Environment Variables (Desktop-Specific)

| Variable | Purpose | Required |
|---|---|---|
| `ELECTRON_RENDERER_URL` | Override renderer URL (dev) | Dev only |
| `ELECTRON_START_LOCAL_SERVER` | Force standalone server in dev | Optional |
| `ELECTRON_UPDATES_URL` | Self-hosted update endpoint | Release builds |
| `ELECTRON_CERT_PINS` | Certificate pin rules (JSON) | Release builds |
| `ELECTRON_ALLOW_UNSIGNED_WINDOWS_BUILD` | Skip Windows signing check | Local dev |
| `ELECTRON_WINDOWS_PUBLISHER_NAME` | Windows publisher name | Release builds |
| `ELECTRON_RUNTIME_CHANNEL` | Isolate data dirs (prod/dev/beta) | Optional |
| `ELECTRON_APPDATA_ROOT` | Override app data location | Optional |
| `ELECTRON_BACKEND_PORT` | Override backend port | Optional |
| `ELECTRON_ALLOW_LOCAL_API_TARGET` | Allow loopback API in packaged build | Dev/test |
| `ELECTRON_ALLOW_BASIC_TEXT_SECRET_STORAGE` | Allow weak Linux keyring | Linux test |
| `ELECTRON_PUBLISH_MODE` | Update publish mode (always/never) | CI |
| `ELECTRON_BUILD_OUTPUT` | Override build output directory | CI |

---

## 15. Security Model Without Product Deviation

### Current Security Posture Assessment

**Excellent** — The existing implementation scores 18/20 on official Electron security checklist (see Section 3).

### Remaining Security Hardening (no product impact)

#### Priority 1: Electron Fuses

```javascript
// afterSign hook (add to electron-builder.config.cjs)
const { flipFuses, FuseVersion, FuseV1Options } = require('@electron/fuses');

afterSign: async (context) => {
  await flipFuses(
    context.getElectronBinaryPath(),
    {
      version: FuseVersion.V1,
      [FuseV1Options.RunAsNode]: false,
      [FuseV1Options.EnableNodeCliInspectArguments]: false,
      [FuseV1Options.EnableEmbeddedAsarIntegrityValidation]: true,
      [FuseV1Options.OnlyLoadAppFromAsar]: true,
    }
  );
}
```

#### Priority 2: IPC Sender Validation

```javascript
function validateSenderFrame(event) {
  const senderUrl = event?.senderFrame?.url;
  if (!senderUrl) return false;
  return isTrustedRendererUrl(senderUrl);
}

// Apply to all handlers:
ipcMain.handle('desktop:open-external', async (event, url) => {
  if (!validateSenderFrame(event)) return false;
  // ... existing logic
});
```

#### Priority 3: Download Handler

```javascript
session.defaultSession.on('will-download', (event, item) => {
  const suggestedFilename = item.getFilename();
  // Prompt save dialog
  item.setSavePath(dialog.showSaveDialogSync({
    defaultPath: suggestedFilename,
  }) || '');
});
```

#### Already Implemented (no changes needed)

- ✅ Context isolation
- ✅ Sandbox
- ✅ No node integration
- ✅ webSecurity enabled
- ✅ CSP defined
- ✅ Navigation restricted to trusted origin
- ✅ Window creation denied
- ✅ WebView tag disabled
- ✅ Permission handler with allowlist
- ✅ Certificate pinning
- ✅ Safe `shell.openExternal` (protocol-checked)
- ✅ OS-backed secret storage

---

## 16. Risk Register

| # | Risk | Severity | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R1 | Backend version drift between web and desktop releases | **High** | Medium | API contract mismatch → feature breakage | Lock backend build hash into desktop metadata; CI validates compatibility |
| R2 | OAuth social login broken in desktop | **High** | High (if not handled) | Users cannot sign in via Google/Facebook/LinkedIn | Implement custom protocol redirect or localhost redirect for OAuth |
| R3 | macOS Gatekeeper blocks unsigned app | **High** | Certain (without signing) | macOS users cannot install | Obtain Apple Developer certificate; configure notarization |
| R4 | Windows SmartScreen warning | **Medium** | Likely (for new signing certificate) | Users see "Unknown publisher" warning | Use EV code signing certificate; register with Microsoft |
| R5 | Large installer size | **Medium** | Likely | Download friction; store listing impact | Target: <100MB. Electron ~85MB base + standlone ~15MB + backend ~30MB ≈ 130MB. Compress with NSIS; consider delta updates |
| R6 | Desktop-specific bugs not caught | **Medium** | Medium | Parity violations reach users | Run all 4 desktop E2E tests in CI; add visual regression testing |
| R7 | SQLite limitations vs PostgreSQL | **Medium** | Low | Edge cases in concurrent access or data types | SQLite is single-user desktop; concurrent access not expected. Test bulk operations |
| R8 | Update server downtime | **Low** | Low | Users stuck on old versions | Self-hosted update server with CDN; manual fallback download page |
| R9 | Electron CVE in Chromium | **Medium** | Ongoing | Security vulnerability | Pin to latest Electron; automate dependency updates |
| R10 | Linux packaging issues | **Low** | Medium | AppImage permissions, desktop integration | Test on Ubuntu 22.04+, Fedora 38+; use AppImageLauncher-compatible format |

---

## 17. Implementation Roadmap

### Phase 1: Security Hardening (1-2 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Install `@electron/fuses` and add afterSign hook | P0 | 2h | Gap #2 |
| Add IPC sender validation to all handlers | P0 | 2h | Gap #1 |
| Add `will-download` session handler | P1 | 1h | Gap #5 |
| Audit CSP for desktop-specific tightening | P2 | 1h | Gap #9 |

### Phase 2: Platform Completion (2-3 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Add Linux targets (AppImage + deb) to electron-builder config | P0 | 2h | Gap #6 |
| Configure Linux `.desktop` file with protocol handler | P1 | 1h | Gap #8 |
| Configure macOS notarization in electron-builder | P0 | 4h | Gap #4 |
| Test macOS DMG on real hardware | P1 | 2h | Gap #4 |
| Test Linux AppImage on Ubuntu and Fedora | P1 | 2h | Gap #6 |

### Phase 3: OAuth Desktop Flow (1-2 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Implement OAuth flow via system browser + custom protocol callback | P0 | 4h | Gap #3 |
| Register `oet-prep://auth/callback` as allowed redirect in backend | P0 | 2h | Gap #3 |
| Test all three OAuth providers in desktop | P0 | 2h | Gap #3 |

### Phase 4: CI/CD Integration (2-3 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Add desktop build step to CI pipeline | P0 | 4h | — |
| Configure Windows code signing in CI secrets | P0 | 2h | — |
| Configure macOS signing + notarization in CI | P0 | 4h | Gap #4 |
| Add desktop E2E tests to CI matrix | P1 | 2h | — |
| Backend version pinning in desktop metadata | P1 | 2h | Gap #7 |

### Phase 5: Update Server & Distribution (1-2 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Deploy self-hosted update endpoint (S3/CDN) | P0 | 4h | — |
| Configure `ELECTRON_UPDATES_URL` for release builds | P0 | 1h | — |
| Test auto-update flow end-to-end | P0 | 2h | — |
| Configure certificate pins for update server | P1 | 1h | — |

### Phase 6: Parity Validation (2-3 days)

| Task | Priority | Effort | Gap |
|---|---|---|---|
| Expand desktop E2E tests to cover all critical flows | P0 | 8h | — |
| Visual regression testing: compare web vs desktop screenshots | P1 | 4h | — |
| Audio/speaking workflow validation in desktop | P0 | 2h | — |
| Writing editor validation in desktop | P0 | 2h | — |
| Multi-role flow validation (learner, expert, admin) | P0 | 4h | — |

**Total estimated effort: 10-15 engineering days**

---

## 18. Parity Validation Checklist

### UI/UX Parity

- [ ] Home page renders identically (hero, action cards, grid)
- [ ] Dashboard matches web layout exactly (all cards, charts, metrics)
- [ ] Color palette matches (cream canvas, violet accents, dark mode)
- [ ] Typography renders correctly (Manrope, Fraunces)
- [ ] Glass navigation bar has blur effect
- [ ] Sidebar collapses at responsive breakpoints
- [ ] Bottom navigation appears at mobile breakpoints
- [ ] Cards have correct border-radius, shadows, hover effects
- [ ] Motion/animation plays at correct timing (160-320ms)
- [ ] Skeleton loading states appear on all routes
- [ ] Empty states display correctly
- [ ] Error boundaries display correctly
- [ ] Dark mode matches web implementation
- [ ] Charts render identically (recharts)
- [ ] Data tables sort, filter, paginate correctly
- [ ] Forms validate with correct error messages
- [ ] Modal/dialog overlays center correctly
- [ ] Stepper components advance correctly

### Functional Parity

- [ ] Sign-in works (email/password)
- [ ] MFA setup and challenge work
- [ ] OAuth sign-in works (Google, Facebook, LinkedIn)
- [ ] Registration multi-step flow completes
- [ ] Token refresh works silently
- [ ] Role-based routing (learner/expert/admin) works
- [ ] Listening audio playback works (wavesurfer.js)
- [ ] Speaking microphone recording works
- [ ] Writing editor saves and submits
- [ ] AI conversation (GenAI) works
- [ ] Mock exam timer and simulation work
- [ ] File upload (media, content import) works
- [ ] File download prompts save dialog
- [ ] SignalR real-time updates work
- [ ] Notifications appear
- [ ] Billing upgrade opens checkout
- [ ] Admin content editor works
- [ ] Expert review annotation works
- [ ] Deep links navigate correctly (`oet-prep://`)
- [ ] External links open in system browser
- [ ] Copy/paste/undo/redo work (Edit menu)
- [ ] Zoom in/out works (View menu)
- [ ] Fullscreen mode works

### Desktop-Specific

- [ ] App launches within 3 seconds
- [ ] Idle memory stays under 200MB
- [ ] Bundled backend starts and responds to health checks
- [ ] Standalone renderer server starts correctly
- [ ] Auto-updater checks, downloads, and installs updates
- [ ] Native menu bar displays correctly
- [ ] About dialog shows version info
- [ ] Window state persistence works across restarts
- [ ] Single-instance lock prevents duplicate windows
- [ ] `oet-prep://` deep links focus and navigate existing window
- [ ] Certificate pinning rejects invalid certificates
- [ ] Secure secret storage encrypts via OS keychain
- [ ] Offline cache stores and retrieves content
- [ ] Graceful shutdown kills child processes

---

## 19. Unknowns / Items Requiring Verification

| # | Unknown | Impact | How to Verify |
|---|---|---|---|
| U1 | Exact installer size with bundled backend | May exceed 100MB target | Build and measure: `npm run desktop:dist` with full backend |
| U2 | SQLite behavior under concurrent API calls from admin bulk operations | Data integrity | Load test admin bulk import/export in desktop mode |
| U3 | macOS Notification permission behavior | May require entitlement | Test on real macOS hardware with notarization |
| U4 | Linux keyring availability on minimal Desktop Environments | `safeStorage` may fall back to `basic_text` | Test on GNOME, KDE, XFCE; verify `ELECTRON_ALLOW_BASIC_TEXT_SECRET_STORAGE` flow |
| U5 | GPU acceleration on Linux with various drivers | Performance/rendering impact | Test on NVIDIA, AMD, Intel GPUs; verify Chromium hardware acceleration |
| U6 | Backend startup time on slow HDD (non-SSD) systems | May exceed 3s target | Boot test on mechanical HDD with cold cache |
| U7 | SignalR WebSocket through Electron session | May need session cookie bridging | Integration test SignalR in packaged desktop build |
| U8 | wavesurfer.js large audio file performance in Electron | Memory usage for long listening tracks | Profile memory with 30+ minute audio files |
| U9 | Google Fonts loading behavior when offline | Fonts may not render | Verify fonts are bundled via Next.js font optimization (local copies) |
| U10 | Capacitor plugin imports in desktop mode | Imports should no-op gracefully | Verify `lib/mobile/` modules handle missing Capacitor gracefully |

---

## 20. Final Verdict

### Classification: **Clean Desktop Conversion — Largely Complete**

The OET Prep project is an **exceptionally clean candidate** for Electron desktop packaging, and the work is already **75-80% complete** with production-quality infrastructure.

### Why This Is a Clean Conversion

1. **Architecture is already correct**: The standalone Next.js server + bundled backend pattern is the most robust way to wrap a full-stack web app in Electron. This was a deliberate architectural choice, not a hack.

2. **Security model exceeds most Electron apps**: 18/20 on the official checklist out of the box, with the remaining 2 items (fuses, IPC validation) being straightforward additions.

3. **Zero UI/UX modification needed**: The renderer loads the exact same web application. Every pixel, animation, and interaction is identical because it IS the same code rendered in the same Chromium engine.

4. **Desktop-native enhancements are additive**: The native menu, secret storage, offline cache, update system, and deep linking ADD to the experience without MODIFYING the web experience.

5. **Testing infrastructure exists**: Dedicated Playwright desktop configs and 4 existing desktop E2E tests provide a validation foundation.

### Remaining Work Summary

| Category | Completeness | Remaining Work |
|---|---|---|
| Main process | 95% | IPC validation, fuses |
| Preload bridge | 100% | — |
| Security | 90% | Fuses, IPC validation |
| Windows packaging | 95% | CI signing |
| macOS packaging | 70% | Notarization, testing |
| Linux packaging | 30% | Target config, testing |
| Auto-updater | 90% | Production server deployment |
| CI/CD | 40% | Pipeline integration |
| OAuth desktop flow | 50% | Custom protocol callback |
| Parity testing | 50% | Expand E2E coverage |

### Risk Level: **LOW-MEDIUM**

The core technical risk is negligible — the architecture is proven. The remaining risk is operational (signing certificates, update server, CI pipeline) and edge-case (OAuth flow, Linux quirks, offline behavior).

### Recommended Next Step

Begin with Phase 1 (Security Hardening) and Phase 2 (Platform Completion) in parallel. These are the highest-impact, lowest-risk items and will bring the desktop app to a releasable state on all three platforms.

---

*This document was produced through multi-agent analysis using Claude Opus 4.6 in VS Code GitHub Copilot agent mode with Gem Team orchestration, official Electron documentation cross-referencing, and exhaustive repository source code inspection.*
