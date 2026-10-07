# Speaking grader calibration

Owner spec 4 Oct 2026: the AI Speaking grader may only lose its **Provisional** label once its scores have
been compared with an OET expert's own marks on real performances. This document covers the expert side and
the harness that compares the two. Owner decisions of 5 Oct 2026: the thresholds below are **approved and must
stay strict** (never relax one to make the grader pass), and calibration audio is kept 365 days only for
performances explicitly promoted here, under consent wording that says so.

## Why

The platform's raw-to-reported conversion (`OetScoring.SpeakingRawToReported`, mapping version
`speaking-map.v0-heuristic`) is a heuristic, not the OET formula. Until AI marks are measured against
expert marks, every candidate-facing Speaking score is labelled "Provisional score — calibration in
progress" (see `docs/speaking/scoring.md`).

## The expert side

Admin → Speaking → **Grader calibration** (`/admin/speaking/grader-calibration`).

1. **Candidates** — finished AI role-plays (practice or Full Mock card) with a usable transcript that are not
   yet in the set **and whose learner had accepted the consent wording that covers calibration (`recording.v3`
   or later) when it was recorded and has not withdrawn it**. Performances recorded under the older wording
   (`recording.v1`/`v2`) cannot be used. No learner identity and no AI result is listed. `Audio: Yes/No` says
   whether a stored, non-warm-up clip exists.
2. **Add to calibration set** (promote) — pins the transcript the grader reads, records whether audio exists,
   **keeps the performance's audio for 365 days** (`SpeakingRecording.RetentionExpiresAt`, never shortened) and
   writes an `AuditEvent` (`SpeakingGraderCalibrationSamplePromoted`). This is the only learner-data write in
   the feature; the admin confirms it each time.
3. **Mark** — Dr Hesham reads the card, hears the audio, reads the transcript (connection-check chatter at the
   start is removed exactly as for the grader) and records the nine criterion scores (linguistic 0–6, clinical
   0–3) and his own **overall /500 in steps of 10**. The overall is stored separately from the criterion sum so
   the score mapping can later be fitted on his judgement, not on ours.
4. **Exclude** — no speech, wrong card, broken audio: excluded with a reason, kept for audit, never reported.
5. **Unavailable** — a sample whose audio expired (day 365) or was deleted by the learner, whose learner withdrew
   consent, or whose transcript was erased is shown as "Unavailable". It stops counting towards coverage and is
   never graded. Marks of a sample inside a running calibration run are frozen until the run is finalised.

### Blind by construction

Nothing under `/v1/admin/speaking/grader-calibration` reads or returns an AI assessment, grade, score or
rationale. `SpeakingGraderCalibrationService` never touches the AI assessment tables, and
`SpeakingGraderCalibrationServiceTests.TheService_NeverReadsAnAiAssessment…` fails the build if it starts to.

### Data kept

`SpeakingGraderCalibrationSamples` holds ids only (session, pinned transcript, card, profession), the audio
flag, and the expert's marks. The learner's words and audio stay in `SpeakingTranscripts` /
`SpeakingRecordings`; nothing is copied.

## Coverage a calibration report needs (approved by the owner, 5 Oct 2026)

| Requirement | Threshold |
| --- | --- |
| Marked performances | ≥ 30 |
| Per expert grade (A, B, C+, C, D, E) | ≥ 3 each |
| Expert overall 320–380 (pass line 350) | ≥ 10 |
| …straddling the pass line | ≥ 4 at 320–340 **and** ≥ 4 at 350–380 |
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

**Pass thresholds — approved by the owner on 5 Oct 2026, strict, never to be relaxed** (`SpeakingGraderCalibrationMetrics.Thresholds`;
`SpeakingGraderCalibrationMetricsTests.TheApprovedThresholds_ArePinned…` fails the build if any value changes): each linguistic criterion
mean error ≤ 0.75, |bias| ≤ 0.5, within one band ≥ 90 %; each clinical criterion mean error ≤ 0.5, |bias| ≤ 0.35, exact ≥ 60 %;
audio-judged Intelligibility mean error ≤ 0.75; end-to-end score mean error ≤ 30, |bias| ≤ 15, within 40 points ≥ 80 %; grade exact
≥ 70 % and within one ≥ 95 %; pass/fail agreement ≥ 85 % with false passes ≤ 10 %; repeatability: criterion scores repeat ≥ 80 %,
score within 20 points ≥ 90 %, pass flips ≤ 5 %; plus the coverage above and every performance graded at least twice.

**The comparison report** (the JSON report, `grader-calibration-report.md` and the step summary of the workflow) also lists every marked
performance beside each of its grades: the expert's nine marks, raw total and overall, then per repeat the grader's nine marks, raw total,
leave-one-out score, grade, error and where Intelligibility was judged from; the 6×6 grade confusion matrix; the fitted map; and how many
grades each exact grader version + model produced (the "Provisional" label is earned per exact version).

