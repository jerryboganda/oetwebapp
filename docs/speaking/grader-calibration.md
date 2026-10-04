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

## What comes next

The calibration run (not built yet) grades each marked sample with the production grader (Claude Max first,
unchanged) and compares it with the expert: per-criterion error and bias, overall error, grade agreement,
pass/fail at 350, and repeatability. When a grader version passes, the raw-to-reported table is refitted on
the expert's own pairs, the grader version is added to the calibrated set, and new scores switch from
"Provisional" to "AI practice estimate". Stored scores are never rewritten.

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
