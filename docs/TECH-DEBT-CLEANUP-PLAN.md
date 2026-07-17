# Technical Debt Audit & Prioritized Cleanup Plan

_Generated: April 2026 — scoped to the OET with Dr Hesham monorepo (`app/`, `backend/`, `electron/`, mobile shells)._

## Execution Status

| Wave | Scope | Status |
| --- | --- |
| **0 — Housekeeping** | stray files, AGENTS.md dedupe, package rename, Dockerfile docs, test-count refresh | ✅ **Executed** |
| **1a — Backend alignment** | `Microsoft.Extensions.Http.Resilience` 9 → 10 | ✅ **Executed** |
| **1b — Sentry 8 → 10** | FE + BE SDK bump | ✅ **Executed** (`@sentry/nextjs` at ^10.50.0) |
| **1c — Secure-storage plugin swap** | `capacitor-secure-storage-plugin` → `@aparajita/capacitor-secure-storage` | ✅ **Executed** (`@aparajita/capacitor-secure-storage` ^6.0.0) |
| **2 — Capacitor 6 → 7** | `@capacitor/*` + `cap sync` regen | ✅ **Executed** (all `@capacitor/*` at ^7.0.0) |
| **3 — Next 15 → 16** | codemod, config, PPR opt-in | ✅ **Executed** (`next` ^16.2.6, Turbopack for dev **and** prod builds; webpack removed) |
| **4 — React 19 effect cleanup** | last `exhaustive-deps` disable → `useEffectEvent` | ✅ **Executed** (commit `d6f5b75`, April 2026) |
| **5a — Dev-dep refresh** | `@types/node` 20 → 22 | ✅ **Executed** |
| **5b — Infra consolidation** | compose docs, `react-select` (already at latest 5.10.2 — no v6 exists), remaining minor deps | ✅ **Executed** (DEPLOYMENT.md compose matrix documented) |

