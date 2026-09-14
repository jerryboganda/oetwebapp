# OET Writing Model Answer Rules — PERMANENT OWNER DIRECTIVES

**Authority:** product owner (Dr Ahmed Hesham) — Writing Rule Enforcement Addendum Rev7-8 (11 Sep 2026), Writing Master Specification "ULTIMATE FINAL" (13 Sep 2026) the FINAL WRITING OWNER CLARIFICATIONS ADDENDUM (14 Sep 2026, rules OA-01..OA-15) and ADDENDUM TWO (14 Sep 2026, rules OA2-01..OA2-20). Latest owner clarification supersedes conflicting older internal wording.
**Applies to:** every person or AI agent who generates, repairs, validates, imports, or assesses OET Writing Model Answers in this repository — regardless of which tool or agent is used.
**Enforcement:** every language rule below is enforced by a named deterministic detector in the deployed validator (`WritingRuleEngine` / `WritingRuleEngine.Rev8`), registry rows `OA-01..OA-15` and `OA2-01..OA2-20` in `docs/canonical-rules/OET_AI_Rules_Master.jsonl`, provenance rows in `WritingRuleProvenance`, and pinned by regression tests (`WritingRev8RegressionFixtureTests.cs` — five clean-letter positives plus every injected defect — and `WritingOwnerAddendumTwoRegressionFixtureTests.cs` — regression classes R2-01..R2-18, each proving BOTH that the injected defect is caught and that the valid alternative passes). If a rule here and the validator ever disagree, BOTH are wrong — fix the validator and update this doc together. A stored Ready flag is valid only for the exact validator version it was verified under: any rule-pack change (bump of `WritingRuleEngine.ValidatorVersion`) invalidates affected answers until revalidated.

---

## 1. THE $0 HARD RULE (no exceptions)

Never call a paid AI API for Writing work: no generation, no semantic validation, no grading. The coding agent writes, repairs, and semantically reviews every letter **itself**, using:
- deterministic validation: `POST /v1/admin/writing/tasks/{id}/model-answer/validate` with `{"letterText": ..., "includeSemantic": false}` — free, ~2 s;
- import with `{"letterText": ..., "includeSemantic": false}` (default is `true` — always pass `false` explicitly);
- the agent's own documented semantic review against the rule map (handoff: `.tools-state/rev8-writing/AGENT-HANDOFF-WRITING-REV10.md`).
Only the owner can lift this rule, explicitly, in writing.

## 2. THE WORKFLOW PRINCIPLE (owner, verbatim intent)

> CASE NOTES + WRITING TASK + CURRENT RULES → produce the best clinically accurate OET Model Answer → validator checks that answer correctly.
> NOT: validator has a limitation → rewrite good clinical English strangely → make the regex stop detecting it → report PASS.

- **If correct professional wording exposes a weakness in the validator, FIX THE VALIDATOR.** Never restructure correct clinical English so a regex stops seeing it (that is a bypass, not a fix). Historical examples: the medication parser was taught dose ranges ("5-10 mg") and combination strengths ("Targin, 20/10 twice daily"); the intro tense check was taught the canonical present-perfect opening ("Mr Weir, who has presented with ...") instead of banning the owner's own preferred openings.
- **Targeted repair only:** existing best draft → identify exact defect → targeted repair → revalidate. Full regeneration only if the content itself is fundamentally wrong.
- **Every new defect type the owner flags becomes a permanent regression test** (positive: the corrected letter lints clean; injection: the deliberately re-introduced defect fires its specific rule; control: valid alternatives produce zero false positives) before any further letters are produced. See `WritingRev8RegressionFixtureTests.cs` for the pattern.
- **Status is determined by the actual saved text + exact source + active rules, never by a stored Ready flag.** A false-clean validator result is not release evidence.

## 3. CONTENT SELECTION PRINCIPLE

The 180–200-word body cap means selection is a tested skill: **do NOT copy every case-note fact into the letter.** Priority order when space forces a choice:
1. NEVER drop: allergies (with reaction type), current medicines with dose + frequency, infection findings + antibiotics given, pain/functional story, smoking/alcohol **with their daily frequencies**, the requested services, follow-up appointments.
2. Drop first (lowest clinical value for the recipient): prosthesis/implant brands, operative approach, drains, pets, entitlement cards (DVA etc.), transient mild symptoms, medicines already **ceased**, day-numbering administrative details.
3. Never buy word count by deleting a clinical fact; buy it from connective tissue.
4. Whatever the writer claims was retained MUST be in the final letter — explanation and letter are always consistent.

