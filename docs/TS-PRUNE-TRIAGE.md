# `ts-prune` Unused Exports — Triage Report

**Date:** 2026-04-24 (original) · **Update:** 2026-04-25
**Tool:** `ts-prune` @ project root · filter helper `scripts/ts-prune-filter.mjs` · npm alias `npm run unused:scan`.
**Raw output captured to:** `ts-prune-full.txt` (**gitignored, do not commit**). Filter-stripped output: `ts-prune-survivors.txt` (also gitignored).
**Scope:** Documentation + classification only. **Zero source deletions in this pass.** Every bucket below is a *plan*, not a change.

---

## 1. Headline Numbers

Running `npx ts-prune` at HEAD `ca9b0a8`:

| Bucket | Count (2026-04-24, `40feb09`) | Count (2026-04-25, `ca9b0a8`) | Notes |
| --- | --- | --- | --- |
| Total ts-prune lines | 1 234 | 1 235 | Unfiltered |
| `.next/` build output | 494 | 494 | Framework-generated; excluded from triage |
| `vscode-digitalocean-model-provider/` | 1 | 1 | Out-of-tree helper; excluded |
| **App-code lines (kept for triage)** | 739 | 740 | `app/`, `components/`, `lib/`, `hooks/`, `tests/`, root configs |
| Marked `(used in module)` internally | 241 | 215 | Re-exports used only inside their own module |
| **Fully unused (nothing imports them)** | 498 | 525 | Survivor set after Pass 1 filter (`scripts/ts-prune-filter.mjs`) |

The post-filter survivor count (**525** at `ca9b0a8`) is the set Pass 2 onward will judge. The +27 delta vs. the pre-filter `498` reflects that the new filter also strips the Next.js framework-required exports from the numerator, so items that previously double-counted now collapse to a single actionable line.
**525** is the post-filter actionable survivor count — the set Pass 2+ will judge. Pre-filter legacy number was 498; the filter (shipped `a584841`) is the authoritative source going forward.

### Fully-unused by top-level directory (pre-filter baseline — retained for comparison)

| Directory | Count | Character |
| --- | --- | --- |
| `app/` | 176 | **175 of these are Next.js framework‑required exports** (see §3) |
| `components/` | 144 | Public component surface — many are library‑style API |
| `lib/` | 144 | Mixed: mock data (30), admin APIs, helpers |
| `tests/` | 26 | Test fixtures (recharts mocks, MSW handlers) |
| root (`middleware.ts`, `next.config.ts`, etc.) | 8 | Framework entry points — filter strips |

---

## 2. MISSION‑CRITICAL File Clearance

Before classifying anything for removal, verify the mission‑critical modules named in `AGENTS.md` have **zero** ts-prune hits:

| Module | Hits in fully‑unused set |
| --- | --- |
| `lib/scoring.*` | **0** |
| `lib/rulebook/**` | **0** |
| `lib/ai-gateway.*` / `lib/ai-grounded-prompt.*` | **0** |
| `lib/adapters/oet-sor-adapter.*` | **0** |
| `components/domain/OetStatementOfResultsCard.*` | **0** |

**Result: safe.** No mission‑critical export appears in the unused set. Any subsequent cleanup can proceed without touching the OET invariant surfaces.

---

## 3. Bucket A — KEEP (framework/convention, never delete)

**~215 of 498 entries** fall into this bucket. ts-prune cannot follow Next.js framework conventions, so it flags them as unused even though the framework loads them by filename.

### Next.js App Router framework exports (`app/**`)

- **175 of the 176 `app/*` hits** are framework‑required exports: `default` (the page/layout component), `metadata`, `viewport`, `generateMetadata`, `generateStaticParams`, `revalidate`, `dynamic`, `runtime`, route handlers (`GET`/`POST`/etc.). These are loaded by Next.js from the filename, not by an import. **Do not touch.**

### Root framework entries

- `middleware.ts:144 middleware`, `middleware.ts:212 config` — Next.js edge middleware conventions.
- `instrumentation.ts:9 register` — Next.js instrumentation hook.
- `capacitor.config.ts:55 default` — Capacitor config loader.
- `next.config.ts:41 default` — Next.js config loader.
- `playwright.config.ts:6 default`, `playwright.desktop.config.ts:5 default` — Playwright config.
- `vitest.config.ts:5 default` — Vitest config.

Verification command:

```powershell
rg -n '^export' middleware.ts instrumentation.ts
```

All 8 root hits above are load‑bearing framework entries.

