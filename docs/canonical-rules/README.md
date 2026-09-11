# Canonical OET AI rule registry

Vendored source of truth for OET Writing (and future Speaking) AI grading, from
`OET_DEVELOPER_COMPLETE_HANDOFF_v1.0_2026-08-31/02_AI_SOURCE_OF_TRUTH/`. Release `1.0`,
`2026-08-31`. Verified SHA-256 against `HANDOFF_SHA256SUMS.txt` at vendor time:

| File | SHA-256 |
|---|---|
| `OET_AI_Rules_Master.jsonl` | `7f6446d304f5ab997c97ff2dd5e2d812b610dee838222d424647abbb54baa0a4` |
| `OET_AI_DEPLOYMENT_CONTRACT.json` | `972cab8e0a617cb3da5e32d06521cd079c8852bbe2813e2204a225140331ef7d` |
| `OET_AI_Source_Manifest.json` | `f0d46c4996bf6a2cef5d2dcddb88ada2b7eab19d33f760fc22a9ef52913550a1` |

`OET_AI_Rules_Master.json` (the non-streaming twin), the transcript evidence corpus, and the
legacy Medicine rule audit are **not** vendored here — only the streaming registry and its
contract/manifest are needed to build the runtime rulebooks. The full handoff package remains
at the source path above for audit.

**Amendment (10 Sep 2026, owner governance decision on the Rev5 audit, item 1 "G-W-116"):**
the six `G-W-116` rows (one per canonical profession) were edited in place — the SHA-256 above
no longer matches the file and is retained only as the original-release provenance pointer, not
a current integrity check. `G-W-116` previously read "unfortunately/fortunately are not banned
by grammar... do not auto-penalise the word itself," which directly contradicted the addendum's
emotional-wording ban. Per the addendum's own precedence rule (the newer clarification
supersedes older conflicting content) and the owner's explicit instruction, it now bans
unfortunately/fortunately/regrettably outright and bans suffering/suffered only when paired with
a dramatising intensifier — factual clinical usage ("suffered a myocardial infarction") remains
correct English and is not flagged. `classification`/`authority` were promoted to
`Hard Rule`/`OET_OFFICIAL` to match. Rebuilt via
`node scripts/rulebooks/build-canonical-writing-rulebooks.mjs` immediately after editing — the 6
`rulebooks/writing/<profession>/rulebook.v1.json` files reflect this edit.

## Revision 8 (11 Sep 2026)

Source: owner *Writing Rule Enforcement & Model Answer Validation Addendum, Revision 8*
(`FINAL_Writing_Rule_Enforcement_Model_Answer_Nursing_Addendum_REV8_11_Sep_2026.pdf`; Revision 7
is its §13). Machine-readable catalogue: `docs/writing-rev8/owner-rules-rev8.json`. Per the
addendum's precedence rule (§7) the latest owner rule wins over older conflicting internal wording.

| Item | Value |
|---|---|
| Registry release | `v1.1-rev8` = v1.0 of 2026-08-31 + OWN-W-001..038 + the supersession amendments below |
| `OET_AI_Rules_Master.jsonl` SHA-256 | `f0ff3ce01873ceb5d53c6736fed658fca5bab47b5d2bf9753a7d73cb3d153d84` (2500 lines, LF) |
| Canonical rulebook version (6 built packs) | `2.1.0-canonical-rev8`, `publishedAt` `2026-09-11T00:00:00Z` |
| Legacy rulebook version (7 hand-maintained packs) | `1.1.0-rev8` |
| Deterministic validator | `WritingRuleEngine.ValidatorVersion` = `writing-rules.rev8.2026-09-11.1` (81 check-ids) |

The v1.0 hashes in the table at the top of this file stay as original-release provenance only.
`HANDOFF_SHA256SUMS.txt`, `OET_AI_DEPLOYMENT_CONTRACT.json` and `OET_AI_Source_Manifest.json` are
the handoff package's own v1.0 artefacts and are deliberately not edited (the contract's
per-profession Writing `active_rules` are v1.0 figures; v1.1-rev8 adds 38 to each).

