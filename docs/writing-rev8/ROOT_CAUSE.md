# Writing Rev8 — Root cause analysis

Owner document: *FINAL Writing Rule Enforcement, Model Answer & Nursing Addendum, Revision 8* (11 Sep 2026).
Branch: `fix/writing-rev8-rule-enforcement`. Validator version introduced: `writing-rules.rev8.2026-09-11.1`.

## 1. Why the live Mr David Taylor Model Answer was shown as clean

Task `07d56634-dc0f-4afc-9b3d-ae1d527f1314` (Medicine, LT-UR, urgent rheumatology referral).

The saved answer broke 12 owner rules. They are pinned by
`WritingRev8LiveFixtureTests.Taylor_LiveDefect_ModelAnswer_Fails_Every_Documented_Rule`:

| # | Owner rule | Defect in the live letter | Why nothing caught it before Rev8 |
|---|---|---|---|
| 1 | OWN-W-001 blank line after Re: | Introduction starts on the line after `Re:` | Detector existed but only raised Major. The pre-Rev8 gate held **Critical findings only** |
| 2 | OWN-W-025 age not duplicated | "aged 55" in both the Re: line and the introduction | Major only, so not blocking |
| 3 | OWN-W-011 full name not repeated | "Mr David Taylor" repeated in the introduction | No detector (`body_uses_last_name_only` was not wired to a canonical rule) |
| 4 | OWN-W-017 urgent: today first | Body opened with the 2000 diagnosis | The detector compared against `urgent_referral`, but the task stored `LT-UR`. The Rev5 normalisation fix (10 Sep) landed, but saved answers were **never revalidated** |
| 5 | OWN-W-029 no today's date in body | "presented on 13 June 2020" | Major only |
| 6 | OWN-W-015 urgent closure AEYC | No "at your earliest convenience" | Same LT-UR no-op as #4, and no revalidation |
| 7 | OWN-W-016 "urgent" only in intro | "urgently assess" in the closure | Same LT-UR no-op |
| 8 | OWN-W-014 contact offer | No contact-offer sentence | No detector existed |
| 9 | OWN-W-010 name at paragraph start | Every body paragraph opened with "He" | No detector existed |
| 10 | OWN-W-021 linker vocabulary | "his brother also has gout" | Detector only checked sentence-initial position |
| 11 | OWN-W-007 emotional wording | "He suffered attacks" | "suffered" was allowed in every mode |
| 12 | OWN-W-023 medication punctuation | "colchicine 1 mg" with no comma | No detector existed |

### The five underlying causes

1. **Gate severity.** `WritingTaskModelAnswerService` held an answer only on `Critical` lint findings, so Major and Minor owner rules never blocked publication. Rev8 now blocks on **every finding except Info** (`WritingRuleEngine.ModelAnswerBlockingFindings`).
2. **Missing or inert detectors.** About 20 owner rules had no deterministic check at all, or a check that never fired. Examples: "the patient", number style, contact offer, "I am writing to" opening, paragraph-start naming, relationship labels, medication-list punctuation, value–unit spacing, DOB colon, 2-digit years, model-mode "suffered", "also"/"but" anywhere, and layout. Rev8 adds the missing detectors (`WritingRuleEngine.Rev8.cs`, registered in `SupportedCheckIdSet`) and maps each owner rule to its checkIds (`docs/writing-rev8/owner-rules-rev8.json`).
3. **No validator provenance.** A saved answer recorded no validator version or rule-pack hash. So nobody could tell which rule set had verified it, and a rule fix (e.g. the Rev5 LT-UR normalisation) did not invalidate earlier "clean" flags. The "224 clean" claim came from catalogue-compatibility checks, not from re-running the lint. Rev8 fixes this:
   - It stores `ValidatorVersion`, `RulePackHash`, `ValidatedAt`, `ValidationReportJson`, `RepairCount` and `BodyWordCount` (migration `20261230090000_WritingModelAnswerValidatorProvenance`).
   - Candidates see only answers whose `ValidatorVersion` equals the current one (`WritingTaskModelAnswerService.CandidateVisibleVerified`).
   - Approval refuses anything not verified by the current validator.
4. **Offline drafting without the rules.** The 224 production answers were imported through the hybrid drafting path. The drafter prompts did not contain the owner rules above. The generator prompt also lacked "at your earliest convenience", and it lacked the letter-type-scoped rules because `LT-*` codes were not normalised in the prompt builder. Rev8 puts the full owner house style (`WritingRev8HouseStyle.ModelAnswerCanonicalRules`) into the generator, repair and semantic-validator prompts.
5. **No semantic validation or repair loop.** Several owner rules cannot be checked with a regex: factual fidelity, certainty level, background placement, hospital-urgent active conditions, and introduction tense. No semantic validator existed. Rev8 adds `WritingModelAnswerSemanticValidator` (`writing.model-answer-validate.v1`). The pipeline is now: generate → deterministic lint → semantic validation → repair only the failed rules (up to 4 attempts) → re-run everything → store only at zero violations.

## 2. Nursing "task cannot be loaded" (P0)

Reproduced in production on 11 Sep 2026 with a finite-balance Nursing learner. The eligibility call returned **HTTP 500**, and the task page showed a dead end.

- **Cause:** `AiPackageCreditService` wrote the credit-transaction `JobId` into a `varchar(64)` column. The Writing job id is built from the user and task identifiers and is longer than 64 characters, so Postgres rejected the insert. Unlimited-credit accounts never took this path. That is why admin and QA accounts never saw the failure.
- **Contributing causes:**
  - The learner task DTO did not carry the task prompt, so a successful load could still render an empty stimulus.
  - The session page printed raw API errors and gave no recovery path.
- **Fix:**
  - `FitColumn` truncates the value and adds a hash suffix, keeping ids unique.
  - The eligibility endpoint now returns typed errors.
  - The learner DTO now carries the task prompt.
  - The session page shows candidate-safe error messages with retry and back actions.
  - `WritingTaskLoadIntegrityService` adds an admin scan (`/v1/admin/writing/tasks/load-integrity`). It proves that every published task, in every profession, loads end to end.

## 3. Candidate grading drift

The candidate grader and the Model Answer validator resolved rules differently:

- LT-* letter types were not normalised in the prompt builder.
- Rule IDs were not cited consistently.
- AI-detected mistakes that had no rule ID were silently dropped from the report.

Rev8 makes the following changes:

- One resolved rule set per Task ID. It is the same rulebook, the same checkIds and the same `RulePackFingerprint`, applied in candidate mode.
- AI findings are persisted as `AI:{ruleId}` or `AI.{criterion}` rows and de-duplicated against deterministic quotes.
- The Score prompt requires every distinct mistake, and never a valid professional alternative.
- Similarity to the Model Answer contributes zero (OWN-W-038).
