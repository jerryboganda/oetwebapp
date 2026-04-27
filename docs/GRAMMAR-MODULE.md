# Grammar Module — Canonical Spec

**Status:** v1.0 (GA pending)
**Owner:** Dr. Ahmed Hesham (content authority) + platform team
**Mission-critical:** yes — listed in `AGENTS.md`.

This document is the single source of truth for how the Grammar module works across the OET, IELTS, and PTE offerings. Every future change to grammar authoring, grading, entitlements, AI drafting, or analytics must update this doc.

---

## 1. Business model

Grammar is a **subscription-gated practice module**. The module drives:

1. **Activation** — new free-tier learners sample 3 lessons per rolling 7-day window; the 4th hits a paywall.
2. **Retention** — paid learners get unlimited lessons with XP, streak, mastery tracking, and review-item callbacks.
3. **Sponsor value** — sponsored seats inherit their sponsor plan; per-seat mastery reports are exposed to the sponsor admin.

### Entitlement matrix

| Tier | Lessons per 7-day window | XP / streak / review-items | Mastery tracking |
| ---- | ------------------------ | -------------------------- | ---------------- |
| Anonymous | Sign-in required | n/a | n/a |
| Free (authenticated) | **3** | ✅ | ✅ |
| Trial | Unlimited | ✅ | ✅ |
| Paid (Starter / Pro) | Unlimited | ✅ | ✅ |
| Sponsor seat | Unlimited (via sponsor plan) | ✅ | ✅ + per-seat report |

Enforced server-side by `IGrammarEntitlementService` (see `backend/src/OetLearner.Api/Services/Grammar/GrammarEntitlementService.cs`). Endpoint: `GET /v1/grammar/entitlement`. Submit endpoint (`POST /v1/grammar/lessons/{id}/submit`) returns **HTTP 402** `grammar_quota_exceeded` when the free-tier cap is hit.

Re-attempts of an already-completed lesson do **not** count against the quota. Only fresh completions do.

---

## 2. Content model

```
GrammarTopic (category)          -- still a soft slug in v1 (tenses / passive_voice / articles / ...)
 └── GrammarLesson               -- backend entity in Domain/LearningContentEntities.cs
      ├── ContentBlocks[]        -- prose / callout / example / note
      ├── Exercises[]            -- mcq / fill_blank / error_correction / sentence_transformation / matching
      └── sourceProvenance       -- REQUIRED; names the rulebook version & rule IDs cited
```

Lessons are persisted with **structured JSON** in `GrammarLesson.ContentHtml` (historical column name; the field carries the full lesson document) and `GrammarLesson.ExercisesJson` (the exercise array). The `ContentHtml` JSON must parse to:

```json
{
  "topicId": "...",
  "category": "...",
  "sourceProvenance": "Dr. Hesham Grammar Rulebook v1 — rule IDs G02.1, G08.1",
  "appliedRuleIds": ["G02.1", "G08.1"],
  "prerequisiteLessonIds": [],
  "contentBlocks": [ { "id": "...", "sortOrder": 1, "type": "...", "contentMarkdown": "..." } ],
  "exercises": [ { "id": "...", "sortOrder": 1, "type": "...", "promptMarkdown": "...", "correctAnswer": ..., "acceptedAnswers": [...], "explanationMarkdown": "...", "difficulty": "...", "points": 1, "appliedRuleIds": ["G02.1"] } ],
  "version": 1,
  "updatedAt": "ISO-8601"
}
```

The TypeScript shape is mirrored in `lib/grammar/types.ts` (`GrammarLessonDocument`, `GrammarLessonUpsertPayload`, `GrammarExerciseAuthoring`).

---

## 3. Rulebook grounding

Canonical grammar rulebook: `rulebooks/grammar/<profession>/rulebook.v1.json` (schema: `rulebooks/schema/rulebook.schema.json`; `kind` enum includes `grammar`).

- `rulebooks/grammar/medicine/rulebook.v1.json` — 50+ rules across 12 sections (Tenses, Present Perfect, Passive Voice, Articles, Subject–Verb Agreement, Conditionals, Register, Linkers, Prepositions, Concession, Reported Speech, Clinical Numeracy).
- `rulebooks/grammar/nursing/rulebook.v1.json` — inherits medicine structure with nurse-specific register.
- `rulebooks/grammar/common/assessment-criteria.json` — 9 marking criteria linked to specific rule IDs.

