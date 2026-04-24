# Unused Code Audit — OET Prep Platform

**Date:** 2026-04-23 (original audit) · **Closure updates:** 2026-04-24, 2026-04-25
**Tools used:** `tsc --noUnusedLocals --noUnusedParameters`, `ts-prune`, `depcheck`, manual grep

---

## Executive Summary

| Category | Count | Estimated LoC Removable | Risk |
|----------|-------|------------------------|------|
| Unused imports / locals (TS6133) | ~~127 across 92 files~~ → **0** | ✅ removed (cumulative → commit `59a5a5f`) | Zero |
| Unused type-only imports (TS6192) | ~~3~~ → **0** | ✅ removed | Zero |
| Unused type declarations (TS6196) | ~~12~~ → **0** | ✅ removed | Zero |
| Unused exports (ts-prune, excl. framework entries) | 768 total (312 in lib/components) | ⏳ Not yet triaged — requires public-API vs dead-code judgement | Low–Medium |
| Unused npm dependencies | 5 prod + 7 dev | ⏳ Pending — requires product/config call | Low |
| Missing npm dependencies | 2 → **1** | ✅ `@capacitor-community/voice-recorder` resolved via `capacitor-voice-recorder@6.0.3` install (commit `ca9b0a8`, Unit 8 step 2); `@next/env` remains and is a depcheck false positive (present in `package.json`) | Low |
| Root orphan artifact files | ~~19 files~~ → **0** | ✅ removed (commit `75efb3f`) | Zero |

**Closed so far:** TS unused-locals family (127 → 0), all 19 root orphan artifacts, and the `@capacitor-community/voice-recorder` missing-dep. **Remaining:** 768 unused ts-prune exports — now gated through the `scripts/ts-prune-filter.mjs` pipeline (Unit 7 `a584841`, `npm run unused:scan`) which reduces the actionable survivor set to **525**; 12 npm dep questions; 1 depcheck false-positive (`@next/env`).

---

## Category 1: Unused Imports & Locals (TSC TS6133/TS6192/TS6196) ✅ Closed

**Original baseline:** 142 diagnostics across 92 files.
**Current:** 0 (verified via `npx tsc --noEmit --noUnusedLocals --noUnusedParameters`).
**Final cleanup:** commit `59a5a5f` removed the last dead local (`lastMockRecorder` in `hooks/usePronunciationRecorder.test.ts`); earlier bulk removals across the session handled the remaining 126. The representative sample list below is retained for historical reference only.

### Representative Samples

| File | Line | Symbol | Code |
|------|------|--------|------|
| `app/(auth)/mfa/recovery/page.tsx` | 5 | `IconKey` | TS6133 |
| `app/achievements/certificates/page.tsx` | 6,9,10 | `LearnerSurfaceSectionHeader`, `Badge`, `Button` | TS6133 |
| `app/achievements/page.tsx` | 11 | `MotionSection` | TS6133 |
| `app/admin/analytics/cohort/page.tsx` | 4 | `Users`, `BarChart3`, `Activity` | TS6133 |
| `app/admin/analytics/content-effectiveness/page.tsx` | 4 | `Trophy`, `BarChart3`, `Clock` | TS6133 |
| `app/admin/analytics/expert-efficiency/page.tsx` | 4 | `Clock`, `BarChart3`, `TrendingUp` | TS6133 |
| `app/billing/referral/page.tsx` | 5,7 | (all imports), `Badge` | TS6192/TS6133 |
| `components/ui/progress.tsx` | 4,5 | `useReducedMotion`, (all imports) | TS6133/TS6192 |
| `components/ui/timer.tsx` | 4 | `useCallback` | TS6133 |
| `lib/api.ts` | 17,68,108,110,114 | `TranscriptLine`, `ExpertReviewRequest`, `GrammarExerciseResult`, `GrammarLessonLearner`, `GrammarOverview` | TS6196 |
| `lib/mobile/offline-crypto.ts` | 16 | `KEY_LENGTH` | TS6133 |
| `lib/rulebook/ai-prompt.ts` | 27 | `criticalRules` | TS6133 |

### Files in test code (flagged as "test-only", lower priority)

| File | Symbol |
|------|--------|
| `hooks/usePronunciationRecorder.test.ts` | `lastMockRecorder` |
| `lib/__tests__/admin-permissions.test.ts` | `href` |
| `lib/__tests__/use-expert-auth.test.tsx` | `mockRouter` |
| `lib/csv-export.test.ts` | `appendChildSpy`, `removeChildSpy` |
| `middleware.test.ts` | `vi` (vitest global — false positive) |
| `tests/e2e/fixtures/api-auth.ts` | `WritingSubmitResponse` |