**Smoke check.** `speaking-grader-calibration.yml` with `smoke: true` signs in as the admin and reads the overview and candidate counts only
(no run, no writes): the authenticated proof that the screen's API is live.

**Scope.** A marked performance is one card, so the harness calibrates the card grader. The combined Full Mock judgement uses the
same grader core, rubric and score map but a different prompt (`speaking.score.v4-combined`), so it stays provisional until
it is calibrated on its own.

## Owner pilot (owner request 7 Oct 2026)

Before collecting the full validation set, the owner can run a **pilot**: dispatch
`speaking-grader-calibration.yml` with `pilot: true` (and `scope: card` or `mock`). A pilot run grades whatever is
expert-marked at the moment it starts — two testers, two Full Mocks, four cards is enough — twice each, and produces
the same comparison report: the expert's nine criterion marks beside each AI grade, raw total, overall /500
(leave-one-out), grade, score difference, pass/fail at 350, where Intelligibility was judged from, and repeatability.

A pilot **cannot pass by design**. Its verdict says `PILOT` and never earns a grader version the loss of the
"Provisional" label: the approved coverage and thresholds below are what the later **validation** run (no `pilot`)
must meet, unchanged. Every threshold miss in a pilot is listed as an advisory note (where the grader stood against
the bar), not as a failure, so a small sample is reported honestly instead of as a pass/fail verdict.

Testers need nothing special: `recording.v3` is the normal current Speaking consent every candidate accepts at the
intro, so a pilot tester simply completes a normal Full Mock as a regular candidate (both cards' audio is then
promoted into the calibration set by the owner, with the usual 365-day retention and audit event).

### The one-performance diagnostic (8 Oct 2026)

To find WHERE a difference from the expert comes from — the grader's judgement, missing audio evidence, the secondary
reviewer or the provisional raw-to-/500 mapping — run a pilot over ONE performance: dispatch the workflow with
`scope: mock`, `pilot: true`, `audio: true` and `sample: <the mock sample id>` (the script's `--sample <id>`, repeatable; the API's
`sampleIds`). Each grade stores `DiagnosticsJson` (numbers and codes only) and the report prints, per grade, the ten things the
owner asked for: (1) the expert's nine marks beside the AI's final nine; (2) the expert's overall /500 beside the AI's, both as a
learner sees it (v0 map) and through a map fitted on the expert's marks (leave-one-out); (3) the AI raw total before mapping;
(4) the mapping version; (5, 6) whether each card got real audio judgement and, if not, the reason and how much audio there was
for how much speech; (7) whether the combined Intelligibility was audio-based or transcript-only; (8) the exact `GraderVersion`;
(9) **Claude's own scores before the secondary reviewer**; (10) what the reviewer did (status, model, every change, bounded ±1).

Read it in **Admin → Speaking → Grader calibration → Comparison runs** (read-only; it shows AI results, so it is kept apart from
the blind marking pages), or in `grader-calibration-report-<run id>.md` (the workflow artifact). Grades made before 8 Oct 2026
carry no diagnostics: items 9 and 10 cannot be recovered for them, only for a fresh run.

## Full Mock calibration (built 7 Oct 2026)

The combined grader (`speaking.score.v4-combined`) is calibrated on its own sample kind: a **whole two-card test**
marked as ONE performance. Admin → Speaking → Grader calibration has a **Single cards | Full Mocks** switch.

- **Candidates** — completed two-card AI exams whose both cards have a usable latest transcript, recorded under the
  calibration consent, not already promoted. No learner identity, no AI result.
- **Promote** — pins BOTH cards' transcripts, keeps both cards' audio for 365 days, writes an audit event
  (`SpeakingGraderCalibrationMockSamplePromoted`).
- **Mark** — the expert opens the mock, hears Card A and Card B, reads both cards and both cleaned transcripts, and
  gives ONE set of the nine criteria and ONE overall /500 for the whole test — blind, like the card view.
- **Harness** — `POST /runs` with `scope: "mock"` freezes the labelled mocks and grades each with the combined core
  (`GradeCombinedForCalibrationAsync`: the same `speaking.score.v4-combined` prompt a learner's Full Mock uses, on the
  pinned transcripts, audio per card combined by the same rule — nothing persisted). Its report compares the expert's
  one mark with each combined grade; everything else (leave-one-out, grades, pass/fail, repeatability) works the same.
- A combined result that never came from real two-card comparisons stays "Provisional"; a passing mock-scope
  validation run plus the owner's agreement is what changes that.


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
| POST | `/runs` `{ repeats?, useAudio?, scope?, pilot?, sampleIds? }` | Start a run over the marked performances (`sampleIds` limits it to those) |
| GET | `/runs/{id}` | Progress + the report so far (per grade: the diagnostics) |
| POST | `/runs/{id}/next` | Queue the next grade unless a learner's is waiting |
| POST | `/runs/{id}/finalize` | Freeze the report |