**Added.** 38 rows `OWN-W-001`..`OWN-W-038` appended to the end of each of the six professions'
Writing blocks (Medicine 268, Nursing 275, Dentistry 275, Pharmacy 278, Physiotherapy 278,
Radiography 275 active Writing rows). Every row uses an existing section of that profession, so
no section is added. `authority` is `DR_HESHAM_DIRECT`; `classification` comes from the catalogue
(`Owner Override`, `Hard Rule`, `Owner Clarification`, `Exception`, `Avoid`,
`Acceptable Alternative`, `Preferred Style`). Rows also carry three optional fields, used only by
these rows:

- `severity` (`critical`/`major`) overrides the derived severity (see "Severity mapping").
- `applies_to` is `"all"` or an array of the legacy letter-type tokens
  `WritingLetterTypeTaxonomy.ToLegacyLetterType` produces (`routine_referral`, `urgent_referral`,
  `discharge`, `transfer_letter`, `non_medical_referral`, `other_letters`) and becomes the rule's
  `appliesTo`. OWN-W-015..019 are urgent-referral only, OWN-W-031 routine/non-medical/other,
  OWN-W-032 discharge/transfer.
- `check_ids` lists the `WritingRuleEngine` detectors that enforce the row. The builder carries it
  as `params.checkIds` only — never a top-level `checkId` — so those detectors keep running exactly
  as before (always-on BUILTIN battery, or the legacy R-rule that already wires the checkId in the
  seven legacy packs) and every finding id, including the parity fixtures' `BUILTIN.<checkId>`
  ids, is unchanged. Rows with no detector (OWN-W-019, 031, 035, 037, 038) are enforced by the AI
  grader and the independent semantic Model Answer validator.

**Model Answer vs candidate behaviour.** One rule set, two modes (addendum §7, §11):
`canonical_rule` is the owner's rule text (the prompt renderer shows title + body);
`ai_grading_logic` (→ `params.aiGradingLogic`) states the split as `MODEL ANSWER: ...` /
`CANDIDATE: ...`, including every acceptable alternative (a different professional opening,
any clear urgent closure wording, any wording of the contact offer, advisory-only length and
contractions for candidates) and ends with "Model Answer similarity contributes ZERO to the
candidate score" (OWN-W-038). At runtime the split is enforced by
`WritingLintInput.IsModelAnswer` (stricter house style; `ModelAnswerBlockingFindings` blocks on
every non-info finding) and the two prompt blocks in `WritingRev8HouseStyle`
(`ModelAnswerCanonicalRules`, `CandidateGradingRules`). OWN-W rows are owner house style, not
claims about official OET requirements, so they are consistent with OW-030.

**Supersession amendments** (edited in place in all six professions, ids kept, following the
G-W-116 precedent above; each `canonical_rule` now starts "Superseded in part by OWN-W-0xx (owner
Rev8, 11 Sep 2026): ..." followed by the earlier wording marked as overridden wherever it
conflicts, and `notes` records the change; `classification`/`authority`, and so severity, are
unchanged):

| Row | Superseded by | What changed |
|---|---|---|
| G-W-112 | OWN-W-009 | "the patient" as the person reference is banned again; title and good/bad examples rewritten |
| G-W-110 | OWN-W-006 | descriptive numbers as words, digits for clinical values; title rewritten |
| G-W-113 | OWN-W-008 | smoker/drinker labels flagged; title rewritten, good example count in words |
| G-W-115 | OWN-W-008 | "non-compliant" no longer accepted even as the case-note term |
| G-W-089, G-W-094 | OWN-W-021 | Model Answers avoid but/so (candidates: vocabulary-preference feedback); good examples no longer use but/so |
| G-W-131 | OWN-W-023 | fixed medication-list syntax replaces "punctuation is flexible"; good example and grading logic rewritten |
| DH-W-041 | OWN-W-001, OWN-W-036 | blank-line layout is an owner hard rule, not a preference |
| DH-W-042 | OWN-W-010, OWN-W-011 (with OWN-W-004, OWN-W-025) | Re: line / DOB / age / name-form templates are mandatory |
| DH-W-012, DH-W-014 | OWN-W-017 | urgent referral: current presentation is always body paragraph 1, even if brief |
| DH-W-045 | OWN-W-006 | word-vs-number style is no longer "secondary" |
| G-W-116 | OWN-W-007 | Model Answers ban suffer/suffered/suffering outright; "sadly" added; candidate logic unchanged |

