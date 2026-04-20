# Review Module (Cross-Skill Spaced Repetition)

> **Status:** MISSION CRITICAL
> **Canonical source of truth** for the learner-facing `/review` surface, the `ReviewItem` entity, and the cross-skill retention queue.
>
> Scope: this module is the **single retention engine** for every mistake the learner makes anywhere in the platform (Writing issues, Speaking findings, Reading/Listening wrong answers, Grammar exercise errors, Vocabulary cards, Pronunciation findings, Mock mistakes).

---

## 1. Purpose

OET prep is a long-horizon exam. Mistakes that are not re-surfaced are forgotten. The Review module is the platform's **forgetting curve defence**: every actionable mistake is written as a `ReviewItem`, scheduled by the shared SM-2 algorithm, and re-shown to the learner on the correct day until it is mastered.

The `/review` page is a **second-order** surface: it never authors content directly. It only shows items that were written upstream by the skill pipelines (Writing eval, Speaking eval, Reading grader, Listening grader, Pronunciation scorer, Grammar grader, Mock report generator, Expert review submissions, Vocabulary flashcards).

---

## 2. Source-type contract (canonical enum)

All `ReviewItem.SourceType` values MUST come from this set. Add a new value by updating `ReviewSourceTypes.cs` **and** this document.

| SourceType | Origin | Stable `SourceId` | PromptKind |
|------------|--------|-------------------|------------|
| `grammar_error` | Incorrect grammar exercise | `{lessonId}:{exerciseId}` | `grammar` |
| `reading_miss` | Incorrect reading question | `{paperId}:{questionId}` | `reading_miss` |
| `listening_miss` | Incorrect listening question | `{attemptId}:{questionId}` | `listening_miss` |
| `writing_issue` | Writing feedback item with severity medium/high | `{evaluationId}:{feedbackItemId}` | `writing_issue` |
| `speaking_issue` | Speaking feedback item with severity medium/high | `{evaluationId}:{feedbackItemId}` | `speaking_issue` |
| `pronunciation_finding` | Low-score phoneme from a pronunciation attempt | `{attemptId}:{phonemeKey}` | `pronunciation` |
| `mock_miss` | Incorrect mock answer (all sub-tests) | `{mockReportId}:{sectionCode}:{questionId}` | `mock_miss` |
| `vocabulary` | **Projection only** from `LearnerVocabulary` — never written directly to `ReviewItems` | virtual id `ri-v-{lvId}` | `vocabulary` |

**Idempotency rule:** `(UserId, SourceType, SourceId)` is a unique key. The seeder MUST skip if a row already exists.

---

## 3. SM-2 algorithm (shared)

Scheduling is delegated to `Sm2Scheduler` / `ISpacedRepetitionScheduler`. Never duplicate the algorithm.

- `quality ∈ [0,5]`
- `0–2` = forgot/hard (interval resets to 1 day)
- `3–5` = pass (interval advances 1 → 6 → round(prev × ef))
- `easeFactor` floor = 1.3

---

## 4. Lifecycle

```
new ── first review ──▶ active
                          │
            suspend ◀─────┤
                          │
     mastered ◀───── ReviewCount ≥ 10 && ConsecutiveCorrect ≥ 8 && IntervalDays ≥ 60
```

| Status | Semantics |
|--------|-----------|
| `active` | In rotation; appears in `/v1/review/due` when `DueDate ≤ today` |
| `mastered` | Graduated; excluded from due queue; counted in summary |
| `suspended` | Learner or admin paused; excluded from queue until resumed |

Administrative verbs: `suspend`, `resume`, `undo` (reverts the last SM-2 transition using the snapshot stored in `ReviewItemTransition`).

---

## 5. Quality scale (4 buttons)

The `/review` session and `/vocabulary/flashcards` session both use **the same 4-button scale**:

| Button | Underlying `quality` | Outcome |
|--------|----------------------|---------|
| Forgot | 0 | interval reset |
| Hard | 2 | interval reset, lower ease |
| Good | 3 | normal advancement |
| Easy | 5 | strongest advancement |

Legacy 6-button callers on the backend are supported (API accepts `0–5`), but the UI must render the 4-button scale.

---

## 6. Writing / Speaking severity filter

