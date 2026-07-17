# Listening Grading Model

**Status:** Authoritative. Read this before changing any code path that touches
Listening scoring, expert review, or grade display.

## Summary

OET Listening is **fully auto-graded**. There is no human reviewer in the
normal grading path. Submitted attempts are scored server-side by
`ListeningGradingService.GradeAsync` using the author-provided answer keys.

The only human-in-the-loop endpoint that exists for Listening is a feature-
flagged **kill-switch** to override a single answer's score post-submission. It
is disabled in production and has no UI.

## Grading rules per part

| Part | Items | Type | Marking |
|------|------:|------|---------|
| A    |    24 | Short-answer (note-completion) | Strict normalised exact match against the author key + author-supplied synonyms. No partial credit. |
| B    |     6 | 3-option MCQ (one per workplace extract) | Exact label or text match against the marked-correct option. |
| C    |    12 | 3-option MCQ (six per presentation) | Same MCQ rule as Part B. |
| **Total** | **42** | | Raw score → scaled via `OetScoring.OetRawToScaled`. |

**Mission-critical anchors** (`backend/src/OetWithDrHesham.Api/Services/OetScoring.cs`):
- Raw 0 → scaled 0
- Raw 30 → scaled 350 (Grade B pass)
- Raw 42 → scaled 500

## Why no expert review for Listening

Listening is fully objective: every item has a single correct answer in the
authoring schema. There is no rubric to apply, no draft to revise, no quality
score to peer-review. Building a "review bundle" UI like Writing/Speaking would
produce dead pixels — the expert would have nothing to assess.

This is why `app/expert/` has `writing/` and `speaking/` review subdirectories
but **no `listening/` subdirectory** by design.

## Expert score-override (kill-switch only)

A single endpoint exists for disputes / authoring errors discovered after
publish:

```
PUT /v1/expert/listening/attempts/{attemptId}/questions/{questionId}/score-override
```

It is **feature-flagged off by default**. See
`backend/src/OetWithDrHesham.Api/Endpoints/ExpertEndpoints.cs` and the
`Features:ListeningExpertOverride` configuration key.

Defaults:
- `appsettings.json`            → `false`
- `appsettings.Development.json` → `true` (developer convenience)
- `appsettings.Production.json`  → `false` (explicit)

Set to `true` in `appsettings.<env>.json` or the environment variable
`FEATURES__LISTENINGEXPERTOVERRIDE=true` only if the operator deliberately
wants to allow manual overrides. The endpoint returns 404 when the flag is
off — defence in depth so even if a stale expert UI tried to call it the
request fails cleanly.

When enabled, the endpoint requires:
1. Caller is in role `expert`.
2. Expert is assigned to a `ReviewRequest` covering the attempt
   (enforced by `EnsureReviewerAssignedToAttemptAsync` in
   `ListeningGradingService.cs`).
3. Request body carries an audit reason.

Override audit lives on `ListeningAttempt.HumanScoreOverridesJson`.

## Publish-gate invariants

`ListeningStructureService.ValidatePaperAsync` enforces these before a paper
can leave Draft:

- 24 A + 6 B + 12 C = 42 items.
- A1/A2 each have exactly 12 short-answer items.
- C1/C2 each have exactly 6 MCQ items.
- Part A items are **short-answer only** — MCQ in Part A is rejected.
- Part B/C items are single-select MCQ with exactly 3 options.
- Every wrong MCQ option carries a canonical distractor category.
- Every question carries non-blank stem + correct answer + canonical skill tag
  + transcript evidence text with start/end timestamps + difficulty 1-5.
- Every extract carries audio start/end cue points + difficulty 1-5.
- Paper carries `SourceProvenance` with a `legal=...` token from
  `LegalAttestationValues` (e.g. `legal=original-authoring-attested`).
- All four required asset roles are attached and primary: `Audio`,
  `QuestionPaper`, `AudioScript`, `AnswerKey`.

## Practice / drill papers

Drill papers (mini-tests, error-bank drills, starter papers) ship with fewer
than 42 items by design. The learner UI on
`app/listening/results/[id]/page.tsx` detects `maxRawScore < 42` and renders a
"Practice Score" frame (percent correct) instead of the canonical OET grade
chrome — the 30/42 → 350/500 → Grade B mapping does not apply outside full
papers.

## TTS provider selection

Section audio is produced by `ListeningTtsService` using whichever provider is
registered in `Program.cs` under the `Listening:TtsProvider` configuration
switch:

- `"stub"` — emits silence (no creds, dev/CI only). **Production startup
  fails** if the value resolves to `"stub"`, so an operator cannot
  accidentally ship a silent paper to a learner.
- Real providers (`"qwen"`, `"openai"`, `"azure"`, …) plug into the same
  `IListeningTtsSynthesisProvider` seam. Add the registration above the
  `default:` case in the DI switch in `Program.cs` and set
  `Listening:TtsProvider` to the matching key.

## Citations

- `PRD-LISTENING-V2.md` — full product spec.
- `docs/LISTENING.md` — module overview.
- `docs/LISTENING-RULEBOOK-CITATIONS.md` — rulebook references.
- `backend/src/OetWithDrHesham.Api/Services/OetScoring.cs` — scaled-score anchors.
- `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningGradingService.cs` — auto-grading + override path.
- `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningStructureService.cs` — publish gate.