DH-W-012/014, DH-W-045 and G-W-116 are not in the catalogue's `supersedes` lists (except
DH-W-012, under OWN-W-017) but were amended because their wording directly contradicted the
named owner rule.

**Building.** Do not hand-edit the six canonical `rulebooks/writing/<profession>/rulebook.v1.json`
files. They are regenerated by `node scripts/rulebooks/build-canonical-writing-rulebooks.mjs`
(run on GitHub Actions under the compute policy, then committed; `--check` verifies the committed
files match). The builder now pins `REGISTRY_SHA256` and refuses to build from any other registry
bytes, so the authoritySource hash can no longer go stale silently: after any registry edit,
update that constant, the version fields and this section, then rebuild.

**Legacy packs.** The seven hand-maintained books (dietetics, occupational-therapy, optometry,
podiatry, speech-pathology, veterinary, other-allied-health) carry copies of the same 38 rules
(same id/title/body/severity/scope; `enforcement: "ai-grounded"`; `params` = classification,
aiGradingLogic, sourceRefs, checkIds; no `checkId`) in an existing `01`..`16` section, now 210
rules each. R-rule bodies that contradicted Rev8 were corrected: R06.7, R06.13, R08.2, R08.14,
R09.1, R12.2, R12.4, R12.8, R12.9, R12.10, R12.11, R13.6, R14.8 and the R04.1 designation line;
titles that contradicted their own bodies were rewritten (R01.5, R03.4, R03.6, R06.13, R07.9,
R08.7, R08.14, R09.1, R10.8, R10.12, R10.13, R11.8, R12.2, R12.8, R14.4, R14.9, R14.13, R15.2,
R16.3, R16.6). No R-rule id, severity, checkId, forbiddenPatterns or appliesTo changed.
`lib/rulebook/__tests__/writing-rulebook-baseline.test.ts` locks that every Writing book carries
OWN-W-001..038 with identical text.

**BUILTIN checkId ↔ OWN-W mapping** (from the catalogue `check_ids`; findings are reported as
`BUILTIN.<checkId>` in the canonical packs, and under the wiring R-rule where a legacy pack has one):

