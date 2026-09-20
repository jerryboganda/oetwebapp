# Live Interlocutor v1.1 implementation evidence

## Status

**NEW UNIT BOUNDARY ONLY — NOT HUB-INTEGRATED / NOT TESTED / NOT ACCEPTED.**

This patch adds only a pure server-side disclosure/projection/rendering boundary plus focused xUnit coverage. It does not wire the boundary into `ConversationHub.SpeakingRoleplay`, does not change provider routing, and does not enable numeric scoring.

Both old Point 5 claims remain unsupported by implementation/acceptance evidence and must not be represented as completed.

## Boundary implemented in this patch

- Seven explicit interlocutor role classes: Patient, RelativeOrCarer, AnimalOwner, Client, Colleague, Examiner, Interviewer.
- Server facts are explicitly classified as AlwaysEligible, Conditional, or NeverDisclose.
- Conditional disclosure requires a server-owned satisfied-condition identifier. Learner/provider text is not interpreted as authorization and no regex/topic guessing is used.
- NeverDisclose facts are never copied into model-safe context, even when a similarly named condition is satisfied.
- Model-safe context contains only safe policy version, case identity, role class, and eligible facts with approved statement variants. It contains no withheld fact IDs, counts, values, condition IDs, disclosure classes, or full-snapshot reference.
- Model output is an ID-selection contract. The deterministic renderer accepts only eligible fact IDs, approved statement variant IDs, and approved non-factual response kinds. Unknown/ineligible/duplicate selections fail closed; arbitrary model text is not a renderable field.
- Projection copies eligible collections so later mutation of snapshot collections cannot alter an already projected model context.

## Tests authored, not executed locally

Focused xUnit tests cover all seven roles; NeverDisclose precedence; conditional before/after explicit server state; incomplete conditional policy; duplicate/malformed fact IDs; hidden-metadata absence from serialized model DTO; withheld/unknown renderer selections; exact-statement rendering; duplicate selection rejection; and rejection of arbitrary freeform text in the model response contract.

Per repository policy, no build/test/lint/typecheck/generation command was run locally. CI evidence is still required before any tested/accepted claim.

## Authoritative amendments carried forward, not claimed implemented here

- Latency amendment: **1.5 s / 250 ms final**.
- Provider amendment: **3 independent provider stacks, configurable, with benchmark default selection**.
- Retention amendment: **scoped 30-day audio retention, separate from other retention policy**.
- Scoring amendment: **numeric scoring remains disabled until both human-validation and expert-validation gates are satisfied**, independent of owner/release booleans.
- Specification amendment: **the Universal 480 specification is authoritative but currently unavailable to this implementation task**. No canonical 480 requirement IDs are invented here.

Provider-stack, latency, retention, hub-integration, factual-validation-after-generation, speech-output, and numeric-scoring gate behavior are therefore pending future implementation and evidence.

## Temporary numeric lock added in second patch

**TEMPORARY NUMERIC LOCK — NOT CALIBRATION-GATE COMPLETE / NOT TESTED / NOT ACCEPTED.**

`SpeakingSimulationV11AssessmentService` now fails closed for v1.1 AI numeric scoring until the authoritative human-calibration and expert-validation evidence contract exists and has been implemented. This is an intentional code hold, not a configurable release/owner boolean and not evidence that calibration has been completed.

- New v1.1 numeric assessment generation is stopped before release-gate evaluation, scoring computation, or provider work.
- Previously persisted card assessments are blocked by the central response projection whenever numeric fields or report JSON are present.
- Previously persisted combined assessment reports are blocked by the same projection boundary.
- A blocked response never substitutes `0` for an unavailable score and does not expose score ranges, confidence numerics, nested criterion scores/bands, or the persisted report object.
- Existing technical/no-score envelopes remain able to project when they contain no numeric/report payload.
- Existing session/card/profession validation and the unrelated LiveTutor human-examiner path remain ahead of the new lock. Endpoint authentication/ownership code was not changed by this patch and must remain enforced by its existing entry points.
- No release approval, owner flag, profession approval, or other existing configuration can unlock this temporary hold. A real unlock pathway must wait for the missing authoritative validation contract; this patch deliberately does not invent one.

Five focused xUnit tests were authored for the hard lock, cached projection, combined persisted report suppression, absence of configurable unlock inputs, and preservation of pre-lock session-id validation. They have not been executed locally because repository policy requires compute verification in GitHub Actions.

The disclosure boundary remains a separate new unit boundary and is still **not hub-integrated**. Full Universal 480 compliance remains blocked because the authoritative 480 specification is still unavailable to this task.
