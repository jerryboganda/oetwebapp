# pnpm Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the entire OET with Dr Hesham Platform JS/TS toolchain from npm to pnpm in one clean cutover, fully validated on the Windows host (and a local Docker image build) before any VPS deploy.

**Architecture:** Pin `pnpm@10.33.0` via corepack + the `package.json` `packageManager` field so local, Docker, and CI all use the identical version. Keep pnpm's strict isolated `node_modules`; fix any phantom-dependency errors by adding the real dependency. Replace `package-lock.json` with `pnpm-lock.yaml`. Convert 2 Dockerfiles, 6 CI workflows, the `docker:*`/`mobile:build` package scripts, host scripts, and AI-direction docs. Leave `.tools/autoskills/**` (already pnpm) untouched.

**Tech Stack:** pnpm 10.33.0, corepack, Node 20 (Docker) / Node 22 (host), Next.js 16, Vitest, Playwright, ASP.NET Core (dotnet, unaffected), GitHub Actions, Docker.

**Reference spec:** [docs/superpowers/specs/2026-06-01-pnpm-migration-design.md](../specs/2026-06-01-pnpm-migration-design.md)

---

## Conventions used in every task

**Branch:** all work lands on `chore/pnpm-migration`.

**Deterministic command transforms** (apply these exact substitutions wherever they appear):

| npm | pnpm |
|-----|------|
| `npm ci` / `npm ci --no-audit --no-fund` / `npm ci --omit=dev` | `pnpm install --frozen-lockfile` |
| `npm install --save` | `pnpm add` |
| `npm install` (no lockfile present) | `pnpm install` |
| `npm run <x>` | `pnpm run <x>` |
| `npm test` | `pnpm test` |
| `npm test -- <args>` | `pnpm test -- <args>` |
| `npx <bin>` | `pnpm exec <bin>` |
| `cache: npm` (setup-node) | `cache: pnpm` |
| `package-lock.json` (COPY/refs) | `pnpm-lock.yaml` |

**Do NOT touch:** anything under `.tools/autoskills/**` (its own pnpm setup), and `npx -y repomix@latest` invocations (one-shot third-party tool, not a project dep).

---

## Task 0: Create branch and capture npm baseline

**Files:** none (git + verification only)

- [ ] **Step 1: Create the migration branch**

```powershell
git checkout -b chore/pnpm-migration
```

- [ ] **Step 2: Confirm pnpm is available via corepack at the pinned version**

```powershell
corepack enable
corepack prepare pnpm@10.33.0 --activate
pnpm --version
```

Expected output: `10.33.0`

- [ ] **Step 3: Record the current npm footprint for later diffing**

```powershell
Select-String -Path package.json,Dockerfile,tools/copilot-extension/Dockerfile,.github/workflows/*.yml,scripts/* -Pattern 'npm ci|npm install|npm run|npm test|npx |cache: *npm|package-lock' | Select-Object Path,LineNumber,Line
```

Expected: a list of every npm reference this plan will convert (matches the tables in the spec §3). No action — this is the baseline checklist.

---

## Task 1: Pin pnpm and rewrite `.npmrc`

**Files:**
- Modify: `package.json` (add `packageManager` field)
- Modify: `.npmrc`

- [ ] **Step 1: Add the `packageManager` field to `package.json`**

In `package.json`, immediately after the `"main": "electron/main.cjs",` line, add:

```json
  "packageManager": "pnpm@10.33.0",
```

- [ ] **Step 2: Convert `.npmrc` from npm peer flag to pnpm equivalents**

Replace the entire contents of `.npmrc` (currently the single line `legacy-peer-deps=true`) with:

```ini
# pnpm configuration (migrated from npm legacy-peer-deps=true)
# Keep peer-dependency resolution permissive to match prior npm behavior.
strict-peer-dependencies=false
auto-install-peers=true
# Strict isolated node_modules (pnpm default) is intentional. Do NOT add
# shamefully-hoist or node-linker=hoisted. Phantom-dependency errors are fixed
# by adding the missing package to package.json.
```