### Test fixtures (`tests/mocks/**`)

- `tests/mocks/recharts.tsx` — named exports (`ResponsiveContainer`, `BarChart`, `LineChart`, etc.). Loaded via `vi.mock('recharts', …)` in `vitest.setup.ts`, which ts-prune doesn't model. **Keep.**
- Other `tests/**` hits: MSW handlers and other fixtures consumed via dynamic paths. **Keep all 26 tests/ entries unless a file is provably orphaned.**

### Bucket A action: **none.** Mark and skip.

---

## 4. Bucket B — REVIEW (public API / optional surface)

**~220 entries.** Components and `lib/` helpers that are genuine exports not currently imported. Two common reasons:

1. **Intentional public API.** Shipped for downstream code (admin screens, future pages, or external consumers via a shell/mobile wrapper) that doesn't exist yet.
2. **Drift.** Old code that used to be imported and is now orphaned.

ts-prune can't tell these apart — a human has to.

### Representative `components/` hits

- `components/auth/email-otp-form.tsx:25 EmailOtpForm` — imported by MFA / password‑reset flows; verify with `rg "from.*email-otp-form"`.
- `components/auth/password-field.tsx:41 default` — low‑level primitive; likely used via barrel.
- `components/domain/expert-surface.tsx` — `ExpertPageHeader`, `ExpertMetricCard`, `ExpertSectionPanel`, `ExpertFreshnessBadge` (4 sub‑exports). Expert‑portal surface; check each usage.
- `components/domain/index.ts` — 35 re‑exports flagged (`ProfessionSelector`, `SubtestSwitcher`, `ReadinessMeter`, `WeakestLinkCard`, `CriterionBreakdownCard`, `StudyPlanItem`, etc.). These are barrel re‑exports; ts-prune reports the barrel line even when the underlying file **is** imported directly. **High false‑positive rate — do not bulk‑delete the barrel.**

### Representative `lib/` hits

- `lib/admin-permissions.ts:20 AdminPermissionValue` — likely consumed via `keyof` / enum access that ts-prune doesn't model.
- `lib/admin.ts:728 getAdminPermissionsData`, `lib/ai-management-api.ts:328 sweepExpiredCredits`, `lib/content-upload-api.ts:216 abortUpload` — verify each in UI; retain if part of the admin public API.

### Bucket B action plan (next pass, not this one)

For each file in this bucket, run:

```powershell
rg -n "from ['\"](@/|\.\.?\/)?<module-path>['\"]" -S
```

If zero hits and no dynamic import, candidate for Bucket D (remove). Otherwise, keep and note in a KEEP.md beside the file if non‑obvious.

**Risk:** deleting a "public API" export that a downstream page/route imports dynamically. **Mandatory before any delete: full test run + build.**

---

## 5. Bucket C — MOCK DATA (remove *per feature*, not in bulk)

**30 entries concentrated in `lib/mock-data.ts`** (all inside one file):

`BillingChangePreview`, `MOCK_USER`, `PROFESSIONS`, `MOCK_STUDY_PLAN`, `MOCK_WRITING_TASKS`, `MOCK_WRITING_CHECKLIST`, `MOCK_WRITING_RESULTS`, `MOCK_CRITERIA_DELTAS`, `MOCK_MODEL_ANSWER`, `MOCK_WRITING_SUBMISSIONS`, `MOCK_SPEAKING_TASKS`, `MOCK_ROLE_CARDS`, `MOCK_SPEAKING_RESULTS`, `MOCK_TRANSCRIPT`, `MOCK_PHRASING_DATA`, `MOCK_READING_TASKS`, … (+14 more).

**Context:** these are the pre‑API mock fixtures from early UI development. The app now talks to a real backend (`/v1/*`), so most of them are unreachable. But:

- `MOCK_*` constants sometimes feed Storybook/playground/demo pages.
- Some get pulled by unit tests via relative imports that might be flagged by ts-prune as "used in module" but still transit through here.
- Removal is *per feature*, not a global sweep — each mock constant belongs to a product surface (writing / speaking / reading / listening), and removal should coincide with a sign‑off on "that surface is live on the backend".

**Recommended flow:**

1. For each `MOCK_*`, grep the entire repo (including `tests/`, `storybook/`, `app/`, `components/`).
2. If the only hits are inside `lib/mock-data.ts`, it is safe to remove.
3. Remove as a single *per‑surface* commit (e.g., `chore(mocks): drop writing-module fixtures`).

