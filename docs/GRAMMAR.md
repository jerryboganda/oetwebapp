# OET Grammar Module — Canonical Design Contract

> **MISSION-CRITICAL.** All grammar logic (topic discovery, lesson delivery,
> exercise grading, mastery tracking, recommendation mapping) MUST route
> through `lib/grammar/*` (TypeScript) and
> `OetLearner.Api.Services.Grammar.*` (.NET). Never inline grading, raw
> string matching, or `dangerouslySetInnerHTML` against admin-authored
> content anywhere else in the codebase.

---

## 1. Mission

Give every learner a structured, OET-aligned path to close the grammar
gaps that stop them passing. The module must:

1. **Deliver structured lessons** organised by topic, level, and exam family.
2. **Grade exercises server-authoritatively.** The client never knows the
   correct answer until after submission.
3. **Integrate with evaluation surfaces.** When a Writing or Speaking
   evaluation flags a grammar rule, it must surface the relevant grammar
   lesson. When an exercise is answered wrongly, a `ReviewItem` must be
   created for spaced repetition.
4. **Reward effort.** Every completed lesson awards XP and counts toward
   the `Grammar Guru` achievement. Mastery is factored into the overall
   readiness score.
5. **Respect paywalls.** Free tier is capped by `FreeTierConfig`.
6. **Operate offline on mobile.** Published lessons and attempt submission
   must survive network loss.

---

## 2. Domain model

```
GrammarTopic                   (curatorial bucket)
 └─ GrammarLesson              (N:1 with GrammarTopic, supports versioning, prerequisites)
      ├─ GrammarContentBlock   (sanitised structured blocks — never raw HTML)
      └─ GrammarExercise       (typed; correct answer lives server-side only)
           └─ GrammarExerciseAttempt  (per submission, per learner)

LearnerGrammarProgress         (per learner × lesson — existing, extended)
GrammarRecommendation          (derived from writing/speaking evaluation flags)
```

### Entities

| Entity | Purpose |
|---|---|
| `GrammarTopic` | Exam-scoped bucket (`oet/tenses`, `oet/articles`, …). Owns display name, description, sort order, level hint, status. |
| `GrammarLesson` | Extends the existing table with `TopicId`, `Version`, `PublishState`, `SourceProvenance`, `PrerequisiteLessonIds` (JSON), `UpdatedAt`. Legacy `Category`/`Status` columns retained for back-compat. |
| `GrammarContentBlock` | `{ order, type: 'prose'|'callout'|'example'|'table'|'note', contentMarkdown, contentJson }`. Rendered through a **sanitising** renderer; admin HTML paste is stripped before persistence. |
| `GrammarExercise` | `{ order, type, promptMarkdown, optionsJson, correctAnswerJson, acceptedAnswersJson, explanationMarkdown, difficulty, points }`. Types: `mcq`, `fill_blank`, `error_correction`, `sentence_transformation`, `matching`. |
| `GrammarExerciseAttempt` | `{ userId, lessonId, exerciseId, userAnswerJson, isCorrect, pointsEarned, attemptIndex, createdAt }`. Immutable analytics row per attempt. |
| `LearnerGrammarProgress` | Extended with `MasteryScore` (0-100), `AttemptCount`, `LastAttemptedAt`. |
| `GrammarRecommendation` | `{ userId, lessonId, source: 'writing'|'speaking'|'diagnostic', sourceRefId, ruleId, relevance, createdAt, dismissedAt }`. |

### Publish gate

A lesson may only transition to `Published` when:

1. `TopicId` is set, and that topic is itself `Published`.
2. At least **1** `GrammarContentBlock` exists.
3. At least **3** `GrammarExercise` rows exist.
4. Every exercise has a non-empty `correctAnswerJson` and
   `explanationMarkdown`.
5. `SourceProvenance` is non-empty.
6. No block or exercise contains the literal token `TODO` or `TBD`.

Publish attempts that fail the gate respond with HTTP 409 and a
structured `publishErrors` array.

---

## 3. Exercise grading contract

**Server is authoritative.** The learner-facing endpoints listed in §4
project lessons through separate DTOs that **never** serialise
`correctAnswerJson`, `acceptedAnswersJson`, or `explanationMarkdown`
until the learner has submitted the exercise.

### Grading strategies by type

