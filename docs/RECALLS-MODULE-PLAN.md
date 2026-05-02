# Recalls Module — Unification & World-Class Plan

> **Status:** Draft v1 (2026-05-02). Owner: platform team.
> **Scope:** Combine the learner-facing **Vocabulary** module (`/vocabulary`) and the generic **Spaced-Repetition Review** module (`/review`) into a single, world-class surface called **Recalls**, and extend it with the OET Listening Vocabulary Lab capability set (per `OET Listening Vocabulary Mastery Module` spec).

This is a phased plan. Phase 0 ships in this PR (unification scaffolding + redirects). Later phases extend behaviour — each phase is independently shippable, behind feature gating, and gated on tests + docs.

---

## 1. Why merge

Both modules already speak the same language:

- **`/vocabulary`** — `VocabularyTerm` + `LearnerVocabulary` (SM‑2 cards) + flashcards + quiz + browse.
- **`/review`** — generic `ReviewItem` (SM‑2 cards) seeded from listening drills, conversation issues, mock errors, etc.

Both schedule through the same `ISpacedRepetitionScheduler` (`Sm2Scheduler.cs`). The split is an IA artefact — for a learner there is one mental model: *"things I am trying to remember"*. We unify the surface, keep both backing tables, and add a shared façade so admins and learners see one feature: **Recalls**.

Invariants that **must not** change:
- SM‑2 math stays in `ISpacedRepetitionScheduler`. Never inlined.
- OET scoring/rulebook gateway routing remains untouched (`AGENTS.md` mission-critical rules).
- All AI calls continue through `IAiGatewayService.BuildGroundedPrompt` with feature codes.

---

## 2. Information architecture

`/recalls` becomes the single entry point with four tabs:

| Tab | Purpose | Backed by |
|-----|---------|-----------|
| **Today** | Due-today queue, streak, exam-readiness score, weak-area summary. | `vocabulary/stats` + `review/summary` aggregator |
| **Words** | Curated vocabulary banks, browse, add to my list, "Listen & type" cards. | `VocabularyService` |
| **Cards** | Unified spaced-repetition flashcard runner (vocab terms **and** generic review items) with quiz modes. | `VocabularyService` + `SpacedRepetitionService` |
| **Library** | Personal mastery dashboard (starred, weak, mastered, by topic / OET part / error type). | Aggregator |

Legacy routes:
- `/vocabulary/*` → 308 redirect to the matching `/recalls/words/*` or `/recalls/cards`.
- `/review` → 308 redirect to `/recalls/cards`.

Sidebar: replace the two items with a single **Recalls** entry (icon: `Brain`).

---

## 3. Backend façade

New endpoint group `/v1/recalls/*` (`LearnerOnly`) acts as an aggregator. It does **not** duplicate logic — it composes the existing services:

```
GET  /v1/recalls/today        → { dueToday, streak, readinessScore, weakTopics, sessions[] }
GET  /v1/recalls/queue?limit  → mixed queue: vocab cards + review items, ordered by dueDate
POST /v1/recalls/grade        → { itemKind: 'vocab'|'review', itemId, quality, typedAnswer? }
                                forwards to VocabularyService.SubmitFlashcardAsync
                                or SpacedRepetitionService.SubmitReviewAsync
POST /v1/recalls/star         → toggles a `Starred` bit (new column on LearnerVocabulary + ReviewItem)
GET  /v1/recalls/library      → filter by mastery, topic, OET part, errorType, starred
POST /v1/recalls/listen-type  → server-side spell check + classified diff (see §6)
```

A new `RecallsService` lives in `backend/src/OetLearner.Api/Services/RecallsService.cs` and depends on the existing `VocabularyService`, `SpacedRepetitionService`, and (later) the TTS gateway.

The two underlying tables (`LearnerVocabulary`, `ReviewItem`) remain. We add:
- `LearnerVocabulary.Starred` (bool, default false)
- `LearnerVocabulary.StarReason` (string?, enum: `spelling | pronunciation | meaning | hearing | confused`)
- `ReviewItem.Starred`, `ReviewItem.StarReason` (mirrors)
- `LearnerVocabulary.LastErrorTypeCode` (nullable, see §6 taxonomy)