The TypeScript loader (`lib/rulebook/loader.ts`) and the .NET loader (`backend/.../Services/Rulebook/RulebookLoader.cs`) both register `kind = grammar` automatically. `RuleKind` now has three members: `Writing | Speaking | Grammar`.

---

## 4. Grading contract

Grading is **server-authoritative**. Endpoint: `POST /v1/grammar/lessons/{lessonId}/submit`.

| Exercise type | Grading rule |
| ------------- | ------------ |
| `mcq` | Exact match of `correctAnswer` (string id). |
| `fill_blank` | Case-insensitive exact match of `correctAnswer` OR any `acceptedAnswers` value. |
| `error_correction` | Case-insensitive match after punctuation normalisation. |
| `sentence_transformation` | Case-insensitive match of `correctAnswer` OR any `acceptedAnswers`. |
| `matching` | All pairs (`left → right`) must match the authored pairs exactly. |

On each incorrect exercise, the endpoint creates a `ReviewItem` with `SourceType = "grammar_error"` and `SourceId = "{lessonId}:{exerciseId}"` so mistakes flow into spaced repetition.

On a completed attempt, the endpoint:

1. Updates `LearnerGrammarProgress` (`Status = "completed"`, `ExerciseScore`, `CompletedAt`).
2. Awards XP via `GamificationService` (capped at one award per lesson).
3. Records activity for streak accounting.
4. Triggers study-plan regeneration when applicable.

---

## 5. Publish gate (backend-authoritative)

`IGrammarPublishGateService.EvaluateAsync` checks:

- Title, description, category non-empty.
- `ContentHtml` parses to JSON and contains `contentBlocks` (≥1), `exercises` (3–12).
- `sourceProvenance` is non-empty.
- `appliedRuleIds` is non-empty (every lesson must cite at least one grammar rule).

Publish (`POST /v1/admin/grammar/lessons/{id}/publish`) re-runs the gate and flips `Status = "active"`. Unpublish flips back to `"draft"`. Every action writes an `AuditEvent`.

The client also pre-checks via `GET /v1/admin/grammar/lessons/{id}/publish-gate` (`adminFetchGrammarPublishGate` in `lib/api.ts`) purely for UX — the backend remains authoritative.

---

## 6. AI-assisted drafting

`POST /v1/admin/grammar/ai-draft` (`RequireAdmin(AdminPermissions.ManageContent)`).

The endpoint routes through **`IGrammarDraftService`**, which:

1. Loads the grammar rulebook for the target profession.
2. Builds a grounded prompt via `IAiGatewayService.BuildGroundedPrompt(Kind = Grammar, Task = GenerateGrammarLesson)`.
3. Calls the gateway with `FeatureCode = AiFeatureCodes.AdminGrammarDraft` (platform-only — BYOK is **refused** by `AiCredentialResolver.PlatformOnlyFeatures`).
4. Parses the JSON reply. Every `appliedRuleIds` value in the reply must exist in the loaded rulebook; unknown rule IDs are dropped.
5. Validates: ≥3 exercises, ≥1 content block, every exercise has `explanationMarkdown`, ≥1 valid `appliedRuleId` per exercise.
6. If validation fails (or the gateway returns nothing parseable / the mock provider is active), falls back to a **deterministic starter template** anchored to the first 3 rules in the rulebook. The response carries a `warning` string so the admin knows to edit before publishing.
7. Persists as `status = "draft"` with `sourceProvenance` that tags the rulebook version and whether the AI or the fallback produced the draft.
8. Writes exactly one `AuditEvent` (`GrammarAiDraftCreated` or `GrammarAiDraftFallback`).

The gateway physically refuses ungrounded prompts via `PromptNotGroundedException` — attempts are recorded in `AiUsageRecord` and raise an error to the caller.

---

## 7. Analytics events

Emitted via `lib/analytics.ts`:

