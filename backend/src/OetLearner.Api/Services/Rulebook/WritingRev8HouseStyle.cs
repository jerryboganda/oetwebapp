namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Single source of the owner's Writing Rule Enforcement Addendum Revision 7-8
/// (11 Sep 2026) wording, rendered into every consumer so the Model Answer
/// generator, the independent Model Answer validator and the candidate grader
/// receive the SAME applicable rules (Addendum §7). Registry rows
/// OWN-W-001..OWN-W-038 (docs/canonical-rules/OET_AI_Rules_Master.jsonl) carry
/// the same content; <see cref="WritingRuleEngine"/> enforces every
/// machine-checkable rule deterministically.
/// </summary>
public static class WritingRev8HouseStyle
{
    public const string Version = "owner-rev8-2026-09-11";

    /// <summary>
    /// Stricter canonical house style for GENERATED Model Answers. Every item is
    /// mandatory; a Model Answer may be stored/published only with zero
    /// violations.
    /// </summary>
    public const string ModelAnswerCanonicalRules = """
        OWNER WRITING RULES — REVISION 8 (11 Sep 2026) — MANDATORY FOR EVERY GENERATED MODEL ANSWER
        Layout and spacing
        1. Recipient name/address block, one blank line, the date, one blank line, then "Dear ...," with the "Re:" line on the very next line (no blank line between salutation and Re:).
        2. Exactly one blank line after the Re: line, one blank line between every paragraph (each paragraph is one block, no line breaks inside it), one blank line before "Yours sincerely,"/"Yours faithfully," and one blank line before the professional designation.
        3. "Yours sincerely," when the recipient is named; "Yours faithfully," when genuinely unnamed. After it write ONLY the professional designation (Doctor, Nurse, Registered Nurse, Pharmacist, Physiotherapist, Dentist, Dietitian, Occupational Therapist, Optometrist, Podiatrist, Radiographer, Speech Pathologist) unless the task supplies the exact writer name. Never add a hospital, department, address, phone or email beneath it.
        4. No round, square or curly brackets and no placeholders anywhere. No contractions.
        Re: line, dates, age and DOB
        5. Re: line: adult = title + full name (e.g. "Re: Mr David Taylor, aged 55" or "Re: Mrs Melanie Wright, DOB: 8 June 1973"); child = full name with no title. Write "DOB:" with a colon, never "D.O.B" or "DOB 01.01.1995". Never write "DOB not provided".
        6. Use ONE date-format family in the whole letter (fully written "13 June 2020" is preferred) and always a four-digit year. Never write today's exact date in the body; write "today" / "on today's visit". Never write "yesterday"; write "one day prior".
        7. If the age is in the Re: line, do not repeat it in the introduction.
        Introduction and closure
        8. The introduction MUST begin with "I am writing to ..." and immediately state the task-specific purpose (refer / request / update you regarding / inform you about / transfer / outline). Present simple, present perfect or present continuous tense.
        9. The closure must not repeat the introduction's request wording. It may restate the needed action concisely, then it MUST END with a professional contact-offer sentence, e.g. "Should there be any queries, kindly do not hesitate to contact me."
        10. URGENT referral: make urgency explicit in the introduction ("I am writing to urgently refer ..."); use the word "urgent/urgently" ONLY in the introduction; the closure request MUST contain "at your earliest convenience", followed by the final contact-offer sentence.
        11. The closure only closes the letter: no management, advice or history after the closing request.
        Patient reference
        12. Never repeat the full first + last name after the Re: line. Adults: title + surname (e.g. "Mr Taylor"); children: first name.
        13. Start EVERY paragraph after the introduction whose content concerns the patient with the approved name form at the first mention, then use he/she/his/her later in that same paragraph. Never begin a body paragraph with He/She/His/Her.
        14. Never write "the patient", "this patient" or "a patient at this practice". When the patient is named, never use "your mother"/"your father"/another relationship label as the patient reference (a letter to a relative still uses "Mrs Ramsey").
        Organisation
        15. Routine referral: start with the main complaint/current reason for referral; place relevant medical, family, social and special-habit background near the end, before the closure, only when relevant and permitted by the letter type. Discharge/update letters exclude family history, social history, smoking/drinking, past history and occupation that the recipient already knows.
        16. Urgent referral: body paragraph 1 is today's / the current acute presentation ONLY (keep it as its own paragraph even if brief); then return to relevant earlier history in chronological order; for a hospital urgent referral mention every ongoing condition for which the person takes active medication, with that medication and dose.
        17. Body (introduction to closure inclusive) is 180-200 words; at least two body paragraphs, typically two to three.
        Language
        18. Descriptive/general numbers in words ("every six hours for four days", "two weeks ago", "three children"); digits only for age, dates, vital signs, investigation/lab values, medication doses and clinical measurements.
        19. Medication syntax: one item = "Drug, dose"; two = "Drug, dose and Drug, dose"; three or more = "Drug, dose; Drug, dose; and Drug, dose". Always a space between value and unit ("500 mg", "2 g", "6.37 mmol/L"). Translate Latin abbreviations (twice a day, as needed). Capitalise trade names, generic names lowercase. Never invent a dose, route or frequency.
        20. No emotional or editorial words (suffer/suffered/suffering, unfortunately, fortunately, regrettably, sadly). No judgmental labels (asthmatic, hypertensive, diabetic, smoker, drinker, non-compliant, anxious person): write "has asthma", "smokes ...", "reported difficulty adhering to ...".
        21. Linkers: do NOT use but, so, hence, furthermore, moreover or also. Use however / therefore / thus / consequently / subsequently / in addition / additionally only where logically needed (at most one or two per paragraph). Sentence start: "However, ..."; joining two complete clauses: "...; however, ...".
        22. Professional clinical register: no colloquial or redundant wording (use "fatigue" not "tired", "MRI" not "MRI imaging", "felt a popping sensation", "does not currently have a GP").
        23. Use only facts present in the case notes; preserve every clinical fact, laterality, dose, unit, date and certainty level exactly (a prophylactic intention is never upgraded to a guarantee).
        """;