Only feedback items with `severity ∈ {"medium", "high"}` become `ReviewItem`s. Low-severity nits never pollute the queue. This is enforced by `ReviewItemSeeder.SeedWritingIssueAsync` / `SeedSpeakingIssueAsync` — call sites MUST not bypass.

---

## 7. Unified queue (with vocabulary projection)

`/v1/review/due` returns **one** merged queue:

1. Native `ReviewItem` rows where `Status='active' && DueDate ≤ today`, ordered by `DueDate`.
2. Virtual `ri-v-{lvId}` rows projected from `LearnerVocabulary` where `NextReviewDate ≤ today && Mastery != 'mastered'`.

Mapping is transparent: if the client submits a rating for a `ri-v-*` id, `SpacedRepetitionService.SubmitReviewAsync` routes it to `VocabularyService.SubmitFlashcardReviewAsync`. The client never has to know which silo owns the card.

Opt out by passing `?includeVocabulary=false` (the dedicated `/vocabulary/flashcards` surface does this).

---

## 8. API surface

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/v1/review/summary` | Counters: total, due, dueToday, mastered, upcoming, bySource, byPromptKind |
| GET | `/v1/review/due?limit=&source=&subtest=&includeVocabulary=` | Due queue (merged) |
| POST | `/v1/review/items` | Manually create an item (legacy; prefer seeder on server) |
| POST | `/v1/review/items/{id}/submit` | Rate an item |
| POST | `/v1/review/items/{id}/suspend` | Pause |
| POST | `/v1/review/items/{id}/resume` | Resume |
| POST | `/v1/review/items/{id}/undo` | Revert last rating |
| DELETE | `/v1/review/items/{id}` | Delete |
| GET | `/v1/review/retention?days=30` | Daily reviewed/correct/accuracy |
| GET | `/v1/review/heatmap` | Grouped counts (source × subtest × criterion) |
| GET | `/v1/review/config` | Per-user caps |
| PUT | `/v1/review/config` | Update caps |

---

## 9. UI contract (DESIGN.md-aligned)

- Uses `LearnerDashboardShell`, `LearnerPageHero`, `LearnerSurfaceCard`, `LearnerSurfaceSectionHeader`, `Card`, `Button`, `ProgressBar`, `MotionSection`, `MotionItem`.
- Never uses raw tailwind swatches (`bg-red-500`, `bg-emerald-600` etc.) for semantic status — uses tokenised classes `text-danger / text-warning / text-info / text-success` + tint-based backgrounds.
- Session is a modal (`ReviewSessionModal`), NOT a separate page.
- Polymorphic item rendering is centralised in `ReviewItemRenderer` keyed on `promptKind`.
- Keyboard shortcuts: `Space` reveal, `1/2/3/4` quality, `U` undo, `S` suspend, `Esc` close.
- Haptic feedback on mobile via `triggerImpactHaptic`.
- Empty state is a calm card with two CTAs: "Start with vocabulary" and "Start grammar lesson".

---

## 10. Dashboard + study-plan integration

- The learner home dashboard (`app/page.tsx`) surfaces a **"Today's Review"** `LearnerSurfaceCard` when `dueToday ≥ 1`.
- The study-plan generator emits a synthetic `review-today` task in `section='today'` whenever `dueToday ≥ 1`.
- Mobile: `LearnerReviewDue` notification (once per 24h, quiet-hours aware).

---

## 11. Never do

- **Never** `db.ReviewItems.Add(...)` directly in new code — always go through `IReviewItemSeeder`.
- **Never** duplicate SM-2 math — always call `ISpacedRepetitionScheduler`.
- **Never** read/write `LearnerVocabulary` from the Review service — call `VocabularyService` methods.
- **Never** show raw JSON (`QuestionJson` / `AnswerJson`) to the learner — go through `ReviewItemRenderer`.
- **Never** compare scores/percentages with inline `>= 350` literals — use `OetScoring` (applies whenever a rating screen shows a projected OET band).

---

## 12. Testing contract

- `ReviewItemSeederTests` — idempotency per source type, severity filter, batch.
- `SpacedRepetitionServiceTests` — unified queue ordering, vocab projection, suspend/resume/undo, retention/heatmap shapes.
- `app/review/__tests__/review.test.tsx` — loading, empty state, modal session, keyboard shortcuts, undo, mastery summary.
- `tests/e2e/learner/review-full-flow.spec.ts` — end-to-end: attempt → seeded → shown → rated → mastered.
