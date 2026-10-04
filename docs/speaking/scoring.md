# Speaking Module — Scoring Model

Owner spec: *Speaking Module — Final Corrections & Grading Calibration Requirements* (4 Oct 2026).
Grading accuracy is the launch-critical requirement; the numbers below are **provisional until the
calibration report passes** (see [Calibration](#calibration)).

## Criteria — the nine official OET criteria

The only scoring model. The ten weighted "v1.1" criteria (one of them, closure/time management, is not
an OET criterion) no longer score anything: `SpeakingCanonicalAssessmentService.UsesV11Async` is true
only for a session that already has a complete v1.1 report, so old reports stay readable.

### Linguistic (0–6 each)

1. **Intelligibility** — pronunciation, stress, intonation, rhythm
2. **Fluency** — speed, flow, hesitation, repetition
3. **Appropriateness of Language** — register, tone, lay-language explanation
4. **Resources of Grammar and Expression** — range and accuracy

### Clinical communication (0–3 each)

5. **Relationship building**
6. **Understanding and incorporating the patient's perspective**
7. **Providing structure**
8. **Information gathering**
9. **Information giving**

Medical or clinical knowledge accuracy is **not** a scoring criterion. A confusing or wrong clinical
explanation is penalised only through the communication criterion it affects (Information giving or
Appropriateness), never as a separate deduction. Rulebook rules guide how evidence is read; they never
create a second penalty on top of a criterion score.

## How the grader is told to score (`speaking.score.v3`)

The grounded system prompt (`RulebookPromptBuilder`, Speaking + Score only) carries:

- **The official OET band descriptors**, rendered from `rulebooks/speaking/common/assessment-criteria.json`
  under the codes the grader replies with (`grammarExpression` = "Resources of Grammar and Expression",
  `structure` = "Providing structure", …). They decide each number; before v3 the prompt had only one-line
  glosses and never saw them.
- **"How to score" principles**: score each criterion holistically against its descriptor; the rulebook is
  interpretation guidance and **never a separate deduction** (a rule's "costs marks" wording describes its own
  criterion); one event counts against at most **one** criterion; **medical or clinical knowledge accuracy is
  not assessed** — a confusing or mistaken explanation lowers only the communication criterion it affects
  (Information giving, or Appropriateness); connection talk is not performance; candidate-facing text never
  contains rule IDs or internal codes.
- The rulebook headed "Key rules (interpretation guidance — not deductions)" (the old heading read "violations
  are auto-mark-deductions"), including the Breaking Bad News / follow-up rules for cards of those types
  (`SpeakingAiAssessmentService.RulebookCardToken`; the grader used to pass the generic `role_play` token, so
  card-scoped rules never reached their cards).
- A reply contract that defers to the JSON in the user message: nine criterion scores with rationales and
  verbatim quotes. The model is **not** asked for a score, grade or readiness band — the server derives all
  three. Writing keeps its own prompt unchanged.

Safety net: `SpeakingLearnerText.ScrubRuleIds` removes any `RULE_nn` the model still writes from the rationales
and summary a candidate reads; the stored text is left as written.

### Graded evidence

The graded transcript starts at the real role-play. The opening connection check ("Hi, can you hear me?" /
"Yeah, I hear you. Go ahead.") is removed from the grader input, the quote check, the interruption signals and
the learner-facing marked transcript by `SpeakingTranscriptEvidence.StripConnectivityChatter`. Only *leading*
connection-check sentences go, so a real greeting and anything said later is always kept; the stored segments
and their hash are never altered.

### Grader version

Every score records `GraderVersion` = `{prompt template}|{mapping version}|{audio stage}` (for example
`speaking.score.v3|speaking-map.v0-heuristic|audio-none`). A score is `provisional` until that exact version —
together with the grading model — has passed calibration; changing the prompt, the mapping or the audio stage
starts a new, uncalibrated version.

## The reported score — one number, everywhere

`OetScoring.SpeakingReportedScaled` (mirrored by `speakingReportedScaled` in `lib/scoring.ts`):

```
raw      = sum of the nine criterion scores        // 0..39 (4 × 0–6 + 5 × 0–3)
reported = SpeakingRawToReported[raw]              // 0..500, always a multiple of 10
```

The reported score is what every learner surface shows, and the letter grade, the readiness band and
the pass line (350) are all derived from that same number — a score displayed as 350 can never carry a
"Borderline" band. A stored score from before this change (unrounded, e.g. 345) is re-derived at read
time (`OetReportedScaledScore`), never shown as stored.

| Reported score | Grade |
|---|---|
| 450–500 | A |
| 350–440 | B |
| 300–340 | C+ |
| 200–290 | C |
| 100–190 | D |
| 0–90 | E |

There is no "B+" candidate-facing grade.

### The mapping is a platform heuristic

`SpeakingRawToReported` (version `speaking-map.v0-heuristic`) is **not** the OET conversion formula. v0
is exactly the former heuristic — the anchor table is linear (`scaled = 5 × percentage`), so
`reported = round-to-10(round(500 × raw / 39))` — written out as a literal so the C# and TypeScript tables
can be compared entry by entry (`SpeakingScoreMappingParityTests`). The two production results that
prompted the spec (308 and 231) were raw 24/39 and 18/39; they now read **310** and **230**.

A calibration fit (below) replaces the literal in both files and bumps `SpeakingMappingVersion`.

### Provisional until calibrated

The server labels every score `provisional` until the grader version that produced it has passed
calibration against expert-labelled performances, and only then `ai_practice_estimate`
(`OetScoring.SpeakingScoreLabel`, driven by `SpeakingCalibratedGraders`, empty until a report passes). A
payload with no label is treated as provisional. The result pages show "Provisional score — calibration in
progress" beside the number while it is provisional.

## Full Mock

A Full Speaking Mock covers Card A and Card B and reports **one** final score out of 500 with **one**
grade; card breakdowns sit beside it. Today the combined number is the average of the two cards'
stored scores, reported in 10-point steps (`SpeakingExamService.GetResultsAsync`); it moves to a single
combined two-card criterion judgement in a later increment.

## Readiness band

Derived from the **reported** score:

| Reported score | Band |
|---|---|
| ≥ 420 | Strong |
| 350–410 | Exam-ready |
| 300–340 | Borderline |
| 250–290 | Developing |
| < 250 | Not ready |

## Evidence verification

Every AI-supplied criterion rationale should include a verbatim transcript quote.
`SpeakingAiAssessmentService` checks each quote against `SpeakingTranscript.SegmentsJson` and logs a
warning for a quote it cannot find; the rationale is still stored.

## Calibration

Nothing yet compares AI scores with expert marks. The plan: Dr Hesham marks real performances blind to the
AI score (nine criteria plus an overall score out of 500) in an admin screen; a harness grades the same
performances and reports per-criterion error, grade and pass/fail agreement and repeatability; a monotone
fit of his own raw-total → overall pairs replaces `SpeakingRawToReported`. Only after that report passes
does a grader version enter `SpeakingCalibratedGraders` and the "provisional" label come off.

## Disclaimer

Every results surface renders `SpeakingComplianceOptions.ScoreDisclaimer`: this is a practice estimate,
not an official OET score.
