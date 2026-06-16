# Listening authoring — content workflow

How a Listening paper goes from blank canvas to a published mock that
learners can attempt. Covers the relational model, the AI-extraction
fast path, the per-question editor, and the TTS-backed audio pipeline.

## Paper layout

A Listening paper has a fixed 5-part / 42-question shape:

| Part | Questions | Audio kind | Question type | Max raw |
|---|---|---|---|---|
| A1 | 1–12 | Consultation (~5 min) | `ShortAnswer` | 12 |
| A2 | 13–24 | Consultation (~5 min) | `ShortAnswer` | 12 |
| B | 25–30 | 6× workplace extracts (~40 s each) | `MultipleChoice3` | 6 |
| C1 | 31–36 | Interview / presentation (~6 min) | `MultipleChoice3` | 6 |
| C2 | 37–42 | Interview / presentation (~6 min) | `MultipleChoice3` | 6 |

Total: 42 raw points. Map to scaled via `OetScoring.OetRawToScaled` — never
inline math (see `ListeningScoringPathAuditTest`).

## Authoring surfaces

| Step | Surface |
|---|---|
| Create paper shell | `POST /v1/admin/papers` (existing) with `subtestCode = "listening"`. |
| Replace whole structure | `PUT /v1/admin/papers/{id}/listening/structure` — accepts the full 42-question payload. |
| Edit one question | `PATCH /v1/admin/papers/{id}/listening/structure/{qid}` — bumps `ListeningQuestion.Version` for any meaningful change (stem, correct answer, options, accepted variants). |
| Synthesize extract audio | `POST /v1/admin/listening/extracts/{id}/synthesize` (planned — Wave 4 of the gap-fill plan). |
| Validate publish gate | `GET /v1/admin/papers/{id}/listening/validate` — author-facing diagnostics. |

The list page at [`app/admin/content/listening/page.tsx`](../../app/admin/content/listening/page.tsx)
lets authors browse and filter papers. Per-question editing is being added
under `app/admin/content/listening/[paperId]/questions/[qid]/page.tsx`.

## Author-supplied fields per question

| Field | Required | Notes |
|---|---|---|
| `Stem` | yes | The question text the learner sees. Markdown allowed for emphasis. |
| `CorrectAnswerJson` | yes | Single canonical answer. For MCQ, the option key (`"A"` / `"B"` / `"C"`). |
| `AcceptedSynonymsJson` | no | JSON array of variants. UK/US spelling, abbreviations, singular/plural. **OET expects exact phrasing — keep this list tight.** |
| `CaseSensitive` | no | Default `false`. Set true only for acronyms or proper nouns. |
| `ExplanationMarkdown` | yes (post-submit) | Why the right answer is right. Surfaced after submit. |
| `TranscriptEvidenceText` | yes | Verbatim audio excerpt that supports the answer. Drives the review-page evidence quote. |
| `TranscriptEvidenceStartMs` / `EndMs` | recommended | Ms offsets within the section audio. Powers the ▶ Replay button. |
| `SkillTag` | recommended | Free-form code (e.g. `numbers_units`, `cause_effect`, `speaker_attitude`). Drives drill targeting. |
| `SpeakerAttitude` | Part C only | `Concerned` / `Optimistic` / `Doubtful` / `Critical` / `Neutral` / `Other`. |
| `DifficultyLevel` | optional | 1–5; surfaced to admins, used by pathway weighting. |

MCQ options additionally carry:

| Option field | Notes |
|---|---|
| `OptionKey` | `A`, `B`, or `C`. |
| `Text` | What the learner sees. |
| `IsCorrect` | Exactly one option per question is correct. |
| `DistractorCategory` | For wrong options: `TooStrong` / `TooWeak` / `WrongSpeaker` / `OppositeMeaning` / `ReusedKeyword` / `OutOfScope`. Drives the distractor heatmap analytics. |
| `WhyWrongMarkdown` | Author-written copy explaining why the distractor is wrong. Surfaced after submit. |

## Transcript JSON

`ListeningExtract.TranscriptSegmentsJson` is an array of:

```json
[
  { "startMs": 0, "endMs": 3500, "speakerId": "s1", "text": "Good morning, Mrs Akin." },
  { "startMs": 3500, "endMs": 7400, "speakerId": "s2", "text": "Morning, doctor." }
]
```

Authors don't write this by hand — it comes out of the AI extraction
pipeline (`GroundedListeningExtractionAi`) or the TTS-aligned synthesis
job. Hand edits should preserve `startMs < endMs` and contiguous-or-gap
segments only.

## AI extraction draft workflow

1. Author uploads a transcript or pastes one into the extraction form.
2. `POST /v1/admin/papers/{id}/listening/extract` calls
   `GroundedListeningExtractionAi`, persists the proposal as
   `ListeningExtractionDraft` (status `Pending`).
3. Admin reviews the proposed 42-question structure in the draft preview.
4. On approval, `ListeningAuthoringService.ReplaceStructureAsync` writes
   the relational rows; the same validation + audit trail applies as for
   manual edits.
5. Reject path records the decision reason for downstream tuning.

## TTS pipeline (Wave 4 — planned)

- Synthesis uses the ElevenLabs-backed `IListeningTtsSynthesisProvider`
  ([`Services/Listening/ElevenLabsListeningTtsSynthesisProvider.cs`](../../backend/src/OetLearner.Api/Services/Listening/ElevenLabsListeningTtsSynthesisProvider.cs)),
  selected when `Listening:TtsProvider=elevenlabs`. The dev/CI default is the
  silence `stub`.
- Provider: ElevenLabs (the platform's only TTS vendor). It requests raw
  `pcm_16000` audio; the configured API key / default voice live in admin
  Voice Design settings.
- Per-segment synthesis → ms-aligned silence padding to honour
  `AudioStartMs` / `AudioEndMs` boundaries → upload via `IFileStorage` →
  write SHA + URL onto `ListeningExtract.AudioContentSha` (column to be
  added when Wave 4 lands).
- Background job pattern matches `ListeningAttemptExpireWorker`: poll
  table for `Pending`, 50-batch, 3 retries with exponential backoff.

## Test integrity

- Original content only — do not paste real OET test items. The official
  candidate declaration forbids reuse of test content; the audit at
  [`backend/tests/OetLearner.Api.Tests/Listening/`](../../backend/tests/OetLearner.Api.Tests/Listening/)
  has no source-content check, so this is enforced by editorial process.
- Healthcare-generic only. Listening is not profession-specific; do not
  introduce profession-specific terminology in Part A consultations.
