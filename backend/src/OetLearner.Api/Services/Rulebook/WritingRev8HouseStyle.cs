namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Single source of the owner's Writing rule wording — Writing Rule
/// Enforcement Addendum Revisions 7-8 (11 Sep 2026), the Writing Master
/// Specification "ULTIMATE FINAL" handoff (13 Sep 2026) and the FINAL
/// WRITING OWNER CLARIFICATIONS ADDENDUM (14 Sep 2026, rules OA-01..OA-15)
/// — rendered into every consumer so the Model Answer generator, the
/// independent Model Answer validator and the candidate grader receive the
/// SAME applicable rules, with only the strictness differing by lane.
/// Registry rows OWN-W-001..OWN-W-038 + OA-01..OA-15
/// (docs/canonical-rules/OET_AI_Rules_Master.jsonl) carry the same content;
/// <see cref="WritingRuleEngine"/> enforces every machine-checkable rule
/// deterministically; <see cref="WritingRuleProvenance"/> carries each
/// rule's authority tag and candidate behaviour.
/// </summary>
public static class WritingRev8HouseStyle
{
    public const string Version = "owner-addendum-2026-09-14";

    /// <summary>
    /// Stricter canonical house style for GENERATED Model Answers. Every item is
    /// mandatory; a Model Answer may be stored/published only with zero
    /// violations.
    /// </summary>
    public const string ModelAnswerCanonicalRules = """
        OWNER WRITING RULES — OWNER CLARIFICATIONS ADDENDUM (14 Sep 2026, OA-01..OA-15) + ULTIMATE FINAL — MANDATORY FOR EVERY GENERATED MODEL ANSWER
        Letter-type classification (source fidelity)
        0. Determine the FUNCTIONAL letter type from BOTH the case notes and the exact Writing Task — never from a catalogue code alone (OA-05). Hospital admission + return to ongoing care = update-on-discharge (state update + admission/treatment context + discharge/return + ongoing-care request). No supported admission/discharge = SIMPLE UPDATE: never invent "ready for discharge", "being discharged", discharge medications or transfer of care. Care/responsibility moving to another service = transfer (continuity actions explicit). Suspected cancer alone does NOT make a referral urgent — preserve exactly the urgency the task/notes support. If the task does not confidently fit a defined type, treat it as Other Letters and build from task function; never force an uncertain task into a wrong template.
        Layout and spacing
        1. Recipient name/address block copied from the task with EACH COMPONENT ON ITS OWN LINE (OA-11: never "Elsternwick, Vic 3185" on one line, never improvised prose metadata), one blank line, the date, one blank line, then "Dear ...," with the "Re:" line on the very next line.
        2. Exactly one blank line after the Re: line, one blank line between every paragraph (each paragraph is one block, no line breaks inside it), one blank line before "Yours sincerely,"/"Yours faithfully," and one blank line before the professional designation.
        3. "Yours sincerely," when the recipient is named; "Yours faithfully," when genuinely unnamed. After it write ONLY the professional designation (Doctor, Nurse, Registered Nurse, Pharmacist, Physiotherapist, Dentist, Dietitian, Occupational Therapist, Optometrist, Podiatrist, Radiographer, Speech Pathologist) unless the task supplies the exact writer name. Never add a hospital, department, address, phone or email beneath it.
        4. No round, square or curly brackets and no placeholders anywhere. No contractions.
        Re: line, dates, age and DOB
        5. Re: line carries the patient's FULL identification (OA-03): adult = title + first + last name (e.g. "Re: Mr David Taylor, aged 55" or "Re: Mrs Betty Weston, DOB: 12 February 1964"); child (0-17) = first + last name with no title, never "Master". Write "DOB:" with a colon. Never write "DOB not provided".
        6. Use ONE date-format family in the whole letter (fully written "13 June 2020" is preferred) and always a four-digit year. The letter date is NEVER invented: it is the source-supported date of treatment — never a date later than the last date documented in the case notes (OA-01 source fidelity). Never write today's exact date in the body; write "today". Never write "yesterday"; write "one day prior". Use "on the following visit" / "later on", never "next visit".
        7. If the age is in the Re: line, do not repeat it in the introduction.
        Introduction and closure
        8. The introduction MUST begin with "I am writing to ..." (OA-02) and IMMEDIATELY state the exact task-specific purpose/request (OA-01): "I am writing to update you regarding Ms Isabel Garcia following her diagnosis and treatment for bacterial meningitis and to request your assistance with the required follow-up of her close contacts." or "I am writing to request your occupational therapy assessment and management of Mrs Weston, who has been diagnosed with carpal tunnel syndrome." Never a vague hand-off ("given a working assessment of possible ..."); if the letter contains a request, the introduction must signal that function. If the task explicitly requests no action, state the informational purpose precisely instead.
        9. Closure paragraphing (OA-06 + OA-07): the task-specific request ("I would be grateful if you could ...") forms its OWN paragraph, beginning with the request sentence. The universal contact-offer sentence ("Should there be any queries, please do not hesitate to contact me.") is a SEPARATE FINAL paragraph before the sign-off. The closure never mechanically repeats the introduction's request: opening = primary request; closure = remaining action or a concise non-verbatim functional request.
        10. URGENT referral: make urgency explicit in the introduction ("I am writing to request your urgent rheumatological assessment and management of Mr Taylor, who has presented with ..."); use the word "urgent/urgently" ONLY in the introduction; the closure request contains "at your earliest convenience", and the contact offer follows as the separate final paragraph (OA-06). Never write "ASAP".
        11. The closure only closes the letter: no management, advice or history after the closing request. If a follow-up/review date is stated in the notes and relevant, include it; if the task states results/imaging are enclosed, mention the enclosure (do not duplicate the values); preserve documented patient-initiated referral/consent accurately.
        Patient reference
        12. After the Re: line, adults = title + surname ("Mr Taylor"), children = first name. OA-03 override: the INTRODUCTION may use the full patient name ONCE when it is syntactically part of the purpose clause ("... regarding Ms Isabel Garcia ..."); every later adult reference returns to title + surname.
        13. Start EVERY paragraph after the introduction whose content concerns the patient with the approved name form at the first mention (OA-14), then use he/she/his/her later in that same paragraph. Never begin a body paragraph with He/She/His/Her.
        14. Never write "the patient", "this patient" or "a patient at this practice". When the patient is named, never use "your mother"/"your father"/another relationship label as the patient reference.
        Organisation
        15. Routine referral: start with the main complaint/current reason for referral; place relevant medical, family, social and special-habit background near the end, before the closure, only when relevant and permitted by the letter type. Discharge/update letters exclude family history, social history, smoking/drinking, past history and occupation that the recipient already knows. Keep only occupational/social details genuinely relevant to the reader.
        16. Urgent referral: body paragraph 1 is today's / the current acute presentation ONLY (keep it as its own paragraph even if brief); then return to relevant earlier history in chronological order; for a hospital urgent referral mention every ongoing condition for which the person takes active medication, with that medication and dose.
        17. Body (introduction to closure inclusive) is 180-200 words; one clear function per paragraph (current problem, progression, management/response, background or plan). Non-medical recipients get plain language within their scope — no unexplained jargon, no GP duties assigned to them.
        Language
        18. Descriptive/general numbers in words ("every six hours for four days", "two weeks ago", "three children"; "type two diabetes mellitus" — OA-12); digits only for age, dates, vital signs, investigation/lab values, medication doses and clinical measurements.
        19. Medication syntax: one item = "Drug, dose"; two = "Drug, dose and Drug, dose"; three or more = "Drug, dose; Drug, dose; and Drug, dose". Always a space between value and unit ("500 mg", "2 g", "6.37 mmol/L", "37.8 °C", "90 bpm", "120/80 mmHg" — OA-15); respiratory rate is "22 breaths/min", never "22 /min". Translate Latin abbreviations (twice a day, at night). Capitalise trade names, generic names lowercase. Never invent a dose, route or frequency. Reader-appropriate abbreviations (MRI) stay abbreviated; never "MRI imaging" (OA-13).
        20. No emotional or editorial words (suffer/suffered, unfortunately, regrettably, sadly). No judgmental labels or emotional observations ("appeared anxious"): write precise neutral findings; a clinically documented anxiety DIAGNOSIS may be stated factually. Restore quantities exactly: never "drinking over six to ten standard drinks daily" — "six to ten standard drinks daily" (OA source fidelity). Never "has been overweight long term" — "has long been overweight".
        21. Linkers: do NOT use but, so, hence, furthermore, moreover or also. Use however / therefore / thus / consequently / subsequently / in addition only where logically needed. Sentence start: "However, ..."; joining two complete clauses: "...; however, ...". Fully grammatical, easy-to-process sentences (OA-08): no overloaded semicolon chains, no note-form fragments.
        22. Voice and grammar (OA-09): complete passive forms — "dexamethasone was continued every six hours", "treatment was changed to benzylpenicillin", never "dexamethasone continued ..." / "treatment changed to ...". "On examination, Ms Garcia was afebrile ...", never "Examination showed afebrile". "bruising on her left arm", not "bruising to her left arm". Investigation results are coordinated grammatically — "a white cell count of 14.0x10^9/L and a CRP of 150" — never comma-spliced independent clauses; "Ms Garcia responded well to treatment." as its own sentence.
        23. Use only facts present in the case notes; preserve every clinical fact, laterality, dose, unit, date and certainty level exactly (a prophylactic intention is never upgraded to a guarantee; a suspicion is never upgraded to a confirmed diagnosis). Name vague objects exactly ("possible tophus removal", never bare "possible removal"). Copy the recipient's name EXACTLY as the task spells it ("Dr Malcolm Still", never "Malcom").
        """;

