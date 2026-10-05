# Speaking Module — API Surface

Auth scopes: `LearnerOnly`, `ExpertOnly`, `AdminOnly` (+ granular admin permissions like `AdminContentRead/Write/Publish`).

## Learner

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/speaking/role-play-cards` | Published cards filtered by `ActiveProfessionId` + universal |
| GET | `/v1/speaking/role-play-cards/{id}` | Single card (404 on profession mismatch) |
| POST | `/v1/speaking/sessions` | Create session. Mode `ai_exam` is refused (409 `speaking_session_exam_managed`): exam cards are created by their exam, which is what takes their credit hold ([state-machines.md](state-machines.md#credits-ai-exam-and-practice-card)) |
| GET | `/v1/speaking/sessions/{id}` | Session detail (owner); carries `liveVoiceAvailable` (at least one live voice provider usable, else the recorder fallback) and `rolePlayEndsAt` (the role-play **deadline**, null until it starts) |
| POST | `/v1/speaking/sessions/{id}/start-warmup` | WarmUp transition |
| POST | `/v1/speaking/sessions/{id}/finish-warmup` | WarmUp → Prep. An AI practice card first passes the live-session admission gate: while the cap is full it answers 200 with the state still `warmup` and `admission {status:"waiting", position, queueLength, estimatedWaitSeconds, pollAfterSeconds}`, with no credit held and no clock started; the page repeats the call ([live-voice.md](live-voice.md#admission-control-live-session-cap-and-wait-queue)) |
| POST | `/v1/speaking/sessions/{id}/start-roleplay` | Prep → Active; stamps `RolePlayStartedAt`, the anchor of every server deadline. Exam cards are timed by the exam clock: 409 `speaking_session_exam_managed` |
| POST | `/v1/speaking/sessions/{id}/end` | Active → Finished (learner). The server also finishes an abandoned Active role-play at its hard stop, see [live-voice.md](live-voice.md#hard-duration-cap) |
| POST | `/v1/speaking/sessions/{id}/submit` | Finished → submitted for marking (needs a recording or a transcript with words) |
| POST | `/v1/speaking/sessions/{id}/consent` | Stamp consent version |
| POST | `/v1/speaking/sessions/{id}/ai-assess` | Run AI assessment (202 `processing` while a recorder-fallback transcript is pending) |
| GET | `/v1/speaking/sessions/{id}/ai-assessment` | Latest assessment |
| GET | `/v1/speaking/sessions/{id}/results` | Grading state: `assessmentState` (processing / completed / failed), `retryable`, `failureReason`, `isFreeSample`, `cardId`, `usesV11` and `inputKind` (`"recording"` \| `"live_voice"` \| `null`: what the learner handed in, which decides the wording of the results pages, see [live-voice.md](live-voice.md#results-wording-by-input-kind)) |
| GET | `/v1/speaking/sessions/{id}/clock` | Server clock: stage, `secondsRemaining`, `expired` and, for an Active session only, `hardStopAt` (deadline + grace) |
| POST | `/v1/speaking/sessions/{id}/recording` | Recorder fallback upload (multipart `audio`, optional `durationSeconds`); 202 `{status:"received"}`; a repeat is 409 `recording_already_received` (the only 409 the client treats as success); after the write window a first upload is 409 `live_voice_transcript_window_closed` |
| POST | `/v1/speaking/sessions/{id}/technical-issue` | Flag a technical issue |
| GET | `/v1/speaking/sessions/{id}/transcript` | Latest transcript |
| GET | `/v1/speaking/realtime/sessions/{id}/preflight[?provider=]` | Live voice: disclosure plus `candidates` (providers to try in order: primary first, health only filters) and `pinned`. `?provider=` is only a request: `pinned` is true only when the server honoured a QA pin, that is when a provider was requested **and** the signed-in learner holds an enabled `speaking_live_voice_pin:<learner user id>` feature flag (one candidate, no failover, circuit bypassed). Any other account gets 200, the normal order and `pinned: false`, never a 403 or 503 ([live-voice.md](live-voice.md#qa-provider-pin)) |
| POST | `/v1/speaking/realtime/sessions/{id}/openai/offer` | Live voice: WebRTC SDP exchange; returns `hardStopAt`. Any provider failure is a generic 503 |
| POST | `/v1/speaking/realtime/sessions/{id}/gemini/token` | Live voice: single-use ephemeral token; returns `expiresAt` and `hardStopAt` |
| POST | `/v1/speaking/realtime/sessions/{id}/turns` | Live voice: advisory per-turn row (a failure never blocks the transcript) |
| POST | `/v1/speaking/realtime/sessions/{id}/transcript` | Live voice: the whole transcript for grading; frozen once grading has taken it |
| GET | `/v1/speaking/mock-sets` | Published mock sets |
| GET | `/v1/speaking/mock-sessions/{id}` | Mock session state |
| POST | `/v1/speaking/mock-sessions/{id}/bridge/start` | Finished1 → Bridge |
| POST | `/v1/speaking/mock-sessions/{id}/bridge/finish` | Bridge → Prep2 |
| GET | `/v1/speaking/drills` | Drill catalogue |
| POST | `/v1/speaking/drills/{id}/attempts` | Start drill |
| POST | `/v1/speaking/drills/attempts/{aid}/recordings` | Upload audio |
| POST | `/v1/speaking/drills/attempts/{aid}/score` | AI scoring |
| GET | `/v1/speaking/pathway` | Recommended drills |
| GET | `/v1/speaking/recordings/mine` | Own recordings |
| DELETE | `/v1/speaking/recordings/{id}` | Delete own recording |
| GET | `/v1/speaking/consents/me` | Consent history |
| POST | `/v1/speaking/consents` | Record consent |
| GET | `/v1/speaking/live-rooms/{id}` | Room detail |
| GET | `/v1/speaking/live-rooms/{id}/token` | Mint LiveKit JWT |
| POST | `/v1/learner/account/erasure-preflight` | GDPR pre-flight inventory |
| GET | `/v1/me/attempts[?limit=&subtest=]` | Candidate activity history across all subtests (the History page's "Attempt activity" list; `limit` defaults to 100, clamped 1-200). A Speaking mock is one row, and Speaking rows carry `resultLabel` ([History notes](#history-notes)) |
| GET | `/v1/submissions[?cursor=&limit=&subtest=]` | Past Evidence list (the History page's submission cards). Speaking attempts that belong to a session or an exam and have no Evaluation row are no longer listed ([History notes](#history-notes)) |

## Expert / tutor

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/expert/speaking/queue` | Review queue |
| POST | `/v1/expert/speaking/queue/{sessionId}/claim` | Claim session |
| POST | `/v1/expert/speaking/queue/{sessionId}/release` | Release claim |
| POST | `/v1/expert/speaking/sessions/{id}/tutor-assessment` | Create draft |
| POST | `/v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}/submit` | Submit |
| POST | `/v1/expert/speaking/sessions/{id}/comments` | Timestamped comment |
| GET | `/v1/expert/speaking/sessions/{id}/assessments` | Dual (AI + tutor) |
| GET | `/v1/expert/training` | Interlocutor training modules |
| GET | `/v1/expert/calibration/samples` | Calibration samples |
| POST | `/v1/expert/calibration/samples/{id}/scores` | Submit score |