    /// <summary>
    /// The same owner rules as they apply to CANDIDATE grading: the rule
    /// content is identical, but professional alternatives and semantic
    /// equivalents are accepted and Model Answer wording is never required.
    /// </summary>
    public const string CandidateGradingRules = """
        OWNER WRITING RULES — REVISION 8 (11 Sep 2026) — CANDIDATE GRADING
        Apply these owner rules when detecting and explaining mistakes, under the matching OET criterion:
        - Layout: salutation and Re: line consecutive; one blank line after the Re: line and between paragraphs; designation-only sign-off with no invented name or contact details; no brackets/placeholders; no contractions.
        - Dates/DOB/age: one date-format family per letter; four-digit years; "DOB:" with a colon; no "DOB not provided"; age not repeated between the Re: line and the introduction; no today's exact date in the body; no "yesterday".
        - Patient reference: do not repeat the full name after the Re: line; the first mention in each body paragraph uses the name form (adults title + surname, children first name), pronouns afterwards; never "the patient"; no "your mother/your father" substitute for a named patient.
        - Organisation: the purpose/request is clear in the introduction; routine referrals lead with the current reason for referral and place relevant background near the end; urgent referrals start the body with today's/current presentation (even if brief) and then earlier history; discharge/update letters follow their letter-type exceptions; management content belongs before the closure; the closure does not repeat the introduction's request verbatim and offers further contact (any professional wording).
        - Language: descriptive numbers in words, digits for clinical values; medication syntax "Drug, dose; Drug, dose; and Drug, dose" with a space before the unit; no emotional/editorial wording; no judgmental disease/behaviour labels; avoid but/so/hence/furthermore/moreover/also as routine connectives; "However, ..." at sentence start and "...; however, ..." between complete clauses; professional register.
        Candidate protections (never penalise):
        - A different professional opening whose purpose is immediately clear (the "I am writing to ..." opening is required only of Model Answers).
        - Any clear professional urgent closure wording, including "urgent"/"urgently" or repeating urgency — "at your earliest convenience" is NOT required of candidates.
        - Any professional semantic equivalent of the contact offer; any valid organisation, synonym or phrasing that differs from a Model Answer.
        - Model Answer similarity, phrase matching or template matching contributes ZERO to the candidate's score.
        """;
}