| Rule | Title | Severity | Scope | Deterministic checkIds |
|---|---|---|---|---|
| OWN-W-001 | One blank line after the Re: line | critical | all | `blank_line_after_re_line`, `model_answer_layout` |
| OWN-W-002 | Salutation and Re: line consecutive | critical | all | `salutation_re_adjacent` |
| OWN-W-003 | One date-format family and four-digit years | major | all | `date_format_consistent`, `year_not_abbreviated` |
| OWN-W-004 | DOB written as "DOB:" + date | major | all | `dob_colon_format`, `dob_age_forbidden_phrase` |
| OWN-W-005 | No brackets or placeholders | critical | all | `no_brackets_in_letter` |
| OWN-W-006 | Descriptive numbers as words | major | all | `number_style_words_vs_digits` |
| OWN-W-007 | No emotional or editorial wording | major | all | `emotional_wording` |
| OWN-W-008 | No judgmental disease or behaviour labels | major | all | `judgmental_labels` |
| OWN-W-009 | Never "the patient" as the person reference | major | all | `body_forbidden_phrase_the_patient` |
| OWN-W-010 | Name at the first mention of every body paragraph | major | all | `paragraph_start_patient_name` |
| OWN-W-011 | Do not repeat the full name after the Re: line | major | all | `body_uses_last_name_only` |
| OWN-W-012 | No relationship label for a named patient | major | all | `relationship_label_patient_reference` |
| OWN-W-013 | Model Answer opens with "I am writing to ..." | critical | all | `intro_opens_i_am_writing_to` |
| OWN-W-014 | Universal contact-offer closure | major | all | `closure_contact_offer` |
| OWN-W-015 | Urgent closure: "at your earliest convenience" then contact offer | critical | urgent | `urgent_closure_phrase` |
| OWN-W-016 | "Urgent" only in the introduction (Model Answer) | major | urgent | `urgent_token_not_repeated` |
| OWN-W-017 | Urgent referral: today/current presentation first, even if brief | critical | urgent | `urgent_body_starts_today` |
| OWN-W-018 | Urgent purpose explicit in the introduction | critical | urgent | `urgent_intro_contains_urgent` |
| OWN-W-019 | Hospital urgent referral: active conditions with current medication | major | urgent | none (AI + semantic validator) |
| OWN-W-020 | Closure does not duplicate the introduction's request | major | all | `no_duplicated_request` |
| OWN-W-021 | Linker vocabulary | major | all | `linker_avoid_words`, `linker_density` |
| OWN-W-022 | Linker punctuation | major | all | `linker_comma_and_case`, `linker_however_punctuation`, `linker_therefore_punctuation`, `linker_in_addition_punctuation` |
| OWN-W-023 | Medication-list syntax | major | all | `medication_list_punctuation`, `latin_abbreviations_translated` |
| OWN-W-024 | Space between value and unit | major | all | `value_unit_spacing` |
| OWN-W-025 | Age not duplicated between Re: line and introduction | major | all | `age_not_duplicated_in_intro`, `re_line_age_dob` |
| OWN-W-026 | Sign-off: professional designation only | critical | all | `signoff_no_invented_name`, `signoff_designation_present`, `yours_sincerely_vs_faithfully`, `yours_sincerely_capitalisation` |
| OWN-W-027 | Body 180-200 words (body only) | major | all | `letter_body_length` |
| OWN-W-028 | At least two body paragraphs | critical | all | `min_body_paragraphs`, `letter_paragraph_count` |
| OWN-W-029 | No today's exact date and no "yesterday" in the body | critical | all | `body_no_todays_date`, `body_forbidden_phrase_yesterday` |
| OWN-W-030 | No contractions | major | all | `no_contractions` |
| OWN-W-031 | Routine referral order and background placement | major | routine, non-medical, other | none (AI + semantic validator) |
| OWN-W-032 | Discharge/update letter-type exceptions | major | discharge, transfer | `discharge_intro_template`, `discharge_intro_no_identity`, `discharge_plan_present`, `discharge_admitted_with_past_simple` |
| OWN-W-033 | Professional clinical register | major | all | `register_colloquial` |
| OWN-W-034 | The closure remains a closure | major | all | `closure_contains_management` |
| OWN-W-035 | Introduction tense | major | all | none (AI + semantic validator) |
| OWN-W-036 | Paragraph spacing survives storage and rendering | critical | all | `model_answer_layout`, `blank_line_between_paragraphs`, `blank_before_closing_phrase`, `date_blank_line_sandwich` |
| OWN-W-037 | Factual fidelity and certainty level | critical | all | none (grounding + semantic validator) |
| OWN-W-038 | Candidate grading: zero Model Answer similarity | critical | all | none (grader policy) |

## What this is for

`scripts/rulebooks/build-canonical-writing-rulebooks.mjs` reads `OET_AI_Rules_Master.jsonl`,
filters `skill == "Writing" && active_for_ai == true`, and regenerates
`rulebooks/writing/<profession>/rulebook.v1.json` for the six professions the platform
actually has live Writing content for (medicine, nursing, dentistry, pharmacy,
physiotherapy, radiography). This **replaces** the legacy 172-rule-per-profession set
(cloned from a single Medicine PDF) as Writing scoring truth — the legacy set is exactly the
`R##.#`-id, `active_for_ai: false` provenance appendix this registry's own deployment contract
marks `never_load_as_scoring_truth`.

The other 7 profession folders (veterinary, optometry, occupational-therapy,
speech-pathology, podiatry, dietetics, other-allied-health) are untouched and stay on the
legacy content until/unless the platform launches Writing for them, at which point this same
build script should be extended to cover them once a canonical pack exists.

