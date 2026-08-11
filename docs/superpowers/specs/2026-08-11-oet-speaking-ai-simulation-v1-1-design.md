# OET Speaking AI Simulation and Assessment v1.1 Design

## Status

Approved design direction for implementation under the active OET Speaking goal. The implementation is website/computer-based only, as required by the source specification. The source PDF remains the acceptance authority.

## Objective

Implement the complete Speaking AI Simulation & Assessment Specification v1.1 without changing the meaning of existing Speaking, tutor-review, or official-result safeguards.

The system must keep two AI roles separate:

- During preparation and scored role play, the AI is only the configured patient, relative, or other interlocutor. It must not coach, score, reveal hidden tasks, correct the candidate, praise performance, or provide real-world medical advice.
- After the role play ends, the AI is only the assessor. It produces transcript evidence, criterion scores, an AI Estimated Practice Score, confidence, feedback, and a practice plan. It must never present the estimate as an official OET result.

## Non-negotiable source requirements

- Full mock: warm-up, two cards, three-minute preparation, and five-minute role play per card.
- Warm-up is unscored and excluded from every card and combined score.
- The assessment is audio-based. Eye contact, gestures, and facial expression are never core scoring inputs.
- Card tasks guide the encounter and may be completed in a clinically natural order.
- Hidden persona facts never reach the candidate UI, learner-visible transcript metadata, or actor wording outside configured reveals.
- Card-to-card persona memory resets by default. Carry-over is allowed only when Card 2 has an explicit author-controlled second-visit flag and an explicit approved indicator; only approved carried facts may carry forward.
- The phrase “your patient” alone never enables follow-up memory.
- Every evidence item has exactly one primary scoring criterion. Teaching feedback may mention a moment under multiple criteria, but only the primary criterion can affect a score.
- Audio, ASR, technical quality, language performance, scoring, and human correction remain separate evidence dimensions.
- Every score is labelled `AI Estimated Practice Score — not an official OET result` and uses a graph visually distinct from the official OET graphic.
- Rulebook Rule 55 is excluded from Speaking runtime behaviour until its relevance is explicitly resolved.

## Existing architecture anchors

The implementation reuses the existing canonical boundaries rather than creating a second session or recording product:

- `SpeakingExamSession` orchestrates the two-card state machine and server-authoritative timestamps.
- Child `SpeakingSession` rows own each card’s recordings, transcripts, AI/tutor projections, consent, and technical-issue state.
- `SpeakingRecording` and `MediaAsset` remain the secure original-audio and storage path.
- `SpeakingTranscript` remains the latest transcript pointer and stores speaker-labelled segments with timestamps and confidence.
- `ConversationHub.SpeakingRoleplay` remains the real-time actor path.
- Existing grounded AI gateway, rulebook services, AI usage ledger, audit services, tutor review, and OET scoring helpers remain mandatory dependencies.
- Existing nine-criterion legacy/tutor contracts remain readable and stable. v1.1 uses a versioned assessment contract so historical rows and tutor workflows are not silently reinterpreted.

## Decomposed implementation workstreams

### Workstream A — Simulation lifecycle and actor

Extend the existing exam/session controller for guided practice, independent practice, and full mock. Persist the spec release, card version, profession pack, persona snapshot, timing policy, and actor prompt version at card reveal. The server remains authoritative for preparation, role-play, closure, expiry, reconnect, and controlled technical retake.

The actor receives only the current card’s hidden runtime snapshot and the current-card conversation state. It uses short ping-pong turns, natural backchannels, emotion, uncertainty, patient-led ordering, configurable silence prompts, and scenario-safe facts. It never receives Card 1 transcript data while Card 2 is independent. For an approved follow-up Card 2, it receives only the author-approved carried-facts projection.

The client displays only the candidate card, visible timer, safe captions, connection state, technical status, and consent state. Hidden prompt text, prohibited facts, model instructions, and private persona metadata are never serialized into learner projections or transcript metadata.

### Workstream B — Transcript, technical evidence, and assessment

Extend the transcription/evaluation boundary to produce speaker labels, segment timestamps, word confidence where available, fillers, pauses, false starts, repeated phrases, interruptions, overlap, monologue duration, jargon, empathy opportunities, permission, checking understanding, recap, closure, and card-task coverage.

Create immutable v1.1 assessment snapshots containing:

- card and combined assessment status (`pending`, `complete`, `technical_review`, or `invalid`);
- transcript and audio-quality references;
- per-evidence timestamp, exact quote, detected behaviour, source rule, assessment state, confidence, suggested improvement, and exactly one primary criterion;
- per-criterion score, maximum, configured weight, rationale, evidence references, confidence, strength, weakness, and one action;
- per-card and combined score, grade band, confidence label/range, and calibration/release identifiers;
- task map, communication timeline, language report, time-management metrics, top five improvements, better alternatives, tips, and practice-plan recommendations.

Low ASR confidence, clipping, missing audio, severe noise, or pipeline latency beyond the approved budget marks the affected card for technical review. It cannot reduce a language score and cannot produce a fabricated combined score.