## Admin

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST/PUT/DELETE | `/v1/admin/speaking/cards/*` | Card CRUD + publish |
| PUT | `/v1/admin/speaking/scripts/{cardId}` | Upsert interlocutor script |
| POST | `/v1/admin/speaking/cards/ai-draft` | AI-draft a card |
| POST | `/v1/admin/speaking/cards/batch` | Not implemented (no endpoint is mapped) |
| GET/POST | `/v1/admin/speaking/drills/*` | Drill CRUD + AI-draft |
| GET/POST | `/v1/admin/speaking/mock-sets/*` | Mock set CRUD + auto-pair |
| GET/POST | `/v1/admin/speaking/shared-resources/*` | Warm-up + criteria PDFs |
| GET/POST | `/v1/admin/speaking/calibration/*` | Calibration samples + drift |
| GET | `/v1/admin/speaking/calibration/drift` | Per-tutor MAE report |
| GET | `/v1/admin/speaking/analytics/{slug}` | Dashboard queries |
| GET | `/v1/admin/speaking/recordings/audit` | Recording-access audit log |
| POST | `/v1/admin/speaking/recordings/{id}/access` | Log + grant access |
| GET | `/v1/admin/ai/live-voice/health` | Live voice provider health: catalog probe, circuit, last failure, counters (`AdminAiConfig`) |
| POST | `/v1/admin/ai/live-voice/{provider}/reset` | Close a provider's circuit (`openai` \| `gemini`); audited as `LiveVoiceProviderCircuitReset` |
| GET | `/v1/admin/ai/live-voice/admission` | Live AI session admission gate: effective cap, kill switch, admitted / waiting counts (`AdminAiConfig`) |
| PUT | `/v1/admin/ai/live-voice/admission` | Set the live-session cap (1..10000) and/or the kill switch; audited as `SpeakingLiveAdmissionSettingsUpdated` ([live-voice.md](live-voice.md#admission-control-live-session-cap-and-wait-queue)) |
| GET | `/v1/admin/ops/snapshot` | Read-only load snapshot: job queue depth by type, DB connections by application name, admission counts, remote-worker placeholder (`AdminSystemAdmin`) |

## Live voice notes

- The five `/realtime/sessions/{id}/...` routes and `start-roleplay` share the `AiLiveSpeaking` limiter: one in-flight
  request per user, the next is answered 429 `rate_limited`.
- Live voice error codes, retryable flags and the failover rules the client applies:
  [live-voice.md](live-voice.md#provider-failover). Numbers (deadline, hard stop, write window, session cap):
  [live-voice.md](live-voice.md#hard-duration-cap); keys: [../env/speaking.md](../env/speaking.md).
- The pin gate applies to the preflight only. The create routes (`openai/offer`, `gemini/token`) are not gated, so a signed-in
  candidate can still choose between two healthy providers: [live-voice.md](live-voice.md#qa-provider-pin).
- Learner-visible texts that changed with `inputKind` (the two live voice consent refusals and the two grading failure
  reasons) are listed in [live-voice.md](live-voice.md#results-wording-by-input-kind).

## History notes

Added 2026-10-01 (**verified in production on 1 Oct 2026**: the E2E runs read the History row of every mock and practice card
they made (`historyListsExam`: one "Full Speaking Mock" row, 4 credits, a results route and a label), and `/v1/submissions` of the
shared QA learner lists no session-bound Speaking row). `GET /v1/me/attempts?limit=&subtest=` returns
`items[{attemptId, subtest, title, contentRef, startedAt, submittedAt, status, balanceSource, creditsUsed, route, resultLabel}]`
and feeds the History page's "Attempt activity" list. The Speaking rows:

- **A full AI mock is one row.** `attemptId` = the exam id (`spx_...`), `subtest` `speaking`, `title` "Full Speaking Mock",
  `contentRef` = the exam id, `startedAt` = the exam's `IntroStartedAt`, `submittedAt` = its `CompletedAt`, `status` `completed`
  for Completed, Cancelled and Expired (else `in_progress`), `route` `/speaking/exam/{id}/results` once finished or
  `/speaking/exam/{id}` while running. `balanceSource` and `creditsUsed` are summed from the ledger rows whose reference starts
  `exam:{examId}:` (4 for a normal mock; 1 and `mock` for a mock-unit exam). The two card attempts are not listed separately, and
  an exam appears once a card attempt exists.
- **`resultLabel`**: `"N/500"` once scored (the persisted combined snapshot, else the rounded average
  `(int)Math.Round((a + b) / 2.0)` of the two graded cards, classic assessment or complete v1.1 card report), `"Marking in progress"`
  while the exam is Completed but not both cards are graded, otherwise `null`. A live-tutor (human-marked) exam is always `null`.
- **A standalone Speaking card** (practice, live voice or recorder, including the legacy recorder's bridge session) is one row as
  before: `route` `/speaking/sessions/{sessionId}/results` once the session is Finished or submitted, else
  `/speaking/sessions/{sessionId}`; credits are matched on the hold reference `practice:{sessionId}`; `resultLabel` is `"N/500"`
  when an assessment exists, `"Marking in progress"` when finished but not graded and not tutor-marked, else `null`. A legacy
  Speaking attempt with no session is unchanged (route `/speaking`, credit match on the content id, `null` label).
- `creditsUsed` sums debits as written, less the refund of a released Speaking hold (a `RefundOnFailure` row whose reference is
  `{hold reference}:...:release`: a cancelled or never-graded card), so a refunded mock does not read 4; a fully refunded row has
  no `balanceSource`. Other subtests still count a refunded hold. Only the learner's latest 600 debit rows are read, so a very old
  exam can show 0 credits.
- The exam's two card attempts are left out of the generic attempt query in SQL ahead of the page's `Take`, and exams are read from
  `SpeakingExamSessions`, so a mock never shows twice and never eats the page size. The new anti-joins read `SpeakingSessions` by
  `AttemptId`, which has no index; add one if the History or Past Evidence queries show up slow.
- **History page.** The page at `/submissions` (also `/history`) prints `resultLabel` right after the status in each row: a score
  such as "192/500" in bold navy, any other label in bold warning tone, nothing when it is null or missing. The page never builds
  or invents the label (the server owns the text), and the Review/Resume button links to the server's `route` unchanged. A
  missing, null or empty `resultLabel` simply shows nothing, so an older server keeps working.

`GET /v1/submissions?cursor=&limit=&subtest=` ("Past Evidence") no longer lists Speaking attempts that belong to a session or an
exam and have no Evaluation row (they could only ever read "Pending"). Legacy recorder submissions stay, including those with a
bridge session next to their Evaluation. The exclusion is applied in the page query (the LINQ path and the SQLite raw-SQL path),
so cursor and limit stay exact. A candidate whose only activity is Speaking therefore sees their mock under Attempt activity only.

## Webhooks (unauthenticated, HMAC-verified)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/v1/speaking/live-rooms/webhooks/livekit` | LiveKit event ingestion |

## SignalR hubs

| Hub | Path | Methods |
|-----|------|---------|
| `ConversationHub` | `/v1/conversation/hub` | Warm-up + role-play turn loop |
| `SpeakingLiveRoomHub` | `/v1/speaking/live-rooms/hub` | `JoinRoom`, `LeaveRoom`; events `CueRaised`, `LiveRoomSnapshot`, `LiveRoomEnded` |