EF Core migration: `2026_05_02_recalls_starred.cs`.

---

## 4. Frontend structure

```
app/recalls/
  layout.tsx              ← four-tab shell + LearnerPageHero
  page.tsx                ← Today
  words/
    page.tsx              ← Browse + my list (merge of /vocabulary + /vocabulary/browse)
    [termId]/page.tsx     ← term detail (replaces /vocabulary/terms/[id])
  cards/
    page.tsx              ← unified runner (flashcards + review + quiz mode picker)
  library/
    page.tsx              ← mastery dashboard

app/vocabulary/page.tsx   ← redirect('/recalls/words')
app/review/page.tsx       ← redirect('/recalls/cards')
…and so on for all sub-routes (handled by `next.config.ts` redirects so deep links survive).
```

A shared component library lives at `components/domain/recalls/`:
- `RecallCard` — the unified flashcard with audio button, IPA, type-to-spell input, star menu, mastery pill.
- `ListenAndType` — diff-rendered spell check (green/red/missing letters).
- `QuizModePicker` — 6 modes (see §5).
- `MasteryDashboard` — charts (mastery distribution, accuracy over time, streak, readiness score).

---

## 5. Quiz modes (per spec §4)

All modes reuse the same `RecallsService.GradeAsync` plumbing.

1. **Listen & type** — audio plays, learner types, server runs the diff classifier.
2. **Word recognition** — audio + 4 options (3 distractors from `similar_sounding_words`).
3. **Meaning check** — term + 4 definitions.
4. **Clinical sentence** — server-rendered cloze using `ExampleSentence`; audio is full-sentence TTS.
5. **High-risk spelling challenge** — pool filtered to `Difficulty='hard'` and curated `british_spelling_risk=true`.
6. **Starred-only** — pool filtered to `Starred=true`.

Mode selection is a pure client choice; the server queue endpoint accepts `?mode=…` as a hint but never branches scoring.

---

## 6. Type-to-spell engine

`POST /v1/recalls/listen-type` runs server-side:

1. Normalise (NFC, lower, strip outer whitespace).
2. Resolve the canonical British spelling from `VocabularyTerm.Term` (British is canonical; American variant stored in a new `AmericanSpelling` column for warnings).
3. Compute Levenshtein diff and classify the error:

| Code | Trigger |
|------|---------|
| `correct` | distance 0 |
| `case_only` | distance 0 ignoring case |
| `british_variant` | matches `AmericanSpelling` |
| `missing_letter` | one insertion needed |
| `extra_letter` | one deletion needed |
| `transposition` | adjacent swap |
| `double_letter` | doubling rule (e.g. `inflamation` → `inflammation`) |
| `hyphen` | hyphen insertion/removal |
| `homophone` | matches `similar_sounding_words` |
| `unknown` | fallthrough |

The classifier is a single pure function `ClassifySpellingError(canonical, american, typed, similarSounding)` in `Services/Recalls/SpellingDiff.cs`, with a focused unit-test suite (≥30 cases). The server returns the diff segments so the UI can render colour-coded letters without re-classifying.

---

## 7. Audio & TTS

Per spec §3 / §9 / §17 we **do not** rely on `SpeechSynthesis`. Audio is server-cached.

Pipeline:
1. Admin (or backfill worker) calls `IRecallsTtsService.GenerateAsync(termId, locale='en-GB', speed='normal'|'slow', kind='word'|'sentence')`.
2. The service routes through a new `IAiGatewayService` feature code `RecallsTts` (platform-only, BYOK refused) so usage is metered.
3. The provider implementation (`AzureTtsProvider`, `GoogleTtsProvider`) is selected by `RecallsOptions.TtsProvider`. Mock provider for tests.
4. Output is a content-addressed MP3/OGG written via `IFileStorage` (SHA‑256 path).
5. URL stored on `VocabularyTerm.AudioUrl` (existing column) + new `AudioSlowUrl`, `AudioSentenceUrl`.
6. Learner UI plays the cached URL — never invokes TTS at click time.

Retention: TTS files are immutable and never expire (audio is canonical content).

---

## 8. Auto-seed from Listening tests (per spec §13)

