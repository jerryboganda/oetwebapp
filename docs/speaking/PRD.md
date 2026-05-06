# Speaking Module PRD

Status: implementation hardening in progress
Date: 2026-05-06
Owner: OET platform admin/content team
Related docs: ../SPEAKING-MODULE-PLAN.md, ../SCORING.md, ../RULEBOOKS.md, ../CONVERSATION.md, ../../AGENTS.md

## Purpose

Deliver an exam-realistic OET Speaking module where admins can author and publish structured Speaking role-play content, pair role-plays into mock sets, and learners can choose guided practice, lesson-style AI patient practice, or strict mock-exam recording before receiving grounded scoring, transcript review, and expert-review options.

## Current Flow

- Admin role-play authoring exists inside ContentPaper editing and `SpeakingStructureEditor`, but it is not discoverable as a dedicated Speaking authoring area.
- Admin mock-set backend CRUD exists, but the UI exposes create/publish/archive and omits edit/update.
- Learners can browse Speaking tasks, view OET-style role cards, run a mic/check flow, record audio, submit for evaluation, view results/transcripts, and request expert review.
- AI patient self-practice delegates to the Conversation module, but the redirect path currently uses the wrong route shape.
- Two-role-play Speaking mock sessions pre-create paired attempts, but the recorder page does not bind submissions to those paired attempt IDs.
- Speaking audio retention worker exists but is not registered as a hosted service.

## Target Flow

1. Admin opens Admin Content > Speaking Authoring.
2. Admin creates/imports a Speaking content paper, uploads candidate/interlocutor card assets, fills structured candidate and hidden interlocutor fields, records source provenance, validates publish blockers, previews learner/expert views, and publishes.
3. Admin opens Speaking Mock Sets, creates or edits a two-role-play mock set from published Speaking content, publishes or archives it.
4. Learner opens Speaking and chooses one of three modes: role-card practice, lesson-style AI patient practice, or strict mock-exam simulation.
5. Learner sees an OET-style candidate card with prep/role-play timing, accepts recording consent, records audio, and submits.
6. Backend uses owned attempts, storage, transcription/evaluation jobs, rulebook-grounded AI, and canonical `OetScoring` helpers only.
7. Learner receives an estimated score disclaimer, criterion feedback, transcript, next drills, AI-patient practice link, and expert-review handoff.
8. Expert review can see the hidden interlocutor card and audio access remains audited.

## Functional Requirements

- Admin Speaking card authoring must be clearly discoverable from the content hub.
- Admin mock-set UI must support update/edit, not only create/publish/archive.
- Mock-set recording submissions must use the session's paired attempt IDs.
- Self-practice redirects must point to `/conversation/{sessionId}`.
- Learner payloads must never expose hidden interlocutor content.
- Speaking pass logic remains universal 350/500 through scoring helpers; no inline pass/fail shortcuts.
- AI practice/scoring remains routed through existing grounded gateway and Conversation/Speaking services.
- Audio retention must be registered and must not clear DB pointers when blob deletion fails.
- Source samples must be ingested through ContentPaper/assets/provenance, not ad hoc seed content or direct file writes.

## Sample Import Inventory

Attached source folder: `Project Real Content/Speaking_`

- `Card 1 ( Already known Pt )/1.pdf`: scanned/image PDF, no extractable text in the local extractor.
- `Card 2 ( Follow up Visit )/2.pdf`: text extractable, medicine follow-up sample.
- `Card 3_ ( Follow up visit )/3.pdf`: text extractable, medicine follow-up sample.
- `Card 4 ( Examination Card )_ MOST IMPORTANT TYPE/4.pdf`: scanned/image PDF, no extractable text in the local extractor.
- `Card 5 ( First visit - Emergency Card )/5.pdf`: scanned/image PDF, no extractable text in the local extractor.
- `Card 6 ( First Visit )/Card 6.pdf`: text extractable, medicine first-visit sample.
- Shared criteria and warm-up PDFs are present.
- Medicine Speaking rulebook PDF is present.

Import decision: treat these PDFs as source assets with explicit provenance and rights status. For scanned PDFs, OCR/manual authoring is required before structured learner fields can be safely published.

## Acceptance Criteria

- Admin Content Hub has first-class Speaking Authoring and Speaking Mock Sets entries.
- Speaking mock sets can be edited from the admin UI and update the backend row.
- `/v1/admin/content?subtest=speaking` returns only Speaking items and includes `subtestCode` in rows.
- AI self-practice response redirects to `/conversation/{sessionId}` and tests assert the real route.
- Mock-set role-play links carry paired attempt IDs and the recorder submits against those IDs.
- Speaking audio retention worker is registered and tested as part of host startup/service registration.
- Documentation records current gaps, target flow, sample import inventory, and validation evidence.

## Non-Goals For This Slice

- Do not create a second Speaking session/recording/scoring schema.
- Do not bypass ContentPaper, IFileStorage/media storage conventions, rulebook services, or AI gateway.
- Do not embed copyrighted PDF body text into seed data. Store/import as source assets and author structured fields through the admin workflow.
