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

### Acoustic evidence — Intelligibility is judged from the sound

Intelligibility is a property of the *sound* of speech; a transcript cannot carry it. When the admin feature flag
`speaking_audio_assessment` is on, every Speaking grade first runs the **audio stage**
(`SpeakingAudioEvidenceService`, feature code `speaking.audio_assess`), then hands its findings to the grader:

1. **Clips.** The candidate's own stored clips: a live-voice session's per-turn clips (the segments' `sourceRecordingId`,
   in the order spoken; archived and warm-up clips never), or a recorder session's recording. The patient's voice is
   not part of them. Nothing is written to disk: each clip is streamed from `IFileStorage` through `ffmpeg`
   (stdin → stdout) to 16 kHz mono, a 600 ms silence is put between clips, the join is cut at 6 minutes and encoded as
   one 48 kbps mp3 (`SpeakingAudioTranscoder`; the API image installs `ffmpeg`).
2. **The judge.** One call per card to the OpenAI audio-chat model (`gpt-audio-1.5` by default, editable on the
   `openai-audio` provider row), pinned to that row and **never** the Speaking grade chain, so the Claude Max grade
   route is untouched. It is sent the audio and a narrow brief — and **never the transcript**, so it cannot read the
   answer off the text. It returns what it heard in the first words, an Intelligibility score 0–6 on the official band
   descriptors with a rationale and observations (clip, second, what), fluency *evidence* (rate, long pauses,
   hesitations, fillers, restarts) and its own confidence. Temperature 0; the credential is the already-funded OpenAI
   key (`LIVEVOICE__OPENAIAPIKEY`), used when the row carries none of its own.
3. **Verification.** The words it says it heard are compared with the first candidate turn of the transcript
   (first twelve words; one contained in the other, or at least 40 % shared). A mismatch (silence, wrong audio, a made-up
   judgement) discards the audio result: `audio_unverified`. A poor recording, a second voice (patient bleed) or a
   judgement that covers only part of the speech keeps the score but sets the stage's confidence to low.
4. **Feeding the grade.** The grader receives an "ACOUSTIC EVIDENCE" block; **the audio score replaces its own
   Intelligibility**, its Fluency stays its own (the fluency evidence only informs it). The stage's findings are stored
   beside the criterion rationales (`_acoustic`, no migration) and the grade's `GraderVersion` carries
   `audio-openai.v1:{model}`.

**No usable audio is not a failure** (owner decision 4 Oct 2026). Every problem — no clip kept, a missing blob, too
short, unusable, unverified, the transcoder or the provider failing, a refusal, a timeout — becomes
`unavailable:<reason>` and grading goes on from the transcript: Intelligibility is the grader's text-only estimate, the
grade's confidence is **low**, and the result says plainly *"Estimated from the transcript only (limited evidence)"*
with the reason in words (`SpeakingIntelligibilityEvidence`, shown under the Intelligibility row). A grade that has no
stored audio judgement — stage off, or an older grade — is labelled the same way. Grading never fails because of audio.