**Correction (10 Sep 2026, Writing Rule Enforcement Addendum Rev5 source-coverage audit):**
the "zero live Writing tasks" claim above is stale/wrong for 5 of these 7. A direct production
query (`WritingTaskModelAnswers` joined to `WritingScenarios`, `IsCandidateVisible = true`)
confirmed real, currently candidate-facing Model Answers on the legacy `R##.#` rulebook for
dietetics (12), occupational-therapy (5), optometry (5), podiatry (5), and speech-pathology
(5) — 32 letters total, none migrated. Only veterinary and other-allied-health genuinely have
no live content. Treat this doc's "5 live professions" framing as outdated wherever it appears
(also `docs/RULEBOOKS.md`, which still shows only the single `medicine/rulebook.v1.json`
example and doesn't mention the canonical `OW-`/`DH-W-`/`G-W-` id scheme at all) until those 5
professions are migrated or this note is otherwise superseded.

**Migration status (10 Sep 2026, owner governance decision, item 2 "The 5 additional
professions"):** checked the registry directly — `OET_AI_Rules_Master.jsonl` has **zero** rows
for Dietetics, Occupational Therapy, Optometry, Podiatry, or Speech Pathology, for either skill
(only Medicine/Nursing/Dentistry/Pharmacy/Physiotherapy/Radiography exist, ~230-240 Writing rows
each). There is no vendored canonical content for these 5 professions to migrate — this build
script cannot construct a "canonical" AI-grounded profession pack out of nothing, and
hand-authoring 5 new profession-specific clinical/register rule packs is a content-authorship
task requiring real subject-matter review, not a safe autonomous code change. What genuinely
**is** already true, and was confirmed by re-running the full audit after today's fixes: every
deterministic check in `WritingRuleEngine.SupportedCheckIdSet` (all 69 check-ids, including
every rule this addendum added — `emotional_wording`, `judgmental_labels`,
`blank_line_after_re_line`, etc.) fires uniformly for **all 11** professions regardless of which
rulebook is loaded, via the "always-on builtin battery" in `WritingRuleEngine.Lint()`. So the 32
legacy-rulebook letters already get the full corrected deterministic rule set live; the gap is
narrowly the richer AI-grounded contextual judgement layer these 6 professions get from their
canonical registry rows. Closing that gap needs an updated content release from the same source
as the original 31-Aug-2026 handoff, covering the 5 missing professions.

## Severity mapping (registry has no severity field — derived, not invented)

The canonical registry has no `critical`/`major` concept; `classification` + `authority` are
mapped to `OetRule.Severity` so the existing prompt renderer's "critical rules always shown,
major rules flagged as high-priority" structure still works:

- **Explicit** — a row's optional `severity` (`critical` or `major`; Rev8 OWN-W rows only) wins
  over everything below. The builder rejects any other value.
- **Critical** — `authority == OET_OFFICIAL` (the six official marking criteria/definitions)
  or `classification` in `{Safety, Hard Rule, Hard Strategy, Validated Override, Owner Override}`
  (Dr Hesham's explicitly hard-mandatory / patient-safety / already-validated-override / owner
  Rev8 override content).
- **Major** — everything else: `Language Rule` (grammar mechanics — articles, prepositions,
  tense, punctuation, cohesion, register), `Strategy`, `Style`, `Technique`, `Preference`,
  `AI Rule`, `Profession Rule`, and the Rev8 `Owner Clarification`, `Exception`, `Avoid`,
  `Acceptable Alternative`, `Preferred Style`.

No canonical rule maps to `minor`/`info` — every active rule must be visible to the grader.
`Enforcement` is `AiGrounded` for every canonical rule (the registry's own
`ai_grading_logic` field instructs contextual judgement, not fixed-pattern matching); `CheckId`
and `ForbiddenPatterns` are left null so `WritingRuleEngine`'s deterministic detectors (keyed
by `CheckId`) never run against them — confirmed safe: the engine skips any rule with a null
`CheckId`/empty `ForbiddenPatterns`. `good_example`/`bad_example`/`ai_grading_logic`/`notes`/
`source_refs`/`classification` (and, for Rev8 rows, `check_ids` as `params.checkIds`) are
preserved under each rule's `params` for admin/audit display; nothing from the canonical record
is discarded. `appliesTo` is `"all"` unless a row sets `applies_to`.

Re-run the build script whenever `OET_AI_Rules_Master.jsonl` is updated upstream (verify the
new file's SHA-256 against a fresh `HANDOFF_SHA256SUMS.txt` from the same release drop first).
