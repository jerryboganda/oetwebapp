# Listening Exam — Final Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the authoritative final specification for the OET Listening module: unify the candidate UI on the black OET-style audio transport player, enable gesture-chained sequential autoplay ($A1 \to A2 \to B \to C1 \to C2$), eliminate post-submit drill recommendations, promote Transcript Review to the top of results, clean Part B/C questions of sentinels and document artifacts, remove the Stem button, and execute the 20-test semantic-cue audio splitting pipeline.

**Architecture:** 
- Frontend: Unified `ListeningAudioTransport` component across `app/listening/paper/[paperId]` and `app/listening/player/[id]`; cleaned `BCQuestionRenderer` (no Stem button, Flag only); restructured `app/listening/results/[id]` (Score $\to$ Transcript Review $\to$ Detailed Question Analysis).
- Backend & Mapping: Regex sanitization in `ListeningLearnerService.MapRelationalQuestion` stripping `PAGE \d+`, `====== PAGE \d+ ======`, and sentinel placeholders (`See PDF` / `CPDF`).
- Audio: Python/FFmpeg segmentation script (`scripts/materials/split-listening-benchmark-audio.py`) dividing all 20 source audios into A1, A2, B, C1, C2 at exact spoken transition cues with automated boundary verification.

**Tech Stack:** Next.js 16 (App Router), React 19, TypeScript, Tailwind CSS v4, ASP.NET Core Minimal API (.NET 10), EF Core PostgreSQL, Python 3.9, FFmpeg.

## Global Constraints

- Full Listening Exam, Part A, Part B, Part C, C1, and C2 must all use the identical black OET-style audio player (`ListeningAudioTransport`).
- Autoplay is mandatory: candidate does not click Play on start or between sections ($A1 \to A2 \to B \to C1 \to C2$).
- No post-submit drill recommendations ("Don't Leave Gaps Drill", "Recommended Next Step", "Next Drill", "Start Drill", "Open recommended drill") on Listening results.
- Result page order: 1. Score Summary $\to$ 2. Full Transcript & Audio Review $\to$ 3. Detailed Question Analysis.
- Part B & C: No "See PDF", "CPDF", "Stem" button, or extraction artifacts (`PAGE 4`, `Practice Test 1`). Flag button preserved. Next Question advances reliably.
- All 20 tests split into A1, A2, B, C1, C2 using spoken semantic transition cues (no universal timestamps, no silent gaps clipping speech).
- Pre-push validation via `pnpm run ship:gate`.

---

### Task 1: Refactor `BCQuestionRenderer` (Remove Stem Button, Preserve Flag, Ensure Clean Prompts)

**Files:**
- Modify: `components/domain/listening/BCQuestionRenderer.tsx`
- Modify: `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`
- Modify: `tests/unit/listening/BCQuestionRenderer.test.tsx`

**Interfaces:**
- Consumes: `ListeningQuestionAnnotation`, standard `BCQuestionRendererProps`
- Produces: Clean MCQ renderer without Stem highlighter button; keeps Flag button and keyboard navigation.

- [ ] **Step 1: Update unit tests in `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`**
  Remove assertions expecting the "Stem" button. Add assertion confirming "Flag" button is present and "Stem" button is absent.

```tsx
it('renders Flag button and does not render Stem button', () => {
  render(
    <BCQuestionRenderer
      questionNumber={25}
      partLabel="Part B"
      prompt="What is the doctor explaining?"
      options={['Option A', 'Option B', 'Option C']}
      value=""
      onChange={() => {}}
    />
  );
  expect(screen.getByRole('button', { name: /flag/i })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /stem/i })).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run test to verify it fails**
  Run: `pnpm exec vitest run components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`
  Expected: FAIL (Stem button currently exists).

- [ ] **Step 3: Modify `components/domain/listening/BCQuestionRenderer.tsx`**
  Remove the Stem highlighter button JSX (lines 190–201) and associated state `isStemHighlighted` / `setIsStemHighlighted`. Keep `Flag` button and option strikethrough logic.

- [ ] **Step 4: Update `tests/unit/listening/BCQuestionRenderer.test.tsx`**
  Remove any old stem-highlighting tests and ensure all tests pass.

- [ ] **Step 5: Run tests to verify pass**
  Run: `pnpm exec vitest run components/domain/listening/__tests__/BCQuestionRenderer.test.tsx tests/unit/listening/BCQuestionRenderer.test.tsx`
  Expected: PASS.

---

### Task 2: Data Sanitization for Part B/C Questions (Strip Sentinels & Extraction Artifacts)

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/Listening/ListeningLearnerService.cs`
- Modify: `lib/listening-authoring-api.ts` or frontend mapping helpers if applicable
- Test: `backend/tests/OetLearner.Api.Tests/Listening/ListeningLearnerServiceTests.cs` (or create targeted test)

