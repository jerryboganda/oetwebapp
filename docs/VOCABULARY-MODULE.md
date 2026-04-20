# Vocabulary Module — Canonical Specification

> **MISSION CRITICAL.** This document is the source of truth for the Vocabulary module. Any change to vocab behaviour, endpoints, rulebook, or scoring must route through the invariants documented here. Drift requires an explicit PR update to this file.

---

## 1. Purpose

Vocabulary is the OET candidate's medical-English learning spine. It provides:

1. **Curated medical term banks** — 500+ OET medical terms + 100/profession for medicine, nursing, dentistry, pharmacy.
2. **SM‑2 spaced repetition flashcards** — daily due‑set, ease‑factor evolution, mastery tiers.
3. **Five quiz formats** — Definition Match (free), Fill‑the‑Blank, Synonym Match, Context Usage, Audio Recognition (premium).
4. **Cross‑module save‑to‑word‑bank** — learners save words from reading passages, writing editor, speaking transcripts, listening transcripts, and post‑mock review.
5. **AI‑grounded gloss** — on‑demand, rulebook‑grounded definitions via the AI Gateway (`VocabularyGloss` feature code).
6. **Gamification** — streaks, achievements (`vocab_50`, `vocab_100`, `vocab_mastered_25`), XP.

---

## 2. Architecture Layers

```
Learner UI (app/vocabulary/**)
  → lib/api.ts (fetchVocabularyTerms, fetchDueFlashcards, fetchVocabStats, …)
  → /v1/vocabulary/* endpoints (LearnerOnly)
  → VocabularyService (SM‑2, quiz, my‑list)
  → LearnerDbContext (VocabularyTerm, LearnerVocabulary, VocabularyQuizResult)

Admin UI (app/admin/content/vocabulary/**)
  → /v1/admin/vocabulary/* endpoints (AdminContentRead / AdminContentWrite)
  → AdminService.ContentAdmin (CRUD + CSV import + AI draft)
  → IAiGatewayService.BuildGroundedPrompt(Kind = Vocabulary, …)
  → rulebooks/vocabulary/<profession>/rulebook.v1.json

Cross‑module integration
  → components/domain/vocabulary/VocabLookupPopover
  → /v1/vocabulary/terms/lookup?q=…
  → /v1/vocabulary/gloss  (grounded AI)
```

---

## 3. Domain Entities

| Entity | Key fields | Purpose |
|--------|-----------|---------|
| `VocabularyTerm` | `Id`, `Term`, `Definition`, `ExampleSentence`, `ContextNotes`, `ExamTypeCode`, `ProfessionId`, `Category`, `Difficulty`, `IpaPronunciation`, `AudioUrl`, `AudioMediaAssetId`, `ImageUrl`, `SynonymsJson`, `CollocationsJson`, `RelatedTermsJson`, `SourceProvenance`, `Status` | Canonical term row. |
| `LearnerVocabulary` | `Id`, `UserId`, `TermId`, `Mastery`, `EaseFactor`, `IntervalDays`, `ReviewCount`, `CorrectCount`, `NextReviewDate`, `LastReviewedAt`, `AddedAt`, `SourceRef` | Per‑user card state (SM‑2). |
| `VocabularyQuizResult` | `Id`, `UserId`, `TermsQuizzed`, `CorrectCount`, `DurationSeconds`, `Format`, `ResultsJson`, `CompletedAt` | Historical quiz sessions. |

`Mastery` enum: `new | learning | reviewing | mastered`.
`Status` enum: `draft | active | archived`.

---

## 4. SM‑2 Scheduler (MISSION CRITICAL)

All SM‑2 scheduling routes through `ISpacedRepetitionScheduler` (`Services/Sm2Scheduler.cs`). Both `VocabularyService` and generic `SpacedRepetitionService` delegate to this single implementation. No inline SM‑2 math anywhere else.