**Release gate.** The stage is dark until a probe passes. `POST /v1/admin/speaking/audio-assess/probe` (admin, multipart:
`audio` + `phrase`) runs a clip through the same pipeline and reports what the model heard, the similarity to the phrase,
the score and observations; the manual workflow `speaking-audio-probe.yml` sends synthetic clips (no learner audio) and
requires: the model heard the clean clip (similarity ≥ 0.6) and judged it; a clip with **no speech** is not accepted as a
judgement (the judge invents nothing); and the same speech buried in noise is not trusted like the clean one (refused,
flagged as a weaker recording, or scored lower). Only then is the flag turned on. Accented synthetic voices are printed for
information only: the first live probe (4 Oct 2026, `gpt-audio-1.5`) judged German-, Spanish- and French-voice renderings
of English as 6/6 like the clean voice, because a synthetic voice is fully intelligible, so Intelligibility discrimination on
real accented speech is **not** established by the probe. The probe is plumbing and sanity evidence; accuracy against human
experts is what [calibration](#calibration) measures (audio-sourced Intelligibility error is one of its pass criteria).

### Grader version

Every score records `GraderVersion` = `{prompt template}|{mapping version}|{audio stage}` (for example
`speaking.score.v3|speaking-map.v0-heuristic|audio-none`, or `…|audio-openai.v1:gpt-audio-1.5` for a grade whose
Intelligibility was judged from audio). A score is `provisional` until that exact version — together with the grading
model — has passed calibration; changing the prompt, the mapping, the audio model or the audio stage starts a new,
uncalibrated version.

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

A Full Speaking Mock covers Card A and Card B and is assessed as **one performance**: one set of nine criterion scores for
the whole test, one reported score out of 500, one grade. It is **never** the average of two card scores.

1. Each card is graded as before (`speaking.grade`, with its own audio stage when that is on). Those grades carry the
   per-card breakdown, the stored audio evidence and the credit settlement: the held credits are committed as soon as both
   cards are graded, whether or not step 2 has finished.
2. When the second card's grade lands, one durable operation is queued (`AiOperation`, resource type `speaking_exam`,
   idempotency key `speaking.assess.exam:{examId}`). The worker runs `SpeakingAiAssessmentService.RunCombinedAssessmentAsync`:
   the same grader core (`GradeCoreAsync`), prompt rubric, band descriptors and reply schema, with **both** transcripts in
   one user message (connection-check chatter removed) and the instruction to score each criterion once for the whole test.
   Neither card's score is in the prompt. It goes through the same pinned Claude Max chain as a card grade.
3. Audio: no extra audio call. The grader gets the one acoustic evidence built from the two card grades' stored
   `_acoustic` blocks. Both cards judged from audio: Intelligibility is the mean of the two judgements, a half rounding up
   (`OetScoring.SpeakingCombinedIntelligibility`), the observations of both are kept (marked "Role-play 1/2") and confidence is
   the weaker of the two. Anything less (audio for one card only, none, or the stage off) is labelled "estimated from the
   transcript only (limited evidence)" with low confidence, never half an audio judgement presented as audio.
4. The result is stored on `SpeakingExamSessions.CombinedAssessmentJson` in the shape of a card's assessment row, so one
   projection reads both; `CombinedScaledSnapshot` / `ReadinessBandSnapshot` hold the reported number and band for History.
   Its grader version is `speaking.score.v3-combined|…`: a different prompt is a different grader, so the combined judgement
   stays "provisional" until it has been calibrated on its own.

`GET /v1/speaking/exams/{id}/results` returns `combinedAssessment` (the same projection a card has: nine criteria with
explanations, report, evidence, grade, label) and `combinedState`: `ready`, `pending` (both cards graded, the whole test is
being judged; no overall number yet and the cards' own numbers are not shown, so a card number is never mistaken for the
result), `failed` (`POST /v1/speaking/exams/{id}/combined-assess` re-queues it, no charge) or `legacy` (an exam that
finished before this existed keeps its averaged number; v1.1-graded exams are averaged as they always were). Live-tutor exams
are human-marked and unchanged. History shows the same stored number, or "Marking in progress" while the combined judgement is
running: it never averages two cards.

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

Dr Hesham marks real performances blind to the AI score (nine criteria plus an overall score out of 500) in
Admin > Speaking > Grader calibration, and a manual harness (`speaking-grader-calibration.yml`) grades the same
performances and reports per-criterion error, grade and pass/fail agreement and repeatability. The pass thresholds
were approved by the owner on 2026-10-05 and are strict; see `docs/speaking/grader-calibration.md`. A monotone fit of
his own raw-total to overall pairs replaces `SpeakingRawToReported` only after a run passes and the owner agrees.
Until then every candidate-facing Speaking score stays "Provisional" and `SpeakingCalibratedGraders` stays empty.
No grader version has been calibrated yet; the combined Full Mock grader needs its own calibration on two-card
performances.

## Disclaimer

Every results surface renders `SpeakingComplianceOptions.ScoreDisclaimer`: this is a practice estimate,
not an official OET score.
