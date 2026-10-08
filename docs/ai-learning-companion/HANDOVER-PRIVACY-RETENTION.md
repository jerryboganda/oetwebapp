# §15 Handover — Privacy, Retention and Learner Data Control

Covers the learner-facing data promises Sami depends on: what is stored, for how long, what
the learner can do about it, and which of these are configuration rather than code.

> Retention values below are the **shipped defaults** read from source. Every one of them is
> an administrator-editable runtime setting — treat the figure as the current default, not a
> contract, and re-read the named setting before quoting it to a candidate.

## 1. Retention windows (shipped defaults)

| Data | Default | Setting | Note |
| --- | --- | --- | --- |
| Speaking recorded audio | **90 days** | `SpeakingCompliance:RetentionDaysDefault` | Raised to 365 when a tutor has reviewed the recording |
| Speaking audio, tutor-reviewed | **365 days** | `SpeakingCompliance:RetentionDaysWhenTutorReviewed` | |
| Speaking audio (legacy worker) | **365 days** | `SpeakingCompliance:AudioRetentionDays` | Retained for the legacy `SpeakingAudioRetentionWorker` |
| Grader-calibration audio | **365 days** | `SpeakingGraderCalibrationService.CalibrationAudioRetention` | Only audio explicitly selected as an anonymised example; kept for calibration |
| Pronunciation audio | **45 days** | `Pronunciation:AudioRetentionDays` | Reaped by `PronunciationAudioRetentionWorker` |
| Conversation audio | **30 days** | `Conversation:AudioRetentionDays` | Reaped by `ConversationAudioRetentionWorker` |
| Live-voice audio | **30 days** | `LiveVoice:RetentionDays` | |
| Live-class recording retention | **7 days** | `SpeakingLiveAdmission:RetentionDays` | Clamped to 1–90 |
| AiAssistant backup artefacts | **30 days** | `AiAssistant:BackupRetentionDays` | |
| **Audit log** | **2555 days (~7 years)** | `SpeakingCompliance:AuditLogRetentionDays` | Deliberately long: entitlement, billing, rule and knowledge changes must stay auditable |
| Remote job records | **30 days** | `RemoteJobs:JobRetentionDays` | Deferred results 7 days |

Sami **voice notes** ride the conversation/audio retention path. Sami does not operate a live
voice room, so the live-voice windows apply to the separate Speaking products, not to chat.

## 2. What the learner can see, edit, export and delete

Sami exposes memory control through `/v1/companion/*` (authorised `LearnerOnly`), and the UI
surfaces them in the companion panels:

| Capability | Endpoint | Notes |
| --- | --- | --- |
| View memory | `GET /v1/companion/memory` | Entries, plus notes and vocabulary bookmarks |
| View recorded errors | `GET /v1/companion/memory/errors` | Error DNA evidence |
| Export memory | `GET /v1/companion/memory/export` | Machine-readable export of the learner's own memory |
| Delete one entry | `DELETE /v1/companion/memory/entries/{entryId}` | Scoped delete — does **not** wipe unrelated academic history |
| Delete a note | `DELETE /v1/companion/memory/notes/{noteId}` | |
| Delete a vocabulary bookmark | `DELETE /v1/companion/memory/bookmarks/{bookmarkId}` | |
| Reset memory | `DELETE /v1/companion/memory` | Full reset, learner-initiated |
| Teaching preferences | `GET`/`PUT /v1/companion/preferences` | Style, depth, English-only, worked examples |
| Handoff history | `GET /v1/companion/handoffs` | Tutor/support escalations the learner raised |

**Scoped vs full deletion is a real product guarantee**, not just an API shape: deleting one
preference or entry must not remove scores, exam date, study history or errors (SAMI §3.2 and
UAT Pack 2 Test 14). When changing memory code, preserve that distinction.

## 3. Memory isolation

Every memory record is namespaced to the candidate's `user_id`. Cross-user or cross-tenant
memory leakage is a **critical failure** under SAMI §3.2 and §23.1 — it is the single most
serious defect class in this surface. Isolation is enforced by scoping every query to the
authenticated user id; the companion endpoints derive the id from the auth claim and never
from a client-supplied value.

## 4. Sensitive uploads

- `CompanionPiiScreen` screens content **before** a learner-authored note is stored
  (`Services/AiTools/Tools/BuiltInTools.cs` calls it on title and body).
- SAMI §7 requires a warning / redaction / minimisation step for likely patient-identifying
  data before storage. The screen is the enforcement point; the UAT asset for this is
  `TEST ASSET D — Sensitive Data Trap` (fictional but shaped like patient identifiers), used
  by UAT Pack 3 Test 16.
- Extracted exam dates and scores from uploaded files must be **shown to the learner and
  confirmed** before permanent save (SAMI §3.2). This is implemented as the
  `companion_record_scores` → `companion_confirm_scores` pair; the first call only stages a
  proposal held server-side.

## 5. Voice notice and consent

Voice capture carries a notice/consent requirement where the platform performs it. The
learner-facing copy states the retention window for the specific flow (for the AI simulation
the API returns a sentence naming the applicable retention days). Raw audio and transcript
retention are the settings in §1; deleting the session audio is a reaper-worker operation,
and a learner may request earlier deletion through support.

## 6. Data-protection posture still owed externally

These are **not** code gaps and no code change closes them:

- UK GDPR / DPA data-flow inventory and DPIA (TV-027).
- Subprocessor/DPA/vendor-training terms for each AI provider (TV-029).
- Persona-name clearance for **Sami** (TV-030).
- OET trademark / material licensing and disclaimers (TV-031).
- Store-specific AI content reporting and payment/steering behaviour (TV-032).

Register: `TO_VERIFY_AND_DECISION_REGISTER.md`.

## 7. Honest limitations of this document

- Retention defaults were read from source on 2026-10-08; live production values are admin
  editable and were **not** independently read from the production database. Verify against
  `/admin/settings` before quoting to a learner or a regulator.
- No automated test asserts any retention window — the CI QA lanes were removed on
  2026-10-08 (see `HANDOVER-TEST-SETS.md`). Retention regressions would not be caught
  automatically.
- The external legal gates in §6 are unresolved and are not beta blockers under SAMI §1.2,
  but they are public-launch blockers.
