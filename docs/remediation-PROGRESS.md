# Remediation Progress Tracker

## Status: Implementation Phase

| # | Task | Status | Notes |
|---|------|--------|-------|
| 1 | PRD written | Done | docs/remediation-PRD.md |
| 2 | Gather exact line numbers for all gaps | Done | |
| 3 | Fix diagnostic hardcoded IDs | Done | Added `fetchDiagnosticTaskId` API with backend-first + legacy fallback. Updated all 4 diagnostic pages (writing, speaking, reading, listening) to use dynamic IDs. Updated `DiagnosticSubTest` type to carry `contentId`. |
| 4 | Fix mock player data-driven launches | Done | Already data-driven in current code; `startMockSection` returns `launchRoute` from backend. No hardcoded IDs found in player. |
| 5 | Remove Speaking AI mode from UI | Done | Already resolved in current code; roleplay and task pages only expose `self` and `exam` modes. Any `?mode=ai` is downgraded to `self`. |
| 6 | Fix encoding artifacts across affected files | Done | Fixed `&quot;` rendering issue in `app/(auth)/terms/page.tsx` lead strings. Reverted JSX text `&quot;` to proper entities (React decodes these correctly in JSX text). No smart-quote corruption found in the audited app files. |
| 7 | Migrate lib/mock-data.ts | Done | Updated header comment to clarify these are canonical domain types consumed by real API responses. Removed misleading "Replace with real API" transitional wording. |
| 8 | Fix pre-existing admin billing test | Done | `app/admin/non-editor-pages.test.tsx` missing `useAuth` mock and billing permissions. Added mocks; test passes. |
| 9 | Audit and harden undocumented routes | Done | All undocumented routes build successfully and have real implementations (peer-review, test-day, score-calculator, vocabulary, strategies, grammar, conversation, community, etc.). No placeholders found. |
| 10 | tsc --noEmit | Done | 0 errors |
| 11 | npm run lint | Done | 0 errors, 0 warnings |
| 12 | npm test | Done | 153/153 files passed, 936/936 tests passed |
| 13 | npm run build | Done | 0 errors, all 200+ pages compiled successfully |

## Last Updated
2026-05-05