> **Notes:** `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.5 was initially planned in Wave 1 but **no stable 10.0.5 exists** on NuGet (only 10.0.1 stable, then 11.0.0-preview). Kept at `10.0.1`. Re-evaluate when 10.0.x patches ship or plan a 11.0 major bump alongside the Sentry upgrade. `jsdom@30` similarly does not exist yet (latest is in the 29.x line) — kept at current.

### Verification for Executed Changes

- `npx tsc --noEmit` → **EXIT 0** ✅
- `npm run lint` → **EXIT 0** ✅
- `npm test` → **112/112 files, 664/664 tests passed, EXIT 0** ✅
- `dotnet restore backend/OetWithDrHesham.sln` → **EXIT 0** ✅
- `dotnet build backend/OetWithDrHesham.sln` → **EXIT 0, 0 errors, 4 pre-existing nullability warnings** ✅
- `dotnet test backend/OetWithDrHesham.sln` → **585/585 passed, 0 failed, EXIT 0** ✅
- `node ./scripts/check-mojibake.mjs` → **no mojibake** ✅

> The plan summary previously cited "77 unit test files / 304 tests" (from `AGENTS.md`); the repo has grown to **112 files / 664 tests** plus **585 backend tests**. `AGENTS.md` updated in this session to match.

E2E matrix and `npm run build` were not exercised in this session.

---

## Executive Summary

This codebase is in **healthy shape overall**: strict TypeScript, zero meaningful `TODO/FIXME/HACK` markers in product code, near-zero `any` / `@ts-ignore` usage, comprehensive test coverage (304 unit tests, 13 E2E projects), and well-documented mission-critical invariants. The majority of technical debt is **dependency drift** (several majors behind), a handful of **lifecycle/effect smells** (6 `react-hooks/exhaustive-deps` disables), **build/infra sprawl** (5 docker-compose files, two Dockerfiles, stray root artifacts), and **opportunities to adopt newer platform features** (Next.js 16 Cache Components, Capacitor 7, Sentry v10).

There is **no emergency debt**. The plan below is ordered by risk × leverage, not by effort.

---

## 1. Findings by Category

### 1.1 Outdated Dependencies (highest signal)

| Package | Current | Latest (Apr 2026) | Severity | Notes |
|---|---|---|---|---|
| `next` | `^16.2.6` | 16.x | **OK** | ✅ Upgraded from 15.5.15. Turbopack for dev and production builds; `--webpack` and the custom webpack hook removed. `eslint-config-next` aligned at `16.2.6`. |
| `@capacitor/*` (7.x) | `^7.0.0` | 8.x | **Medium** | ✅ Upgraded from 6.x. Capacitor 8 is available but requires native project regeneration + mobile E2E. |
| `@sentry/nextjs` | `^10.50.0` | 10.x | **OK** | ✅ Upgraded from 8.x. PII scrubbing verified. |
| `@aparajita/capacitor-secure-storage` | `^6.0.0` | 8.x | Low | ✅ Swapped from abandoned community plugin. Latest 8.0.0 available for future bump. |
| `react-select` | `^5.10.2` | 5.10.2 | **OK** | ✅ Already at latest. No v6 exists on npm. |
| `@google/genai` | `^1.17.0` | newer | Medium | AI gateway is centralized (`AiGatewayService`), so upgrade blast radius is small. |
| `jsdom` | `^29.0.1` | 29.x | OK | Dev-only. At latest stable. |
| `@types/node` | `^22` | `^22` | OK | ✅ Aligned with Node 22 LTS runtime. |
| `electron` | `^41.1.0` | 41 | OK | On track — verify Fuses + electron-builder pair still hold for code signing. |
| `Microsoft.Extensions.Http.Resilience` | `10.x` | 10.x | **OK** | ✅ Aligned with .NET 10. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.1` | 10.0.1 | OK | Stable; no further patches available. |

**Backend alignment is strong** (ASP.NET Core 10, EF Core 10.0.5, JwtBearer 10.0.5) — only `Http.Resilience` is off-track.

### 1.2 Deprecated / Risky Patterns

- **6 `react-hooks/exhaustive-deps` disables** in learner hot paths — `app/conversation/[sessionId]/page.tsx`, `app/writing/player/page.tsx`, `app/listening/player/[id]/page.tsx`, `app/vocabulary/quiz/page.tsx`, `app/vocabulary/flashcards/page.tsx`, `lib/hooks/use-dashboard-home.ts`. These are classic stale-closure risks. Most are justified in comments, but each deserves a targeted refactor to `useEffectEvent` (React 19), `useRef`, or stable callbacks.
- **Community/abandoned plugin** `capacitor-secure-storage-plugin` holds sensitive auth state — security debt (see §1.1).
- **`ai-studio-applet`** is the `package.json` `name` — legacy/placeholder, should be `oet-with-dr-hesham` to match product. Cosmetic.
- **Duplicate entry in `AGENTS.md`**: the "AI Conversation Module (MISSION CRITICAL)" paragraph is pasted twice back-to-back. Safe to delete the duplicate.
- **Unused/stray root files**: `nul` (Windows reserved name, almost certainly from an accidental `> nul` redirection), `build_final.txt`, `tsconfig.tsbuildinfo` (should be gitignored, not committed). Verify none are in git tracking.
- No `framer-motion` holdovers — `motion/react` migration is complete. ✅
- No `@ts-ignore` usage in app code; a single `@ts-expect-error` for an optional peer dep (`lib/mobile/pronunciation-recorder.ts`) which is appropriate. ✅

### 1.3 Build / Infra Sprawl

- **5 compose files** at root: `docker-compose.backend.yml`, `desktop.yml`, `production.yml`, `production.hostports.yml`, `production.prebuilt-web.yml`. Consolidate with compose overrides (`docker compose -f base.yml -f override.yml`) or document the decision matrix in `DEPLOYMENT.md`.
- **Two Dockerfiles** (`Dockerfile`, `Dockerfile.prebuilt`). Acceptable but undocumented in README.
- Electron `npmRebuild: false` + `buildDependenciesFromSource: false` — fine, but means native modules must ship prebuilt; add a CI check.

### 1.4 Modernization Opportunities

- **Next.js 16 Cache Components** — the dashboard, progress, and admin analytics pages are prime candidates for `use cache` + `cacheLife`/`cacheTag` once on Next 16. Would measurably cut TTFB for auth'd routes.
- **React 19 APIs under-used** — `useActionState`, `useOptimistic`, and `useEffectEvent` (stable as of React 19.2) would eliminate most of the 6 exhaustive-deps disables.
- **Tailwind v4** already adopted ✅ — no debt here.
- **.NET 10 Minimal API + OpenAPI** is current; consider `Microsoft.AspNetCore.OpenApi` native schema filters instead of any remaining Swashbuckle-only customisations.

### 1.5 What's **Not** Debt

- TypeScript strictness, test coverage, skill/instruction system, scoring/rulebook/AI-gateway contracts, content-upload pipeline, RBAC, CSP headers — all well-engineered. Do not touch.

---

## 2. Prioritized Cleanup Plan

Each wave is independently shippable with its own rollback. **Verification gate** for every wave: `npx tsc --noEmit && npm run lint && npm test && npm run build && npm run backend:test`.

### Wave 0 — Housekeeping (0.5 day, zero risk)

1. Delete stray files: `nul`, `build_final.txt`. Add `tsconfig.tsbuildinfo` to `.gitignore` if tracked.
2. De-duplicate the "AI Conversation Module" block in [AGENTS.md](AGENTS.md).
3. Rename `package.json` → `"name": "oet-with-dr-hesham"`.
4. Document Dockerfile / compose-file matrix in [DEPLOYMENT.md](DEPLOYMENT.md).

**Rollback:** trivial revert.

### Wave 1 — Security-tinted dependency upgrades (1–2 days, medium risk)

1. **Replace `capacitor-secure-storage-plugin`** with `@aparajita/capacitor-secure-storage`. Abstraction already lives at [lib/mobile/secure-storage.ts](lib/mobile/secure-storage.ts) — swap the import surface only. Add a one-time migration to re-key existing tokens on first launch.
2. **Bump `@sentry/nextjs` 8 → 10** and align `Sentry.AspNetCore` 5 → latest. Re-verify PII scrubbing in `SentryBootstrap.ScrubPii` (the `.csproj` comment explicitly warns about `SendDefaultPii`).
3. **Align backend `Microsoft.Extensions.Http.Resilience` 9.0.0 → 10.x** and `Npgsql.EntityFrameworkCore.PostgreSQL` → 10.0.5+.

**Verification extras:** Sentry smoke test in staging (throw a test error, confirm scrubbing); mobile sign-in/out flow on Android + iOS; backend integration tests for Brevo retry.

**Rollback:** pinned versions; keep previous `package-lock.json` / `.csproj` on a rollback branch.

### Wave 2 — Capacitor 6 → 7 (2–4 days, medium risk)

1. Bump `@capacitor/core`, `@capacitor/android`, `@capacitor/ios`, `@capacitor/cli` to 7.x.
2. Bump all `@capacitor/*` plugin packages to their 7-compatible majors.
3. Regenerate native projects (`cap sync`), run Android + iOS E2E smoke (`npm run test:e2e`), verify deep-link scheme and push notifications.
4. Re-run the `lib/mobile/*` integration tests (secure storage, biometric, push, deep-link).

**Rollback:** native folders (`android/`, `ios/`) are regenerated — ensure a clean git state pre-bump; revert via git.

### Wave 3 — Next.js 15 → 16 (3–5 days, medium risk)

1. Run the official Next 16 codemod.
2. Update `next.config.ts` — PPR / Cache Components opt-in flags.
3. Address any middleware / route-handler signature changes ([middleware.ts](middleware.ts), [instrumentation.ts](instrumentation.ts)).
4. Re-validate `output: 'standalone'` Docker build and Electron packaging (the standalone output is consumed by `electron-builder.config.cjs`).
5. Re-run full E2E matrix and desktop-packaged smoke.

**Rollback:** revert `package.json` + `next.config.ts`; caches are non-persistent.

### Wave 4 — React 19 effect cleanup ✅ Executed (April 2026)

**Outcome:** React 19.2 shipped `useEffectEvent` as a stable named export (verified `node_modules/@types/react/index.d.ts:1787–1791`), decoupling this wave from the Wave 3 Next.js upgrade. All six `react-hooks/exhaustive-deps` disables were closed incrementally across the session; the final remaining one in `lib/hooks/use-dashboard-home.ts` landed in commit `d6f5b75`.

**Change:** effect now reads mutation-but-not-reactive values (`authChecked`, `dismissedFromBanner`, `queryClient`, `refetchHome`, etc.) through `useEffectEvent`; dependency array reduced to `[authLoading, isAuthenticated]`.

**Verification:** `tsc --noEmit` EXIT 0 · `next lint` clean · `lib/hooks/__tests__/use-dashboard-home.test.tsx` 1/1 · `hooks/usePronunciationRecorder.test.ts` 6/6 · repo-wide `react-hooks/exhaustive-deps` disable count **6 → 0**.

**Follow-on (optional, not required for wave closure):** converting learner-side mutation flows (writing submit, conversation turn, pronunciation attempt) to React 19 Actions + `useOptimistic` remains an opportunity, but is modernization rather than debt cleanup.

### Wave 5 — Infra consolidation & modernization (2–3 days, low risk)

1. Collapse the 5 compose files into `docker-compose.yml` + named override files using the `COMPOSE_FILE` pattern; document in DEPLOYMENT.md.
2. Adopt Next 16 Cache Components on `app/dashboard/*`, `app/progress/*`, and `app/admin/analytics/*` (read-mostly, auth-aware). Use `cacheTag()` keyed by user id and invalidate via `updateTag()` on mutations.
3. Remaining minor dep bumps: `react-select` 5 → 6, `@google/genai`, `jsdom`, `@types/node`.

---

## 3. Out of Scope (intentional)

- Rulebook JSON schema — governed by `docs/RULEBOOKS.md`; do not touch as part of cleanup.
- Scoring functions — locked by `docs/SCORING.md` invariants.
- `OetStatementOfResultsCard` styling — pixel-locked per `docs/OET-RESULT-CARD-SPEC.md`.
- Admin RBAC, AI gateway contract, content-upload pipeline — all current, all well-tested.

---

## 4. Suggested Order of Operations

```
Wave 0  ──►  Wave 1  ──►  Wave 2  ──┐
                  │                 ├──►  Wave 5
                  └──►  Wave 3  ────┘

                          Wave 4  ──►  (shipped independently — stable `useEffectEvent`)
```

Waves 1 and 2 are independent. Wave 5 is the polish pass after the major bumps land. **Wave 4 was decoupled** once React 19.2's stable `useEffectEvent` landed and has shipped ahead of Wave 3.

---

## 5. Tracking

Recommend creating a single GitHub Milestone **"Tech Debt 2026-Q2"** with one issue per wave, each gated on:

- [ ] `npx tsc --noEmit` clean
- [ ] `npm run lint` clean
- [ ] `npm test` — 77/77 files, 304/304 tests
- [ ] `npm run build` — 169+ pages
- [ ] `npm run backend:test` green
- [ ] E2E smoke green (`npm run test:e2e:smoke`)
- [ ] Staging deploy verified (Sentry + mobile flows for Waves 1–2)