**Interfaces:**
- Consumes: Raw question stems and option strings from database or DTOs.
- Produces: Sanitized question text with no `PAGE \d+`, `======.*======`, `Practice Test \d+`, or `See PDF` placeholders.

- [ ] **Step 1: Write failing backend test for question text sanitization**

```csharp
[Fact]
public void SanitizeQuestionText_RemovesPageMarkersAndArtifacts()
{
    var raw = "====== PAGE 4 ======\nPractice Test 1\nWhat is the nurse discussing with the patient?";
    var clean = ListeningLearnerService.SanitizeQuestionPrompt(raw);
    Assert.Equal("What is the nurse discussing with the patient?", clean);
}
```

- [ ] **Step 2: Run test to verify failure**
  Run: `dotnet test backend/tests/OetLearner.Api.Tests/ --filter "FullyQualifiedName~SanitizeQuestionText"`
  Expected: FAIL (method not yet implemented).

- [ ] **Step 3: Implement `SanitizeQuestionPrompt` in `ListeningLearnerService.cs`**
  Add regex replacement to strip:
  - `(?i)==+\s*PAGE\s*\d+\s*==+`
  - `(?i)^PAGE\s*\d+\s*$`
  - `(?i)^Practice Test\s*\d+\s*$`
  - Trim extraneous whitespace and sentinel `See PDF` / `CPDF`.
  Apply in `MapRelationalQuestion` and `ExtractQuestions`.

- [ ] **Step 4: Run test to verify pass**
  Run: `dotnet test backend/tests/OetLearner.Api.Tests/ --filter "FullyQualifiedName~SanitizeQuestionText"`
  Expected: PASS.

---

### Task 3: Upgrade Full Listening Exam (`app/listening/paper/[paperId]`) to Universal Black OET-Style Player & Sequential Autoplay

**Files:**
- Modify: `app/listening/paper/[paperId]/page.tsx`
- Modify: `components/domain/listening/player/ListeningAudioTransport.tsx` (if mobile styling refinements needed)
- Test: `app/listening/paper/[paperId]/page.test.tsx` (or Vitest suite)

**Interfaces:**
- Consumes: `ListeningExamSubSection`, `ListeningAudioTransport`, `BCQuestionRenderer`, `PartANotesDocument`
- Produces: Unified exam page with black OET-style transport, sequential autoplay ($A1 \to A2 \to B \to C1 \to C2$), reliable Part B Next Question navigation, no manual Play required.

- [ ] **Step 1: Integrate `ListeningAudioTransport` into `paper/[paperId]/page.tsx`**
  - Replace the white-box `SubSectionAudio` + `SubSectionTimer` in `paper/[paperId]/page.tsx` with `ListeningAudioTransport`.
  - Pass:
    - `isPlaying`: boolean playback state
    - `progressSeconds`: current audio progress in seconds
    - `durationSeconds`: current audio duration in seconds
    - `canScrub={false}`: locked in exam mode
    - `canPause={false}`: audio cannot be paused in exam mode
    - `isHalted={audioFailure}`
    - `audioState={audioBuffering ? 'buffering' : 'ready'}`
    - `saveState={saveState}`
    - `answeredCount={answeredCount}`
    - `totalQuestions={session.questions.length}`
    - `attemptSecondsRemaining={remaining}`
    - `onSubmit={requestAdvance}`
  - Ensure the internal `<audio>` element automatically plays on mount when section opens.

