# Design Spec: Repo-wide npm → pnpm Migration

- **Status:** Draft for review
- **Date:** 2026-06-01
- **Owner:** (pending)
- **Scope:** Migrate the entire OET Prep Platform JS/TS toolchain from npm to pnpm in a single clean cutover.

## 1. Goal

Standardize the whole repository on pnpm (package manager, Dockerfiles, CI workflows, scripts, and AI-direction docs) **and** gain pnpm's faster, content-addressed install/build performance.

This is a **migration only**. No dependency upgrades, no monorepo/workspace restructuring, no Docker base-image changes beyond adding pnpm, no feature work.

### Locked decisions

| # | Decision | Choice |
|---|----------|--------|
| 1 | Goal | Standardize on pnpm **everywhere** + optimize install/build speed (Mix of B + A) |
| 2 | Strictness | **Strict** — pnpm default isolated `node_modules`; fix phantom-dependency errors by adding the real dependency. No `shamefully-hoist`, no `node-linker=hoisted`. |
| 3 | Cutover | **Single** feature branch + PR; full host validation + local Docker image build before the VPS ever sees it. |
| 4 | Provisioning | **Corepack + pinned `packageManager`** field. Pin `pnpm@10.33.0` to match `.tools/autoskills`. |

## 2. Non-Goals

- Do **not** modify `.tools/autoskills/**` (already on `pnpm@10.33.0` with its own lockfile and supply-chain hardening; isolated subtree).
- Do **not** upgrade or change any application dependency versions, except adding packages that strict mode reveals as missing (phantom dependencies).
- Do **not** restructure into a pnpm workspace / monorepo.
- Do **not** trigger a production VPS deploy as part of this PR. Deploy is a separate, later, user-initiated step after merge.

## 3. Current State (npm footprint)

Verified by grep/read on 2026-06-01.

### 3.1 package.json
- `"name": "oet-prep"`, `"main": "electron/main.cjs"`, no `packageManager` field.
- Standard scripts use bare commands (`next dev`, `next build`, `vitest run`, `eslint …`, `dotnet …`) — these are package-manager-agnostic and run fine under pnpm.
- `mobile:build` chains `npm run build && cap sync` (contains literal `npm run`).
- Four `docker:*` scripts hardcode npm inside `node:22-alpine`:
  - `docker:test` → `… node:22-alpine npm test`
  - `docker:lint` → `… node:22-alpine npm run lint`
  - `docker:tsc` → `… node:22-alpine npx tsc --noEmit`
  - `docker:tsc:setup` → `… node:22-alpine npm ci --ignore-scripts --loglevel=error`
- Several scripts shell out to `npx -y repomix@latest` (third-party one-shot; left as-is — not part of the project dependency graph).

### 3.2 Dockerfiles (2)
- Root `Dockerfile`: `npm ci --no-audit --no-fund` (install stage) and `npm run build` (build stage). Feeds the production VPS blue/green slots — highest-risk surface.
- `tools/copilot-extension/Dockerfile`: `npm ci --omit=dev || npm install --omit=dev`. **In scope** (confirmed).

### 3.3 CI workflows (6) under `.github/workflows/`
`qa-smoke.yml`, `mobile-ci.yml`, `mobile-release.yml`, `desktop-release.yml`, `speaking-ci.yml`, `oet-gapclosure-validation.yml` — all use `npm ci` + `npm run lint|test|build`.

### 3.4 Scripts
`scripts/one-click-local-deploy.ps1` (`cmd /c npm ci`, `cmd /c npm run backend:run`, `cmd /c npm run dev`), `scripts/OET-Launch.ps1`, `scripts/run-all-gates.sh`, `scripts/install-admin-deps.sh`, `scripts/setup-local-postgres.ps1`, plus user-facing message strings in `scripts/ts-prune-filter.mjs` and `scripts/desktop-packaged-smoke.cjs`.

### 3.5 AI-direction docs
`AGENTS.md`, `.github/copilot-instructions.md`, `.codex/AGENTS.md`, and the instruction files `validation`, `testing`, `backend`, `deployment` reference the npm command ladder. The `.tools/autoskills` scope note should be updated to state the rest of the repo is now pnpm too.

### 3.6 Lockfile
`package-lock.json` exists at repo root and must be removed; `pnpm-lock.yaml` becomes the source of truth.

## 4. Target State

### 4.1 Package manager provisioning (corepack)
- Add `"packageManager": "pnpm@10.33.0"` to root `package.json`.
- Local, Docker, and CI all run `corepack enable` (Node 22 ships corepack) so the exact pinned pnpm version is used everywhere — zero drift.

### 4.2 pnpm configuration
- Add a root `.npmrc` (or `pnpm-workspace.yaml` settings) containing only what is required. Defaults (isolated `node_modules`) are kept — strict mode.
- Add an `onlyBuiltDependencies` allowlist for packages that legitimately run install/build scripts (pnpm blocks build scripts by default). Candidates to confirm during validation: `esbuild`, `electron`, `sharp`, `@sentry/cli`, `workerd`/`@cloudflare/*` if present, and any other native postinstall packages surfaced by `pnpm install`. The exact list is finalized empirically in the validation loop, not guessed.