---

## Category 2: Unused Exports (ts-prune)

768 total unused exports detected at the 2026-04-23 baseline. After excluding Next.js framework entry points (`page.tsx`, `layout.tsx`, `route.ts`, etc.) and config files, **312 were in `lib/` and `components/`**. As of 2026-04-25, the Pass 1 filter script `scripts/ts-prune-filter.mjs` (shipped in `a584841`, Unit 7) has been wired into `npm run unused:scan` (Unit 2 `8f8986f`); the post-filter actionable survivor count at HEAD `ca9b0a8` is **525** — see `docs/TS-PRUNE-TRIAGE.md` for the full methodology and the per-bucket action plan. No `lib/` / `components/` export has been deleted yet; Pass 2 is gated on per-file `rg` confirmation.

### High-Confidence Unused Exports in `lib/`

| File | Export | Confidence |
|------|--------|------------|
| `lib/admin-permissions.ts:20` | `AdminPermissionValue` | Medium (may be used by backend types) |
| `lib/admin.ts:728` | `getAdminPermissionsData` | Medium |
| `lib/ai-management-api.ts:328` | `sweepExpiredCredits` | Medium (API function — may be WIP) |
| `lib/content-upload-api.ts:216` | `abortUpload` | Medium (API function) |
| `lib/mock-admin-data.ts` | 13 exports (`mockContentLibrary`, `mockTaxonomy`, `mockUsers`, `mockFlags`, `mockAuditLogs`, `mockCriteria`, `mockAIConfigs`, `mockReviewOps`, `mockBillingPlans`, `mockReviewOpsKPIs`, `mockQualityAnalytics`, `mockBillingInvoices`, `mockContentRevisions`) | High — mock data |
| `lib/mock-data.ts` | 25+ exports (`MOCK_USER`, `PROFESSIONS`, `MOCK_STUDY_PLAN`, `MOCK_WRITING_TASKS`, `MOCK_WRITING_CHECKLIST`, `MOCK_READING_TASKS`, `MOCK_LISTENING_TASKS`, `MOCK_REPORTS`, `MOCK_BILLING`, `MOCK_DIAGNOSTIC_SESSION`, etc.) | High — mock data |
| `lib/mock-expert-data.ts:26` | `MOCK_REVIEW_QUEUE` | High — mock data |
| `lib/mock-expert-data.ts:102` | `MOCK_WRITING_REVIEW_DETAIL` | High — mock data |

> **⚠ CAUTION:** Mock data exports may be consumed by test files not detected by ts-prune. Verify each mock export has zero references in `**/*.test.*` before removal.

### Unused Exports in app/ pages

Many `page.tsx` default exports are framework entry points and are **false positives**. All 217 route pages are safe — do NOT remove.

---

## Category 3: Unused npm Dependencies

### Production (`dependencies`) — depcheck flagged

| Package | Status | Recommendation |
|---------|--------|----------------|
| `@capacitor/filesystem` | No direct import found | **Keep** — used via Capacitor runtime on mobile |
| `@google/genai` | No import found | **Investigate** — may be backend-only or future use |
| `autoprefixer` | Used by PostCSS config | **False positive** — keep |
| `class-variance-authority` | No direct import | **Investigate** — may be used in component variants |
| `postcss` | Used by build tooling | **False positive** — keep |

### Dev Dependencies — depcheck flagged

| Package | Status | Recommendation |
|---------|--------|----------------|
| `@capacitor/android` | Capacitor platform | **False positive** — keep |
| `@capacitor/ios` | Capacitor platform | **False positive** — keep |
| `@tailwindcss/postcss` | PostCSS plugin | **False positive** — keep |
| `@tailwindcss/typography` | Tailwind plugin | **False positive** — keep |
| `electron-builder` | Build tool | **False positive** — keep |
| `tailwindcss` | CSS framework | **False positive** — keep |
| `tw-animate-css` | Animation plugin | **Investigate** — verify usage in globals.css or Tailwind config |

### Missing Dependencies

