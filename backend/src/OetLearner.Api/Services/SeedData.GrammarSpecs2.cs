using OetLearner.Api.Services;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    // ── OET SUBJECT-VERB AGREEMENT ──────────────────────────────────────
    private static void AddOetSvaLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-sva-reverse-rule",
            ExamTypeCode: "oet",
            Title: "Reverse rule: along with / together with",
            Description: "These phrases do NOT change subject number.",
            Category: "subject_verb_agreement",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 11,
            RuleIds: new[] { "G05.2" },
            Intro: "'Along with', 'together with', 'as well as', 'in addition to' do NOT change the number of the subject.",
            Example: "Example: **Mr Jones**, along with his wife, **was** informed of the diagnosis.",
            Note: "The head noun (Mr Jones) is singular, so 'was' — not 'were'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Mr Jones, along with his wife, were informed." },
                        new { id = "b", label = "Mr Jones, along with his wife, was informed." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Reverse rule (G05.2) — head noun is singular.",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "The patient, together with her family members, ___ informed of the plan.",
                    Array.Empty<object>(),
                    "was",
                    new[] { "was" },
                    "Reverse rule (G05.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The doctor, along with the nurses, were present.'",
                    Array.Empty<object>(),
                    "The doctor, along with the nurses, was present.",
                    new[] { "The doctor, along with the nurses, was present" },
                    "Head noun 'doctor' is singular (G05.2).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-sva-proximity-or-nor",
            ExamTypeCode: "oet",
            Title: "Proximity rule with 'or' and 'nor'",
            Description: "Verb agrees with the closer subject.",
            Category: "subject_verb_agreement",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 12,
            RuleIds: new[] { "G05.1" },
            Intro: "With 'or' or 'nor', the verb agrees with the closer subject.",
            Example: "Example: Neither the GP **nor** the parents **are** aware of the results.",
            Note: "Order subjects so the verb sounds natural.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Neither the GP nor the parents is aware." },
                        new { id = "b", label = "Neither the GP nor the parents are aware." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Proximity rule (G05.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Either the consultant or the registrars ___ on call.",
                    Array.Empty<object>(),
                    "are",
                    new[] { "are" },
                    "Agrees with 'registrars' (G05.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Neither the nurses nor the doctor were available.'",
                    Array.Empty<object>(),
                    "Neither the nurses nor the doctor was available.",
                    new[] { "Neither the nurses nor the doctor was available" },
                    "Agrees with the closer subject 'doctor' (G05.1).",
                    "advanced",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-sva-collective",
            ExamTypeCode: "oet",
            Title: "Collective nouns are singular",
            Description: "'Team', 'staff', 'family' take singular verbs.",
            Category: "subject_verb_agreement",
            Level: "beginner",
            EstimatedMinutes: 8,
            SortOrder: 13,
            RuleIds: new[] { "G05.3" },
            Intro: "Collective nouns in OET letters take singular verbs.",
            Example: "Example: The **team was** consulted regarding further management.",
            Note: "British English may sometimes use plural — OET prefers singular.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "The team were consulted." },
                        new { id = "b", label = "The team was consulted." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Collective noun -> singular (G05.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "The family ___ informed of the prognosis.",
                    Array.Empty<object>(),
                    "was",
                    new[] { "was" },
                    "Singular (G05.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The staff are reviewing the case notes.'",
                    Array.Empty<object>(),
                    "The staff is reviewing the case notes.",
                    new[] { "The staff is reviewing the case notes" },
                    "Singular collective noun (G05.3).",
                    "intermediate",
                    1),
            }));
    }

    // ── OET CONDITIONALS ────────────────────────────────────────────────
    private static void AddOetConditionalLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-conditional-zero",
            ExamTypeCode: "oet",
            Title: "Zero conditional for medical facts",
            Description: "If + present simple, present simple.",
            Category: "conditionals",
            Level: "beginner",
            EstimatedMinutes: 9,
            SortOrder: 14,
            RuleIds: new[] { "G06.1" },
            Intro: "Zero conditional describes predictable medical facts.",
            Example: "Example: **If** blood pressure **rises** above 140/90, antihypertensive therapy **is** indicated.",
            Note: "Both clauses in present simple.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "If BP rises above 140/90, antihypertensive therapy ___ indicated.",
                    Array.Empty<object>(),
                    "is",
                    new[] { "is" },
                    "Zero conditional (G06.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is a zero conditional?",
                    new object[]
                    {
                        new { id = "a", label = "If HbA1c is > 9%, insulin is considered." },
                        new { id = "b", label = "If HbA1c were > 9%, insulin would be considered." },
                    },
                    "a",
                    Array.Empty<string>(),
                    "Both clauses present simple (G06.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'If glucose rises above 10, treatment is adjusting.'",
                    Array.Empty<object>(),
                    "If glucose rises above 10, treatment is adjusted.",
                    new[] { "If glucose rises above 10, treatment is adjusted" },
                    "Present simple passive (G06.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-conditional-first",
            ExamTypeCode: "oet",
            Title: "First conditional for likely outcomes",
            Description: "If + present simple, will + base form.",
            Category: "conditionals",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 15,
            RuleIds: new[] { "G06.2" },
            Intro: "First conditional describes likely outcomes of the current plan.",
            Example: "Example: **If** her symptoms **persist**, we **will arrange** further imaging.",
            Note: "Used in closure sentences.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "If her symptoms persist, we ___ (arrange) further imaging.",
                    Array.Empty<object>(),
                    "will arrange",
                    new[] { "will arrange" },
                    "First conditional (G06.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is first conditional?",
                    new object[]
                    {
                        new { id = "a", label = "If the symptoms worsen, you should contact us." },
                        new { id = "b", label = "If the symptoms worsened, you would contact us." },
                    },
                    "a",
                    Array.Empty<string>(),
                    "'Should' is acceptable first-conditional form (G06.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite as first conditional: 'Worsening symptoms -> attend the ED.'",
                    Array.Empty<object>(),
                    "If the symptoms worsen, she should attend the emergency department.",
                    new[] { "If the symptoms worsen, she should attend the emergency department", "If the symptoms worsen, she will attend the emergency department" },
                    "First conditional (G06.2).",
                    "intermediate",
                    2),
            }));
    }

    // ── OET REGISTER ────────────────────────────────────────────────────
    private static void AddOetRegisterLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-no-contractions",
            ExamTypeCode: "oet",
            Title: "No contractions in clinical letters",
            Description: "Always write 'do not', 'cannot', 'I am'.",
            Category: "formal_register",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 16,
            RuleIds: new[] { "G07.1" },
            Intro: "Contractions are informal and never acceptable in OET letters.",
            Example: "Example: **Do not** hesitate to contact me — not 'Don't hesitate'.",
            Note: "Every contraction is a deduction in Genre & Style.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Don't hesitate to contact me.'",
                    Array.Empty<object>(),
                    "Do not hesitate to contact me.",
                    new[] { "Do not hesitate to contact me" },
                    "No contractions (G07.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "I'm writing to refer Mr Jones." },
                        new { id = "b", label = "I am writing to refer Mr Jones." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Always expand contractions (G07.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She can't attend the appointment.'",
                    Array.Empty<object>(),
                    "She cannot attend the appointment.",
                    new[] { "She cannot attend the appointment" },
                    "'Cannot' — not 'can't' (G07.1).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-the-patient",
            ExamTypeCode: "oet",
            Title: "Never use 'the patient' in the body",
            Description: "Use title + last name, first name (children), or a pronoun.",
            Category: "formal_register",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 17,
            RuleIds: new[] { "G07.2" },
            Intro: "'The patient' is an anonymous label. OET letters use the name or a pronoun.",
            Example: "Example: **Mr Jones** presented with chest pain. **He** was admitted.",
            Note: "Every 'the patient' occurrence is a deduction.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The patient reported chest pain.'",
                    Array.Empty<object>(),
                    "Mr Jones reported chest pain.",
                    new[] { "Mr Jones reported chest pain", "He reported chest pain", "She reported chest pain" },
                    "Replace 'the patient' (G07.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "The patient was admitted for observation." },
                        new { id = "b", label = "Mrs Miller was admitted for observation." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Use the name (G07.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Ms Jones presented with a cough. ___ reported a three-week history.",
                    Array.Empty<object>(),
                    "She",
                    new[] { "She" },
                    "Pronoun (G07.2).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-conditions-lowercase",
            ExamTypeCode: "oet",
            Title: "Conditions are lowercase",
            Description: "'Hypertension', 'asthma' — lowercase.",
            Category: "formal_register",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 18,
            RuleIds: new[] { "G07.4" },
            Intro: "Medical conditions are common nouns — lowercase. Eponymous diseases keep the capital.",
            Example: "Example: She has **hypertension** and **type 2 diabetes**. He has **Parkinson's disease**.",
            Note: "Never: 'She has Hypertension.'",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Mr Jones has Hypertension and Type 2 Diabetes.'",
                    Array.Empty<object>(),
                    "Mr Jones has hypertension and type 2 diabetes.",
                    new[] { "Mr Jones has hypertension and type 2 diabetes" },
                    "Conditions are lowercase (G07.4).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "She was diagnosed with Crohn's disease." },
                        new { id = "b", label = "She was diagnosed with crohn's disease." },
                    },
                    "a",
                    Array.Empty<string>(),
                    "Eponymous disease keeps capital (G07.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "The patient suffers from ___ (asthma/Asthma).",
                    Array.Empty<object>(),
                    "asthma",
                    new[] { "asthma" },
                    "Lowercase (G07.4).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-no-smoker-label",
            ExamTypeCode: "oet",
            Title: "Never label 'smoker' or 'drinker'",
            Description: "Describe behaviour instead.",
            Category: "formal_register",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 19,
            RuleIds: new[] { "G07.3" },
            Intro: "Patients 'smoke' or 'drink'; they are not 'smokers' or 'drinkers'.",
            Example: "Example: **She smokes** 20 cigarettes per day — not 'She is a heavy smoker'.",
            Note: "Labelling is stigmatising and unprofessional.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'He is a heavy smoker and drinker.'",
                    Array.Empty<object>(),
                    "He smokes heavily and drinks alcohol regularly.",
                    new[] { "He smokes heavily and drinks alcohol regularly" },
                    "Behaviour not label (G07.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "She is a non-smoker." },
                        new { id = "b", label = "She does not smoke." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Behaviour, not label (G07.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite: 'He is a non-drinker.'",
                    Array.Empty<object>(),
                    "He does not drink alcohol.",
                    new[] { "He does not drink alcohol", "He does not drink" },
                    "Behaviour phrasing (G07.3).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-no-colloquial",
            ExamTypeCode: "oet",
            Title: "Replace colloquial with clinical",
            Description: "'Tired' -> 'fatigued', 'bad' -> 'poor'.",
            Category: "formal_register",
            Level: "intermediate",
            EstimatedMinutes: 7,
            SortOrder: 29,
            RuleIds: new[] { "G07.5" },
            Intro: "OET letters need clinical vocabulary — replace everyday words.",
            Example: "Example: 'tired' -> **fatigued**; 'bad sleep' -> **poor sleep**; 'sick' -> **unwell**.",
            Note: "Each colloquial word lowers your Genre & Style score.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She complained of bad sleep and being tired.'",
                    Array.Empty<object>(),
                    "She complained of poor sleep and fatigue.",
                    new[] { "She complained of poor sleep and fatigue" },
                    "Clinical vocabulary (G07.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "matching",
                    "Match colloquial to clinical.",
                    new object[]
                    {
                        new { left = "tired", right = "fatigued" },
                        new { left = "bad", right = "poor" },
                        new { left = "sick", right = "unwell" },
                    },
                    new object[]
                    {
                        new { left = "tired", right = "fatigued" },
                        new { left = "bad", right = "poor" },
                        new { left = "sick", right = "unwell" },
                    },
                    Array.Empty<string>(),
                    "Clinical replacements (G07.5).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "fill_blank",
                    "He reported ___ (clinical word for 'tired').",
                    Array.Empty<object>(),
                    "fatigue",
                    new[] { "fatigue" },
                    "Clinical noun (G07.5).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-register-no-labels",
            ExamTypeCode: "oet",
            Title: "Never label 'anxious' or 'non-compliant'",
            Description: "Describe behaviour instead of labelling.",
            Category: "formal_register",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 30,
            RuleIds: new[] { "G07.6" },
            Intro: "Never write 'anxious patient' or 'non-compliant patient'. Describe the behaviour.",
            Example: "Example: 'She expressed concern about...'; 'He reported difficulty adhering to...'",
            Note: "Labelling is unprofessional.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The non-compliant patient missed three doses.'",
                    Array.Empty<object>(),
                    "Mr Jones reported difficulty adhering to his medication and missed three doses.",
                    new[] { "Mr Jones reported difficulty adhering to his medication and missed three doses" },
                    "Describe behaviour (G07.6).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "The patient is anxious." },
                        new { id = "b", label = "She expressed concern about the procedure." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Behaviour not label (G07.6).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite: 'He is non-compliant with medication.'",
                    Array.Empty<object>(),
                    "He reported difficulty adhering to his medication.",
                    new[] { "He reported difficulty adhering to his medication" },
                    "Behaviour-based phrasing (G07.6).",
                    "advanced",
                    2),
            }));
    }
}