### 4.3 package.json script rewrites
- `mobile:build`: `npm run build && cap sync` → `pnpm run build && cap sync`.
- `docker:test`: `npm test` → `corepack enable && pnpm test` (in-container).
- `docker:lint`: `npm run lint` → `corepack enable && pnpm run lint`.
- `docker:tsc`: `npx tsc --noEmit` → `corepack enable && pnpm exec tsc --noEmit`.
- `docker:tsc:setup`: `npm ci --ignore-scripts …` → `corepack enable && pnpm install --frozen-lockfile --ignore-scripts`.
- Leave `npx -y repomix@latest` untouched (one-shot third-party tool, not a project dep).

### 4.4 Dockerfiles
- Root `Dockerfile`:
  - Install stage: `npm ci --no-audit --no-fund` → `corepack enable && pnpm install --frozen-lockfile --prod=false` (prod stage prunes later as today).
  - Build stage: `npm run build` → `pnpm run build`.
  - Swap the npm cache mount for a pnpm store cache mount (`--mount=type=cache,target=/root/.local/share/pnpm/store`) to preserve/improve layer caching.
  - Copy `pnpm-lock.yaml` (replace `package-lock.json` in COPY lines).
- `tools/copilot-extension/Dockerfile`:
  - `npm ci --omit=dev || npm install --omit=dev` → `corepack enable && pnpm install --prod --frozen-lockfile`.
  - Update COPY of lockfile accordingly.

### 4.5 CI workflows (all 6)
For each workflow:
- Add `pnpm/action-setup@v4` (or run `corepack enable`) before `actions/setup-node`.
- Set `actions/setup-node` `cache: 'pnpm'`.
- `npm ci` → `pnpm install --frozen-lockfile`.
- `npm run X` → `pnpm run X`; `npx Y` → `pnpm exec Y`.

### 4.6 Scripts
- `one-click-local-deploy.ps1`: `cmd /c npm ci` → `cmd /c pnpm install --frozen-lockfile`; `cmd /c npm run …` → `cmd /c pnpm run …`.
- `OET-Launch.ps1`, `run-all-gates.sh`, `install-admin-deps.sh`, `setup-local-postgres.ps1`: npm → pnpm commands.
- User-facing message strings in `ts-prune-filter.mjs` and `desktop-packaged-smoke.cjs`: npm → pnpm.

### 4.7 AI-direction docs
- `AGENTS.md`, `.github/copilot-instructions.md`, `.codex/AGENTS.md`: update the validation ladder and any `npm run …` references to pnpm.
- Instruction files `validation`, `testing`, `backend`, `deployment`: update command ladders to pnpm.
- Update the `.tools/autoskills` scope note to record that the main repo is now pnpm as well.

### 4.8 Lockfile
- Delete `package-lock.json`.
- Commit generated `pnpm-lock.yaml`.

## 5. Validation & Cutover Gate (Windows host)

Run in order; do not touch the VPS until all are green.

```
corepack enable
pnpm install                      # generates pnpm-lock.yaml; surfaces phantom deps
npx tsc --noEmit                  # (or: pnpm exec tsc --noEmit)
pnpm run lint
pnpm test
pnpm run build                    # most likely place strict mode bites
pnpm run backend:build
pnpm run backend:test
docker build -f Dockerfile .      # prove the prod image builds under pnpm
```

**Phantom-dependency loop:** each `Cannot find module 'X'` failure → add `X` to `package.json` as an explicit dependency, re-run `pnpm install`, continue. Each native postinstall block → add the package to `onlyBuiltDependencies`, re-run.

**Cutover:** all work lands on one feature branch (e.g. `chore/pnpm-migration`) as a single PR. Production VPS deploy is a separate later step, not part of this PR.

## 6. Risks & Mitigations

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Strict mode surfaces phantom deps at `pnpm run build` | High (expected) | Fix-and-add loop; this is the intended correctness win. |
| Native build scripts blocked by pnpm | Medium | `onlyBuiltDependencies` allowlist, finalized empirically. |
| Prod Docker build differs from host | Medium | `docker build -f Dockerfile .` locally before VPS. |
| CI cache key mismatch / slow first run | Low | `cache: 'pnpm'` + pnpm store; first run warms cache. |
| Corepack disabled in some CI image | Low | Use `pnpm/action-setup@v4` which provisions pnpm directly. |

## 7. Rollback

The entire change is one branch. If any outcome is unacceptable, do not merge — `main` stays on npm, untouched. No production system is modified until the merged change is explicitly deployed in a separate step.

## 8. VPS Deploy Reminders (for the later, separate deploy step)

- Override `ROUTER_IMAGE=oetwebsite-nginx-router:local` on every build/up command (the `.env.production` digest pin breaks build tags).
- Never run `docker compose down -v` or remove protected volumes `oetwebsite_oet_postgres_data` / `oetwebsite_oet_learner_storage`.

## 9. Open Items

- Final `onlyBuiltDependencies` list (resolved during validation).
- Branch name confirmation.
- Owner assignment.
