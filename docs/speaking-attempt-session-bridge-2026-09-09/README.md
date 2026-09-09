# Speaking Attempt→Session grading bridge — 2026-09-09

## What was found

A full learner acceptance pass over the Speaking role-play module (the
follow-up to `docs/speaking-corpus-reclassification-2026-09-09/`) found that
the **only reachable learner UI for Speaking practice** — the Selection page
(`/speaking/selection`) → card preview (`/speaking/roleplay/[id]`) → recorder
(`/speaking/task/[id]`) → `submitSpeakingRecording()` — has produced a
**permanently failed grade for every real submission** since 2026-08-30
(commit `ba5df294ad`, "W7 Speaking canonical assessment, turn idempotency,
credit reserve").

That commit correctly built a new canonical, session-based AI assessment
pipeline (`SpeakingAiAssessmentService`, real Whisper ASR, real per-criterion
scoring) but also *hard-disabled* the older Attempt-based grading path it was
replacing — `SpeakingEvaluationPipeline.CompleteEvaluationAsync` began
returning `evaluation.State = Failed`, `StatusReasonCode =
"canonical_speaking_required"`, message *"Legacy attempt-based Speaking
grading is disabled. Submit through the typed Speaking session flow."* —
for any submission with no linked `SpeakingSession`. The recorder UI at
`/speaking/task/[id]` was never migrated to create one (it still calls
`POST /v1/speaking/attempts/{id}/submit`, the older Attempt API), and there
is no learner-facing button anywhere that calls `POST /v1/speaking/sessions`
(that typed-session flow's own UI, `/speaking/sessions/[id]/*`, was built
for and is only linked from `app/admin/onboarding/interlocutor` — staff
interlocutor-trainee rehearsal). The result: a fully built, polished
recording UI (native mobile recorder, exam auto-submit timer, CBT
paper-destruction compliance step, mock-exam integration) whose every
submission silently dead-ends.

Restoring the *old* attempt-based scorer was rejected — it graded a
**fabricated mock transcript** (`BuildMockDevelopmentTranscript`, explicitly
labelled "mock/dev... configure a production ASR provider before using
transcript text as final learner evidence"), not the candidate's actual
speech. Shipping that back would mean real learners receiving real-looking
scores for words they never said.

## The fix

`SpeakingEvaluationPipeline.CompleteEvaluationAsync` now bridges an
Attempt-based submission into the same canonical pipeline the typed-session
flow already uses, instead of failing unconditionally:

1. If the submission has real, uploaded audio (`Attempt.AudioObjectKey`)
   and a real (non-mock) `ISpeakingTranscriptionProvider` is available,
   transcribe it for real (OpenAI Whisper — the same provider/interface the
   typed-session flow uses, `Services/Speaking/OpenAiWhisperSpeakingProvider.cs`).
2. Synthesize a `SpeakingSession` + `SpeakingTranscript` row wrapping that
   real transcript (mirrors, but does not call,
   `SpeakingSessionService.CreateSessionAsync` — the Attempt already paid
   its AI-package credit at creation, so the bridge must not reserve a
   second one).
3. Run the existing `SpeakingAiAssessmentService.RunAssessmentAsync` against
   it — the exact same AI-grounded, rubric-clamped scorer the typed-session
   flow uses. No second scoring implementation.
4. Map the result onto the legacy `Evaluation`/`Attempt` columns
   (`ScoreRange`, `CriterionScoresJson`, `ConfidenceBand`,
   `ModelExplanationSafe`, `Attempt.AnalysisJson.speakingBand`) so
   `GetSpeakingEvaluationSummaryAsync` — the endpoint the results page reads
   — renders a real score. (This mapping was also missing for the
   *pre-existing* linked-session branch; fixed for both paths at once.)

If no real ASR provider is configured, or transcription fails, the
submission now fails **honestly and retryably** — `StatusReasonCode =
"speaking_transcription_unavailable"`, `Retryable = true`, a message the
learner can act on — instead of the old message that told them to use a
flow with no button. It never falls back to grading a mock transcript.

## Known external dependency

Real grading requires a configured Whisper (or equivalent) ASR credential.
In production, `AiProviders` registry row `whisper-asr` (the credential this
bridge and the admin/session transcription pipeline resolve through) has
**no API key configured** as of this writing — confirmed via direct
production DB query. The sibling *Conversation* module (the live AI-patient
flow) has its own, separately configured key
(`CONVERSATION__WHISPERAPIKEY` in `.env.production` on the VPS), so it is
unaffected. Until an admin sets a key for `whisper-asr` (Admin → AI
Providers, or `PUT /v1/admin/runtime-settings` → `SpeakingWhisper`), this
bridge degrades to the new honest/retryable failure rather than grading —
by design, never to a fabricated score.

## Tests

`backend/tests/OetLearner.Api.Tests/Speaking/SpeakingEvaluationPipelineTests.cs`
(new) pins: the linked-session path now populates real score data; the
bridge succeeds end-to-end against a real (non-mock) ASR fake; the bridge
fails honestly/retryably when only the mock provider is available (never
grades off a fabricated transcript) and when transcription throws; no orphan
`SpeakingSession`/`SpeakingTranscript` rows are left behind on failure; the
pre-existing live-tutor human-review routing is unchanged.
