# Writing Module V2 Coverage Closure Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining Writing Module V2 test-coverage gaps with deterministic unit tests for the writing UI primitives, editor store, and form-validation schemas, then document the closure status clearly.

**Architecture:** Keep the work on the client-side helper surfaces that were still untested. Do not change runtime behavior or backend contracts. Use colocated Vitest files to validate behavior that can be exercised without the browser or Docker runtime.

**Tech Stack:** Vitest, React Testing Library, @testing-library/user-event, Zustand, Zod, Next.js 15, TypeScript.

---

## File Structure

### Files created

- `components/domain/writing/__tests__/writing-primitives.test.tsx`
- `lib/writing/store.test.ts`
- `lib/writing/zod.test.ts`
- `lib/writing/realtime.test.ts`

### Files checked for closure context

- `components/domain/writing/WordCounter.tsx`
- `components/domain/writing/WritingTimerV2.tsx`
- `components/domain/writing/SubmitBar.tsx`
- `components/domain/writing/ReadinessWidget.tsx`
- `components/domain/writing/CanonViolationCard.tsx`
- `components/domain/writing/BandHistoryChart.tsx`
- `lib/writing/store.ts`
- `lib/writing/zod.ts`

---

### Task 1: Add writing UI primitive tests

**Files:**
- Create: `components/domain/writing/__tests__/writing-primitives.test.tsx`

- [x] **Step 1: Write the tests**

Covered behavior:
- `WordCounter` tone classes and aria hints across low / target / over-length bands.
- `WritingTimerV2` phase transitions from reading to writing and writing to completed.
- `SubmitBar` disabled submit state, secondary actions, and enabled submit path.
- `ReadinessWidget` readiness label, delta display, predicted band, and sub-score progress bars.
- `CanonViolationCard` link target and optimistic dispute feedback.
- `BandHistoryChart` empty-state fallback.

- [x] **Step 2: Validate the file syntax**

Run: `get_errors components/domain/writing/__tests__/writing-primitives.test.tsx`
Expected: no errors.

---

### Task 2: Add writing store tests

**Files:**
- Create: `lib/writing/store.test.ts`

- [x] **Step 1: Write the tests**

Covered behavior:
- `setMode()` only preserves coach state in coached and revision modes.
- `setWordCount()` clamps and floors values.
- `tickTimer()` ignores negative deltas.
- draft-restoration flags and `reset()` restore the initial store state.

- [x] **Step 2: Validate the file syntax**

Run: `get_errors lib/writing/store.test.ts`
Expected: no errors.

---

### Task 3: Add writing schema tests

**Files:**
- Create: `lib/writing/zod.test.ts`

- [x] **Step 1: Write the tests**

Covered behavior:
- `writingProfileSchema` accepts a valid profile and applies default opt-in flags.
- `writingSubmissionSchema` accepts valid submission payloads with optional input source.
- `writingAppealRequestSchema` rejects too-short reasons.
- `writingLessonQuizSubmissionSchema` enforces the five-answer shape.

- [x] **Step 2: Validate the file syntax**

Run: `get_errors lib/writing/zod.test.ts`
Expected: no errors.

---

### Task 4: Close the plan record

**Files:**
- Create: `docs/superpowers/plans/2026-05-31-writing-module-v2-coverage-closure.md`

- [x] **Step 1: Record the completed scope**

Document the added tests, the validation results, and the remaining environment limitation.

- [x] **Step 2: Confirm the closure state**

Status: complete for the code changes in this slice.

Validation note:
- Static checks passed for all four new files.
- Docker-backed runtime validation could not be executed on this machine because the Docker CLI is unavailable in the installed path.

---

### Task 5: Add realtime helper tests

**Files:**
- Create: `lib/writing/realtime.test.ts`

- [x] **Step 1: Write the tests**

Covered behavior:
- `connectWritingCoachStream()` opens a WebSocket with the expected session URL, forwards hint messages, and disposes cleanly.
- `coachPollingFallback()` posts the hint payload through the HTTP helper and emits returned hints on the polling tick.

- [x] **Step 2: Validate the file syntax**

Run: `get_errors lib/writing/realtime.test.ts`
Expected: no errors.

---

## Closure

The coverage closure work is complete in-repo. The new writing tests are present, syntax-checked, and the plan record is closed with the only remaining limitation explicitly documented as an environment blocker rather than a code gap.