| Type | Comparison |
|---|---|
| `mcq` | Exact match on `optionId`. |
| `fill_blank` | Canonicalise (trim, lowercase, collapse whitespace) → exact match against `correctAnswerJson[]`. `acceptedAnswersJson` adds synonym list. |
| `error_correction` | Canonical compare against corrected sentence list. |
| `sentence_transformation` | Canonical compare; optional regex pattern in `acceptedAnswersJson.regex`. |
| `matching` | All pairs must match; partial-credit allowed (`points × correctPairs / totalPairs`). |

`GrammarGradingService.GradeAttemptAsync(lessonId, userId, answers)`:

1. Loads the lesson's exercises.
2. Applies the per-type strategy.
3. Returns `GrammarGradingResult { Score, Correct, Incorrect,
   PerExercise[], MasteryScore, RecommendedNext }`.
4. Writes a `GrammarExerciseAttempt` row per exercise.
5. Updates `LearnerGrammarProgress.MasteryScore` using an exponential
   moving average (weight 0.4 on latest score).
6. For each incorrect exercise, creates a `ReviewItem` with
   `SourceType="grammar_error"` (spaced repetition).
7. If `MasteryScore ≥ 80`, emits `grammar_lesson_mastered` domain event →
   XP award + achievement checker.

### XP / gamification hooks

| Event | XP | Source |
|---|---|---|
| Lesson start | 0 | analytics only |
| Exercise answered correctly | 2 | `grammar_exercise_correct` |
| Lesson completion (any score) | 10 | `grammar_lesson_completed` |
| Lesson mastered (≥ 80 %) | 15 | `grammar_lesson_mastered` |
| Topic mastered (all lessons ≥ 80 %) | 50 | `grammar_topic_mastered` |
| Achievement `grammar_lessons_5` unlock | 100 | checker in `AchievementService` |

---

## 4. Endpoints

