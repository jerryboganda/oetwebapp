# OET Writing Model Answer Rules — PERMANENT OWNER DIRECTIVES

**Authority:** product owner (Dr Ahmed Hesham) — Writing Rule Enforcement Addendum Rev7-8 (11 Sep 2026), Writing Master Specification "ULTIMATE FINAL" (13 Sep 2026) the FINAL WRITING OWNER CLARIFICATIONS ADDENDUM (14 Sep 2026, rules OA-01..OA-15) ADDENDUM TWO (14 Sep 2026, rules OA2-01..OA2-20), OWNER CLARIFICATIONS ROUND 3 (15 Sep 2026, rules OA3-01..OA3-05), the final Medicine layout/punctuation patch (16 Sep 2026, rules OA4-01..OA4-03) the SENIOR ASSESSOR RELEASE AUDIT (16 Sep 2026, rules OA5-01..OA5-38) and the CROSS-MODEL AUDIT remediation (17 Sep 2026, rules OA6-01..OA6-02; active validator `writing-rules.cross-profession.2026-09-18.1`, rule pack `2.5.0-cross-model-audit`). Latest owner clarification supersedes conflicting older internal wording — but every factual/source claim is settled by the ORIGINAL SOURCE PDF, never by an audit sentence alone (e.g. the Weir source records DOB 20 Sep 1970; the Taylor task spells "Dr Malcom Still").
**Applies to:** every person or AI agent who generates, repairs, validates, imports, or assesses OET Writing Model Answers in this repository — regardless of which tool or agent is used.
**Enforcement:** every language rule below is enforced by a named deterministic detector in the deployed validator (`WritingRuleEngine` / `WritingRuleEngine.Rev8`), registry rows `OA-01..OA-15`, `OA2-01..OA2-20`, `OA3-01..OA3-05`, `OA4-01..OA4-03`, `OA5-01..OA5-38` and `OA6-01..OA6-02` in `docs/canonical-rules/OET_AI_Rules_Master.jsonl`, provenance rows in `WritingRuleProvenance`, and pinned by regression tests (`WritingRev8RegressionFixtureTests.cs` — five clean-letter positives plus every injected defect — `WritingOwnerAddendumTwoRegressionFixtureTests.cs` — regression classes R2-01..R2-18, each proving BOTH that the injected defect is caught and that the valid alternative passes — and `WritingSeniorAuditG1RegressionTests.cs` .. `WritingSeniorAuditG7RegressionTests.cs` for the Senior Assessor Release Audit classes, each proving the audited defect fires for a Model Answer, valid alternatives pass, the candidate lane stays silent and the clean fixtures stay clean). The Senior Assessor Release Audit detectors live in `WritingRuleEngine.SeniorAuditG1.cs` .. `G7.cs` and run for **Model Answers only**. If a rule here and the validator ever disagree, BOTH are wrong — fix the validator and update this doc together. A stored Ready flag is valid only for the exact validator version it was verified under: any rule-pack change (bump of `WritingRuleEngine.ValidatorVersion`) invalidates affected answers until revalidated.

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
| Re: line identity is a source fact (OA2 §2) | "Re: Mr John Smith, DOB: 1 January 1980" when the source records no DOB (an invented DOB) | "Re: Mr John Smith" (or ", aged N" when the source gives an age). Source check: the Weir source PDF DOES record DOB 20 Sep 1970, so the Weir Re: line is "Re: Mr Michael Weir, DOB: 20 September 1970" — the production notes lost that DOB (data fix) |
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
| Recipient spelling (source fidelity) | letter "Dr Malcolm Still" when the Taylor task spells "Dr Malcom Still" (or vice versa) | copy the task's spelling exactly — the Taylor source says "Dr Malcom Still" (checked when the task carries "Address the letter to ...") |
| Invented letter date (source fidelity) | letter dated 30 May 2015 when the notes' latest date is 23 May 2015 | use the source-supported treatment date (checked whenever the canonical notes are supplied) |
| Full name is free in the introduction (OA3-01, supersedes OA-03) | full name flagged merely because it also sits in the Re: line | the intro may use title + surname OR the full patient name; a full name recurring in any LATER body paragraph still fails |
| Patient-name spelling is hard source fidelity (OA3-02) | "Mr David Taylr" / "Mr Davod Taylor" / Re: surname altered when the notes spell it exactly | copy the canonical notes' spelling everywhere (inert when the notes never name the patient; another person's name is never a spelling error) |
| DOB has priority over age (OA3-03) | "Re: Mr David Taylor, aged 55", or no DOB, when the notes record "DOB 01/08/1965" | "Re: Mr David Taylor, DOB: 1 August 1965"; "aged X" is correct only when the source supplies no DOB |
| Canonical "at" result wording (OA3-04, supersedes OA-10) | "a reduced glucose of 10 mg/dL" / headless "white cell count 14.0x10^9/L" | "a reduced glucose level at 10 mg/dL" / "the white cell count was 14.0x10^9/L" (Model Answer only; candidates keep every grammatical alternative) |
| No dangling treatment modifier (OA3-05) | "A catheter urine culture grew Staphylococcus saprophyticus, treated with five days of Keflex." | "... grew Staphylococcus saprophyticus, and Mr McDonald was treated with Keflex for five days." |
| Complete sentences, no fragments (OA5, audit §3.1) | "With poorly controlled diabetes, ... after a myocardial infarction." / "However, no effusion." / "Lives with her long-term boyfriend, ..." / "And prescribed eflornithine cream ..." / "... if clinically indicated. At your earliest convenience." | "Mr Mathis has poorly controlled diabetes, ..." / "... on the medial aspect, with no effusion." / "Ms Hoffmann lives with ..." / "Eflornithine cream was also prescribed ..." / "... if clinically indicated, at your earliest convenience." |
| Coordinated passive clauses keep their auxiliary (OA5, OA2-09) | "The Department of Human Services was notified, and family immunisation discussed." / "... and atorvastatin, 20 mg daily added." | "..., and family immunisation was discussed." / "..., and atorvastatin, 20 mg daily, was added." |
| Parallel "with" lists (OA5) | "with a petechial rash on the abdomen and legs, bruising on the left arm and unable to touch her chin to her chest" | "... bruising on the left arm and inability to touch her chin to her chest" / "..., and was unable to touch her chin to her chest" |
| No malformed word forms (OA5) | "is an ex-smokes of 35 years" / "has been continued smoking" / "social drinks alcohol" | "smoked for thirty-five years and quit seven years ago" / "has continued smoking" / "drinks alcohol socially" |
| No preposition before "today" (OA5) | "presented on today" / "At review on today, ..." / "Pathology on today showed ..." / "By today, attacks had become ..." | "presented today" / "At today's review, ..." / the real earlier source date ("On 4 January 2018, ...") |
| Possessive after a patient name (OA5) | "Mrs Clarke temperature was ..." / "in view of Mrs MacIntyre history" / "Erika fasting sugars" | "Mrs Clarke's temperature was ..." / "in view of Mrs MacIntyre's history" / "Her fasting sugars" |
| No typographic corruption (OA5) | "on the medial aspect . However" / "type two diabetes mellitus mellitus" / "... symptoms. today, Ms Day presented" | "on the medial aspect. However" / "type two diabetes mellitus" / "... symptoms. Today, Ms Day presented" |
| Introductory adverbial comma, every form (OA4-02 + OA5) | "On examination she appeared healthy" / "On admission Ms Pristiely's blood pressure" / "In August 2019 Mr Mills" | "On examination, she appeared healthy" / "On admission, Ms Pristiely's blood pressure" / "In August 2019, Mr Mills" |
| Descriptive numbers, no mixed forms, past-event ages in words (OA2-15 + OA5) | "30 to thirty-five cigarettes" / "5-six cigarettes" / "40 units of alcohol weekly" / "had an appendectomy at 15" | "thirty to thirty-five cigarettes" / "five to six cigarettes" / "forty units of alcohol weekly" / "had an appendectomy at age fifteen" (identification ages such as "aged 61" stay digits) |
| Time notation and superscript units (OA5) | "8am" / "BMI 29.2 kg/m2" / "18,000 cells per mm3" | "8 am" / "BMI 29.2 kg/m²" / "18,000 cells per mm³" |
| Vital signs always carry units (OA-15 + OA5) | "blood pressure 148/98" / "Pulse 96, blood pressure 110/70" | "blood pressure 148/98 mmHg" / "pulse 96 bpm, blood pressure 110/70 mmHg" |
| Generic medicine names lowercase (OA5) | "and Indomethacin, 25 mg twice daily" / "Frusemide, 40 mg daily" | "and indomethacin, 25 mg twice daily" / "frusemide, 40 mg daily" |
| Age agrees with the DOB at the letter date (OA5) | "Mr Greenbaum, a 23-year-old lawyer" with DOB 5 October 1994 in a letter dated 25 September 2020 | "Mr Greenbaum, a 25-year-old lawyer", or no age at all |
| Minors: DOB-derived child status (OA5) | "Re: Ms Sally Webster, DOB: 10 November 2003" and "Ms Webster" in a letter dated 21 February 2020 | "Re: Sally Webster, DOB: 10 November 2003" and "Sally" in the body |
| An adult is never called by the first name alone (OA5) | "I am writing to refer Erika for ..." / "Erika fasting sugars remain ..." | "I am writing to refer Miss Stone for ..." / "Miss Stone's fasting sugar levels remain ..." |
| Re: line title agrees with the pronouns (OA5) | "Re: Mr Betty Weston" with "Her sleep ...", "She reported ..." | the title and the pronouns both follow the source |
| "was born on" in the notes is a DOB (OA3-03 + OA5) | "Re: Mrs Mary Clarke" when the notes say "Mrs Mary Clarke was born on 17 September 1960" | "Re: Mrs Mary Clarke, DOB: 17 September 1960" |
| Re: line age when the source has no DOB (OA5) | "Re: Mr Allen Mathis" when the notes say "61 years old" and record no DOB | "Re: Mr Allen Mathis, aged 61" |
| Recipient block is exactly the task's content (OA5) | "Dr Helena Rao / City Eye Centre / 45 Bridge Street" for "a neuro-ophthalmologist"; "Dr Anne Childers" for "Dr Anne Childers MBBS FRANZCOG"; "London" without "NW1 2TG" | "Neuro-Ophthalmologist"; "Dr Anne Childers MBBS FRANZCOG"; "London" then "NW1 2TG" |
| Bare role recipient keeps the role salutation (OA2-17 + OA5) | "Gynaecology Registrar" recipient with "Dear Doctor," | "Dear Gynaecology Registrar," (still "Yours faithfully,"); a specialty-only recipient is a role too: "Cardiologist" → "Dear Cardiologist,", "a neuro-ophthalmologist" → "Dear Neuro-Ophthalmologist," (both "Yours faithfully,") |
| Sign-off shape (OA5) | "Yours sincerely, Doctor" followed by "Doctor" again | "Yours sincerely,", one blank line, "Doctor" |
| Letter type follows the task function (OA-05 + OA5) | an Emergency Registrar task whose notes say "needs admission ... for stabilisation" catalogued and written as LT-RR | re-catalogue as LT-UR and write the urgent contract |
| No paraphrased duplicate request (OA-07, OA2-05 + OA5) | intro "request your neurological assessment and management" + closure "assess Mr Weir and advise on further management" | the closure carries one DISTINCT next step ("consider MRI if clinically indicated") |
| Canonical request paragraph before the contact offer (OA-06 + OA5) | "Please monitor for signs of post-traumatic stress disorder ..." / no request paragraph | "I would be grateful if you could monitor Ms Wu for ..." as its own paragraph immediately before the contact offer |
| Discharge introduction states the ongoing-care request (OA-01 + OA5) | "I am writing to introduce Ms Pristiely, admitted on 2 August 2019 ... and discharged today." | "... and to request your ongoing monitoring during her recovery." |
| Background never opens or mixes into current paragraphs (OA2-12 + OA5) | remote history as the first body paragraph; smoking/allergies beside today's working diagnosis | current presentation first; background in one dedicated paragraph before the closure |
| No unidiomatic request wording (OA5) | "per this referral" / "for dermatologist review" | delete "per this referral" / "for dermatological review" |
| No emotional or affective observations (OA5; mental-health letters exempt) | "He is anxious and dyspnoeic" / "quite worried" / "in distress" / "no obvious anxiety" | state the symptom or reported concern neutrally, or omit it |
| No note-style query (OA5) | "acute asthma with query pneumonia" / "?pneumonia" | "acute asthma with suspected pneumonia" |
| Fatigue, not tiredness (OA5) | "has experienced tiredness, sore eyes ..." | "has experienced fatigue, sore eyes ..." |
| No illogical diagnostic wording (OA5) | "confirm the working diagnosis of possible fibromyalgia" / "A viral infection was assessed" | "assess Ms Topp and clarify the possible diagnosis of fibromyalgia" / "The assessment was a viral infection." |
| Neutral behaviour wording (OA5) | "medication non-compliance" / "defaulted on her follow-up" / "bizarre behaviour" / "drinks heavily" | "difficulty adhering to his medication" / "did not attend her follow-up appointment" / the observed behaviour / the exact quantity and frequency |
| Weight-based doses and alphanumeric brands are list items (OA5) | "isoniazid, 5 mg/kg daily, rifampin, 10 mg/kg daily, ..." / "insulin NovoMix30 25 units" | "isoniazid, 5 mg/kg daily; rifampin, 10 mg/kg daily; ... and ethambutol, 2.8 g twice weekly" / "NovoMix30, 25 units" |
| Formulation stays with the medicine (OA5) | "Salbutamol, 5 mg, nebules were given" | "Salbutamol nebules, 5 mg, were given" |
| One coherent medication frequency (OA5) | "glipizide, 5 mg twice daily each morning" | "glipizide, 5 mg twice daily" or "glipizide, two 5 mg tablets each morning" (whichever the source supports) |
| Chronology must be possible on the letter date (OA5) | "hurt his back at work on today and presented four days later" / "her last period of today" / "warfarin was recommenced on 26 February" in a letter dated 25 February 2015 | "hurt his back on 17 March and presented today, four days later" / "her last menstrual period was on 26 June 2019" / date the letter on the source's today |
| Letter date = the source's today / latest documented encounter (OA5) | MacIntyre dated 26 June 2019 (the LMP) when the source says Today's Date 24/08/19; Betty Johnson dated 25 February 2015 when the notes run to 21/03/15 | 24 August 2019; 21 March 2015 (numeric note dates count; appointments, planned dates and the LMP never do) |
| Owner-required material fact (OA2-14 + OA5) | Weir letter with dizziness and blackouts but no blood pressure | "His blood pressure was 88/70 mmHg." |
| HARD GLOBAL RULE: one functional request, never in both the introduction and the closure (OA6-01, judged by request concept, not wording) | intro "request follow-up of close contacts" + closure "contact close family and friends, ..."; intro "request your ongoing care pending outpatient endocrinological review" + closure "monitor Mrs Meadows's symptoms and electrolytes until her endocrinologist's appointment"; intro "for regular wound dressing" + closure "... for dressing changes"; intro "request your psychiatric assessment" + closure "confirm the diagnosis and provide further management" | the closure carries one DISTINCT source-supported step: "advise Ms Garcia's close contacts on prompt medical attention and observation, and consider chemoprophylaxis"; "encourage Mrs Meadows to attend her endocrinologist's appointment"; "visit Ms Sorocco daily for the first week and then once or twice weekly"; "provide further management"; "clarify the possible diagnosis" and "arrange bronchoscopy and biopsy to establish the diagnosis" remain correct |
| The closure requests only what the case notes plan (OA6-02) | "monitor Mrs Meadows's symptoms and electrolytes" when no case-note line plans that monitoring (a recorded result such as "Electrolytes: low sodium, high potassium" is not a plan) | request an action the notes plan: "arrange ... haemoglobin testing and INR monitoring" when the notes say "Haemoglobin should be checked" / "Refer to Queensland Medical Laboratory for INR monitoring" |
| A tablet count before the strength is one medication item (OA5 row + parser fix, 17 Sep 2026) | (validator used to read "two" as a drug name) | "Glipizide, two 5 mg tablets each morning, was continued." |

