# Pronunciation Module

This document is the canonical contract for the learner-facing Pronunciation module at `/pronunciation`.

## Scope

The module exists to help OET candidates improve:

- phoneme accuracy
- consonant-cluster production
- word stress in medical vocabulary
- sentence stress and rhythm
- clinical intonation patterns

It is a **practice and coaching** subsystem. It does **not** produce an official CBLA score.

## Business rules

1. Learners record audio on a drill page.
2. Audio is scored by the pronunciation ASR pipeline.
3. Scores are persisted per attempt and rolled up per phoneme.
4. Learners receive grounded AI coaching tied to pronunciation rule IDs.
5. Progress drives spaced-repetition recommendations.
6. Speaking-linked pronunciation insights are advisory only.

## Data model

- `PronunciationDrill`
  - content unit the learner practises
  - has `TargetPhoneme`, `Focus`, `Profession`, `PrimaryRuleId`, example words, minimal pairs, sentences, model audio, and tips
- `PronunciationAttempt`
  - upload/scoring lifecycle row for one learner recording
- `PronunciationAssessment`
  - immutable scored output for an attempt
- `LearnerPronunciationProgress`
  - per-user, per-phoneme rolling progress + spaced-repetition state
- `LearnerPronunciationDiscriminationAttempt`
  - aggregate result for the minimal-pair listening game

## Rulebook

Pronunciation rulebooks live under `rulebooks/pronunciation/<profession>/rulebook.v1.json`.

Every grounded AI pronunciation call must use:

- `RuleKind.Pronunciation`
- one of:
  - `AiTaskMode.ScorePronunciationAttempt`
  - `AiTaskMode.GeneratePronunciationDrill`
  - `AiTaskMode.GeneratePronunciationFeedback`

## Provider selection

Configured in `PronunciationOptions.Provider`:

- `azure`
- `gemini`
- `whisper`
- `mock`
- `auto`

`auto` prefers Azure, then Gemini native audio, then Whisper. Mock fallback is allowed only outside production; production fails closed with `PRONUNCIATION_ASR_UNAVAILABLE` when no real provider is configured.

Credential resolution is registry-first for these provider codes:

- `azure-phoneme`
- `gemini-pronunciation-audio`
- `whisper-asr`

`IPronunciationCredentialResolver` caches the registry snapshot for 30 seconds. Admin provider/account create, update, reset, and deactivate operations invalidate the cache immediately so the next learner attempt sees the new credentials.

Gemini native-audio scoring uses:

- `PronunciationOptions.GeminiApiKey`
- `PronunciationOptions.GeminiBaseUrl`
- `PronunciationOptions.GeminiModel`
- AI provider row code `gemini-pronunciation-audio` when managed through the registry

Gemini requests still go through `IAiGatewayService.BuildGroundedPrompt()` and `CompleteAsync()` with inline audio attachments. UI and endpoint code must not call Gemini directly.

## Audio upload constraints

Learner audio uploads are accepted only when the `Content-Type` is in `PronunciationOptions.AllowedMimeTypes` and the stored blob length is at or below `PronunciationOptions.MaxAudioBytes`.

Provider selection happens before blob persistence. If no configured provider is available, the attempt remains `awaiting_upload` and no audio key is stored. Size validation happens after storage length is known; oversized blobs are deleted before the attempt is marked `refused`.

## Scoring contract

Raw pronunciation dimensions are scored 0-100:

- `accuracy`
- `fluency`
- `completeness`
- `prosody`

`overall` is the arithmetic mean of those four dimensions.

The advisory OET Speaking projection uses the canonical anchor table:

- 0 -> 0
- 60 -> 300
- 70 -> 350
- 80 -> 400
- 90 -> 450
- 100 -> 500

Implemented in:

- frontend: `lib/scoring.ts` -> `pronunciationProjectedScaled()` / `pronunciationProjectedBand()`
- backend: `OetScoring.cs` -> `PronunciationProjectedScaled()` / `PronunciationProjectedBand()`

Never compare `overall >= 70` inline at call sites. Use the scoring helpers.

## AI policy

All pronunciation AI calls go through the grounded gateway and produce one `AiUsageRecord` row.

Feature codes:

- `pronunciation.score`
- `pronunciation.linguistic.score.v1`
- `pronunciation.feedback`
- `pronunciation.tip`
- `admin.pronunciation_draft`

## Learner flows

### Drill flow

1. `/pronunciation`
2. pick a drill
3. `/pronunciation/[drillId]`
4. play model audio
5. record
6. submit
7. receive score + coaching + projected Speaking band

### Listening discrimination flow

1. open drill
2. launch `/pronunciation/discrimination/[drillId]`
3. hear A/B minimal-pair rounds
4. submit accuracy

### Speaking-linked flow

Expert-reviewed speaking attempts create advisory pronunciation rows via `PronunciationService.CreateFromSpeakingReviewAsync()`.

Those rows:

- do not replace drill-level ASR scoring
- do not expose fake per-phoneme output
- are surfaced as practice guidance only

## Admin CMS

Admin pages:

- `/admin/content/pronunciation`
- `/admin/content/pronunciation/new`
- `/admin/content/pronunciation/[drillId]`
- `/admin/content/pronunciation/ai-draft`

Publish gate requires:

- non-empty label
- non-empty target phoneme
- non-empty tips
- at least 3 example words
- at least 1 practice sentence

## Accessibility

Minimum requirements:

- labeled filter controls
- recording status announced with `aria-live`
- non-colour-only score communication
- keyboard-operable listening game buttons
- model audio remains playable with native controls

## Retention

Raw learner audio is deleted after `PronunciationOptions.AudioRetentionDays` by `PronunciationAudioRetentionWorker`.

Assessments and progress are retained.

## Verification

Required checks before merge:

- `npx tsc --noEmit`
- `npm run lint`
- `npm test`
- `dotnet test backend/OetLearner.sln`
