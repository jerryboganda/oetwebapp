# Recalls Content Pack — Seed JSON Schema (v1)

> Loaded by [`RecallsContentSeeder`](../../Services/Recalls/RecallsContentSeeder.cs) at API boot. Idempotent. Never deletes rows. Survives container/volume rebuilds because Git is the source of truth.

## File layout

```
backend/src/OetLearner.Api/Data/SeedData/recalls/
├── SCHEMA.md                  ← this file
├── medicine.json              ← ≥100 terms
├── nursing.json               ← ≥100 terms
├── dentistry.json
├── pharmacy.json
├── physiotherapy.json
├── occupational-therapy.json
├── radiography.json
├── optometry.json
├── veterinary.json
├── dietetics.json
├── speech-pathology.json
└── podiatry.json
```

One JSON file per OET profession. The filename (without `.json`) is the canonical `professionId` written into `VocabularyTerm.ProfessionId`.

## File schema

```jsonc
{
  "schemaVersion": 1,
  "professionId": "medicine",                     // REQUIRED, must match filename
  "examTypeCode": "OET",                          // REQUIRED
  "sourceProvenance": "ai-author:claude-opus-4.7:v1",  // stamped onto every row + per-row suffix
  "defaultStatus": "draft",                       // 'draft' default; admin promotes to 'active'
  "terms": [
    {
      "term": "auscultation",                     // REQUIRED, 1-128 chars, British canonical spelling
      "definition": "The act of listening...",   // REQUIRED, 1-1024 chars
      "exampleSentence": "On auscultation,...",  // REQUIRED, 1-2048 chars (clinical sentence ideal)
      "category": "procedure",                    // REQUIRED — functional tag (see below)
      "oetSubtestTags": ["listening_c", "reading_c"], // REQUIRED, ≥1 (see below)
      "difficulty": "medium",                     // easy | medium | hard
      "ipa": "/ˌɔːskəlˈteɪʃ(ə)n/",                // optional
      "americanSpelling": null,                   // optional, only when notably different
      "synonyms": ["listening with stethoscope"], // optional, JSON array
      "collocations": ["chest auscultation", "cardiac auscultation"], // optional
      "relatedTerms": ["palpation", "percussion"],
      "contextNotes": "Always document...",       // optional, ≤1024 chars
      "commonMistakes": ["asculation", "ausculation"], // plausible learner misspellings
      "similarSounding": ["oscillation", "consultation"] // distractors for word-recognition quiz
    }
  ]
}
```

## Allowed values

### `category` (functional tag)

The functional dimension of the matrix. Lowercase, snake_case.

- `body_system` — anatomical/system terms (cardiovascular, respiratory, etc.)
- `symptom` — patient-reported or observed signs/symptoms
- `condition` — diseases, syndromes, diagnoses
- `procedure` — clinical procedures, examinations, surgeries
- `investigation` — tests, imaging, labs
- `medication` — drug classes, named drugs, dosing
- `equipment` — devices, instruments, PPE
- `patient_communication` — phrases used in patient interaction
- `professional_communication` — handover, referral, documentation phrases
- `anatomy` — body parts, structures
- `general` — fallback only

### `oetSubtestTags` (subtest dimension)

Where this term typically appears in the OET. Array; one term may legitimately appear in multiple subtests.

- `listening_a` — Part A: consultation extracts (patient+clinician)
- `listening_b` — Part B: short workplace extracts (handovers, briefings)
- `listening_c` — Part C: lectures and interviews
- `reading_a` — Part A: rapid info-locating across multiple short texts
- `reading_b` — Part B: short workplace texts (procedural)
- `reading_c` — Part C: long magazine-style health articles
- `writing` — referral letter terminology
- `speaking` — role-play patient communication

### `difficulty`

- `easy` — common terms a learner already knows in plain English
- `medium` — clinical terms requiring focused study
- `hard` — high-risk spelling/pronunciation (most spelling errors land here)

## Quality rules

1. **British spelling canonical.** `term` is the British form. American variants go in `americanSpelling`.
2. **Clinical example.** `exampleSentence` should be a sentence a clinician would actually say or write.
3. **Plausible mistakes.** `commonMistakes` must be misspellings a real learner would attempt — not random typos.
4. **Distractor honesty.** `similarSounding` must genuinely sound similar (used as quiz distractors that aren't trivially wrong).
5. **No duplicates within file.** `(term, examTypeCode, professionId)` is the upsert key. Same term twice in one file is a content bug.
6. **Length limits enforced** — see `VocabularyTerm` entity attributes.

## Idempotency contract

The seeder:

- Hashes each row's content; only updates DB rows when hash changes.
- **Never deletes** rows. If you remove a term from the JSON, the DB row stays (admins can archive via `/admin/content/vocabulary`).
- Skips files that fail JSON schema validation (logged as warning).
- Default `Status='draft'`. Admin promotes to `'active'` via existing flow.
- Stamps `SourceProvenance = "ai-author:claude-opus-4.7:v1:<profession>:<term-slug>"`.

## Promotion to active

Seeded rows ship as `draft`. Admin promotes via:

```bash
# Bulk promote all draft rows for a profession after review
curl -X POST /v1/admin/vocabulary/bulk-status \
  -H "Authorization: Bearer <admin>" \
  -d '{ "professionId": "medicine", "fromStatus": "draft", "toStatus": "active" }'
```

(Endpoint to be added in Wave 4 if not present.)
