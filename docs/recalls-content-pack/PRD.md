# PRD — Recalls Real Content Pack v1

> **Status:** In progress (2026-05-05). Owner: platform team.
> **Goal:** Replace seed/demo Recalls content with **1,200+ high-quality, OET-targeted vocabulary terms** across all 12 professions, surviving container rebuilds, with matrix tagging (functional × OET subtest), rich learner-facing fields, and a backfill path for ElevenLabs audio.

## 1. Problem

The Recalls module ([docs/RECALLS-MODULE-PLAN.md](../RECALLS-MODULE-PLAN.md)) ships with seed/demo vocabulary. Subscribers get effectively no real value from `/recalls/words` until the bank is populated with profession-specific clinical vocabulary that maps to actual OET test scenarios.

## 2. Scope

### In scope (this PRD)

- Seed-loader infrastructure that hydrates `VocabularyTerm` rows from versioned JSON files in `backend/src/OetLearner.Api/Data/SeedData/recalls/<profession>.json` at boot.
- Idempotent upsert keyed on `(Term, ExamTypeCode, ProfessionId)` + content hash; never deletes learner-modified rows or learner SM-2 progress.
- Schema additions:
  - `VocabularyTerm.OetSubtestTagsJson` (string, default `"[]"`) — matrix subtest dimension (Listening A/B/C, Reading A/B/C).
  - `VocabularyTerm.CommonMistakesJson` (string, default `"[]"`) — input to spelling diff classifier.
  - `VocabularyTerm.SimilarSoundingJson` (string, default `"[]"`) — input to word recognition quiz mode (distractors).
  - Existing `Category` column carries the functional dimension (symptom, medication, procedure, etc.).
- 1,200+ authored terms across **all 12 OET professions** (Medicine, Nursing, Dentistry, Physiotherapy, Pharmacy, Radiography, Occupational Therapy, Optometry, Veterinary Science, Dietetics, Speech Pathology, Podiatry).
- ElevenLabs wiring already present via `IConversationTtsProviderSelector`; ship audio backfill admin endpoint.
- Frontend matrix filter on `/recalls/words` (functional × subtest).
- Tests: backend (seeder idempotency, schema), frontend (matrix filter).

### Out of scope (deferred)

- Translation/localisation of definitions.
- AI-generated images per term.
- Per-term reviewer sign-off workflow (ships with `status='draft'` so existing admin review path applies).

## 3. Quality contract

- All seeded rows ship as `Status='draft'` with `SourceProvenance='ai-author:claude-opus-4.7:v1:<profession>:<row>'` so they are not learner-visible until an admin promotes them. Honours the zero-hallucination contract in [docs/RECALLS-DATA-ENTRY-PLAN.md](../RECALLS-DATA-ENTRY-PLAN.md) §4.
- Every term must include at minimum: term, definition, exampleSentence, category (functional tag), oetSubtestTags (≥1), difficulty, profession.
- IPA and AmericanSpelling are best-effort; reviewer override expected.
- Common mistakes and similar-sounding lists must be plausible and clinically honest (inputs to the spelling diff classifier and the word-recognition quiz mode).

## 4. Persistence guarantee

- Postgres volume `oetwebsite_oet_postgres_data` survives `docker compose --build`.
- Seed JSON files live in repo (Git is the source of truth). On every boot, `RecallsContentSeeder.EnsureAsync` scans the JSON, hashes each row, and upserts only when content has changed. **It never deletes** rows that were created by other paths (admin import, manual entry).
- Even if the postgres volume is wiped, content rehydrates from Git on next boot.

## 5. Acceptance criteria

- [ ] Migration `20260505000000_AddVocabularySubtestTags` applied; new columns present.
- [ ] `RecallsContentSeeder` runs at boot, idempotent, logged, never deletes.
- [ ] 12 JSON files exist, each ≥100 valid terms, schema-validated.
- [ ] `npx tsc --noEmit`, `npm run lint`, `npm test`, `npm run backend:test` relevant suites green.
- [ ] `/recalls/words` shows the matrix filter and serves seeded terms when promoted to `active`.
- [ ] ElevenLabs audio backfill admin endpoint accepts batch-by-profession requests.

## 6. Dependencies

- `Conversation:ElevenLabsApiKey` (or `ELEVENLABS_API_KEY`) — audio backfill only. Text content does not need it.
- No new npm/NuGet packages.

## 7. Risks

- **Medical accuracy.** Mitigated by `Status='draft'` default — admin review gates learner exposure.
- **JSON file size.** ~50KB per profession × 12 ≈ 600KB total in repo; acceptable.
- **Audio cost.** 1,200 terms × ~50 chars ≈ ~60k chars on ElevenLabs ≈ ~$15-30 per backfill. User must opt-in by setting the key + running backfill.

## 8. Phasing

1. **Wave 1 — Infrastructure (this session, agent-owned):** schema, seeder, JSON format, backfill endpoint, UI filter, tests.
2. **Wave 2 — Content authoring (this session, 6 parallel subagents):** 12 profession JSON files.
3. **Wave 3 — Audio backfill (user-initiated):** set ElevenLabs key, run admin endpoint.
4. **Wave 4 — Promote to active (admin-initiated):** review draft rows in `/admin/content/vocabulary` and bulk-activate per profession.