## 4. LANGUAGE RULES (each enforced — wrong → right, all real corrections)

| Rule | Wrong (from real cases) | Right |
|---|---|---|
| Premium clinical register, even when the case notes are colloquial | "feeling tired, stressed and sluggish" | "with fatigue, stress and lethargy" |
| No vague durations or note-form comparatives (OA-08) | "has been overweight long term" / "for a long time" | "has long been overweight" (14 Sep 2026 owner preference) |
| No emotional/judgmental observations | "He appeared anxious with shortness of breath." | "He had shortness of breath." |
| Weak preposition in exam findings | "bruising to her left arm" | "bruising on her left arm" |
| Passive voice for medications (OA-09) | "amitriptyline ceased" / "dexamethasone continued six-hourly" | "amitriptyline was discontinued" / "dexamethasone was continued every six hours" |
| Passive voice for treatment change (OA-09) | "Treatment changed to benzylpenicillin" | "Treatment was changed to benzylpenicillin" |
| Incomplete complement structures (OA-09) | "Examination showed afebrile" | "On examination, she was afebrile" |
| Impossible quantities (OA-08) | "drinking over six to ten standard drinks daily" | "drinking six to ten standard drinks daily" |
| Named objects (source fidelity) | "possible removal" | "possible tophus removal" / "possible removal of the tophus" |
| Coordinated results (OA-10) | "the white cell count was 14.0, the CRP was 150" (comma splice) | "The white cell count was 14.0x10^9/L and the C-reactive protein level was 150." |
| Result nouns need a head word (OA2-11) | "a cholesterol of 6.37 mmol/L" / "a CRP of 150" | "the cholesterol level was 6.37 mmol/L" / "the C-reactive protein level was 150" |
| Counts are counts, not bare plurals (OA2-08) | "Lumbar puncture showed 1000 white cells" | "Lumbar puncture showed a white cell count of 1000" |
| Never force a preposition (OA2-08) | a regex demanding "at" or "of" | "the temperature was 37.8 °C" and "a temperature of 37.8 °C" are BOTH correct |
| Supine position (OA2-10) | "on supine position" | "when supine" / "while supine" / "when lying supine" / "in the supine position" |
| No narrative semicolon (OA2-07) | "...twice daily; dexamethasone was continued..." / "...in 2010; kidney stones were noted..." | a full stop, or a clean "and" — the semicolon is reserved for medication lists |
| Background placement (OA2-12) | history, habits or social facts inside the opening current-problem paragraph | a dedicated background paragraph immediately before the closure |
| Descriptive numbers as words (OA2-15) | "20 cigarettes daily" / "a 48-hour ketamine infusion" | "twenty cigarettes daily" / "a forty-eight-hour ketamine infusion" (doses, dates, vitals and lab values stay numeric) |
| Role salutation (OA2-17) | "Dear Sir/Madam," or "Dear Admitting Officer," for a task naming "The Admissions Officer" | "Dear Admissions Officer," — and a role is not a name, so the letter still closes "Yours faithfully," |
| Allied-health reader (OA2-18) | translating diabetes mellitus / hypothyroidism / arthrosis for an occupational therapist | keep the clinical term; decide inclusion by what matters to their functional, safety or therapy role |
| Contact offer (OA2-19) | "Please do not hesitate to contact me with any queries." | "Should there be any queries, kindly do not hesitate to contact me." |
| One medication identity (OA2-18/R2-18) | "colchicine, also known as Lengout" repeated in two paragraphs | name it once, then use one consistent identity |
| Vital signs carry no invented diagnosis (OA2-14) | "He was hypotensive." | "His blood pressure was 88/70 mmHg." |
| Re: line identity is a source fact (OA2 §2) | "Re: Mr Michael Weir, DOB: 20 September 1970" when the notes record no DOB | "Re: Mr Michael Weir" |
| English frequencies — no Latin | "Lipitor, 20 mg nocte" | "Lipitor, 20 mg at night" |
| Medication list punctuation (OA2-16 latest override) | "Zyloric 300 mg" / comma-only lists / "; and" before the final drug | comma after drug: "Zyloric, 300 mg daily"; 2 drugs: "Drug, dose and Drug, dose"; 3+: "Drug, dose; Drug, dose and Drug, dose" — semicolons BETWEEN pairs, NO semicolon before the final "and" |
| Legitimate dose formats are first-class | (parser used to miss these) | ranges "5-10 mg four-hourly", combination strengths "Targin, 20/10 twice daily", unitless dose + frequency |
| Value/unit spacing and units (OA-15) | "37.8°C", "22 /min" | "37.8 °C", "22 breaths/min" (bpm, mmHg, °C, breaths/min are the vital-sign units) |
| Never strip clinical precision for word count | "smoking 20 cigarettes" | "smoking 20 cigarettes daily" |
| Diabetes house form (OA-12, Model Answer only) | "type 2 diabetes mellitus" in a Model Answer | "type two diabetes mellitus" (candidates keep "type 2" unpenalised) |
| Introduction purpose (OA-01/02/04) | "I am writing to update you regarding Ms Garcia's diagnosis and treatment." / "given a working assessment of possible MS" | "I am writing to update you regarding Ms Isabel Garcia's treatment for bacterial meningitis and request follow-up of her close contacts." / "I am writing to request your neurological assessment and management of Mr Weir, who has presented with features suggestive of multiple sclerosis." |
| Full name in the introduction (OA-03) | full name repeated anywhere in the body | allowed ONCE in the intro purpose clause ("regarding Ms Isabel Garcia"), then title + surname |
| Closure paragraphing (OA-06/07) | request merged into a body paragraph; contact offer merged into the request paragraph; verbatim request repeat | request sentence STARTS its own paragraph; universal contact offer is a separate FINAL paragraph; closure = remaining action only |
| Address block (OA-11) | "Elsternwick, Vic 3185" on one line | each component on its own line, copied from the task |
| No duplicated request | closure repeating the intro's request wording verbatim | reword one side (keep the closure wording that grounds); phrase matching never applies to candidates |
| Sentence length | >30 tokens | split (hyphenated words count as one; no minimum) |
| Anonymous recipient | "Dear Sir/Madam" + "Yours sincerely" | "Dear Sir/Madam" + "Yours **faithfully**" |
| Recipient spelling (source fidelity) | letter "Dr Malcom Still" when the task says "Dr Malcolm Still" (or vice versa) | copy the task's spelling exactly (checked when the task carries "Address the letter to ...") |
| Invented letter date (source fidelity) | letter dated 30 May 2015 when the notes' latest date is 23 May 2015 | use the source-supported treatment date (checked whenever the canonical notes are supplied) |