| Package | Used In |
|---------|---------|
| ~~`@capacitor-community/voice-recorder`~~ | ~~`lib/mobile/pronunciation-recorder.ts`~~ — ✅ resolved `ca9b0a8` (replaced with `capacitor-voice-recorder@6.0.3`; import renamed; obsolete `.d.ts` shim deleted; `@ts-expect-error` suppression removed) |

---

## Category 4: Root Orphan Files

19 files at repo root that are dev/debug artifacts:

| File | Size | In .gitignore? | Classification | Action |
|------|------|----------------|----------------|--------|
| `fix_routes.py` | — | Yes | Ad-hoc script | **Safe to delete** |
| `nul` | — | Yes | Windows artifact | **Safe to delete** |
| `pages_rel.txt` | — | No | Debug output | **Safe to delete** |
| `tsc.out.txt` | — | No | TSC output dump | **Safe to delete** |
| `vitest_dot.txt` | — | No | Test output dump | **Safe to delete** |
| `vitest_full_check.txt` | — | No | Test output dump | **Safe to delete** |
| `vitest_full2.txt` | — | No | Test output dump | **Safe to delete** |
| `vitest_json.json` | — | No | Test JSON dump | **Safe to delete** |
| `lint_output.txt` | — | No | Lint output dump | **Safe to delete** |
| `audit-01-sign-in-snapshot.md` | — | No | Audit snapshot | **Safe to delete** |
| `backend-bootstrap.json` | — | No | Backend debug dump | **Safe to delete** |
| `backend-dashboard.json` | — | No | Backend debug dump | **Safe to delete** |
| `backend-health.json` | — | No | Backend debug dump | **Safe to delete** |
| `backend.err` | — | No | Error log | **Safe to delete** |
| `frontend.err` | — | No | Error log | **Safe to delete** |
| `audit_eslint.txt` | — | No | This audit's artifact | Delete after audit |
| `audit_lint.txt` | — | No | This audit's artifact | Delete after audit |
| `ts-prune-output.txt` | — | No | This audit's artifact | Delete after audit |
| `depcheck-output.json` | — | No | This audit's artifact | Delete after audit |

---

## Safe Cleanup Plan

### Phase 1: Zero Risk — Root Artifacts & Unused Imports ✅ Executed

**Outcome:** both sub-phases shipped. Root orphan sweep landed in commit `75efb3f`; unused-import/local removals landed incrementally across the session with the final diagnostic closed in commit `59a5a5f`. Snippets below retained as a how-to reference.

**Effort:** ~30 minutes | **Risk:** None

#### Step 1a: Delete root orphan files

```powershell
cd "c:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App"
git rm -f fix_routes.py nul pages_rel.txt tsc.out.txt vitest_dot.txt `
  vitest_full_check.txt vitest_full2.txt vitest_json.json lint_output.txt `
  audit-01-sign-in-snapshot.md backend-bootstrap.json backend-dashboard.json `
  backend-health.json backend.err frontend.err
# Also clean up audit artifacts (not tracked):
Remove-Item audit_eslint.txt, audit_lint.txt, ts-prune-output.txt, `
  depcheck-output.json, depcheck.err.log -ErrorAction SilentlyContinue
```

#### Step 1b: Remove unused imports (TSC TS6133/TS6192)

Run to identify and manually remove (no ESLint autofix for unused imports in flat config):

```powershell
npx tsc --noEmit --noUnusedLocals --noUnusedParameters --pretty false 2>&1 | `
  Select-String "TS6133|TS6192|TS6196"
```

**Before/After Example 1** — `app/achievements/certificates/page.tsx`:
```diff
- import { LearnerSurfaceSectionHeader } from '@/components/domain/LearnerSurfaceSectionHeader';
- import { Badge } from '@/components/ui/badge';
- import { Button } from '@/components/ui/button';
  import { ... } from 'lucide-react';