When a Listening drill / mock is graded, every wrong free-text answer where the gap targets a known `VocabularyTerm` (or fuzzy-matches one) is auto-added to the learner's recall queue with `SourceRef='listening:<paperId>:<itemId>'` and `Starred=true`, `StarReason='hearing'`. This already partially exists for `ReviewItem`; we route both into the unified queue.

---

## 9. Admin surface

`/admin/content/vocabulary/*` becomes `/admin/content/recalls/*` (alias preserved). Adds:
- Bulk CSV importer with the spec §8 column set (word, topic, OET part, difficulty, meaning, example, common mistake, similar word, British audio).
- "Generate audio" action per term (calls TTS gateway, writes to MediaAsset).
- "Approve / replace audio" admin review queue.
- Candidate-error analytics view (top mistyped terms, top misheard terms).

All mutations write `AuditEvent` rows (existing pattern).

---

## 10. Phasing

| Phase | Scope | Ships |
|-------|-------|-------|
| **0 — Unification scaffold** | New `/recalls` shell with redirect glue, sidebar item, plan doc, façade endpoint stubs delegating to existing services, shared types. **No behavioural change.** | This PR |
| **1 — MVP feature parity** | Move existing vocab + review UIs under `/recalls/*` tabs. `Today` aggregator. Star button (UI + DB column). | Sprint 1 |
| **2 — Listening Lab core** | Type-to-spell engine, server-side TTS pipeline (Azure default), 6 quiz modes, mastery dashboard, exam-readiness score formula. | Sprint 2 |
| **3 — Intelligence** | AI mistake explanation (grounded), personalised revision plan, auto-seed from Listening drills/mocks, admin analytics. | Sprint 3 |
| **4 — Polish** | Achievements (`recall_50`, `recall_streak_7`, `cardiology_complete`), leaderboards (opt-in), offline audio cache (Capacitor), waveform animation. | Sprint 4 |

Each phase has its own acceptance criteria mirroring spec §18.

---

## 11. Test strategy

- **Unit:** SM‑2 scheduler unchanged tests; new `SpellingDiff` classifier tests; `RecallsService.GradeAsync` dispatch tests; redirect map tests.
- **Component (Vitest):** `RecallCard`, `ListenAndType`, `QuizModePicker`, `MasteryDashboard`. Reuses Proxy `motion/react` mock per `AGENTS.md`.
- **Backend (.NET):** `RecallsServiceTests` (aggregator delegation), `SpellingDiffTests`, `RecallsTtsServiceTests` (mock provider).
- **E2E (Playwright):** `tests/e2e/recalls.spec.ts` — 4 tab nav, listen-and-type happy path, star round-trip, redirects from legacy routes.

Validation gates per `AGENTS.md`: `npx tsc --noEmit`, `npm run lint`, `npm test`, `npm run build`, `npm run backend:test`.

---

## 12. Security & operational notes

- TTS feature code `RecallsTts` is **platform-only** (BYOK refused) — same posture as `PronunciationScore` / `ConversationEvaluation`.
- Audio storage is content-addressed (`IFileStorage`); no raw `File.*` writes.
- Star/unstar and grade endpoints rate-limited at the existing learner global limiter.
- Redirects are HTTP 308 (preserve method) for `POST` deep-links from native clients.
- Migration is additive (nullable columns); no destructive change.

---

## 13. Open questions / non-goals

- Translation overlay (Arabic) — **non-goal** for v1; revisit when localisation framework lands.
- Public leaderboard — opt-in only; default off.
- Authoring of TTS-only "phrase" cards (no `VocabularyTerm` row) — deferred to phase 4.

---

## 14. Mission-critical compliance checklist

- [x] SM‑2 routed through `ISpacedRepetitionScheduler`.
- [x] AI calls routed through `IAiGatewayService.BuildGroundedPrompt` with explicit feature codes.
- [x] Audio I/O through `IFileStorage`.
- [x] Audit events on every admin mutation.
- [x] No raw `score >= 350` comparisons; readiness score routes through a typed helper in `lib/scoring.ts` (extension point, not a new scale).
- [x] No new direct `fetch()` from app/components — extend `lib/api.ts`.
