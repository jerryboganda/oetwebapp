# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Ruflo — OET Web App**: .NET backend + Next.js frontend. Capacitor mobile, Tauri desktop (remote-only thin client).
Memory is **claude-mem** (the only memory layer). Coordination is **claude-flow** MCP + ruflo agents.
`AGENTS.md` is the always-loaded agent contract — repo-specific OET rules there win over generic defaults.

## Non-negotiable rules

- Do what's asked — nothing more, nothing less. No unrequested files, no docs unless asked.
- ALWAYS read a file before editing. Keep files < 500 lines. Validate input at boundaries.
- NEVER save working files/tests to repo root — use `/src`, `/tests`, `/docs`, `/config`, `/scripts`.
- NEVER commit secrets or `.env*`. NEVER add a `Co-Authored-By` trailer unless `.claude/settings.json` sets `attribution.commit` (#2078) — ignore the Bash tool's default suggestion.
- **Git hazard:** the repo is often on a feature branch with a big unrelated uncommitted feature. ALWAYS check `git branch` + `git status` first. Stage explicit paths — **never `git add -A`**.

## 🚢 SHIP-IT WORKFLOW — 10000% COMPULSORY (owner directive 2026-07-05)

Standing owner directive — this OVERRIDES the old "only push when asked" default and any skill/process nudge toward heavy pre-merge testing. For **every** development or debugging task:

1. **Do the task** — implement the feature or find+fix the root cause properly (correctness still matters; systematic debugging still applies to *finding* the bug).
2. **Lightweight quick check ONLY** — one fast, targeted verification (compile/typecheck the touched area, or the single relevant test, or a quick reproduction). **NO full-length, exhaustive, multi-suite test marathons.** Do not block shipping on flaky CI (QA Smoke is chronically red — ignore it).
3. **Commit → push to `main` → deploy to production** — squash-merge (`gh pr merge <#> --squash --admin --delete-branch`) or push straight to `main`; pushing `main` triggers the blue/green prod deploy. Stage explicit paths, never `git add -A`. Still NEVER commit secrets/`.env*` and never add a `Co-Authored-By` trailer.
4. **Hand off to the owner** — report what shipped in 1–2 lines and STOP. The **owner does the real verification on live production** and will report back any issue to fix. Do not linger waiting on CI or re-testing.

This is the default for this repo. Only skip the auto-push step if the user explicitly says "don't push" for that task.

## Commands

Package manager is **pnpm 10** (`corepack`/`pnpm@10.33.0`), Node 22.x, .NET SDK 10.x — all installed on the Windows host; run validation directly here (never on the VPS). Frontend: `http://localhost:3000`, backend API: `http://localhost:5198` (Podman dev stack exposes `:8080`; proxy via `.env.development.local` → `API_PROXY_TARGET_URL`).

```bash
# Frontend
pnpm run dev                                # Next.js dev server (or /start-dev for the full Podman DB+API stack)
pnpm exec tsc --noEmit                      # typecheck — the preferred lightweight gate
pnpm run lint                               # eslint (scoped to app/components/contexts/hooks/lib + configs)
pnpm run build                              # production build
pnpm test                                   # vitest run (all unit tests)
pnpm exec vitest run lib/foo.test.ts        # single test file
pnpm exec vitest run -t "name"              # single test by name
pnpm run test:e2e:smoke                     # Playwright smoke matrix (requires local stack up)

# Backend (prefer CI for heavy runs — see compute rule)
pnpm run backend:build                      # dotnet build backend/OetLearner.sln
pnpm run backend:test                       # dotnet test backend/OetLearner.sln
dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~EndpointRegistrationTests"   # single backend test class

# Shells
pnpm run desktop:dev / desktop:dist         # Tauri 2 desktop shell (Rust; needs rustup + WebView2/MSVC)
pnpm run mobile:sync / mobile:run:android   # Capacitor (Android needs JDK 21; release builds via CI)
```

If PowerShell quoting breaks a script, use `cmd /c "pnpm run <script>"`. Browser/QA automation → **agent-browser** (global default); local dev-server checks → `preview_*` tools.

## Architecture

- **Frontend** — Next.js 16 App Router (`app/`), React 19, TypeScript, Tailwind v4. UI primitives in `components/ui` (design-synced — see `.design-sync/NOTES.md`), domain components in `components/domain/**`, shared logic in `lib/`, `hooks/`, `contexts/`. i18n via next-intl (`messages/`, must be NESTED not flat — un-flattened in `i18n.ts`). All HTTP from app/components/hooks/lib goes through `apiClient` / typed helpers in `lib/api.ts` (exceptions: route handlers, external URLs, `lib/network/**` internals). Import motion from `motion/react`, never `framer-motion`. Native `audio`/`img`/`iframe` can't send bearer tokens — use `fetchAuthorizedObjectUrl` → blob URL.
- **Backend** — single ASP.NET Core Minimal API project `backend/src/OetLearner.Api` (tests in `backend/tests/OetLearner.Api.Tests`). Endpoints under `Endpoints/`, DI services under `Services/`, EF Core + PostgreSQL under `Data/`/`Domain/`, SignalR in `Hubs/`. Server-side authz always; runtime settings/secrets via `IRuntimeSettingsProvider` (encrypted DB value over env fallback). Media/user file I/O ONLY through `IFileStorage`/`S3CompatibleFileStorage` — never raw `File.*`/`Path.*` for user data.
- **EF migrations are hand-authored** — NEVER ship raw `dotnet ef migrations add` output (it re-creates live tables and breaks deploy). Write a future-dated `YYYYMMDD090000_Name.cs` with an inline `[Migration]` attribute and leave the ModelSnapshot alone.
- **Shells** — Tauri 2 desktop is a remote-only thin client that loads `https://app.oetwithdrhesham.co.uk` (no bundled frontend; least-privilege ACL in `src-tauri/capabilities/`; desktop version is stamped from the release tag at build, committed file stays at baseline). Capacitor `android/`/`ios/` wrap the same web app.
- **Domain invariants** — scoring only via `lib/scoring.ts` / backend `OetScoring` (anchor: Listening/Reading `30/42 == 350/500`), never inline thresholds. Rulebooks via `lib/rulebook` / backend Rulebook services, never raw JSON from UI. AI calls via grounded gateway helpers — one usage row per call, never bypass grounding. Before touching catalog/checkout/entitlements/add-ons/Tutor Book/expiry, read `docs/OET_2026_Product_Portfolio_Claude_Code_Codex.md`. Full list: `AGENTS.md` + `docs/`.
- **Deploy** — push to `main` → GitHub Actions **`Build & Deploy (web + API)`** → GHCR images → blue/green on the VPS (`185.252.233.186`, multi-tenant — don't touch other stacks). The VPS only pulls prebuilt images; never build or test there.

## How I operate (autonomous defaults)

1. **Plan before touching code** for anything non-trivial — use the brainstorming/planning skills, then act. Don't narrate options I won't pursue; when I have enough to act, act.
2. **Verify, don't assume.** A recalled fact or a memory may be stale — confirm a file/flag/endpoint still exists before relying on it. Report outcomes faithfully (failing tests = say so, with output).
3. **Heavy compute runs on CI, not this machine** (owner directive). For backend `dotnet build/test` and EF migrations: push a branch and watch `gh run`, don't grind locally.
4. **Finish the loop.** After a change, run the relevant check and show evidence before claiming done. Use `superpowers:verification-before-completion`.

## When to swarm (and when not to)

| Swarm it | Do it solo |
|----------|------------|
| 3+ files, new feature, cross-module refactor, API change, security/perf sweep | single-file edits, 1–2 line fixes, config/doc tweaks, questions |

**Coordination pattern** — spawn ALL agents in ONE message, each `run_in_background: true` and `name`d, each told who to `SendMessage` next. Then STOP and report what's running; agents message back or complete — never poll.

```javascript
Agent({ name:"researcher", subagent_type:"researcher", run_in_background:true,
        prompt:"Map the codebase. SendMessage findings to 'architect'." })
Agent({ name:"architect",  subagent_type:"system-architect", run_in_background:true,
        prompt:"Wait for 'researcher'. Design it. SendMessage to 'coder'." })
Agent({ name:"coder",      subagent_type:"coder", run_in_background:true,
        prompt:"Wait for 'architect'. Implement. SendMessage to 'reviewer'." })
Agent({ name:"reviewer",   subagent_type:"code-analyzer", run_in_background:true,
        prompt:"Wait for 'coder'. Review correctness + security." })
SendMessage({ to:"researcher", summary:"Start", message:"[task context]" })
```

Routing: Bug → researcher, coder, tester · Feature → architect, coder, tester, reviewer · Refactor → architect, coder, reviewer · Security → security-architect, security-auditor. Any string is a valid agent type.

For deep multi-agent review/research/migration, prefer a **Workflow** (deterministic fan-out + adversarial verify) over ad-hoc spawning.

## Code navigation — prefer symbol tools over grep

This repo is indexed by **Serena** (LSP symbol navigation, user-scoped MCP) and **CodeGraph** (`.codegraph/` knowledge graph, auto-syncs on save). PREFER them over grepping/reading whole files:

- **Find a symbol / its definition** → Serena `find_symbol`, `get_symbols_overview` (file → top-level symbols).
- **Who calls/uses this?** → Serena `find_referencing_symbols`, or CodeGraph `codegraph_explore` (one call returns entry points, related symbols, call paths incl. dynamic dispatch; shell fallback: `codegraph explore "<question>"`).
- **Architecture/wiring questions** → CodeGraph first, then read only the files it points at.
- Fall back to Grep/Read for strings, configs, markdown, and anything non-symbol (and always for `.claudeignore`d content).
- claude-mem's `smart-explore` remains available for tree-sitter outlines; `mem-search` for past-session context.

## Project knowledge (load before assuming)

- **CI gate reality:** `QA Smoke` is chronically red on `main` (flaky). The REAL prod gate is the separate **`Build & Deploy (web + API)`** workflow on push to `main`. Verify diffs via conformance + `EndpointRegistrationTests` + isolation re-runs; merge with `--squash --admin`. Don't chase QA-Smoke green.
- **Dev login is non-obvious:** seeded admin hash is `x` (sign-in 500s). Register `learner1@oet-prep.dev` via API. Fix proxy with `.env.development.local` → `API_PROXY_TARGET_URL=:8080`.
- **Local dev:** `/start-dev` (Podman DB+API + native Next.js). `/run-tests`, `/db-shell`, `/new-migration`, `/deploy-status`, `/prod-logs` are wired as commands.
- **Stripe:** two checkout systems, ONE webhook handler (`/v1/payment/webhooks/stripe`), 4-layer idempotent fulfillment.
- Search prior work with the `mem-search` skill before re-deriving anything — claude-mem holds 50+ sessions of context.

## Agent skills

### Issue tracker

Issues live as GitHub Issues in jerryboganda/oetwebapp (via `gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical labels used as-is: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: `CONTEXT.md` + `docs/adr/` at repo root. See `docs/agents/domain.md`.