    /// <summary>
    /// The same owner rules as they apply to CANDIDATE grading: descriptor-first,
    /// source-grounded and fair. Professional alternatives and semantic
    /// equivalents are accepted; Model Answer wording is never required; and the
    /// complete false-positive firewall (Ultimate Final §17) below is
    /// non-negotiable.
    /// </summary>
    public const string CandidateGradingRules = """
        OWNER WRITING RULES — ULTIMATE FINAL (13 Sep 2026) — CANDIDATE GRADING
        Two lanes. Grade the candidate from the case notes + exact task + the six official criteria + validated English. The Model Answer's house style is NOT the candidate's standard: a house-style deviation is coaching feedback unless an independent OET criterion (clarity, accuracy, register, cohesion, safety, professionalism) is genuinely affected.

        Letter-type classification — derive the functional type from the case notes + exact task, never from a stored code alone. Do NOT force a discharge template when the notes do not support admission/discharge; do NOT invent "ready for discharge" facts. Suspected cancer alone is not automatic urgency — assess whether the task/notes actually support urgent action. A routine cancer-screening task is not urgent.

        CANDIDATE FALSE-POSITIVE FIREWALL — these professional alternatives must NOT be penalised:
        1. Any professional opening whose purpose/action is immediately clear ("I am writing to ..." is a Model-Answer house form only).
        2. More than four body paragraphs — no count-only penalty; assess organisation/readability.
        3. Word count slightly outside 180-200 — no fixed arithmetic deduction; assess relevance, omission and excess.
        4. Correct use of "but"/"so"/"also" — standard-correct English is never a grammar error; style feedback only unless real cohesion/register weakness exists.
        5. No universal contact-offer sentence — another professional, task-complete closure can pass fully.
        6. Urgent closure without "at your earliest convenience" — any clear professional urgency is acceptable; repeating "urgent/urgently" is not automatically penalised.
        7. A different clear, grammatically sound medication list (the exact house comma/semicolon form is not a universal template; ambiguity, grammar or safety impact is what scores).
        8. A descriptive number written as a digit — no fixed penalty unless standard usage, clarity or register is actually harmed.
        9. A pronoun-first paragraph with a clear referent; and natural use of "the patient" — flag only real ambiguity, repetition or impersonal-tone impact.
        10. An alternate clear DOB/patient-identification form — score genuine identification/professionalism problems, not template mismatch.
        11. Brackets/parentheses — not automatically wrong; assess readability/register impact.
        12. Familiar, profession-appropriate abbreviations the intended reader would understand.
        13. A clinically correct paraphrase/synonym, however far from Model Answer wording — Model Answer similarity contributes ZERO to the score.
        14. A different coherent paragraph order when task needs, clinical chronology and reader action remain clear.
        15. A clinically documented anxiety (or similar) diagnosis stated factually — a diagnosis is not a judgemental label.
        16. "type 2 diabetes mellitus" written with the digit — Model Answers use "type two" (OA-12); a candidate is never penalised for the digit.
        17. A clear professional opening other than "I am writing to ..." (OA-01/OA-02): Purpose is assessed semantically, never by template wording.
        18. The full patient name once in the introduction's purpose clause ("regarding Ms Isabel Garcia") — clear professional naming variations are accepted when clarity and cohesion are intact (OA-03).
        19. Different closure paragraphing or a closure request phrased differently from the Model Answer — house paragraphing is not imposed on candidates (OA-06/OA-07): duplication is judged semantically, never by phrase matching.

        ALWAYS score as genuine errors: invented facts/dates/doses/diagnoses/admissions/discharges; upgraded certainty (suspected -> confirmed, prophylaxis -> guarantee); important omissions the recipient needs; wrong recipient/task action; delayed or vague purpose; tense changes that alter the timeline; grammar/punctuation/spelling errors; comma splices and incomplete constructions (e.g. "examination showed afebrile"); mixed date families or abbreviated years; collapsed layout that harms professional readability; emotional/judgemental wording; colloquial register where a professional equivalent exists; unsafe unexplained shorthand; clinical values without units or with missing unit spaces; medication errors that affect clarity or safety.

        Clean answers stay clean: never manufacture a finding to fill a feedback template. Every finding must state the exact quote, the OET criterion it affects, why it matters for the reader, and a corrected wording.
        """;
}
