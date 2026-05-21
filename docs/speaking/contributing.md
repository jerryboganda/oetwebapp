# Contributing to the Speaking Module

Three audiences contribute here: **content authors**, **engineers**, and **tutors**. Each has its own rules of engagement.

## Content authors (role-play cards, drills, calibration samples)

- **Originality**: no leaked or recalled OET test material. The platform's batch generator includes a Levenshtein originality guard (≥ 0.85 similarity blocks save); manual additions must also be original.
- **Profession accuracy**: pair each card with a clinically realistic scenario. If unsure, ping the clinical reviewer assigned to the profession.
- **Emotional realism**: each card needs a realistic patient emotion (`PatientEmotion`) with `ResistanceLevel` set appropriately. Avoid trivial "patient agrees with everything" flows — that is not testable speech.
- **Interlocutor scripts**: never leak script content into the candidate-facing card. The two are stored separately for a reason.
- **Lifecycle**: Draft → Reviewed → Published. Only `AdminContentPublish` permission can publish.

## Engineers

- **Tests first**: every new endpoint needs an integration test (`backend/tests/OetLearner.Api.Tests/Speaking/`). Every new page needs an a11y spec (`tests/a11y/`). UI flows that cross routes need an E2E spec (`tests/e2e/`).
- **Migrations**: one migration per concern. Never combine schema changes with data backfill in the same migration — use a follow-up data-migration script.
- **AI prompts**: any change to a prompt template that affects scoring must be reviewed by the AI lead. Cache-control headers must be set on persona/system blocks; without them the multi-turn cost balloons.
- **Compliance gates**: never bypass `SpeakingComplianceConsent` checks. Recording deletion must always write an `AuditEvent` row.
- **Feature flags**: ship behind `Features__SpeakingV2`. Stage → 5% → 25% → 100% rollout. Document the rollout in `docs/speaking/changelog.md`.

## Tutors

- **Calibration cadence**: complete at least one calibration sample per week. Drift report on the admin dashboard tracks per-tutor MAE.
- **Queue hygiene**: claim a session only if you can finish the assessment within 60 minutes. Idle claims auto-release after 15 minutes.
- **Live-room conduct**: stay in role. Never coach. Use cue buttons from the interlocutor script — do not invent prompts outside the card's resistance window.
- **Assessment quality**: 9 criterion scores + overall markdown + ≥ 1 timestamped comment are required before submit. Vague feedback ("Improve fluency") will be flagged.

## PR checklist

Use `.github/PULL_REQUEST_TEMPLATE/speaking.md` for every Speaking-scoped PR.