Grounding note: premium wording still grounds — "fatigue, stress and lethargy" maps to case notes saying "tired, stressed and sluggish" (proven in production). Grounding is sentence-level traceability, not verbatim copying.

## 5. DETECTOR ↔ RULE MAP (as of 17 Sep 2026, cross-model audit — validator `writing-rules.cross-profession.2026-09-18.1`)

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
| OA2-09 complete auxiliaries | `incomplete_clinical_construction` (incl. OA5 gapped passive auxiliary "..., and family immunisation discussed" and "with"-list parallelism "... and unable to touch ...", both Model Answer only) + `medication_passive_grammar` + `treatment_change_grammar` |
| OA2-10 supine wording | `supine_position_wording` |
| OA2-11 result head nouns | `result_head_noun` |
| OA2-12 dedicated late background paragraph | `background_paragraph_placement` (Model Answer only) |
| OA2-13 task- and reader-aware relevance | semantic validator checklist item 9 (not machine-checkable) |
| OA2-14 material vital sign, no invented interpretation | `vital_sign_interpretation_unsupported` + `owner_required_fact_missing` (Model Answer only; owner list in WritingRuleEngine.SeniorAuditG6.cs: Weir notes carrying 88/70 require "88/70 mmHg" in the body) |
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
| Source fidelity: letter date | `letter_date_unsupported` (needs canonical notes, the task or the scenario TodayDate; Model Answer: equals an explicit today's date, never earlier than the latest documented encounter, never later than every documented date; written and numeric note dates both read) |
| Source fidelity: recipient spelling | `recipient_name_mismatch` (needs the task's "Address the letter to ..." block) |
| Note-form medication voice | `medication_passive_grammar` |
| Smoking/drinks daily frequency | `lifestyle_frequency_precision` |
| Colloquial/vague/judgmental wording (incl. "for a long time", "appeared anxious") | `register_colloquial` — **no case-notes exemption by design** |
| Latin abbreviations (incl. nocte, mane) | `latin_abbreviations_translated` |
| Dose/list syntax (ranges, ratios, semicolons) | `medication_list_punctuation` + `MedicationItemRe` |
| Minor/adult naming (age attribution) | `WritingPatientAgeExtractor` (relatives' ages never classify the patient); for Model Answers the DOB-derived age at the letter date decides (0-17 child, 18 adult) before any naming detector runs, and `minor_naming_convention` also flags a titled child reference in the body |
| OA3-01 introduction full-name freedom | `body_uses_last_name_only` (intro free; post-introduction recurrence fires) |
| OA3-02 patient-name spelling fidelity | `patient_name_spelling` (needs canonical notes that name the patient) |
| OA3-03 DOB priority over age | `re_line_dob_priority` (needs canonical notes carrying a DOB) |
| OA3-04 canonical "at" result wording | `result_at_wording` (Model Answer only) + `result_noun_fragment` (of -> at repair) |
| OA3-05 no dangling treatment modifier | `dangling_treatment_modifier` (both modes) |
| OA4-01 address and render layout | `address_slash_separator` (slash-joined address components) + `salutation_re_same_line` (salutation and Re: on one physical line) + `address_punctuation` |
| OA4-02 introductory adverbial comma | `intro_adverbial_comma` (Model Answer only; OA5 extends it to every phrase and date form whatever word follows) |
| OA4-03 patient title fidelity | `patient_title_mismatch` (title switch inside the letter; OA5 adds the Re: line title vs the letter's pronouns, Model Answer only) |
| OA5 complete sentences / no fragments (audit §3.1) | `sentence_fragment` (Model Answer only: linker + verb with no subject, coordinator start, bare-verb start, subjectless ", and was" clause, and verbless sentences) |
| OA5 malformed word forms (converge-run damage) | `malformed_word_form` (Model Answer only: "a/an ex-/former smokes/drinks", "has been continued smoking", "social drinks alcohol"; doubled words belong to `typographic_corruption`) |
| OA5 malformed "today" phrases | `malformed_today_phrase` (Model Answer only; "on/at/since today", "<visit noun> of today", "by today" + past narration, sentence-initial "By today") |
| OA5 possessive after a patient name | `missing_possessive_name` (Model Answer only; title + surname or the Re: line first name directly followed by a possessed noun phrase from a closed lexicon) |
| OA5 typographic corruption | `typographic_corruption` (Model Answer only; space before punctuation, doubled words except had/that and capitalised place names, lowercase sentence start) |
| OA5 descriptive numbers, time notation, vital units, generic names | `number_style_words_vs_digits` + `value_unit_spacing` + `numerical_values_have_units` + `conditions_lowercase` (each extended with a Model-Answer-only branch: mixed ranges / alcohol / past-event ages; "8am" and "kg/m2"; a unit directly after every vital value; capitalised generic medicines mid-sentence) |
| OA5 age / DOB consistency | `age_dob_inconsistent` (Model Answer only; DOB-derived age at the letter date, else the notes-stated age; relatives' and past-event ages ignored) |
| OA5 adult first-name use | `body_uses_last_name_only` (Model Answer branch: a bare first name for a known adult) |
| OA5 "was born on" DOB | `re_line_dob_priority` (Model Answer branch: "<patient> was born on <date>" in the notes when no DOB label exists) |
| OA5 Re: line age when the source has no DOB | `re_line_age_when_no_dob` (Model Answer only; needs canonical notes that state the patient's age and carry no DOB/"born on" entry; patient age via `WritingPatientAgeExtractor`) |
| OA5 recipient block copied exactly from the task | `address_content_unsupported` (Model Answer only; needs the exact Writing Task with a "letter ... to"/"write to" recipient instruction; token containment against task + notes, dropped name/post-nominal/postcode check; defers misspelled recipient names to `recipient_name_mismatch`) |
| OA5 bare role salutation | `role_salutation_matches_task` (Model Answer branch: a role recipient line written without "The") |
| OA5 sign-off shape | `signoff_designation_present` (Model Answer branch: text after the closing phrase, repeated designation, extra lines) |
| OA5 letter type follows the task function (OA-05, DECISIONS C.17) | `letter_type_function_mismatch` (Model Answer only; source-gated: a routine-referral task whose case notes carry a present urgent plan or whose Writing Task addresses the letter to an emergency role; negated/past lines and suspected cancer alone never fire) |
| OA5 request function and closure | `no_duplicated_request` (paraphrased duplicate) + `closure_request_paragraph` (canonical "I would be grateful ..." paragraph before the contact offer, all professions except LT-OT) + `intro_purpose_vague` (discharge/"introduce" intro without an ongoing-care request) — each branch Model Answer only |
| OA5 background placement in any body paragraph | `background_paragraph_placement` (Model Answer branch: background opening a paragraph before current content, or mixed into a current paragraph) |
| OA5 register and neutral behaviour wording | `register_colloquial` (Model Answer branches: emotional observations with a mental-health exemption, "query X"/"?X", "tiredness", "confirm the working diagnosis of possible X", "<condition> was assessed", "per this referral", "dermatologist review") + `judgmental_labels` (compliance, defaulted, bizarre behaviour, drinks heavily) |
| OA5 medication syntax extensions | `medication_list_punctuation` (Model Answer branches: weight-based doses and alphanumeric brands, stranded formulation; `semicolon_overuse` exempts the extended list) + `medication_frequency_conflict` (Model Answer only; a multiple daily count followed directly by one time-of-day slot) |
| OA5 narrated chronology | `narrated_chronology_contradiction` (Model Answer only; no notes needed: a past event dated after the letter date, "today ... and ... N days later", "last period of today" in a pregnancy; future/planned sentences exempt) |
| OA6-01 same functional request in introduction and closure (HARD GLOBAL RULE) | `no_duplicated_request` branch `DetectCmaDuplicatedRequestConcept` (Model Answer only; request concepts compared between the introduction and the closure request sentence: contact follow-up, interim care until the same review, dressing, diagnosis confirmation after an assessment request; stands down when the verbatim or family branch already reports the letter) |
| OA6-02 closure requests only what the notes plan | `request_action_unsupported` (Model Answer only; source-gated on the canonical case notes: a monitor/check/repeat/measure/track/test request for blood pressure, electrolytes, renal function, INR, haemoglobin, glucose, weight, symptoms, medications, liver function, lipids, thyroid function, full blood count or oxygen saturation needs a case-note line that plans monitoring of that parameter) |

Every check id carries provenance (authority tag + score-bearing/coaching-only/accept-alternative) in `WritingRuleProvenance.cs` and an OET criterion mapping in `WritingAssessmentV11RuleEngine.CheckIdCriteria`; the completeness contract is test-enforced.

## 6. SCOPE GATE

Medicine (5 live types: DG Garcia, NM Weston, RR Weir, TR McDonald, UR Taylor — Medicine has no OT unless the production catalogue gains one; never invent a fake task) → repaired, revalidated and stored under the current rule-pack hash → **STOP: full owner-review pack** → owner approval → Nursing → owner review → remaining professions → Track B → 224 catalogue. Each gate requires explicit owner approval. All five Medicine letters are permanent regression fixtures; treat them as the reference standard.

**Senior Assessor Release Audit scope note (16 Sep 2026):** every OA5 detector and every OA5 branch added to an existing detector runs for **Model Answers only** — candidate scoring, the parity snapshots and the candidate false-positive firewall are unchanged (firewall item 26 in `WritingRev8HouseStyle.CandidateGradingRules`). The rule rows are global (all six canonical packs), and `closure_request_paragraph`'s canonical-request branch applies to every profession except Other Letters, so non-Medicine Model Answers that open the pre-contact paragraph with "Please ..." will also be held. Source-data fixes the validator cannot make: add the Weir DOB row (20 Sep 1970) and the Priya Sharma 10/02/19 and MacIntyre "Today's Date: 24/08/19" entries to production notes; re-catalogue Cochrane and OET test 14 as LT-UR. Bumping the validator hides every stored Model Answer (all professions) until each is re-imported with `includeSemantic=false` and re-approved.

---

*Handoff trail: `.tools-state/rev8-writing/AGENT-HANDOFF-WRITING-REV8.md` → `REV9` → `REV10` → `REV11` → `REV12` (local machine). This document is the permanent, in-repo home of the rules.*