| Event | Fires on |
| ----- | -------- |
| `grammar_page_viewed` | `/grammar` overview |
| `grammar_topic_viewed` | `/grammar/topics/[slug]` |
| `grammar_lesson_viewed` | `/grammar/[lessonId]` load |
| `grammar_recommendation_clicked` | Recommendation strip |
| `grammar_recommendation_dismissed` | Recommendation strip |
| `grammar_lesson_started` | Start-lesson button |
| `grammar_lesson_completed` | Submit success |
| `grammar_lesson_mastered` | Mastery ≥ 80% on completion |
| `grammar_exercise_submitted` | Per-exercise (item analysis) |
| `grammar_paywall_shown` | Entitlement banner visible |
| `grammar_paywall_upgrade_clicked` | Banner CTA click |
| `grammar_draft_generated` | Admin AI draft created |
| `grammar_draft_rejected` | Admin AI draft discarded |

---

## 8. Observability

- `ILogger<GrammarDraftService>` logs info (success), warning (fallback), error (ungrounded refusal).
- `ILogger<GrammarPublishGateService>` logs info on publish.
- AI usage: every `admin.grammar_draft` call lands in `AiUsageRecord` (attributed to admin user + token cost estimate); surfaced on `/admin/ai-usage`.

---

## 9. File map

### Backend

| Path | Purpose |
| ---- | ------- |
| `rulebooks/grammar/medicine/rulebook.v1.json` | Grammar rulebook (canonical) |
| `rulebooks/grammar/nursing/rulebook.v1.json` | Nursing register variant |
| `rulebooks/grammar/common/assessment-criteria.json` | Grammar marking rubric |
| `backend/.../Services/Grammar/GrammarDraftService.cs` | Grounded AI authoring |
| `backend/.../Services/Grammar/GrammarPublishGateService.cs` | Publish + stats |
| `backend/.../Services/Grammar/GrammarEntitlementService.cs` | Free-tier quota |
| `backend/.../Services/Rulebook/RulebookLoader.cs` | `RuleKind.Grammar` registered |
| `backend/.../Services/Rulebook/AiGatewayService.cs` | `AiTaskMode.GenerateGrammarLesson` reply contract |
| `backend/.../Endpoints/LearningContentEndpoints.cs` | Learner grammar endpoints (+ entitlement) |
| `backend/.../Endpoints/AdminEndpoints.cs` | Admin CRUD + publish-gate + AI-draft |
| `backend/.../Services/SeedData.GrammarCatalog.cs` | Catalog generator |
| `backend/.../Services/SeedData.GrammarSpecs{1..4}.cs` | 50 starter lesson specs |

### Frontend

| Path | Purpose |
| ---- | ------- |
| `lib/rulebook/types.ts` / `loader.ts` / `ai-prompt.ts` | `RuleKind = 'writing' \| 'speaking' \| 'grammar'` |
| `lib/grammar/types.ts` | Learner + authoring types |
| `lib/api.ts` | `adminGenerateGrammarAiDraft`, `fetchGrammarEntitlement`, admin publish/stats helpers |
| `lib/analytics.ts` | Grammar events |
| `components/domain/grammar/grammar-entitlement-banner.tsx` | Paywall UI |
| `components/domain/grammar/grammar-lesson-editor.tsx` | `draftToApi` returns typed `GrammarLessonUpsertPayload` |
| `app/grammar/*` | Learner pages |
| `app/admin/content/grammar/*` | Admin pages |

---

## 10. Retention policy

- `GrammarLesson` entities are never hard-deleted. Archive sets `Status = "archived"`.
- `LearnerGrammarProgress` rows persist for the life of the account.
- `AiUsageRecord` rows for `admin.grammar_draft` follow the platform-wide retention (90 days for detailed rows; aggregates kept indefinitely).
- Audit trail (`AuditEvent`) retained indefinitely for compliance.

---

## 11. Open follow-ups (post-v1)

1. Add `grammar/<profession>` rulebooks for dentistry, pharmacy, physiotherapy, etc.
2. Implement item-analysis dashboard off `grammar_exercise_submitted` events.
3. Per-rule drill-down on the rulebook browser (`/rulebook/grammar/medicine/G02.1`).
4. Sponsor-seat per-member mastery report.
5. E2E tests (`tests/e2e/grammar-*.spec.ts`).