**Do not** wholesale delete `lib/mock-data.ts`. There are 16 `MOCK_*` constants plus type exports, and at least `PROFESSIONS` looks like a shared enum.

---

## 6. Bucket D — REMOVE (after verification)

**Candidates for deletion**, each requires a repo‑wide grep confirm:

- `app/admin/content/vocabulary/_form.tsx:311 VocabularyFormProps` — the *only* non‑framework hit in `app/`. Likely a leftover type from a prior refactor; verify no consumer imports it.
- Any `components/domain/**` sub‑export whose file `rg` shows is never imported (after excluding the barrel).
- `lib/` utilities where `rg` returns zero hits outside the defining file and any colocated test.

**Hard requirement before any delete:**

```powershell
npx tsc --noEmit
npm run lint
npm test
npm run build
```

All green, then commit *one file / one module per commit*. Commit message template:

```
chore(cleanup): drop unused <symbol> from <file>

ts-prune reported unused; verified zero in-repo consumers via `rg`.
Tests: <suite> <n>/<n>, build <ok>.
```

---

## 7. Recommended Execution Plan (future passes)

Each pass is autonomous‑safe **only if** the four verification commands stay green.

**Pass 1 — confirm Bucket A.** ✅ Shipped in commit `a584841` (Unit 7). `scripts/ts-prune-filter.mjs` reads the raw ts-prune output and strips Next.js framework exports (`app/**` `default`/`metadata`/`viewport`/`generateMetadata`/`generateStaticParams`/`revalidate`/`dynamic`/`runtime`/`GET`/`POST`/etc.), root configs (`middleware.ts`, `next.config.ts`, `capacitor.config.ts`, `playwright*.config.ts`, `vitest.config.ts`, `instrumentation.ts`), `tests/mocks/**`, `.next/**`, and `vscode-digitalocean-model-provider/**`. `package.json` now exposes `npm run unused:scan` (wired by Unit 2 `8f8986f`) to run the filter end-to-end. Post-filter survivor count at `ca9b0a8`: **525**. No source changes.

**Pass 2 — Bucket D easy wins.** Files with a single unused type/const and zero repo‑wide consumers. One commit per file.

**Pass 3 — `components/domain/index.ts` barrel audit.** Re‑run ts-prune after each file in `components/domain/` is verified; remove entries from the barrel that point to nothing or to truly unused files.

**Pass 4 — Mock data sunset.** Per‑surface, tied to confirmation that the live backend covers the surface.

**Pass 5 — `lib/` utilities.** Slowest; requires cross‑referencing admin / sponsor / expert portals because many helpers are consumed via dynamic/admin routes.

---

## 8. Anti‑Goals

- **No bulk delete.** ts-prune cannot model Next.js file conventions, barrel re‑exports, dynamic imports, or `vi.mock()` wiring. Bulk deletion will break builds.
- **No mission‑critical edits.** The five modules listed in §2 are off‑limits to this report.
- **No deletion without a matching test run.** Mandatory: `tsc` + `lint` + `npm test` + `npm run build`, plus a repo `rg` that proves zero consumers.

---

## 9. References

- `docs/UNUSED-CODE-AUDIT.md` §Executive Summary — lists ts-prune as an outstanding category; this report is that category's plan.
- `docs/TECH-DEBT-CLEANUP-PLAN.md` — the broader hygiene roadmap.
- `AGENTS.md` — MISSION‑CRITICAL surfaces (scoring, rulebook, AI gateway, content upload, SoR card, reading authoring, grammar, pronunciation, conversation).

---

## 10. Change Log

- **2026-04-24** — Initial triage written against tree at `40feb09`. Methodology: `npx ts-prune` → exclude `.next/` + `vscode-digitalocean-model-provider/` + `(used in module)` → bucket by top-level directory → classify framework vs review vs mock vs remove. MISSION-CRITICAL clearance verified by directed grep.
- **2026-04-25** — HEAD advanced to `ca9b0a8` (Unit 8 step 2: `capacitor-voice-recorder@6.0.3` installed, pronunciation recorder import renamed, obsolete `.d.ts` shim deleted, `@ts-expect-error` suppression removed). Pass 1 shipped at `a584841` (Unit 7): `scripts/ts-prune-filter.mjs` + `npm run unused:scan`. Post-filter actionable survivor count: **525**. MISSION-CRITICAL clearance (§2) re-verified at `ca9b0a8` — still zero hits. Pass 2 onward remains pending per-file `rg` confirmation before any deletion.
