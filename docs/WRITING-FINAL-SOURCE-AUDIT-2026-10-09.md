# Writing Model Answers - final source-vs-answer audit (9 Oct 2026)

Owner request: one FINAL full semantic audit of all 223 verified Model Answers against their source case notes, with Daniels and Erika Stone re-checked after repair. Source precedence is CASE NOTES / SOURCE PDF > MODEL ANSWER.

## Result

- **223 answers audited, each by two independent reviewers** (pass A: consecutive blocks; pass B: a different split). 6,172 + 6,396 = 12,568 atomic claims (every name, date, number, drug/dose/frequency, side, finding, action, request) were traced to a source line; the per-claim ledgers are in the evidence folder.
- **181 answers: no finding from either reviewer.** 42 answers carry at least one finding: **13 confirmed wording conflicts with the notes (repair prepared)**, 12 more need an owner decision (14 items) (letter-date anchor, an editorial closing, or a source that contradicts itself), 17 more contain a specific fact that is in no stored note line, so only the original PDF page can settle it.
- **Writing is not yet closed.** The owner's bar is zero factual discrepancy; 13 answers still fail it until REPAIRS.json is applied, and 12 + 17 more need a decision or a look at the PDF.
- **Daniels** (pack 137, `cbffa23c-...`, live text captured 9 Oct 05:43 UTC after the repair): indapamide is `once daily` and matches the notes; every other claim traces. One defensible note (verapamil `has been taking` vs the notes' `had been taking`). **Erika Stone** (pack 178, `db7a7a73-...`, live text): `Glipizide, two 5 mg tablets each morning` matches note 8; clean. Both come from the peer session's live capture, not from a fresh pull by me.
- Medicine dose, route, laterality and medicine-frequency: **no dose, frequency or laterality error was found in any of the 223** by either reviewer or by the deterministic cross-check. After the heuristic dose/frequency/side hints were resolved, the errors found are completed-vs-planned actions (4 confirmed, 1 judgement call), unplanned or invented requests (3, plus 1 to check), meaning changes (6), a title and an address (2), and letter-date anchors (9).

## Limits (read before relying on this)

1. **Texts audited are exports, not a fresh live pull.** 167 letters are the 22 Sep set, 54 Medicine answers are the 21 Sep production snapshot, Daniels and Erika Stone are the 9 Oct live captures. The agent holds no admin credentials. `verify-live-final.ps1` re-runs the deterministic gate on the live answers; `apply-repairs.ps1` skips any answer whose live text no longer contains the `find` string.
2. **The source is the stored case notes plus the DOB/ROM lines recovered verbatim from the stimulus PDFs** (the 9 Oct grounding session found ~91 scenarios whose stored notes lost the DOB line). The original PDFs of the 223 tasks are not on disk here. A fact absent from every stored line is reported as UNVERIFIABLE, never assumed wrong.
3. **Reviewers are language-model agents** reading each pack twice independently. Agreement was high (181 clean-clean, 19 discrepancy-discrepancy) but this is not a proof. Heuristic hints (names, dates, number+unit, dose, frequency, laterality, completed-vs-planned) were given to them and each hint had to be resolved.
4. **Nothing was executed or tested**: compile in `Build images` is the only check of the code below; the 45-case Validator self-check has not been run. Not tested - owner QA.

## Confirmed conflicts with the notes - repair prepared (REPAIRS.json, status READY)

| # | Answer | Class | Conflict | Edit (find -> replace) |
|---|---|---|---|---|
| 17 | Nursing - Greg Whitby | completed-vs-planned | Note 15 'To be informed of the possible side effects' is a plan; the letter says it was done. | `he has been informed of the possible side effects of the ant` -> `he is to be informed of the possible side effects of the antibiotics` |
| 30 | Nursing - Ms Elizabeth Carmel | completed-vs-planned | Note 32 'Referrals are to be initiated ...'; the letter says they have been initiated. | `Referrals have also been initiated to` -> `Referrals are also to be initiated to` |
| 87 | Nursing - Mr Peter Black | completed-vs-planned | Note 28 'Contact with Quitline to be encouraged'; the letter says it has been encouraged. | `Quitline contact has been encouraged` -> `Quitline contact is to be encouraged` |
| 139 | Pharmacy - Mr Pablo Martinez | completed-vs-planned | Note 22 'Plan: counsel on lifestyle, exercise and diet'; the letter says 'I counselled him'. | `I counselled him on lifestyle, exercise and diet.` -> `I plan to counsel him on lifestyle, exercise and diet.` |
| 101 | Nursing - Mr Derek Shepherd | invented-content | 'upon his request' is in no source line and makes the planned progression (note 32) conditional on the patient asking. | `upon his request,` -> `` |
| 84 | Nursing - Ms Kylie Weiss | meaning-change | Note 12 'Dyslipidaemia, previously untreated'; she is now on atorvastatin 40 mg. 'untreated' is false in the present tense. | `has untreated dyslipidaemia` -> `has previously untreated dyslipidaemia` |
| 117 | Pharmacy - Robert Anderson | meaning-change | Note 31: gingko + warfarin 'increases the risk of blood clot failure'; the letter says the risk of a blood clot (the opposite). | `increase the risk of a blood clot.` -> `increase the risk of blood clot failure.` |
| 192 | Medicine - Mr Julian McDonald | meaning-change | Note 16 'alcohol intake greater than 6-10 standard drinks/day'; the letter caps it at six to ten. | `drinks six to ten standard drinks daily` -> `drinks over six to ten standard drinks daily` |
| 42 | Nursing - Mrs Jane LaPaglia | diagnosis-timeline | Note 14: the pneumonia was hospital-acquired, so it was not an admission diagnosis; the letter says admitted with pneumonia (and then calls it hospital-acquired). | `renal failure secondary to dehydration, mild dementia and pn` -> `renal failure secondary to dehydration and mild dementia.` |
| 64 | Nursing - Mrs Margery Drake | clinical-course | 'Significant assistance' was the day-3 status (note 23); the discharge summary (note 32) gives assistance for bathing and washing only. | `, requiring significant assistance.` -> `.` |
| 66 | Nursing - Julia Montoa | meaning-change | Note 23 'Partner risk discussed: no IV drug use, no recent overseas travel' is the PARTNER's history and says 'recent'; the letter makes it the patient's history. | `There is no history of such infection, IV drug use or overse` -> `She has no history of such infection. Her partner has no IV drug use o` |
| 134 | Pharmacy - Mr Erwin Morrison | identity-title | Note 1 'Ms Katrina Morrison'; the letter says 'Mrs'. | `Today, Mrs Katrina Morrison presented` -> `Today, Ms Katrina Morrison presented` |
| 107 | Occupational Therapy - Urgent Referral - Amelia Brooks | unplanned-request | Notes 40-41 plan a REVIEW for bath equipment and an ASSESSMENT for domestic support; the letter asks the recipient to ARRANGE them. (One reviewer; confirmed on re-read.) If the live validator rejects this as a repeat of the introduction's request, send me its output. | `I would be grateful if you could arrange a bath board or alt` -> `I would be grateful if you could review bath board or alternative show` |

Each edit keeps the body inside 180-200 words by estimate (word counts of the stored answers plus the edit delta); the live validator decides. `apply-repairs.ps1` validates first and imports/approves only with `-Apply`.

## Needs an owner decision (REPAIRS.json, status NEEDS-OWNER-DECISION)

- **#31 Nursing - Mrs Beryl Casey** (date-anchor): Discharge letter dated on the admission day (4 February 2014) and says 'admitted today'; it narrates day 5-7 events. Discharge is 'day 7' (about 10-11 February), which no note dates. Decide the letter date (set the task's TodayDate) or accept the admission date. _Wording fix is safe whichever letter date you choose; the letter date itself needs your decision._
- **#97 Nursing - Mrs Nina Davies** (date-anchor): Transfer letter dated 28 June 2017 (admission) saying 'admitted today' but narrating 29 June - 2 July; patient ready for discharge 2 July. Letter date should be 2 July 2017 or later. _Set TodayDate to 2 July 2017 if you agree._
- **#55 Nursing - Bruce Brew** (date-anchor): Task says admitted 5 days ago (6 July) = 11 July 2017; the letter is dated 8 July and says 'Today, endoscopy ... confirmed'. The validator forbids a letter date later than every documented date unless TodayDate is set, so set TodayDate = 11 July 2017 first. _Set the task's TodayDate to 11 July 2017, change the letter date line to 11 July 2017, then apply._
- **#18 Nursing - Mr O'Riley** (date-anchor): Dated on the admission day (2 September 2009) but reports completed CABG x4, routine recovery and return home; no note gives the discharge/letter date. Check the PDF.
- **#23 Nursing - Ms Olivia Hawthorne** (date-anchor): Letter dated 6 April 2019, the date of an EARLIER review (note 23: no bleeding, no dysmenorrhoea), while the letter says the IUD could not be removed 'today' with spotting/menorrhagia. The date of today's visit is in no stored note. Check the PDF.
- **#74 Nursing - Ms Michelle Norris** (date-anchor): Dated 22 April 2015 (the District Nurse's planned re-dressing, note 41) and asks to re-dress 'today'; discharge was 19 April (note 35) and the review 'in one week' no longer fits. Check the PDF for the writing date.
- **#85 Nursing - Mr Michael Collins** (date-anchor): DOB 1 September 1940 and letter date 11 January 2018 make him 77; note 3 says age 78. Either the date format (dd/mm vs mm/dd) or the letter date is wrong. Check the PDF.
- **#209 Medicine - Ms Isabel Garcia** (date-anchor): Letter dated the day she presented (23 May 2015) but narrates later culture results and completed 4- and 5-day courses. Check the PDF for the letter/discharge date.
- **#66 Nursing - Julia Montoa** (date-anchor): 'her last sexual contact was fourteen days ago' counts from the 15 May visit (note 20) but the letter is dated 18 May. _Reword to '... fourteen days before her presentation' if the letter date stays 18 May._
- **#134 Pharmacy - Mr Erwin Morrison** (date-anchor): Letter and TodayDate are 21 January 2015, but note 13 puts 21 January three days BEFORE the wife's visit ('Three days ago ... (on 21 January)'), i.e. today = 24 January 2015. Check the PDF for the stated date.
- **#43 Nursing - Alisha John** (recipient-address): The task text stores 'Nor=h Adelaide 3001' (OCR); the letter writes 'Adelaide 3001', dropping 'North'. Correct the task text first (the validator compares the address with the stored task), then add 'North'.
- **#128 Pharmacy - Mrs Tomomi Aoki** (unplanned-request): The task is to inform the new GP of the medication history (note 29). The letter asks her to 'provide prescribing care' (introduction) and to 'continue' vitamin B6 and prenatal vitamin prescriptions (closing); no note plans either. _Editorial: the house closure template needs a request, but the notes plan none. Your wording decision._
- **#194 Medicine - Mr Patrick Newton** (completed-vs-planned): Notes 34-35 'Plan: advise on smoking cessation' / 'Plan: counsel on IBD and likely investigations'; the letter says 'I advised ... and discussed ...'. Same class as Martinez (#139) but the author is the consulting GP on the day. Decide whether 'Plan:' lines may be read as done in the consultation.
- **#137 Pharmacy - ADR report re Mrs Daniels** (medication-status): Live (repaired) Daniels text says she 'has been taking ... verapamil, 80 mg twice daily' while note 6 says verapamil 'she had been taking' before the switch to Drug X; the letter later says 'which she took'. One reviewer calls it a discrepancy, the other a defensible note. The indapamide frequency (once daily) is correct. _Optional: 'had been taking' for verapamil._

## Needs the original PDF page (REPAIRS.json, status CHECK-PDF)

- **#63 Nursing - Ms Osburn**: Re: line 'Ms Monica Osburn': 'Monica' is in no source line (every note says 'Ms Osburn').
- **#127 Pharmacy - Ms Marion Stokes**: Re: line 'Ms Marion Stokes': 'Marion' is in no source line (notes say 'Ms Stokes').
- **#126 Pharmacy - Mr Alex Roden**: Re: line 'Mr Ian Roden'; the patient-details note says 'Alex Roden' while notes 1-2 say Ian; the task says 'Rodin'. The source contradicts itself.
- **#206 Medicine - Ms Betty Weston**: 'Mrs Betty Weston': no note gives a title; the patient is divorced and the scenario header says 'Ms'.
- **#90 Nursing - Mr James Andresen**: Patient name 'Andresen' appears in no stored note line (only in the scenario title).
- **#61 Nursing - Keisha Odet**: Recipient postcode 'EC1 1BB': the task postcode is garbled ('EG1 1%%-'); '1BB' is in no source line.
- **#65 Nursing - Shelly Kate**: Recipient street '111-1 Devonshire St': the task is garbled ('111-1 Wa Devonshire St').
- **#79 Nursing - Ann Ballard**: Child's sex/name/DOB are in no stored line; note 10 says the mother is 60 AND 28; dressing frequency is 'daily' (note 27) and 'every 4-7 days' (note 33).
- **#114 Pharmacy - Ellen White**: Letter date '6 March 2019' is in no source line (incident 3 Feb, Board deadline 10 March).
- **#180 Medicine - Janet Pristiely**: Insulin 50 IU and a statin 40 mg are written as 'continued' at discharge; the discharge plan lines do not say so; re-admission year inferred.
- **#198 Medicine - Mrs Karen Jackson**: Closing asks the GP to 'monitor the pregnancy and discuss delivery options'; the task asks for anxiety management and the stored notes contain no plan line (one may have been lost).
- **#27 Nursing - Mr Lionel Ramamurthy**: Sender role 'Charge Nurse' is in no stored line.
- **#32 Nursing - Kate Murray**: Recipients 'Mr and Mrs Murray' are named nowhere (task says 'the child's parents').
- **#46 Nursing - Ms Elizabeth Carmel**: 'inject insulin thirty minutes BEFORE meals': note 33 gives the 30 minutes but no direction.
- **#19 Nursing - Mr Tom Clarke**: Recipient title 'Mr Kumar' assumed (task gives the name only).
- **#69 Nursing - Harry Kovacs**: Recipient title 'Mr Dyer' assumed; the writer role in the task (Case Manager) differs from the sign-off.
- **#164 Speech Pathology - Discharge - Alice Brown**: 'returns to your care home today': no note gives the discharge date; the 'no coughing' line is limited to mildly thick fluids.

## Code shipped with this audit (commit c6e1c4a0e)

- **Source precedence**: `WritingTaskModelAnswerService` refuses to approve, and `WritingTaskCaseNotesService.ReplaceAsync` holds a published answer (`model_answer_source_conflict`), when the answer disagrees with the current notes on a medicine frequency, a completed-vs-planned action, the letter date, the recipient, age/DOB or Re: identity, an unplanned requested action, a re-labelled vital sign or an unsupported discharge. Candidates were never graded against the Model Answer: no grader, reviewer, applier or prompt builder reads it (`LoadVerifiedTaskModelAnswerAsync` is display-only).
- **Source grounding**: `WritingSourcePresence` no longer lets a sign on another value in the same sentence (`... flexion was 90 degrees with extension to -5 degrees`) block the proof for the claimed value, so the Physiotherapy DOB and 90-degree knee-flexion false `invented` claims are removed in the real sentence form. Doses, frequencies, ages and names are still never suppressed by code.
- **Completed-vs-planned detector** now also covers `informed`, `initiated`, `encouraged` and `counselled` (the verbs behind four confirmed errors).
- **Validator self-check**: 45 cases (26 existing + 5 new completed-action cases + 14 source-grounding / source-precedence probes: DOB and knee flexion removed, a real absence kept, the other side kept, a dose never suppressed, the reviewer prompt carries the computed source check, the grader prompt carries the SOURCE-GROUNDING rule, the prompts rank the notes above any Model Answer).

## Evidence folder

`.tools-state/writing-final-audit-2026-10-09/`: `REPAIRS.json`, `apply-repairs.ps1`, `verify-live-final.ps1`, `consolidated.txt` (both reviewers side by side for every flagged answer), `results/A01-A10.json` and `B01-B10.json` (per-claim ledgers), `packs/` (source + answer + hints for each of the 223), `index.json`.

## Closure run (live, 9 Oct 2026, 16:01 UTC)

The owner asked for the repairs, the self-check and the live verification to be done without any manual step and without
any agent credential. A temporary job (`WritingFinalAuditJob`, shipped through the normal pipeline, removed afterwards) ran
inside production after each deploy: it applied the audited edits only where the LIVE text still contained them and the full
Model Answer gate passed, ran the Validator self-check on the server, and re-validated every stored answer against the CURRENT
case notes (no apply, no AI call). Four runs (commits c6e1c4a0e, then rounds two to four) took it from 17 applied repairs and
6 failing active answers to:

- Validator self-check: **45 / 45** (26 abbreviation cases + 5 completed-action cases + 14 source-grounding / precedence probes).
- Candidate-visible verified answers: **223**; re-validated against the current notes: **223 passed, 0 failed, 0 source conflicts**.
- Repairs: 33 entries, all applied or already applied; the gate refused three wordings in earlier runs and changed nothing.
- Decisions: Casey, O'Riley, Hawthorne, Collins and Norris state no letter date (the PDFs give none); Brew (task: admitted 5 days
  ago) and Davies (ready for discharge 2 July) use the source-derived writing day for the gate; McDonald is unchanged (owner rule
  OA-08: "six to ten standard drinks daily"); Roden follows the PDF patient-details line (Alex).
- Checked against the original PDF text layer (embedded text only, no OCR): Osburn "Monica", Andresen, the Charge Nurse role,
  the White letter date 6 March 2019, Casey "Day 7", Norris re-dress 19/04/15 and 22/04/15, Collins "78 years" with DOB 01/09/1940
  and an OT date of 11/01/18 (the PDF contradicts itself), Brew "admitted 5 days ago", Brown final review 27/08/26.
- Not checked against the PDF image: the scanned PDFs (Stokes, Weston, Ballard, Garcia, Morrison) were settled from the scenario
  titles and explicit TodayDate; Carmel "thirty minutes before meals" is an inference the stored note does not state.

Evidence: `.tools-state/writing-final-audit-2026-10-09/live-run-1.json` .. `live-run-4.json`.