```text
if quality >= 3:
  reviewCount++; correctCount++
  interval = reviewCount==1 ? 1 : reviewCount==2 ? 6 : round(prevInterval * easeFactor)
  easeFactor = max(1.3, easeFactor + 0.1 - (5-q)*(0.08 + (5-q)*0.02))
else:
  reviewCount++; interval = 1
nextReviewDate = today + interval
mastery:
  mastered  if reviewCount >= 10 && correctCount >= 8
  reviewing if reviewCount >= 4
  learning  if reviewCount >= 1 && quality < 3
  new       if reviewCount == 0
```

---

## 5. Endpoints

### Learner (`/v1/vocabulary/*` — `LearnerOnly`)

| Method | Path | Notes |
|--------|------|-------|
| GET | `/terms` | paged browse, filter `examTypeCode`, `category`, `profession`, `search` |
| GET | `/terms/{termId}` | detail view (synonyms, collocations, related terms) |
| GET | `/terms/lookup?q=…` | cross‑module save popover; exact + slug match |
| GET | `/categories` | distinct taxonomy from DB |
| GET | `/my-list` | user's saved terms |
| POST | `/my-list/{termId}` | idempotent add; `SourceRef` optional |
| DELETE | `/my-list/{termId}` | remove |
| GET | `/flashcards/due?limit=20` | SM‑2 due |
| POST | `/flashcards/{lvId}/review` | body `{ quality }` (0‑5) |
| GET | `/stats` | authoritative mastery counts + streak |
| GET | `/daily-set?count=10` | mix new + due for today |
| GET | `/quiz?count=10&format=definition_match` | 5 formats; premium gated |
| POST | `/quiz/submit` | `{ answers, durationSeconds, format }` |
| GET | `/quiz/history?page=1&pageSize=20` | past sessions |
| POST | `/gloss` | body `{ word, context? }` — grounded AI gloss (rate limited) |

### Admin (`/v1/admin/vocabulary/*` — `AdminContentWrite` + `PerUserWrite`)

| Method | Path | Notes |
|--------|------|-------|
| GET | `/items` | paged admin list with all statuses |
| GET | `/items/{id}` | full admin detail |
| POST | `/items` | create |
| PUT | `/items/{id}` | update (status, IPA, synonyms, audio, provenance) |
| DELETE | `/items/{id}` | soft delete via `Status=archived` or hard delete |
| POST | `/import` | RFC 4180 CSV with dry‑run flag |
| POST | `/import/preview` | parse and validate only |
| GET | `/categories` | taxonomy browser |
| POST | `/categories` | upsert category |
| POST | `/ai/draft` | batch AI draft via grounded gateway; returns drafts for review |
| POST | `/ai/draft/accept` | commit accepted drafts (one DB write per term) |

---

## 6. Publish Gate (Rulebook Invariant)

A `VocabularyTerm` is only promotable to `Status="active"` when:

1. `Term`, `Definition`, `ExampleSentence`, `Category` are non‑empty.
2. `SourceProvenance` is non‑empty (human curator, AI‑draft‑reviewed, or CSV‑import‑with‑reviewer).
3. For medical categories (`medical`, `anatomy`, `pharmacology`, `procedures`, `symptoms`, `conditions`), either `IpaPronunciation` or `AudioUrl` is set.
4. `SynonymsJson` parses to an array (may be empty but must be valid JSON).

Publish‑gate violation returns HTTP 400 with code `VOCAB_PUBLISH_GATE` and a violated‑fields list.

---

## 7. Scoring & Projection

**Vocabulary itself never projects to an OET scaled score.** Quiz results are a 0–100 percentage; this is a pedagogical metric, not a CBLA equivalent. `lib/scoring.ts` and `OetScoring` are never imported from vocabulary code. Any attempt to compare `score >= 350` in vocab code is a bug.

---

## 8. AI Gateway Usage (MISSION CRITICAL)

Every AI call in the vocabulary module goes through the Rulebook‑Grounded Gateway:

```csharp
var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
{
    Kind = RuleKind.Vocabulary,
    Profession = ExamProfession.Medicine,
    Task = AiTaskMode.GenerateVocabularyTerm,  // or GenerateVocabularyGloss
});
var res = await gateway.CompleteAsync(new AiGatewayRequest
{
    Prompt = prompt,
    UserInput = userInput,
    FeatureCode = AiFeatureCodes.AdminVocabularyDraft,  // or VocabularyGloss
    UserId = userId,
});
```