- [ ] **Step 3: Verify pnpm reads the config and the pin**

```powershell
pnpm config get strict-peer-dependencies
```

Expected output: `false`

- [ ] **Step 4: Commit**

```powershell
git add package.json .npmrc
git commit -m "build(pnpm): pin pnpm@10.33.0 via corepack and migrate .npmrc"
```

---

## Task 2: Generate the pnpm lockfile and delete the npm lockfile

**Files:**
- Create: `pnpm-lock.yaml` (generated)
- Delete: `package-lock.json`
- Possibly modify: `package.json` (only if phantom deps appear during build, handled in Task 3)

- [ ] **Step 1: Generate `pnpm-lock.yaml` from the existing `package.json`**

```powershell
pnpm install
```

Expected: pnpm resolves all dependencies and writes `pnpm-lock.yaml`. It may print a notice that build scripts for some packages were ignored (e.g. `esbuild`, `@sentry/cli`, `sharp`). Note the exact package names it lists — they feed Task 2 Step 2.

- [ ] **Step 2: Approve required build scripts via `onlyBuiltDependencies`**

pnpm blocks postinstall/build scripts by default. For each package pnpm reported as "ignored build scripts" in Step 1 that genuinely needs them, add it to an `onlyBuiltDependencies` array in `package.json` (root level, e.g. after `"packageManager"`). Use the actual list pnpm printed; the typical set for this stack is:

```json
  "pnpm": {
    "onlyBuiltDependencies": [
      "esbuild",
      "sharp",
      "@sentry/cli",
      "electron",
      "electron-winstaller"
    ]
  },
```

Then re-run to apply approvals:

```powershell
pnpm install
pnpm rebuild
```

Expected: no remaining "ignored build scripts" warning for the approved packages.

- [ ] **Step 3: Delete the npm lockfile**

```powershell
git rm package-lock.json
```

- [ ] **Step 4: Verify a clean frozen install works (simulates CI/Docker)**

```powershell
Remove-Item -Recurse -Force node_modules
pnpm install --frozen-lockfile
```

Expected: install completes with no "lockfile out of date" error.

- [ ] **Step 5: Commit**

```powershell
git add pnpm-lock.yaml package.json
git commit -m "build(pnpm): generate pnpm-lock.yaml, approve build scripts, drop package-lock.json"
```

---

## Task 3: Host validation ladder (phantom-dependency fix loop)

**Files:**
- Possibly modify: `package.json` (add any missing deps surfaced by strict mode)

- [ ] **Step 1: Type-check**

```powershell
pnpm exec tsc --noEmit
```

Expected: PASS. If it fails with `Cannot find module 'X'` / `Cannot find type definitions for 'X'`, add the real package: `pnpm add -D X` (types) or `pnpm add X` (runtime), then re-run.

- [ ] **Step 2: Lint**

```powershell
pnpm run lint
```

Expected: PASS (same result as before migration). Fix phantom deps the same way if any appear.

- [ ] **Step 3: Unit tests**

```powershell
pnpm test
```

Expected: PASS (same suite result as the npm baseline).

- [ ] **Step 4: Production build — the most likely place strict mode bites**

```powershell
pnpm run build
```

Expected: PASS. For each `Module not found: Can't resolve 'X'`, run `pnpm add X` (or `-D` for build-only), re-run `pnpm install`, then re-run the build. Repeat until green.

- [ ] **Step 5: Backend build + test (sanity — unaffected by pnpm, must still pass)**

```powershell
pnpm run backend:build
pnpm run backend:test
```

Expected: PASS.

- [ ] **Step 6: Commit any phantom-dependency additions**

```powershell
git add package.json pnpm-lock.yaml
git commit -m "build(pnpm): add explicit deps surfaced by pnpm strict mode"
```

If no deps were added, skip the commit (nothing staged).

---

