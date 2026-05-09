# OET Platform Product-Manual Remediation PRD

## Scope
Full end-to-end remediation of all gaps identified in the 7 product-manual documents against the live codebase (Option A — Complete Remediation).

## Gaps Identified

### 1. Diagnostic Hardcoded Task IDs (P0)
- `app/diagnostic/writing/page.tsx` → `DIAGNOSTIC_WRITING_TASK_ID = 'wt-001'`
- `app/diagnostic/speaking/page.tsx` → `DIAGNOSTIC_SPEAKING_TASK_ID = 'st-001'`
- `app/diagnostic/reading/page.tsx` → `DIAGNOSTIC_READING_TASK_ID = 'rt-001'`
- `app/diagnostic/listening/page.tsx` → `DIAGNOSTIC_LISTENING_TASK_ID = 'lt-001'`
**Fix**: Fetch diagnostic task IDs dynamically from a backend diagnostic-config endpoint based on learner profession/goals. Create fallback to admin-configured content library if API unavailable.

### 2. Mock Player Section Launches (P0)
- `app/mocks/player/[id]/page.tsx` uses static/hardcoded section-to-route mapping.
**Fix**: Drive section launch paths entirely from the mock session config returned by the backend. Remove any hardcoded task ID strings.

### 3. Speaking AI Mode Downgrade (P0)
- `/speaking/roleplay/[id]` and `/speaking/task/[id]` expose an `ai` mode that is not functionally available and is internally downgraded to `self`.
**Fix**: Remove the `ai` mode option from mode-selection UI. Keep only `self` and `exam`. Preserve backend enum value if needed for future compatibility.

### 4. UI Encoding Artifacts (P1)
- 49 files across `app/` contain visible encoding artifacts such as escaped HTML entities or misdecoded curly punctuation rendered as literal text.
**Fix**: Replace with proper Unicode characters or JSX escaping. Audit each file and apply targeted string replacements.

### 5. lib/mock-data.ts Transitional State (P1)
- Still used as a source of truth for type imports across the codebase.
- Contains mock fallback data and transitional TODO comments.
**Fix**:
  a. Extract all real domain types into `lib/types/` files.
  b. Update all import sites to use new type locations.
  c. Strip mock fallback objects; keep only type definitions.
  d. Add clear JSDoc on any remaining shared types.

### 6. Undocumented Routes Audit & Harden (P2)
Routes found in `app/` but NOT listed in the product-manual route inventory:
- `strategies/[id]`
- `admin/bulk-operations`
- `admin/content/papers/import`
- `admin/content/publish-requests`
- `admin/content/vocabulary/ai-draft`
- `admin/community`
- `admin/permissions`
- `admin/roles`
- `expert/onboarding`
- `expert/ask-an-expert`
- `expert/annotation-templates`
- `expert/mobile-review`
- `expert/private-speaking`
- `billing/score-guarantee`
- `community/threads/[threadId]`
- `conversation/[sessionId]/results`
- `grammar/error.tsx` (error boundary?)
- `mocks/bookings`
- `mocks/speaking-room/[bookingId]`
- `peer-review`
- `private-speaking` + `/private-speaking/success`
- `review`
- `score-calculator`
- `test-day`
- `vocabulary/*` (browse, flashcards, terms/[termId])
**Fix**: For each, determine if it is (a) a real feature needing documentation, (b) a legacy/placeholder page that should be removed, or (c) an error boundary / utility route. Remove placeholders, harden real features, add to product manual.

## Success Criteria
- `tsc --noEmit` returns 0 errors.
- `npm run lint` returns 0 errors/warnings.
- `npm test` passes 113/113 files, 675/675 tests.
- `npm run build` compiles all pages successfully.
- No hardcoded diagnostic IDs remain in `app/diagnostic/*`.
- No `ai` mode selectable in Speaking UI.
- All 49 encoding-artifact files are clean.
- `lib/mock-data.ts` is either removed or reduced to zero runtime data.

## Orchestration Plan
Use Ralph/OmO-style loops:
1. **Research Phase**: Gather exact line numbers and file contents for each gap.
2. **Batch Edit Phase**: Apply parallel multi_edit and edit operations.
3. **Verification Phase**: Run tsc, lint, tests, build after each batch.
4. **Reconciliation Phase**: Fix any regressions; update PROGRESS.md.
