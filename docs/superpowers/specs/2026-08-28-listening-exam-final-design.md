# Listening Exam — Final Implementation Specification

**Date**: 2026-08-28  
**Status**: Approved Specification  
**Scope**: Full Listening Exam, Separate Parts A/B/C practice, C1/C2 sub-sections, candidate exam interface, candidate results/review, audio handling, transcripts, and all 20 Benchmark/Nova test packages.

---

## 1. Executive Summary

This document specifies the authoritative design for the OET Listening module. It unifies the candidate exam interface on the single black OET-style audio transport player, implements gesture-chained sequential autoplay across all sections ($A1 \to A2 \to B \to C1 \to C2$), eliminates post-submit drill recommendations from Listening results, promotes the Full Transcript & Audio Review to the top of the results page, cleans Part B & Part C questions of extraction artifacts and sentinels, removes the Stem button, and establishes a semantic-cue audio splitting pipeline for all 20 Benchmark/Nova tests.

---

## 2. Universal Black OET-Style Audio Player Architecture

### 2.1 Component Consolidation (`ListeningAudioTransport`)
- The single visual and interactive audio transport bar is `components/domain/listening/player/ListeningAudioTransport.tsx`.
- The legacy white-card implementation in `app/listening/paper/[paperId]/page.tsx` (`SubSectionAudio` + `SubSectionTimer`) is replaced with `ListeningAudioTransport`.
- Both Full Exam (`/listening/paper/[paperId]`) and Practice routes (`/listening/practice/[part]` and `/listening/player/[id]`) share the identical component with:
  - Dark theme (`bg-navy`, white typography, crisp contrast).
  - Inline progress track with scrub protection in strict exam mode (`canScrub=false`).
  - Attempt timer with danger/warning color thresholds.
  - Save-state indicator (`Saved`, `Saving`, `Offline saved`, `Sync issue`).
  - Responsive mobile/tablet layout (no button overflow, no overlapping timer text, fluid container).

### 2.2 Autoplay & Sequential Forward State Machine
- **Gesture Chaining**: When a candidate initiates the exam via the **"Start Exam"** or **"Start Practice"** action, that gesture activates the browser media context.
- **Sequential Playback**:
  - Full Exam forward order:
    $$\text{A1} \longrightarrow \text{A2} \longrightarrow \text{Part B} \longrightarrow \text{C1} \longrightarrow \text{C2}$$
  - Advancing to a subsequent sub-section automatically loads the segment's audio asset and invokes playback immediately.
  - No manual click on Play is required between sections.
- **Playback Guard & Resiliency**:
  - If a restrictive browser policy temporarily halts autoplay, the player displays a high-visibility, single-tap `"Tap to Play — [Section Title]"` action inside the transport bar without resetting attempt countdowns or losing form state.

---

## 3. Results & Review Reordering & Cleanup

### 3.1 Removal of Post-Submit Drill Recommendations
- The candidate result screen (`app/listening/results/[id]/page.tsx`) completely eliminates all post-submit drill recommendation cards:
  - *"Don't Leave Gaps Drill"*
  - *"Recommended Next Step"*
  - *"Next Drill"*
  - *"Start Drill"*
  - *"Open recommended drill"*
  - Any orphan recommendation containers and hooks.

### 3.2 Result Page Vertical Structure
The candidate results page strictly adheres to the following vertical layout:
1. **Score Summary Panel**:
   - `ResultsScorePanel` with raw score, percentage accuracy, scaled score, grade, and `ScoreConversionEvidence`.
2. **Full Transcript & Audio Review**:
   - `Full transcript & audio — permanent access` card with immediate CTA to `Open Transcript Review`.
   - `Show Script` post-submit access card.
3. **Performance Breakdown & Timing**:
   - `ListeningPartBreakdown` (Parts A, B, C item analysis).
   - `TimeUsedSummary` (telemetry-based duration per section).