## Task 4: Rewrite `package.json` scripts (mobile:build + docker:*)

**Files:**
- Modify: `package.json` (`scripts` block)

- [ ] **Step 1: Convert `mobile:build`**

Replace:

```json
    "mobile:build": "npm run build && cap sync",
```

with:

```json
    "mobile:build": "pnpm run build && cap sync",
```

- [ ] **Step 2: Convert the four `docker:*` scripts that hardcode npm inside `node:22-alpine`**

Replace these four lines:

```json
    "docker:test": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine npm test",
    "docker:lint": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine npm run lint",
    "docker:tsc": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine npx tsc --noEmit",
    "docker:tsc:setup": "docker volume create oet_web_node_modules_node22 && docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine npm ci --ignore-scripts --loglevel=error",
```

with:

```json
    "docker:test": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine sh -c \"corepack enable && pnpm test\"",
    "docker:lint": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine sh -c \"corepack enable && pnpm run lint\"",
    "docker:tsc": "docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine sh -c \"corepack enable && pnpm exec tsc --noEmit\"",
    "docker:tsc:setup": "docker volume create oet_web_node_modules_node22 && docker run --rm -v .:/src:ro -v oet_web_node_modules_node22:/src/node_modules -w /src node:22-alpine sh -c \"corepack enable && pnpm install --frozen-lockfile --ignore-scripts\"",
```

- [ ] **Step 3: Verify the JSON is still valid**

```powershell
node -e "JSON.parse(require('fs').readFileSync('package.json','utf8')); console.log('package.json OK')"
```

Expected output: `package.json OK`

- [ ] **Step 4: Commit**

```powershell
git add package.json
git commit -m "build(pnpm): convert mobile:build and docker:* scripts to pnpm via corepack"
```

---

## Task 5: Convert the root production `Dockerfile`

**Files:**
- Modify: `Dockerfile`

- [ ] **Step 1: Convert the `deps` stage (install)**

Replace:

```dockerfile
COPY package.json package-lock.json .npmrc ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
```

with:

```dockerfile
COPY package.json pnpm-lock.yaml .npmrc ./
RUN corepack enable && \
    --mount=type=cache,target=/pnpm/store \
    PNPM_HOME=/pnpm pnpm install --frozen-lockfile
```

> Note: BuildKit `--mount` must precede `RUN`'s command but follow `RUN`. If the inline form errors in this Dockerfile's syntax (`# syntax=docker/dockerfile:1.7`), use the split form:
> ```dockerfile
> COPY package.json pnpm-lock.yaml .npmrc ./
> RUN corepack enable
> RUN --mount=type=cache,target=/pnpm/store pnpm config set store-dir /pnpm/store && pnpm install --frozen-lockfile
> ```

- [ ] **Step 2: Convert the `builder` stage (build)**

Replace:

```dockerfile
RUN --mount=type=cache,target=/app/.next/cache npm run build
```

with:

```dockerfile
RUN corepack enable && --mount=type=cache,target=/app/.next/cache pnpm run build
```

If the inline `--mount` placement errors, use:

```dockerfile
RUN corepack enable
RUN --mount=type=cache,target=/app/.next/cache pnpm run build
```

- [ ] **Step 3: Convert the `validate` stage CMD**

Replace:

```dockerfile
CMD ["npx", "tsc", "--noEmit"]
```

with:

```dockerfile
CMD ["pnpm", "exec", "tsc", "--noEmit"]
```

> The `validate` stage copies `node_modules` from `deps` but does not run `corepack enable`. Add it to the stage so `pnpm` is on PATH. Insert after its `COPY . .` line:
> ```dockerfile
> RUN corepack enable
> ```

- [ ] **Step 4: Build the production image locally — the cutover gate**

```powershell
docker build -f Dockerfile -t oet-web:pnpm-check --build-arg NEXT_PUBLIC_API_BASE_URL=https://example.invalid .
```

