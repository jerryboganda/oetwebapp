# Listening Part B & C — Release-Blocking Fixes Specification

**Date**: 2026-08-28  
**Status**: Approved Specification  
**Scope**: Atlas Practice Series & Nova Practice Series; Standalone Parts A/B/C and Full Listening Exam.

---

## 1. Scope & Objective

This specification defines the complete architectural and data fix for the four critical release-blocking defects in the OET Listening module:
1. **Critical Issue 1 — Question Stems**: Ensure real clinical question stems appear above answer choices for every Part B (Q25–Q30) and Part C (Q31–Q42) question across Atlas and Nova.
2. **Critical Issue 2 — Full Exam Part B Complete Set**: Enable the full 6-question set (Q25–Q30) with single-question active cards, jump-to navigation (`25` through `30`), and sequential "Next Question" progression with persistent answers.
3. **Critical Issue 3 — Full Part C Complete 12 Questions**: Enable all 12 questions (C1: Q31–Q36, C2: Q37–Q42) with real stems, jump-to navigation, and seamless C1 $\to$ C2 transition.
4. **Critical Issue 4 — Standalone Autoplay**: Autoplay audio upon candidate launching Standalone Part A, Part B, or Part C without requiring a manual Play click, with fallback modal overlay if browser policy restricts unmuted media.

---

## 2. Question Data Model & Authoritative Content (Backend & DB)

### 2.1 Authoritative Question Stems
- All question records for Part B and Part C in Atlas and Nova series are populated with authentic stems extracted verbatim from official source materials (`Benchmark Listeninig Tests.pdf`, `starter-mock-1.json`, `starter-mock-2.json`).
- Stems never contain placeholder sentinels (`"See PDF"`, `"CPDF"`, `"View PDF"`) or PDF extraction artifacts (`PAGE \d+`, `Practice Test \d+`, `\uF0B7`, `====== PAGE \d+ ======`).
- `ListeningLearnerService.MapRelationalQuestion` and `ExtractQuestions` map authentic stems to the candidate DTO `text` field.
- Validation rules in `ListeningLearnerService` and test suites assert:
  - Part B question count $= 6$ (numbers $25, 26, 27, 28, 29, 30$).
  - Part C question count $= 12$ (numbers $31..42$).
  - Stem is non-empty and distinct from generic headings (e.g., `"PART B QUESTION 25"` is rejected).

---

## 3. Candidate Navigation & Interface (Frontend)

### 3.1 Single Active Question Card Architecture
- **Active Question Card**: Displays:
  1. Section / Extract Header (e.g. `Part B — Workplace Extracts`, `Part C — Extract 1 (Q31–Q36)`).
  2. Question Badge (`Q25`) & Review Flag toggle button.
  3. Real Question Stem in prominent typography (`<h3>` with clean contrast).
  4. Three multiple-choice options (`A`, `B`, `C`) with selection and strikethrough controls.
- **Jump-to Navigation Bar**:
  - Part B renders pills: `25`, `26`, `27`, `28`, `29`, `30`.
  - Part C renders pills: `31`..`36` (C1) and `37`..`42` (C2).
  - Visual status per pill: Active (primary), Answered (highlighted/check), Flagged (warning badge), Unanswered (muted).
  - Clicking any pill immediately switches the active question view.
- **Sequential Stepper Controls**:
  - `Previous Question` button (disabled on first item).
  - `Next Question` button: Autosaves current selection, advances active index $i \to i+1$, and updates visible question without reloading or interrupting audio.
  - At the end of C1 (Q36), clicking `Next Question` seamlessly loads Q37 (C2), updates the extract header to "Extract 2", and switches audio context.
- **Annotation & Answer Persistence**:
  - Answers are stored in memory and autosaved asynchronously via `saveListeningAnswer`.
  - Review flags and strikethroughs are persisted across all question jumps and prev/next actions.

---

## 4. Audio Autoplay Lifecycle & Resiliency

### 4.1 Standalone & Full Exam Autoplay
- Launching any standalone attempt (`/listening/practice/[part]`) or full exam (`/listening/paper/[paperId]`) registers the user's initial click gesture.
- `ListeningAudioTransport` initializes and immediately invokes `.play()` on the audio element.
- Autoplay runs once per sub-section and is guarded by `hasAutoplayedRef` to prevent double playback or unwanted audio resets during question navigation.
- If browser security blocks unmuted autoplay, a full-screen overlay modal prompts `"Click to Start Audio"` to satisfy the gesture requirement and immediately begin playback without resetting attempt timers.

---

## 5. QA Verification Matrix

| Series | Mode | Part | Questions | Stem Required | Autoplay Required |
|---|---|---|---|---|---|
| Atlas | Standalone | A | Q1–Q24 | Note Completion / Real prompts | Yes |
| Atlas | Standalone | B | Q25–Q30 | Authentic stems, 6 MCQs | Yes |
| Atlas | Standalone | C | Q31–Q42 | Authentic stems, 12 MCQs (C1+C2) | Yes |
| Atlas | Full Exam | B | Q25–Q30 | Authentic stems, Jump 25..30, Next nav | Yes (exam audio continuous) |
| Atlas | Full Exam | C | Q31–Q42 | Authentic stems, Jump 31..42, C1 $\to$ C2 | Yes (exam audio continuous) |
| Nova | Standalone | A | Q1–Q24 | Note Completion / Real prompts | Yes |
| Nova | Standalone | B | Q25–Q30 | Authentic stems, 6 MCQs | Yes |
| Nova | Standalone | C | Q31–Q42 | Authentic stems, 12 MCQs (C1+C2) | Yes |
| Nova | Full Exam | B | Q25–Q30 | Authentic stems, Jump 25..30, Next nav | Yes (exam audio continuous) |
| Nova | Full Exam | C | Q31–Q42 | Authentic stems, Jump 31..42, C1 $\to$ C2 | Yes (exam audio continuous) |

---