Grounding note: premium wording still grounds — "fatigue, stress and lethargy" maps to case notes saying "tired, stressed and sluggish" (proven in production). Grounding is sentence-level traceability, not verbatim copying.

## 5. DETECTOR ↔ RULE MAP (as deployed 14 Sep 2026, Addendum Two)

| Owner rule | Detector |
|---|---|
| OA2-01 owner gate over a clean validator | no detector — expressed as the regression suite itself plus the §13 evidence package: a visible defect with a PASS is a validator defect |
| OA2-02 admission/discharge must be evidenced | `discharge_function_missed` (notes prove admission AND discharge but the letter is a vague simple update) + `discharge_language_unsupported` (the opposite direction) |
| OA2-03 introduction purpose / no invented request | `intro_purpose_vague` + `closure_request_paragraph` |
| OA2-04 full patient identity, once, in the purpose clause | `body_uses_last_name_only` (allows "regarding/on Ms Isabel Garcia") + `re_line_full_name` + `re_line_identity_unsupported` |
| OA2-05 request placement, no mechanical duplication | `no_duplicated_request` + `closure_request_paragraph` |
| OA2-06 closure paragraphing is structural | `closure_request_paragraph` (Model Answer only) |
| OA2-07 no narrative semicolon | `semicolon_overuse` (ANY prose semicolon in a Model Answer; medication lists exempt) + `results_comma_splice` |
| OA2-08 result/vital grammar, no forced preposition | `result_noun_fragment` + `numerical_values_have_units`; no detector forces "at" or "of" |
| OA2-09 complete auxiliaries | `incomplete_clinical_construction` + `medication_passive_grammar` + `treatment_change_grammar` |
| OA2-10 supine wording | `supine_position_wording` |
| OA2-11 result head nouns | `result_head_noun` |
| OA2-12 dedicated late background paragraph | `background_paragraph_placement` (Model Answer only) |
| OA2-13 task- and reader-aware relevance | semantic validator checklist item 9 (not machine-checkable) |
| OA2-14 material vital sign, no invented interpretation | `vital_sign_interpretation_unsupported` + the Weir 88/70 mmHg fixture assertion |
| OA2-15 descriptive numbers as words | `number_style_words_vs_digits` + `lifestyle_frequency_precision` (both accept number-words) |
| OA2-16 medication separator override | `medication_list_punctuation` (no semicolon before the final "and") |
| OA2-17 role-based salutation | `role_salutation_matches_task` + role-aware `yours_sincerely_vs_faithfully` |
| OA2-18 allied-health readers | `non_medical_no_jargon` stays an intentional no-op; enforced through the prompt + the R2-15 false-positive control |
| OA2-19 canonical contact template | `canonical_contact_template` (Model Answer only) |
| OA2-20 house style is not candidate scoring | `WritingRuleProvenance` candidate behaviours + the candidate false-positive firewall in `WritingRev8HouseStyle.CandidateGradingRules` |
| OA2 Taylor brand/generic duplication | `brand_generic_duplication` |
| OA-01/OA-04 vague introduction purpose | `intro_purpose_vague` (working-assessment hand-off fires in both modes; topic-only intro fires for Model Answers when the letter carries a request) |
| OA-03 Re-line identity / intro full-name allowance | `re_line_full_name` + `body_uses_last_name_only` (one purpose-clause full name allowed in the intro) |
| OA-05 discharge vs simple update | `discharge_language_unsupported` (derived admission/discharge markers; never fires for LT-DG classification decisions) |
| OA-06 closure paragraphing | `closure_request_paragraph` (Model Answer only) |
| OA-07 duplicate request | `no_duplicated_request` (Model Answer only; closure region = last two paragraphs) |
| OA-08 sentence control | `semicolon_overuse` + `illogical_quantity_range` + `register_colloquial` (incl. "overweight long term", "bruising to her/his/the ...") |
| OA-09 voice + grammar | `incomplete_clinical_construction` + `medication_passive_grammar` (incl. "continued") + `treatment_change_grammar` |
| OA-10 results coordination | `results_comma_splice` (finite-clause splices; decimal-safe) |
| OA-11 address block | `address_punctuation` (Model Answer: any comma-joined components) |
| OA-12 diabetes words / descriptive numbers | `diabetes_type_words` (Model Answer only) + `number_style_words_vs_digits` |
| OA-13 abbreviations | `register_colloquial` ("MRI imaging") |
| OA-14 per-paragraph naming | `paragraph_start_patient_name` |
| OA-15 units + spacing | `value_unit_spacing` + `numerical_values_have_units` + `respiratory_rate_unit_style` (bare "22 /min") |
| Source fidelity: letter date | `letter_date_unsupported` (needs canonical notes; fires when the letter date exceeds every documented note date) |
| Source fidelity: recipient spelling | `recipient_name_mismatch` (needs the task's "Address the letter to ..." block) |
| Note-form medication voice | `medication_passive_grammar` |
| Smoking/drinks daily frequency | `lifestyle_frequency_precision` |
| Colloquial/vague/judgmental wording (incl. "for a long time", "appeared anxious") | `register_colloquial` — **no case-notes exemption by design** |
| Latin abbreviations (incl. nocte, mane) | `latin_abbreviations_translated` |
| Dose/list syntax (ranges, ratios, semicolons) | `medication_list_punctuation` + `MedicationItemRe` |
| Minor/adult naming (age attribution) | `WritingPatientAgeExtractor` (relatives' ages never classify the patient) |

Every check id carries provenance (authority tag + score-bearing/coaching-only/accept-alternative) in `WritingRuleProvenance.cs` and an OET criterion mapping in `WritingAssessmentV11RuleEngine.CheckIdCriteria`; the completeness contract is test-enforced.

## 6. SCOPE GATE

Medicine (5 live types: DG Garcia, NM Weston, RR Weir, TR McDonald, UR Taylor — Medicine has no OT unless the production catalogue gains one; never invent a fake task) → repaired, revalidated and stored under the current rule-pack hash → **STOP: full owner-review pack** → owner approval → Nursing → owner review → remaining professions → Track B → 224 catalogue. Each gate requires explicit owner approval. All five Medicine letters are permanent regression fixtures; treat them as the reference standard.

---

*Handoff trail: `.tools-state/rev8-writing/AGENT-HANDOFF-WRITING-REV8.md` → `REV9` → `REV10` → `REV11` → `REV12` (local machine). This document is the permanent, in-repo home of the rules.*