The v1.1 rubric is configurable and seeded with the PDF’s recommended initial weights, which sum to 100%:

| Criterion | Weight |
| --- | ---: |
| Intelligibility & pronunciation | 10% |
| Fluency & continuity | 12% |
| Grammar & vocabulary | 8% |
| Appropriateness / plain language | 10% |
| Relationship building & empathy | 14% |
| Patient perspective | 10% |
| Information gathering | 10% |
| Information giving & checking | 12% |
| Structure & task management | 9% |
| Closure & time management | 5% |

The release stores the rubric version and calibration state with every assessment. Scores are unavailable for release when the rubric has not passed the approved calibration gate.

### Workstream C — Results, feedback, and tutor review

Add a learner-safe v1.1 result projection and responsive result surface. It must show one final /500 estimate for a valid full mock, both card scores, graph, band, confidence, criterion breakdown, task map, timeline, language report, timing report, top five improvements, timestamp-linked alternatives, targeted tips, practice plan, and playable audio at each evidence timestamp.

The graph uses platform-owned colours and typography, not the official OET green band-strip convention. The required disclaimer is persistent on the graph and in the result summary. A technical-review card shows the reason and controlled-retake/review action instead of a guessed score.

Tutor comments and overrides preserve the original AI assessment, evidence, calibration state, and audit trail. Tutor/admin access is limited by assignment or explicit permission and all access/changes are logged.

### Workstream D — Authoring, governance, privacy, and operations

Extend Speaking authoring and review with candidate card, hidden persona, allowed/prohibited facts, reveal conditions, task map, scenario pack, difficulty, assessment anchors, timing, profession pack, card/persona/rubric/model versions, explicit second-visit flag, and explicit second-visit trigger phrase. Publish validation blocks incomplete or unsafe cards.

Add release governance for rubric weights, calibration evidence, profession packs, silence prompts, confidence presentation, graph legal sign-off, latency/concurrency/cost budgets, retention policy, and Rule 55 status. Missing owner approval is represented as a release-blocking state, not silently replaced with an unapproved production value.

Enforce the PDF roles matrix: candidates see only their attempts; tutors see assigned candidates; content authors cannot access candidate PII; clinical reviewers see flagged/disputed content; language assessors see benchmark/calibration data; admins have audited full access; support receives ticket-linked, time-limited access.

Use the existing consent, encrypted storage, retention worker, deletion/erasure, AI usage, audit, and provider-health paths. Keep audio/transcript retention explicitly visible at consent and prevent model-training reuse without separate lawful consent.

Record per-turn latency, STT/LLM/TTS usage, cost estimate, concurrency bucket, degradation state, and release identifiers. The system must alert on cost or latency budget breaches and mark the session `technical_review` rather than silently degrading turn-taking.

## Owner-approval gates

The source PDF intentionally leaves several release values to the owner. The code will implement the configuration and fail-closed gate for each item:

- The recommended starting AI turn-start target is 2.5 seconds p95, but production activation requires an approved concurrency target and SLA record.
- A positive maximum concurrent full-mock-session budget is required before live mode is enabled.
- A positive per-completed-attempt cost ceiling covering STT, actor LLM, assessor LLM, and TTS is required before live mode is enabled.
- Non-Medicine scoring remains blocked until an approved profession pack and interim behaviour decision exists.
- Silence prompts, confidence label/range presentation, graph legal/brand approval, and retention duration require explicit versioned approvals.
- Rule 55 remains excluded until an owner decision changes its status.

Development fixtures may exercise each branch, but an unapproved branch cannot be presented as a production-ready release.

## Acceptance evidence

Automated regression coverage must directly prove S-01 through S-18 from the PDF, including:

- exact two-card timing and warm-up exclusion;
- actor-only behaviour and hidden-persona protection;
- short turns, barge-in/overlap preservation, transcript confidence, and technical fairness;
- jargon, empathy, interruptions, fillers, task coverage, recap, closure, and timing evidence;
- primary-criterion uniqueness and no double-counted score deductions;
- card, combined, confidence, and graph disclaimer outputs;
- bad-news behaviour;
- technical-review/no-fabricated-score handling;
- audio-only scoring;
- profession-pack isolation;
- timestamp playback;
- audited tutor override;
- explicit follow-up memory and “your patient” negative classification;
- approved concurrency/SLA latency measurement.

Focused frontend, backend, migration-shape, API-contract, and authenticated browser checks must be run for the touched workstream. Production completion additionally requires the deployed SHA, migration history, web/API health, and owner-side authenticated browser/provider acceptance.

## Deliberate exclusions

- No mobile/native delivery is included in this PDF implementation.
- No official OET score or official OET graphic is produced.
- No visual behaviour is scored.
- No Rulebook Rule 55 behaviour is added.
- No parallel session, recording, or storage schema is introduced.
- No secrets, provider credentials, or customer recordings are accessed during implementation.