```

**Before/After Example 2** — `components/ui/timer.tsx`:
```diff
- import { useState, useEffect, useCallback, useRef } from 'react';
+ import { useState, useEffect, useRef } from 'react';
```

**Before/After Example 3** — `components/ui/progress.tsx`:
```diff
- import { useReducedMotion } from '@/lib/motion';
- import { motion, AnimatePresence } from 'motion/react';
```

**Before/After Example 4** — `lib/api.ts` (unused type exports):
```diff
- export interface TranscriptLine { ... }
- export interface ExpertReviewRequest { ... }
- export interface GrammarExerciseResult { ... }
- export interface GrammarLessonLearner { ... }
- export interface GrammarOverview { ... }
```

**Before/After Example 5** — `lib/mobile/offline-crypto.ts`:
```diff
- const KEY_LENGTH = 256;
```

#### Verification (Phase 1)

```powershell
npx tsc --noEmit            # Must pass (0 errors)
npm run lint                 # Must pass (0 errors/warnings except react-hooks)
npm test                     # Must be 77/77 files, 304/304 tests
npm run build                # Must compile 169+ pages
```

---

### Phase 2: Low Risk — Unused Type Declarations & Test Locals ✅ Executed

Rolled up into Phase 1 closure. TS6196 count 12 → 0; test-local strays (`lastMockRecorder`, `appendChildSpy`, `removeChildSpy`, `href`, `mockRouter`) all removed. `middleware.test.ts` `vi` was correctly identified as a Vitest global (left untouched).

**Effort:** ~20 minutes | **Risk:** Low

- Remove 12 unused type declarations (TS6196) from `lib/api.ts` and `tests/e2e/fixtures/api-auth.ts`
- Remove unused test variables (`lastMockRecorder`, `appendChildSpy`, `removeChildSpy`, `href`, `mockRouter`)
- Skip `middleware.test.ts` `vi` — it's a vitest global (false positive)

#### Verification (Phase 2)

```powershell
npx tsc --noEmit --noUnusedLocals --noUnusedParameters  # Target: 0 TS6133/TS6192/TS6196
npm test                     # All 304 tests pass
```

---

### Phase 3: Medium Risk — Unused Mock Data Exports

**Effort:** ~1 hour | **Risk:** Medium — requires verifying each export against test imports

Target files:
- `lib/mock-data.ts` — 25+ potentially unused exports
- `lib/mock-admin-data.ts` — 13 potentially unused exports
- `lib/mock-expert-data.ts` — 2+ potentially unused exports

**Process:**
1. For each flagged export, grep across all `**/*.test.*` and `**/*.spec.*` files
2. If zero references found anywhere, remove the export and its data
3. If referenced only in tests, keep but mark as test-only

#### Verification (Phase 3)

```powershell
npx tsc --noEmit
npm test
npm run build
```

---

### Phase 4: Needs Owner Input — API Functions & Dependencies

**Effort:** Variable | **Risk:** Medium–High

These may be WIP, future-use, or consumed by external systems:

| Item | Reason to Investigate |
|------|----------------------|
| `lib/ai-management-api.ts:sweepExpiredCredits` | No frontend caller — may be admin-only or backend-triggered |
| `lib/content-upload-api.ts:abortUpload` | No frontend caller — may be WIP upload cancellation |
| `lib/admin.ts:getAdminPermissionsData` | May be used by future admin pages |
| `@google/genai` dependency | No import found — verify if backend uses it |
| ~~`@capacitor-community/voice-recorder`~~ | ✅ Resolved 2026-04-25 (`ca9b0a8`): installed `capacitor-voice-recorder@6.0.3` (pin chosen for Capacitor 6 peer compatibility; the community package was renamed upstream from v7). Dynamic import in `lib/mobile/pronunciation-recorder.ts:44` renamed accordingly; obsolete ambient `.d.ts` shim deleted; `@ts-expect-error` removed. Mobile recorder suite 45/45 green. |
| `@next/env` | Listed as missing — used in `capacitor.config.ts` |

> **⚠ MISSION CRITICAL exclusions:** Per AGENTS.md, scoring (`lib/scoring.ts`), rulebook (`lib/rulebook/`), AI gateway, and content upload core code must NEVER be flagged as unused even if ts-prune lists them — they are indirect/critical-path dependencies.

---

## Verification Checklist (Run After Each Phase)

- [x] `npx tsc --noEmit` — 0 errors
- [x] `npx tsc --noEmit --noUnusedLocals --noUnusedParameters` — 0 TS6133/TS6192/TS6196
- [x] `npm run lint` — 0 errors (repo-wide `exhaustive-deps` disable count 6 → 0 closes related debt)
- [x] Unit test suites exercised for touched files (dashboard-home 1/1, pronunciation recorder 6/6)
- [ ] `npm test` — full 664/664 sweep (spot-checked per-file; full sweep pending next autonomous-safe window)
- [ ] `npm run build` — compiles 169+ pages successfully (not re-run this session)
- [ ] `npm run backend:build` — builds without errors
- [ ] `npm run backend:test` — all backend tests pass
- [x] `git diff --stat` — reviewed before every commit
