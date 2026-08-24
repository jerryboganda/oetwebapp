---
name: oet-drill-author
description: Drafts OET Speaking role-play cards and short drills for controlled admin authoring and review.
---

# Speaking Drill Author - Rules Package

Sources of truth in this repository:

- `docs/speaking/ai-providers.md` — routes: card.draft.v1, drill.draft.v1 (admin authoring tools; ephemeral caching).
- `backend/src/OetLearner.Api/Services/Speaking/` — role-play card models, speaking session loop, dual-grader v2.
- `lib/rulebook/speaking-rules.ts`.

## Card anatomy (required fields)

profession, scenario (one line), patientOpening (first patient utterance), cues (fixed content hints), expectedCues (stages), difficulty A/B/C.

## Authoring discipline

- Realistic clinical depth within a 2-4 minute role-play: history-taking A, counselling/education B, complex management C.
- Language plain and natural for a real patient/carer; never academic.
- No factual contradictions between cues and the working diagnosis; no invented findings that would be examiner-visible as errors.
- Drills: one task per card, explicit learner verb task ("Ask the patient about..."), immediate feedback loop with model utterances.