Expected: image builds successfully through the `runner` stage. If the install stage fails on a native build script, add the package to `onlyBuiltDependencies` (Task 2 Step 2) and rebuild.

- [ ] **Step 5: Commit**

```powershell
git add Dockerfile
git commit -m "build(pnpm): convert root Dockerfile to pnpm + corepack + store cache"
```

---

## Task 6: Convert `tools/copilot-extension/Dockerfile`

**Files:**
- Modify: `tools/copilot-extension/Dockerfile`

- [ ] **Step 1: Convert the install line**

Replace:

```dockerfile
COPY package.json package-lock.json* ./
RUN npm ci --omit=dev 2>/dev/null || npm install --omit=dev
```

with:

```dockerfile
COPY package.json pnpm-lock.yaml* ./
RUN corepack enable && pnpm install --prod --frozen-lockfile
```

- [ ] **Step 2: Verify the extension image builds**

```powershell
docker build -f tools/copilot-extension/Dockerfile -t oet-copilot-ext:pnpm-check tools/copilot-extension
```

Expected: image builds successfully. If this sub-project has no `pnpm-lock.yaml`, the `*` glob makes the COPY optional and `pnpm install --prod` will resolve from `package.json`; confirm the build still completes.

- [ ] **Step 3: Commit**

```powershell
git add tools/copilot-extension/Dockerfile
git commit -m "build(pnpm): convert copilot-extension Dockerfile to pnpm"
```

---

## Task 7: Convert CI workflow — `qa-smoke.yml`

**Files:**
- Modify: `.github/workflows/qa-smoke.yml` (3 jobs)

- [ ] **Step 1: Add pnpm provisioning before every `actions/setup-node@v4`**

In each of the 3 jobs, immediately **before** the `- uses: actions/setup-node@v4` step, insert:

```yaml
      - uses: pnpm/action-setup@v4
        with:
          version: 10.33.0
```

- [ ] **Step 2: Switch setup-node cache to pnpm**

In each `actions/setup-node@v4` `with:` block, replace:

```yaml
          cache: npm
```

with:

```yaml
          cache: pnpm
```

- [ ] **Step 3: Convert the run commands**

Apply the transform table: `npm ci` → `pnpm install --frozen-lockfile`; `npm run lint` → `pnpm run lint`; `npm run build` → `pnpm run build`; `npx tsc --noEmit` → `pnpm exec tsc --noEmit`; `npx vitest run --reporter=dot` → `pnpm exec vitest run --reporter=dot`; `npx playwright install --with-deps chromium firefox webkit` → `pnpm exec playwright install --with-deps chromium firefox webkit`.

- [ ] **Step 4: Verify no npm references remain**

```powershell
Select-String -Path .github/workflows/qa-smoke.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
```

Expected: no matches.

- [ ] **Step 5: Lint the YAML**

```powershell
node -e "require('js-yaml')" 2>$null; python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/qa-smoke.yml')); print('qa-smoke.yml OK')"
```