4. **Detailed Question-by-Question Analysis**:
   - `Detailed Review` accordion containing each item with Candidate Answer, Correct Answer, Explanation, Distractor Analysis, and `ReportAnswerControl`.

---

## 4. Part B & Part C Experience & Question Cleansing

### 4.1 Component Refinement (`BCQuestionRenderer`)
- **Remove "Stem" Button**: The `"Stem"` highlight button is completely removed from `BCQuestionRenderer.tsx` and its unit tests.
- **Preserve "Flag" Button**: The review flag button remains fully interactive.
- **Render Clear Prompts**: Prompts are rendered as standard heading elements before the A/B/C options.

### 4.2 Removal of Placeholders & Sentinels
- Completely eliminate placeholder text:
  - `"See PDF"`, `"CPDF"`, `"PDF"`, `"View PDF"`.
- Strip document extraction artifacts from question stems and options:
  - `PAGE \d+`, `====== PAGE \d+ ======`, `Practice Test \d+`, scan headers/footers, and OCR markers.
- Implement sanitization filters at the mapping and data ingestion layers (`ListeningLearnerService.MapRelationalQuestion`).

### 4.3 Reliable Part B Navigation
- On clicking **"Next Question"**:
  - Current answer is committed to local state and autosaved to the API.
  - Active question index increments from $i \to i+1$.
  - Progress badge updates (`Question 2 of 6`).
  - No stale data, no blank UI flashes, and no full-page reloads.

---

## 5. Audio Splitting Rules for 20 Benchmark/Nova Tests

### 5.1 Semantic Boundary Transitions
Each of the 20 source audio files is split into exactly 5 discrete audio files using semantic spoken cues:
1. **A1**: Begins at 0:00 (full introduction and Extract 1) $\to$ ends immediately before the announcer's first spoken word introducing A2/Extract 2.
2. **A2**: Begins at the first spoken word of the A2 announcer introduction (*"Extract 2..."* or *"This is a Benchmark sample test paper..."*) $\to$ ends immediately before the word **"Now"** in *"Now look at Part B."*
3. **Part B**: Begins exactly at the word **"Now"** in *"Now look at Part B."* $\to$ ends immediately before **"Now"** in *"Now look at Part C."*
4. **C1**: Begins exactly at the word **"Now"** in *"Now look at Part C."* $\to$ ends immediately before **"Now"** in *"Now look at Extract 2."*
5. **C2**: Begins exactly at the word **"Now"** in *"Now look at Extract 2."* $\to$ ends at the natural ending of the source audio.

### 5.2 Audio Quality & Verification
- Segmentation is executed via `ffmpeg` using high-fidelity encoding or stream-copy to preserve sample rates and eliminate clicks, pops, or volume alterations.
- An automated verification report audits the first 3 seconds and last 3 seconds of every generated file to guarantee no spoken words are truncated and no excessive silent lead-in remains.
- The 5 assets per test are bound to the respective test papers under `Role = Audio` and `Part = A1, A2, B, C1, C2`.

---

## 6. Verification & Acceptance Plan

- [ ] Project builds with 0 TypeScript/lint errors (`pnpm run ship:gate`).
- [ ] Full Exam uses the black `ListeningAudioTransport` player with autoplay.
- [ ] Part A, B, C practice routes use `ListeningAudioTransport` with autoplay.
- [ ] Sequential autoplay advances through $A1 \to A2 \to B \to C1 \to C2$ without requiring manual Play clicks.
- [ ] Post-submit drill recommendations are completely removed from Results.
- [ ] Results page displays Transcript Review directly below Score Summary.
- [ ] Part B and Part C render real question prompts with no "See PDF" / "CPDF" sentinels.
- [ ] Stem button is removed from `BCQuestionRenderer`.
- [ ] Part B Next Question advances reliably.
- [ ] 20 Benchmark/Nova tests are segmented at exact semantic boundaries and verified.
