# Speaking Module — Scoring Model

## Two assessor types

| Type | Role | Authority |
|------|------|-----------|
| `SpeakingAiAssessment` | Advisory AI score (`IsAdvisory = true`) | Reference only — never displayed as official |
| `SpeakingTutorAssessment` | Human tutor score | Authoritative when `IsFinal = true` |

## Criteria (9 total)

### Linguistic (0–6 scale each)

1. **Intelligibility** — pronunciation, stress, rhythm, intonation
2. **Fluency** — smooth speech, suitable speed, minimal hesitation
3. **Appropriateness of Language** — tone, register, vocabulary fit
4. **Resources of Grammar and Expression** — accuracy + range

### Clinical communication (0–3 scale each)

5. **Relationship building** — opening, respect, empathy, non-judgmental
6. **Understanding patient perspective** — ICE elicitation, picking up cues
7. **Providing structure** — sequencing, signposting, summarising
8. **Information gathering** — open → closed questions, clarifying
9. **Information giving** — checking prior knowledge, pausing, chunking, checking understanding

## Projected scaled score

`OetScoring.SpeakingProjectedScaled` aggregates the 9 criteria into a 0–500 scale aligned with OET's published reporting.

```
linguistic_avg = avg(criteria 1..4) / 6        // 0..1
clinical_avg   = avg(criteria 5..9) / 3        // 0..1
projected      = round(((linguistic_avg * 0.55) + (clinical_avg * 0.45)) * 500, -1)  // nearest 10
```

(0.55 / 0.45 weighting is the platform's current heuristic — subject to calibration with assessor feedback.)

## Readiness band mapping

| Scaled score | Band |
|--------------|------|
| ≥ 400 | Strong |
| 350–399 | Exam-ready |
| 300–349 | Borderline |
| 200–299 | Developing |
| < 200 | Not ready |

## Evidence verification

Every AI-supplied criterion rationale must include a verbatim transcript quote. `SpeakingAiAssessmentService` verifies the quote is a substring of `SpeakingTranscript.SegmentsJson`. Non-verifiable quotes flip `ConfidenceBand` to `low` and tag the assessment for tutor review.

## Disclaimer

Every results surface renders `SpeakingComplianceOptions.ScoreDisclaimer`: this is a practice estimate, not an official OET score.