Expected output: `qa-smoke.yml OK` (use whichever YAML parser is available; or rely on the editor's YAML diagnostics).

- [ ] **Step 6: Commit**

```powershell
git add .github/workflows/qa-smoke.yml
git commit -m "ci(pnpm): convert qa-smoke workflow to pnpm"
```

---

## Task 8: Convert CI workflow — `speaking-ci.yml`

**Files:**
- Modify: `.github/workflows/speaking-ci.yml`

- [ ] **Step 1: Add pnpm provisioning + cache**

Before the workflow's `actions/setup-node@v4` step, insert the `pnpm/action-setup@v4` block (version `10.33.0`) and set `cache: pnpm` in setup-node. If this workflow has no `npm ci` step but runs `pnpm run`/`pnpm test`, ensure an install step exists: add `- run: pnpm install --frozen-lockfile` after setup-node if dependencies are needed.

- [ ] **Step 2: Convert run commands**

Replace:

```yaml
        run: npm run lint
```
```yaml
        run: npm test -- --reporter=verbose
```
```yaml
        run: npx tsc --noEmit --pretty false
```
```yaml
        run: npm run build
```

with, respectively:

```yaml
        run: pnpm run lint
```
```yaml
        run: pnpm test -- --reporter=verbose
```
```yaml
        run: pnpm exec tsc --noEmit --pretty false
```
```yaml
        run: pnpm run build
```

- [ ] **Step 3: Verify + commit**

```powershell
Select-String -Path .github/workflows/speaking-ci.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
git add .github/workflows/speaking-ci.yml
git commit -m "ci(pnpm): convert speaking-ci workflow to pnpm"
```

Expected: grep returns no matches before commit.

---

## Task 9: Convert CI workflow — `oet-gapclosure-validation.yml`

**Files:**
- Modify: `.github/workflows/oet-gapclosure-validation.yml`

- [ ] **Step 1: Add pnpm provisioning + cache**

Insert the `pnpm/action-setup@v4` (version `10.33.0`) block before `actions/setup-node@v4`; set `cache: pnpm`.

- [ ] **Step 2: Convert run commands**

Replace `run: npm ci --no-audit --no-fund` → `run: pnpm install --frozen-lockfile`; `run: npm test` → `run: pnpm test`; `run: npm run lint` → `run: pnpm run lint`.

- [ ] **Step 3: Verify + commit**

```powershell
Select-String -Path .github/workflows/oet-gapclosure-validation.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
git add .github/workflows/oet-gapclosure-validation.yml
git commit -m "ci(pnpm): convert oet-gapclosure-validation workflow to pnpm"
```

Expected: grep returns no matches before commit.

---

## Task 10: Convert CI workflow — `mobile-ci.yml`

**Files:**
- Modify: `.github/workflows/mobile-ci.yml` (4 jobs)

- [ ] **Step 1: Add pnpm provisioning + cache to all 4 jobs**

Before each `actions/setup-node@v4`, insert the `pnpm/action-setup@v4` (version `10.33.0`) block; set `cache: pnpm` in each setup-node.

- [ ] **Step 2: Convert run commands in all 4 jobs**

`run: npm ci` → `run: pnpm install --frozen-lockfile`; `run: npm run lint` → `run: pnpm run lint`; `run: npm run build` → `run: pnpm run build`.

- [ ] **Step 3: Verify + commit**

```powershell
Select-String -Path .github/workflows/mobile-ci.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
git add .github/workflows/mobile-ci.yml
git commit -m "ci(pnpm): convert mobile-ci workflow to pnpm"
```

Expected: grep returns no matches before commit.

---

## Task 11: Convert CI workflow — `mobile-release.yml`

**Files:**
- Modify: `.github/workflows/mobile-release.yml` (2 jobs)

- [ ] **Step 1: Add pnpm provisioning + cache to both jobs**

Before each `actions/setup-node@v4`, insert the `pnpm/action-setup@v4` (version `10.33.0`) block; set `cache: pnpm`.

- [ ] **Step 2: Convert run commands**

`run: npm ci` → `run: pnpm install --frozen-lockfile`; `run: npm run build` → `run: pnpm run build`.

- [ ] **Step 3: Verify + commit**

```powershell
Select-String -Path .github/workflows/mobile-release.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
git add .github/workflows/mobile-release.yml
git commit -m "ci(pnpm): convert mobile-release workflow to pnpm"
```

Expected: grep returns no matches before commit.

---

## Task 12: Convert CI workflow — `desktop-release.yml`

**Files:**
- Modify: `.github/workflows/desktop-release.yml`

- [ ] **Step 1: Add pnpm provisioning + cache**

Before `actions/setup-node@v4`, insert the `pnpm/action-setup@v4` (version `10.33.0`) block; set `cache: pnpm`.

- [ ] **Step 2: Convert run commands**

`run: npm ci` → `run: pnpm install --frozen-lockfile`; `run: npm run desktop:dist` → `run: pnpm run desktop:dist`; `run: npm run test:e2e:desktop:packaged` → `run: pnpm run test:e2e:desktop:packaged`.

- [ ] **Step 3: Verify + commit**

```powershell
Select-String -Path .github/workflows/desktop-release.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
git add .github/workflows/desktop-release.yml
git commit -m "ci(pnpm): convert desktop-release workflow to pnpm"
```

Expected: grep returns no matches before commit.

---

## Task 13: Convert Playwright-only workflows — `speaking-e2e.yml`, `speaking-a11y.yml`

**Files:**
- Modify: `.github/workflows/speaking-e2e.yml`
- Modify: `.github/workflows/speaking-a11y.yml`

- [ ] **Step 1: Inspect each for an install/setup step**

```powershell
Get-Content .github/workflows/speaking-e2e.yml
Get-Content .github/workflows/speaking-a11y.yml
```

Identify whether each has `actions/setup-node` + an install step. If present, apply the pnpm provisioning + cache + transform table exactly as in Task 7. If they only run `npx playwright …` against a prebuilt environment, convert `npx playwright …` → `pnpm exec playwright …` and add the `pnpm/action-setup@v4` block before any node setup so `pnpm exec` resolves.

- [ ] **Step 2: Verify both files**

```powershell
Select-String -Path .github/workflows/speaking-e2e.yml,.github/workflows/speaking-a11y.yml -Pattern 'npm ci|npm run|npm test|cache: *npm|npx '
```

Expected: no matches.

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/speaking-e2e.yml .github/workflows/speaking-a11y.yml
git commit -m "ci(pnpm): convert speaking e2e/a11y workflows to pnpm exec"
```

- [ ] **Step 4: Final workflow sweep — confirm only autoskills still uses anything non-pnpm**

```powershell
Select-String -Path .github/workflows/*.yml -Pattern 'npm ci|npm run|npm test|cache: *npm' | Select-Object Path,LineNumber,Line
```

Expected: no matches under `.github/workflows/` (the `.tools/autoskills/.github/workflows/*` files are a separate tree and already pnpm — they must NOT appear here and must NOT be edited).

---

## Task 14: Convert host scripts

**Files:**
- Modify: `scripts/one-click-local-deploy.ps1`
- Modify: `scripts/OET-Launch.ps1`
- Modify: `scripts/run-all-gates.sh`
- Modify: `scripts/install-admin-deps.sh`
- Modify: `scripts/setup-local-postgres.ps1`
- Modify: `scripts/ts-prune-filter.mjs` (comment text)
- Modify: `scripts/desktop-packaged-smoke.cjs` (message text)

- [ ] **Step 1: `one-click-local-deploy.ps1`**

Replace `cmd /c npm ci` → `cmd /c pnpm install --frozen-lockfile`. Replace the warning text `node_modules is missing. Installing from package-lock.json with npm ci.` → `node_modules is missing. Installing from pnpm-lock.yaml with pnpm install.`. Replace `throw "npm ci failed with exit code $LASTEXITCODE."` → `throw "pnpm install failed with exit code $LASTEXITCODE."`. Replace `cmd /c npm run backend:run` → `cmd /c pnpm run backend:run`. Replace `cmd /c npm run dev` → `cmd /c pnpm run dev`.

- [ ] **Step 2: `OET-Launch.ps1`**

Replace `npm run backend:run > ""$backendLog"" 2>&1` → `pnpm run backend:run > ""$backendLog"" 2>&1`.

- [ ] **Step 3: `run-all-gates.sh`**

Replace `npm run lint > /tmp/g_lint.log 2>&1` → `pnpm run lint > /tmp/g_lint.log 2>&1`. Then grep the rest of the file for other `npm ` usages and apply the transform table to each.

```bash
grep -nE 'npm (ci|install|run|test)|npx ' scripts/run-all-gates.sh
```

- [ ] **Step 4: `install-admin-deps.sh`**

Replace `npm install --save \` → `pnpm add \`. Replace the closing message `Restart the dev server (npm run dev) for the new packages to be picked up.` → `Restart the dev server (pnpm run dev) for the new packages to be picked up.`.

- [ ] **Step 5: `setup-local-postgres.ps1`**

Replace `Run the backend with:  npm run backend:run` → `Run the backend with:  pnpm run backend:run`.

- [ ] **Step 6: `ts-prune-filter.mjs` and `desktop-packaged-smoke.cjs` message text**

In `ts-prune-filter.mjs` replace the comment `npm run unused:scan` → `pnpm run unused:scan`. In `desktop-packaged-smoke.cjs` replace `Run npm run desktop:dist first` → `Run pnpm run desktop:dist first`.

- [ ] **Step 7: Verify no stray npm references remain in scripts**

```powershell
Select-String -Path scripts/* -Pattern 'npm ci|npm install|npm run|npm test|package-lock' | Where-Object { $_.Line -notmatch 'repomix' }
```

Expected: no matches.

- [ ] **Step 8: Commit**

```powershell
git add scripts/one-click-local-deploy.ps1 scripts/OET-Launch.ps1 scripts/run-all-gates.sh scripts/install-admin-deps.sh scripts/setup-local-postgres.ps1 scripts/ts-prune-filter.mjs scripts/desktop-packaged-smoke.cjs
git commit -m "chore(pnpm): convert host scripts and messages to pnpm"
```

---

## Task 15: Update AI-direction docs

**Files:**
- Modify: `AGENTS.md`
- Modify: `.github/copilot-instructions.md`
- Modify: `.codex/AGENTS.md`
- Modify: `.github/instructions/validation.instructions.md`
- Modify: `.github/instructions/testing.instructions.md`
- Modify: `.github/instructions/backend.instructions.md`
- Modify: `.github/instructions/deployment.instructions.md`
- Modify: `.tools/autoskills/AGENTS.md` (scope note only — one line)

- [ ] **Step 1: Find every npm command reference in the docs**

```powershell
Select-String -Path AGENTS.md,.github/copilot-instructions.md,.codex/AGENTS.md,.github/instructions/*.instructions.md -Pattern 'npm run|npm test|npm ci|npm install|`npm`|package-lock'
```

- [ ] **Step 2: Apply the transform table to each match**

For every hit, convert the command ladder: `npm run <x>` → `pnpm run <x>`, `npm test` → `pnpm test`, `npm ci` → `pnpm install --frozen-lockfile`, `npx <bin>` → `pnpm exec <bin>`. In `AGENTS.md`/`copilot-instructions.md`/`validation.instructions.md` the validation ladder code blocks (e.g. `npm run lint`, `npm test`, `npm run build`, `npm run backend:build`, `npm run backend:test`, `npm run check:encoding`, `npm run test:e2e:smoke`) become their `pnpm run …` equivalents. Where docs reference `package-lock.json` as a tracked file, change to `pnpm-lock.yaml`.

- [ ] **Step 3: Update the autoskills scope note**

In `.tools/autoskills/AGENTS.md`, find the note stating the main repo uses npm while this subtree uses pnpm, and update it to record that the **entire repository now uses pnpm**; this subtree keeps its own pinned pnpm + supply-chain hardening. Do not change any commands inside that file.

- [ ] **Step 4: Verify docs no longer prescribe npm (except historical/migration mentions)**

```powershell
Select-String -Path AGENTS.md,.github/copilot-instructions.md,.codex/AGENTS.md,.github/instructions/*.instructions.md -Pattern 'npm run|npm test|npm ci|npm install'
```

Expected: no matches (any remaining `npm` mention should be prose explaining the migration, not a prescribed command).

- [ ] **Step 5: Commit**

```powershell
git add AGENTS.md .github/copilot-instructions.md .codex/AGENTS.md .github/instructions/validation.instructions.md .github/instructions/testing.instructions.md .github/instructions/backend.instructions.md .github/instructions/deployment.instructions.md .tools/autoskills/AGENTS.md
git commit -m "docs(pnpm): update AI-direction command ladders to pnpm"
```

---

## Task 16: Full cutover verification gate

**Files:** none (verification only)

- [ ] **Step 1: Clean install from the committed lockfile**

```powershell
Remove-Item -Recurse -Force node_modules
corepack enable
pnpm install --frozen-lockfile
```

Expected: PASS, no lockfile drift.

- [ ] **Step 2: Run the full host ladder**

```powershell
pnpm exec tsc --noEmit
pnpm run lint
pnpm test
pnpm run build
pnpm run backend:build
pnpm run backend:test
pnpm run check:encoding
```

Expected: every command PASS.

- [ ] **Step 3: Build the production Docker image (final gate before any VPS deploy)**

```powershell
docker build -f Dockerfile -t oet-web:pnpm-final --build-arg NEXT_PUBLIC_API_BASE_URL=https://example.invalid .
```

Expected: image builds through the `runner` stage.

- [ ] **Step 4: Confirm no npm artifacts or commands remain repo-wide (excluding autoskills + repomix)**

```powershell
Select-String -Path package.json,Dockerfile,tools/copilot-extension/Dockerfile,.github/workflows/*.yml,scripts/* -Pattern 'npm ci|npm install|npm run|npm test|cache: *npm|package-lock' | Where-Object { $_.Line -notmatch 'repomix' }
Test-Path package-lock.json
```

Expected: no Select-String matches; `Test-Path package-lock.json` returns `False`.

- [ ] **Step 5: Push the branch and open the PR**

```powershell
git push -u origin chore/pnpm-migration
```

Then open a PR titled `chore: migrate repo from npm to pnpm`. Do NOT deploy to the VPS from this PR. Production deploy is a separate, later, user-initiated step.

---

## Post-merge VPS deploy reminders (separate later step — NOT part of this plan)

- Override `ROUTER_IMAGE=oetwebsite-nginx-router:local` on every build/up command (the `.env.production` digest pin breaks Docker build tags).
- Never run `docker compose down -v` or remove the protected volumes `oetwebsite_oet_postgres_data` / `oetwebsite_oet_with_dr_hesham_storage`.
- Verify the production `Dockerfile` build on the VPS uses the committed `pnpm-lock.yaml` and that `corepack enable` succeeds in the build image (`node:20-alpine` ships corepack).

---

## Self-Review (completed by plan author)

- **Spec coverage:** §4.1 pin/corepack → Task 1. §4.2 `.npmrc`/`onlyBuiltDependencies` → Task 1 + Task 2. §4.3 scripts → Task 4. §4.4 Dockerfiles → Tasks 5–6. §4.5 CI (all real workflows) → Tasks 7–13. §4.6 host scripts → Task 14. §4.7 docs → Task 15. §4.8 lockfile → Task 2. §5 validation ladder → Task 3 + Task 16. §6/§7 risks/rollback → branch-per-PR + Task 16 gates. §8 VPS reminders → final section.
- **Scope correction vs spec:** spec said "6 CI workflows"; actual npm-using main-app workflows are 8 (qa-smoke, speaking-ci, oet-gapclosure-validation, mobile-ci, mobile-release, desktop-release, plus Playwright-only speaking-e2e/speaking-a11y). All covered. Dockerfiles are `node:20-alpine` (not 22) — corepack still available; noted in Task 5.
- **`.npmrc` correction:** spec assumed a new `.npmrc`; one already exists with `legacy-peer-deps=true`. Task 1 Step 2 converts it rather than creating it.
- **Placeholder scan:** the only deferred item is the exact `onlyBuiltDependencies` list, which is correctly resolved empirically from pnpm's own output in Task 2 Step 1–2 (not guessable without running install). All command transforms are concrete.
- **Consistency:** `pnpm@10.33.0`, `pnpm install --frozen-lockfile`, and `pnpm/action-setup@v4 version: 10.33.0` used identically across all tasks.
