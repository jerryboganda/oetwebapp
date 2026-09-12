# Writing Model Answers — Coverage Matrix & Rev8 Validation Status

> Prepared 12 Sep 2026 in response to the *Additive Priority Requirements* (item 4/5).
> **No paid generation or semantic validation was run** — this is the verified
> pipeline map, the coverage grid, and the cost estimate needed to approve the
> paid run.

## 1. Taxonomy (verified from code)

**Professions** — `ExamProfession` (`Services/Rulebook/RulebookLoader.cs:229`) and the
seeded catalogue (`Services/SeedData.cs:965-975`). The brief's seven:

| # | Profession | Seed code |
| --- | --- | --- |
| 1 | Medicine | `medicine` |
| 2 | Nursing | `nursing` |
| 3 | Pharmacy | `pharmacy` |
| 4 | Physiotherapy | `physiotherapy` |
| 5 | Dentistry | `dentistry` |
| 6 | Radiography | `radiography` |
| 7 | Other Allied Health | `other-allied-health` |

**Letter types** — `WritingLetterTypeTaxonomy.cs`:

| Code | Letter type | In brief's list? |
| --- | --- | --- |
| `LT-RR` | Routine referral | ✅ |
| `LT-UR` | Urgent referral | ✅ |
| `LT-DG` | Discharge | ✅ |
| `LT-TR` | Transfer | ✅ |
| `LT-NM` | Non-medical (referral to a non-medical profession) | ✅ |
| `LT-OT` | Other Letters | — (not requested) |
| `LT-RP` | Response | ❌ retired |

## 2. Coverage grid (7 professions × 5 requested letter types = 35 cells)

Live per-cell counts require a production DB query (see §4) — the repo carries no
seeded matrix. Cells are marked with their **expected** state based on the task
catalogue; `Not available in current task set` is a legitimate, expected outcome
for a profession that genuinely does not contain that letter type — do **not**
invent a task to fill it.

| Profession | Routine (`LT-RR`) | Urgent (`LT-UR`) | Discharge (`LT-DG`) | Transfer (`LT-TR`) | Non-medical (`LT-NM`) |
| --- | --- | --- | --- | --- | --- |
| Medicine | _query_ | _query_ (live defect: Mr David Taylor task `07d56634…`) | _query_ | _query_ | _query_ |
| Nursing | _query_ | _query_ | _query_ | _query_ | _query_ |
| Pharmacy | _query_ | _query_ | _query_ | _query_ | _query_ |
| Physiotherapy | _query_ | _query_ | _query_ | _query_ | _query_ |
| Dentistry | _query_ | _query_ | _query_ | _query_ | _query_ |
| Radiography | _query_ | _query_ | _query_ | _query_ | _query_ |
| Other Allied Health | _query_ | _query_ | _query_ | _query_ | _query_ |

`_query_` = count of candidate-visible verified model answers vs tasks, to be filled
by the commands in §4.

## 3. Production state (verified from `docs/writing-rev8/ROOT_CAUSE.md`)

- **224 production model answers** were imported via the offline hybrid-drafting
  path, with the owner rules absent from the drafting prompts.
- They were **never revalidated** under Rev8 — the historic "224/224 clean" claim
  came from **catalogue-compatibility checks, not the lint**.
- Rev8 stores validator provenance (`ValidatorVersion`, `RulePackHash`, `ValidatedAt`,
  `ValidationReportJson`, `RepairCount`) and hides from candidates any answer whose
  `ValidatorVersion` ≠ current (`WritingTaskModelAnswerService.CandidateVisibleVerified`).
- **Consequence:** until the Rev8 revalidation actually runs, the candidate-visible
  verified count is **not** 224. Do not declare "224/224 clean" until the full
  production revalidation passes (brief, item 5).

**Pipeline (verified):**
`generate → deterministic lint → semantic validation → repair only failed rules (≤4 attempts) → re-run → store only at zero violations`.

## 4. How to fill the live counts (read-only, no AI cost)

Admin endpoints (`Endpoints/WritingTaskModelAnswerAdminEndpoints.cs`, group `/v1/admin/writing`):

```
# All model answers (paged) — group by profession x letterType client-side
GET /v1/admin/writing/model-answers?status=&page=&pageSize=

# One task's model answer
GET /v1/admin/writing/tasks/{id}/model-answer

# Deterministic-only revalidation (includeSemantic=false ⇒ NO Anthropic call ⇒ no cost)
POST /v1/admin/writing/model-answers/revalidate?apply=false&includeSemantic=false&profession=&offset=0&limit=100&onlyUnverified=true
```

Direct SQL (read-only):

```sql
SELECT t."Profession", t."LetterType",
       count(*)                                  AS tasks,
       count(m."Id")                             AS answers,
       count(*) FILTER (WHERE m."ValidatorVersion" = 'writing-rules.rev8.2026-09-11.1') AS rev8_verified
FROM "WritingScenarios" t
LEFT JOIN "WritingTaskModelAnswers" m ON m."TaskId" = t."Id"
GROUP BY 1, 2
ORDER BY 1, 2;
```

## 5. Cost estimate for the paid run (requires approval)

**Rev8 semantic gate** — `WritingModelAnswerSemanticValidator.cs`:
provider `anthropic`, model **`claude-sonnet-5`**, `Temperature 0`,
`MaxTokens 16000`, `EnableExtendedThinking true`, `ThinkingEffort "high"`,
PromptVersion `writing.model-answer-validate.v1`.
**Generator** — same model, `MaxTokens 32000`, `ThinkingEffort "max"`, up to 4 attempts.

| Operation | Calls | Notes |
| --- | --- | --- |
| Semantic revalidation of 224 existing answers | 224 | 1 paid call each; repairs add up to 4 more per failing answer |
| Generation for missing cells (≤35) | ≤35 | 1 call + up to 4 attempts |
| Semantic revalidation of newly generated | ≤35 | 1 paid call each |
| **Worst case** | **~294 validation + ~175 generation calls** | extended thinking at high/max effort — the heaviest AI path in the system |

**Recommendation:** run §4 first (deterministic, free) to learn the real failure
count, then approve only the semantic calls actually needed. Auto-reload stays
**OFF**; do not launch a bulk run without explicit approval.

## 6. Status

| Requirement | Status |
| --- | --- |
| Pipeline verified | ✅ |
| Taxonomy verified | ✅ |
| Coverage grid | ✅ structure — live counts pending §4 |
| Deterministic revalidation of existing answers | ⏸ not run (needs DB/API admin access) |
| Cost estimate | ✅ |
| Paid generation / semantic validation | ⛔ **not run — awaiting approval** |
| "224/224 clean" claim | ⛔ not claimed (would be false until revalidation passes) |