### Learner (`/v1/grammar/*`, `RequireAuthorization("LearnerOnly")`)

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/v1/grammar/overview` | Topics + progress + recommendations summary |
| `GET` | `/v1/grammar/topics` | List topics (filter by `examTypeCode`, `level`) |
| `GET` | `/v1/grammar/topics/{slug}` | Topic detail + lesson list |
| `GET` | `/v1/grammar/lessons` | Flat lesson list with filters |
| `GET` | `/v1/grammar/lessons/{lessonId}` | Lesson detail (learner DTO — no correct answers) |
| `POST` | `/v1/grammar/lessons/{lessonId}/start` | Idempotent progress init |
| `POST` | `/v1/grammar/lessons/{lessonId}/attempts` | Submit answers → server-graded result |
| `POST` | `/v1/grammar/lessons/{lessonId}/complete` | **Deprecated**; now forwards to `/attempts` with server-side grading |
| `GET` | `/v1/grammar/progress` | Per-learner mastery summary |
| `GET` | `/v1/grammar/recommendations` | Derived from writing/speaking flags |
| `POST` | `/v1/grammar/recommendations/{id}/dismiss` | Learner dismisses a recommendation |

### Admin (`/v1/admin/grammar/*`, permissions: `AdminContentRead` / `AdminContentWrite`)

| Method | Path | Purpose |
|---|---|---|
| `GET/POST` | `/v1/admin/grammar/topics` | List / create topics |
| `GET/PUT/DELETE` | `/v1/admin/grammar/topics/{id}` | Topic CRUD |
| `GET` | `/v1/admin/grammar/lessons` | Paged list (existing — expanded filters) |
| `GET` | `/v1/admin/grammar/lessons/{id}` | Full lesson with exercises |
| `POST` | `/v1/admin/grammar/lessons` | Create (now accepts `topicId`, `exercises[]`, `contentBlocks[]`, `prerequisiteLessonIds[]`, `sourceProvenance`) |
| `PUT` | `/v1/admin/grammar/lessons/{id}` | Update |
| `POST` | `/v1/admin/grammar/lessons/{id}/publish` | Run publish gate + publish |
| `POST` | `/v1/admin/grammar/lessons/{id}/unpublish` | Back to draft |
| `POST` | `/v1/admin/grammar/lessons/{id}/archive` | Existing — archive |
| `POST` | `/v1/admin/grammar/ai-draft` | Grounded AI lesson draft generator |
| `POST` | `/v1/admin/grammar/imports` | Bulk JSON import |

---

## 5. Security

- **XSS hardening.** `GrammarContentBlock.contentMarkdown` is rendered
  through `SafeRichText` (server-side sanitised at write time; additional
  DOMPurify pass at render time for paranoia). The legacy
  `GrammarLesson.ContentHtml` column is retained but **no longer rendered
  by any UI path** — migration sweeps move content into blocks.
- **Authoritative DTO projection.** See §3; the learner endpoints use
  compile-time-separate DTO types (`GrammarLessonLearnerDto`,
  `GrammarExerciseLearnerDto`) that cannot include correct answers.
- **Rate limits.** `/attempts` is rate-limited per user.
- **Audit events.** Every admin mutation writes an `AuditEvent`.

---

## 6. AI content generation

Draft generation is a thin adapter on top of
`AiGatewayService.BuildGroundedPrompt` + `CompleteAsync`. The prompt
embeds the rulebook + scoring + guardrails and uses
`AiTaskMode.GenerateContent`. Output is JSON:

```json
{
  "title": "...",
  "description": "...",
  "level": "intermediate",
  "estimatedMinutes": 12,
  "contentBlocks": [{ "type": "prose", "contentMarkdown": "..." }],
  "exercises": [{ "type": "mcq", "promptMarkdown": "...", "options": [...], "correctAnswer": "...", "explanation": "..." }],
  "appliedRuleIds": [...],
  "selfCheckNotes": "..."
}
```

The draft endpoint **always stores the result as `PublishState=Draft`**;
an admin must review before publish. AI calls are recorded via
`IAiUsageRecorder` with `FeatureCode=grammar_draft`.

---

## 7. Cross-system integration

| Source | Hook |
|---|---|
| `WritingCoachService` | For every suggestion whose rule ID maps to a grammar topic (see `GrammarRuleMap`), create a `GrammarRecommendation(source="writing", sourceRefId=attemptId, ruleId=..., lessonId=...)`. |
| Speaking rubric | Same, `source="speaking"`. |
| Diagnostic | Future: feed into recommendations with `source="diagnostic"`. |
| Review queue | Failed `GrammarExerciseAttempt` → `ReviewItem`. |
| Readiness | `ReadinessService` factors `grammar_mastery_avg` with weight 0.1. |
| Progress dashboard | New `GrammarMasteryWidget`. |
| AI Conversation | Surfaces weak-topic drill scenarios. |

---

## 8. Frontend contract (lib/grammar)

```ts
// lib/grammar/types.ts
export type ExerciseType = 'mcq' | 'fill_blank' | 'error_correction' | 'sentence_transformation' | 'matching';

export interface GrammarExerciseLearner {
  id: string;
  order: number;
  type: ExerciseType;
  promptMarkdown: string;
  options?: { id: string; label: string }[];
  points: number;
  // correctAnswer, explanation are NEVER present until after submission
}

export interface GrammarExerciseResult extends GrammarExerciseLearner {
  userAnswer: unknown;
  isCorrect: boolean;
  pointsEarned: number;
  correctAnswer: unknown;
  explanationMarkdown: string | null;
}
```

A single `GrammarExerciseRunner` React component is polymorphic over
`type`; one sub-component per type handles its UI and emits
`{ exerciseId, answer }` up to the runner.

---

## 9. Non-functional requirements

| Area | Requirement |
|---|---|
| Performance | Topic hub ≤ 300 ms p95; attempt grading ≤ 150 ms p95 |
| Offline | Published lessons cached on mobile; attempts queued and synced |
| Accessibility | Full keyboard support; ARIA live regions for feedback; respect `prefers-reduced-motion` |
| i18n | All user-facing strings flow through `lib/i18n`; Arabic/RTL supported |
| Test coverage | `GrammarGradingService` ≥ 90 % branch; E2E learner + admin paths |

---

## 10. Migration and rollout

1. **Phase 1 migration (`AddGrammarV2`)** adds tables/columns, copies
   `GrammarLesson.Category` → new `GrammarTopic`.
2. **Phase 2** enables server grading behind the new `/attempts`
   endpoint; old `/complete` is server-re-graded internally.
3. **Phase 3** seeds topics + 25 OET lessons.
4. **Phase 4** feature-flag `grammar_lessons` enabled at 100 %.
