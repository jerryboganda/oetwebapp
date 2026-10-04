# Speaking grader calibration

Owner spec 4 Oct 2026: the AI Speaking grader may only lose its **Provisional** label once its scores have
been compared with an OET expert's own marks on real performances. This document covers the expert side
(live) and what the comparison will do with it (next increment).

## Why

The platform's raw-to-reported conversion (`OetScoring.SpeakingRawToReported`, mapping version
`speaking-map.v0-heuristic`) is a heuristic, not the OET formula. Until AI marks are measured against
expert marks, every candidate-facing Speaking score is labelled "Provisional score — calibration in
progress" (see `docs/speaking/scoring.md`).

## The expert side

Admin → Speaking → **Grader calibration** (`/admin/speaking/grader-calibration`).

1. **Candidates** — finished AI role-plays (practice or Full Mock card) with a usable transcript that are not
   yet in the set. No learner identity and no AI result is listed. `Audio: Yes/No` says whether a stored,
   non-warm-up clip exists.
2. **Add to calibration set** (promote) — pins the transcript the grader reads, records whether audio exists,
   **keeps the performance's audio for 365 days** (`SpeakingRecording.RetentionExpiresAt`, never shortened) and
   writes an `AuditEvent` (`SpeakingGraderCalibrationSamplePromoted`). This is the only learner-data write in
   the feature; the admin confirms it each time.
3. **Mark** — Dr Hesham reads the card, hears the audio, reads the transcript (connection-check chatter at the
   start is removed exactly as for the grader) and records the nine criterion scores (linguistic 0–6, clinical
   0–3) and his own **overall /500 in steps of 10**. The overall is stored separately from the criterion sum so
   the score mapping can later be fitted on his judgement, not on ours.
4. **Exclude** — no speech, wrong card, broken audio: excluded with a reason, kept for audit, never reported.

### Blind by construction

Nothing under `/v1/admin/speaking/grader-calibration` reads or returns an AI assessment, grade, score or
rationale. `SpeakingGraderCalibrationService` never touches the AI assessment tables, and
`SpeakingGraderCalibrationServiceTests.TheService_NeverReadsAnAiAssessment…` fails the build if it starts to.

### Data kept

`SpeakingGraderCalibrationSamples` holds ids only (session, pinned transcript, card, profession), the audio
flag, and the expert's marks. The learner's words and audio stay in `SpeakingTranscripts` /
`SpeakingRecordings`; nothing is copied.

## Coverage a calibration report needs (proposed — the owner confirms)

| Requirement | Threshold |
| --- | --- |
| Marked performances | ≥ 30 |
| Per expert grade (A, B, C+, C, D, E) | ≥ 3 each |
| Expert overall 320–380 (pass line 350) | ≥ 10 |
| Marked performances with audio | ≥ 80 % |

The page shows progress against each line and says in plain words what is still missing.

## The harness (calibration run)

`speaking-grader-calibration.yml` (manual, resumable) drives a run over the expert-marked performances:

1. `POST /runs` freezes the set (every labelled, non-excluded performance) and creates `Repeats` (at least two) grades for each.
2. `POST /runs/{id}/next` queues **one** grade at a time as a durable operation (`speaking_calibration_grade`) that the existing
   worker runs: the same grader core and prompt a learner's grade uses, on the transcript pinned at promotion, the audio stage
   on every grade when the run asks for it (whatever the learner-facing flag says), and `UserId = null` (no plan gate). **Nothing
   is persisted except numbers**: no assessment row, no credit, no Jev call, no learner text. It answers `queued`, `busy`
   (one is running), `yield` (a learner's `speaking.grade` / `writing.grade` is queued or running: nothing starts), `done`
   or `complete`. A failed grade is retried up to three attempts.
3. `POST /runs/{id}/finalize` freezes the report (and the grader version that actually graded) and writes an audit event.
   `GET /runs/{id}` shows the report so far at any time.

**The report** (`SpeakingGraderCalibrationMetrics`): per-criterion mean error, bias, exact and within-one-band agreement (Wilson 95 %
intervals), Intelligibility split by judged-from-audio vs transcript-only, raw-total error, then the end-to-end score through
a map fitted **without** each performance (leave-one-out): mean error, bias, within 40 points, grade exact / adjacent with the
6×6 confusion matrix, pass/fail at 350 (agreement, false passes, false fails), and repeatability (criterion scores that repeat,
score within 20 points, grade and pass flips). It also shows the platform heuristic against the expert (`V0OnExpert`: the current
map on the expert's own criteria; `V0EndToEnd`: what the platform shows today) and the monotone fitted map
(`Mapping.Fitted`, isotonic regression on the expert's own raw-total to overall pairs, anchored 0→0 and 39→500, rounded to 10).

**Proposed pass thresholds** (`SpeakingGraderCalibrationMetrics.Thresholds`; the owner confirms them): each linguistic criterion
mean error ≤ 0.75, |bias| ≤ 0.5, within one band ≥ 90 %; each clinical criterion mean error ≤ 0.5, |bias| ≤ 0.35, exact ≥ 60 %;
audio-judged Intelligibility mean error ≤ 0.75; end-to-end score mean error ≤ 30, |bias| ≤ 15, within 40 points ≥ 80 %; grade exact
≥ 70 % and within one ≥ 95 %; pass/fail agreement ≥ 85 % with false passes ≤ 10 %; repeatability: criterion scores repeat ≥ 80 %,
score within 20 points ≥ 90 %, pass flips ≤ 5 %; plus the coverage above and every performance graded at least twice.

**Scope.** A marked performance is one card, so the harness calibrates the card grader. The combined Full Mock judgement uses the
same grader core, rubric and score map but a different prompt (`speaking.score.v3-combined`), so it stays provisional until
it is calibrated on its own (a later step: expert marks for whole two-card tests).

## What passing does

When a run passes and the owner agrees: a code change replaces the `SpeakingRawToReported` literal (C# and TypeScript) with
`Mapping.Fitted`, bumps `SpeakingMappingVersion`, adds the grader version (with its model) to the calibrated set, and commits the
report. New scores then switch from "Provisional" to "AI practice estimate". Stored scores are never rewritten.

## Endpoints (`AdminOnly`; reads `AdminContentRead`, writes `AdminContentWrite`)

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/v1/admin/speaking/grader-calibration/` | Coverage + every sample |
| GET | `/candidates?take=` | Finished AI cards not yet promoted |
| POST | `/samples` `{ sessionId }` | Promote (keeps audio 365 days, audit event) |
| GET | `/samples/{id}` | Blind labelling view |
| GET | `/samples/{id}/audio/{recordingId}` | Stream a clip |
| PUT | `/samples/{id}/label` | Nine criterion scores + overall (steps of 10) + notes |
| POST | `/samples/{id}/exclude` `{ reason }` | Mark unusable |
| GET | `/runs` | Recent runs with progress |
| POST | `/runs` `{ repeats?, useAudio? }` | Start a run over the marked performances |
| GET | `/runs/{id}` | Progress + the report so far |
| POST | `/runs/{id}/next` | Queue the next grade unless a learner's is waiting |
| POST | `/runs/{id}/finalize` | Freeze the report |