Feature codes:

| Code | Tier | Purpose |
|------|------|---------|
| `admin.vocabulary_draft` | platform‑only (BYOK refused) | Admin batch draft / single suggest |
| `vocabulary.gloss` | free 5/day, paid unlimited | Learner on‑demand gloss |

Every call produces exactly one `AiUsageRecord` row via `IAiUsageRecorder`.

---

## 9. Monetization / Entitlements

| Feature | Free | Premium |
|---------|------|---------|
| Browse, My List, Flashcards | ✅ unlimited | ✅ unlimited |
| Definition Match quiz | ✅ unlimited | ✅ unlimited |
| Fill‑the‑Blank / Synonym / Context / Audio Recognition | ❌ upsell | ✅ unlimited |
| AI Gloss | ✅ 5/day | ✅ unlimited |
| My List size cap | 500 terms | unlimited |

Enforcement: `/v1/vocabulary/quiz?format=…` returns HTTP 402 (`VOCAB_PREMIUM_REQUIRED`) for free users on non‑`definition_match` formats.

---

## 10. Cross‑Module Integration (V4 Contract)

All "save to vocab" surfaces call:

```ts
await addToMyVocabulary(termId, { sourceRef: 'reading:passageId:offset', context: '...snippet...' });
```

For unknown words (no existing `VocabularyTerm`), the popover calls:

```ts
const gloss = await glossVocabulary(word, { context });
// Optionally: admin.createFromGloss(gloss) → new active term
```

Surfaces:

| Surface | Handler |
|---------|---------|
| Reading passage | `components/domain/reading/PassageRenderer` (long‑press / text‑select → `VocabLookupPopover`) |
| Writing editor | `components/domain/writing/WritingEditor` + AI coach flagged terms |
| Speaking transcript | `app/speaking/transcript/[id]/page.tsx` word chips |
| Listening transcript | listening transcript viewer |
| Post‑mock review | "Words to review" section emits `ReviewItem(SourceType="vocabulary")` |

---

## 11. Mobile Offline

Handled by existing `lib/mobile/offline-sync.ts`. Vocab reads the IndexedDB `vocabulary` store when offline and queues `add` / `review` actions for replay on reconnect. The sync engine reconciles SRS `nextReviewDate` using last‑writer‑wins + advancement (a later review always advances the schedule).

---

## 12. Analytics Events (`lib/analytics.ts`)

| Event | When |
|-------|------|
| `vocabulary_home_viewed` | hub mount |
| `vocab_browse_viewed` | browse mount |
| `flashcards_viewed` | flashcards mount |
| `vocab_quiz_viewed` | quiz mount |
| `vocab_term_detail_viewed` | detail view mount |
| `vocab_added` | add to my list (payload: termId, source) |
| `vocab_removed` | remove from my list |
| `flashcard_rated` | review submit (payload: quality, mastery) |
| `vocab_quiz_started` | quiz started (payload: format, count) |
| `vocab_quiz_submitted` | quiz finished (payload: format, score, duration) |
| `vocab_daily_set_completed` | daily set fully reviewed |
| `vocab_saved_from_reading` | cross‑module save (and analogues for writing/speaking/listening/mock) |
| `vocab_gloss_requested` | AI gloss call |

---

## 13. Rulebook Location

`rulebooks/vocabulary/<profession>/rulebook.v1.json` — embedded into the API assembly via `OetRulebooks/vocabulary/...`.

The rulebook schema adds `kind: "vocabulary"` to the shared enum. `RulebookLoader.FolderOf(kind)` maps `RuleKind.Vocabulary → "vocabulary"`.

---

## 14. Change Log

- **2026‑04‑20** — v1 spec created (Phase V1 kickoff). Replaces ad‑hoc behaviour documented in `docs/mega-master-prompt.md §3.5`.
