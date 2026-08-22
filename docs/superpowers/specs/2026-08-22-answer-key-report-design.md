# Answer Key Report Design

## Goal

Give Reading and Listening candidates a clear, post-submit way to report a
potentially incorrect official answer, and give content admins a dedicated
queue to review those reports and correct the key. This is not the in-exam
Flag bookmark, not Ask AI, and not Escalations.

## Problem

After submit, candidates can see their answer beside the official key. There
is no first-class path to tell an admin “this official answer looks wrong.”
Personal Flag is a private bookmark. Learner Escalations has no admin reader
for this case. Admin Escalations is a different product.

## Scope

In:

- Official Reading paper results (`/reading/paper/{paperId}/results`).
- Official Listening results (`/listening/results/{id}`) and transcript
  review (`/listening/review/{id}`).
- Learner POST/GET on the submitted attempt.
- Admin queue at `/admin/content/answer-reports`.
- Deep links into the existing question editors and scoring-system re-mark
  page. Admins correct keys with existing authoring; they do not override a
  score from this queue.

Out:

- Changing Flag / Flagged-for-review semantics.
- LearnerEscalations / `/admin/escalations`.
- Writing or Speaking.
- Mock-session results (no reliable official-attempt mapping in v1).
- Pre-submit key disclosure.
- Mass regrade of every attempt on the paper.
- Listening expert score override.

## Candidate flow

1. Candidate submits a Reading or Listening attempt.
2. On the results / review card they see **Report this answer**, separate
   from Flag and Ask AI.
3. Modal: reason chips (`wrong_official_answer`, `missing_accepted_variant`,
   `other`) plus optional details (max 2000).
4. Disclosure: “This does not change your mark immediately.”
5. Confirm creates a report. Toast success. Button becomes **Reported**.
6. Duplicate pending report for the same user + question + attempt is
   blocked (HTTP 409). Marks do not change.

## Admin flow

1. Content Home / sidebar **Answer reports** opens
   `/admin/content/answer-reports` (`content:read`).
2. Dense Hallmark table with status + assessment filters.
3. Row shows paper title, part/question, candidate display name (never
   email), learner answer snapshot, official answer snapshot, reason,
   details, created time, status.
4. Actions: Investigate / Resolve / Dismiss with an optional resolution
   note. Terminal statuses (`resolved`, `dismissed`) are locked.
5. Links: question editor for that paper/question, and
   `/admin/content/scoring-system` if a one-attempt re-mark is needed after
   the key is fixed.
6. Every create/update writes `AuditEvent`.

## Data model

`AssessmentAnswerKeyReport`:

| Field | Notes |
| --- | --- |
| Id | `akr-{guid:N}`, varchar 64 |
| Assessment | `reading` \| `listening` |
| AttemptId, PaperId, QuestionId | varchar 64 |
| QuestionNumber | int snapshot |
| PartCode | varchar 16 snapshot (`A` / `B1` / …) |
| QuestionStemSnapshot | varchar 512 |
| PaperTitleSnapshot | varchar 200 |
| ReporterUserId | varchar 64 |
| LearnerAnswerSnapshot | varchar 512 |
| OfficialAnswerSnapshot | varchar 512 |
| ReasonCode | see candidate flow |
| Details | varchar 2000, optional |
| Status | `open` \| `investigating` \| `resolved` \| `dismissed` |
| ResolutionNote | varchar 2000, nullable |
| ResolvedByAdminId, ResolvedAt | set on terminal status |
| CreatedAt, UpdatedAt | UTC |

Indexes: `(Status, CreatedAt)`, `(Assessment, Status)`,
`(ReporterUserId, Assessment, QuestionId, AttemptId)`. Pending uniqueness is
enforced in the service (409 `answer_key_report_already_open`) because
filtered unique indexes are fragile on the InMemory test provider.

## APIs

Learner (`LearnerOnly`, own submitted attempt):

- `POST /v1/reading-papers/attempts/{attemptId}/answer-reports`
- `GET  /v1/reading-papers/attempts/{attemptId}/answer-reports`
- `POST /v1/listening-papers/attempts/{attemptId}/answer-reports`
- `GET  /v1/listening-papers/attempts/{attemptId}/answer-reports`

Body: `{ questionId, reasonCode, details? }`.

Errors: 404 unknown/other-user attempt or question not on that paper; 400
not submitted / invalid reason; 409 pending duplicate. Snapshots capture
the learner answer and current official key at report time (keys are
already visible on review).

Admin (`content:read` list/detail, `content:write` update):

- `GET   /v1/admin/answer-key-reports?status=&assessment=&limit=`
- `GET   /v1/admin/answer-key-reports/{id}`
- `PATCH /v1/admin/answer-key-reports/{id}` `{ status, resolutionNote? }`

Admin payloads include display name, editor URL, and scoring-system URL.
They never include reporter email.

## Correction path

1. Admin opens the editor from the queue and fixes `CorrectAnswerJson` /
   accepted variants with existing authoring.
2. Optional: create/approve/execute a scoring-system re-mark for the named
   attempt. This queue does not itself regrade.

## Privacy and safety

- Never serialize reporter email.
- Never leak official keys on unsubmitted attempts.
- Keep Flag as a personal bookmark.
- Ask AI stays advisory and does not create a report.