- [ ] **Step 2: Refactor Part B item rendering to use `BCQuestionRenderer`**
  - For Part B questions in `paper/[paperId]/page.tsx`, render `BCQuestionRenderer` for the active item.
  - Wire `Next question` button to cleanly increment `partBQuestionIndex`, save current answer, and update progress without blank flashes or page reloads.

- [ ] **Step 3: Test autoplay and sequential transitions**
  Run: `pnpm exec vitest run app/listening/`

---

### Task 4: Restructure Candidate Results Screen (Remove Drills, Move Transcript Review to Top)

**Files:**
- Modify: `app/listening/results/[id]/page.tsx`
- Modify: `app/listening/results/[id]/page.test.tsx` (if present, or create)

**Interfaces:**
- Consumes: `ListeningReviewDto`
- Produces: Result screen with exact layout: 1. Score Summary $\to$ 2. Full Transcript & Audio Review $\to$ 3. Part Breakdown / Time Summary $\to$ 4. Detailed Question Review. Zero drill recommendation cards.

- [ ] **Step 1: Update/write result page layout test**
  Assert that Transcript Review section precedes Detailed Review and that no drill recommendations are present.

- [ ] **Step 2: Update `app/listening/results/[id]/page.tsx`**
  - Reorder JSX:
    1. `ResultsScorePanel` + `ScoreConversionEvidence`
    2. Full Transcript & Audio Access Card (`Full transcript & audio — permanent access` & `Show Script`)
    3. `ListeningPartBreakdown` + `TimeUsedSummary` + AI scoring disclaimer
    4. `Detailed Review` question list
  - Verify all references to "Don't Leave Gaps Drill", "Recommended Next Step", "Next Drill", "Start Drill", "Open recommended drill" are completely removed.

- [ ] **Step 3: Run Vitest on results page**
  Run: `pnpm exec vitest run app/listening/results/`
  Expected: PASS.

---

### Task 5: 20-Test Audio Splitting Pipeline & Verification Script

**Files:**
- Create: `scripts/materials/split-listening-benchmark-audio.py`
- Test: Execute script against the 20 source files and generate `docs/listening/audio-split-audit-report.md`

**Interfaces:**
- Consumes: 20 source MP3 files in `OET Materials & Videos Data/Materials/Listening/Benchmark Exams/` or `Extra Listening Exams/`.
- Produces: 5 discrete MP3 segments per test ($A1, A2, B, C1, C2$) cut at exact semantic spoken transition cues, plus a verification log.

- [ ] **Step 1: Write `split-listening-benchmark-audio.py`**
  - Read each source MP3.
  - Determine semantic transition cue points per test:
    - A1: 0:00 to start of A2 announcer intro
    - A2: first spoken word of A2 intro to before "Now" in "Now look at Part B."
    - Part B: "Now look at Part B." to before "Now" in "Now look at Part C."
    - C1: "Now look at Part C." to before "Now" in "Now look at Extract 2."
    - C2: "Now look at Extract 2." to end of audio.
  - Run `ffmpeg` to extract segments with lossless stream-copy (`-c copy`) or high-bitrate MP3 (`-b:a 192k`).
  - Output files to audio destination directory.
  - Verify boundary speech integrity.

- [ ] **Step 2: Run segmentation script and verify output**
  Run: `python scripts/materials/split-listening-benchmark-audio.py`
  Expected: 100 generated MP3 files (5 parts $\times$ 20 tests) with clean transitions and verification audit.

---

### Task 6: Comprehensive Quality Gate & Verification

- [ ] **Step 1: Run TypeScript compiler check**
  Run: `pnpm exec tsc --noEmit`
  Expected: 0 errors.

- [ ] **Step 2: Run ESLint**
  Run: `pnpm run lint`
  Expected: 0 errors.

- [ ] **Step 3: Run full Vitest test suite for touched areas**
  Run: `pnpm exec vitest run components/domain/listening app/listening`
  Expected: All tests PASS.

- [ ] **Step 4: Run pre-push gate**
  Run: `pnpm run ship:gate`
  Expected: PASS.
