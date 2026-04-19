using OetLearner.Api.Services;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    private static GrammarSeedSpec[] GrammarStarterSpecs()
    {
        var list = new List<GrammarSeedSpec>();
        AddOetTenseLessons(list);
        AddOetPassiveLessons(list);
        AddOetArticleLessons(list);
        AddOetSvaLessons(list);
        AddOetConditionalLessons(list);
        AddOetRegisterLessons(list);
        AddOetLinkerLessons(list);
        AddOetPrepositionLessons(list);
        AddOetConcessionLessons(list);
        AddOetNumeracyLessons(list);
        AddOetReportedSpeechLessons(list);
        AddIeltsLessons(list);
        AddPteLessons(list);
        return list.ToArray();
    }

    // ── OET TENSES ──────────────────────────────────────────────────────
    private static void AddOetTenseLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-past-simple",
            ExamTypeCode: "oet",
            Title: "Past simple for completed visits",
            Description: "Describe every completed consultation in past simple.",
            Category: "tenses",
            Level: "beginner",
            EstimatedMinutes: 10,
            SortOrder: 1,
            RuleIds: new[] { "G01.1", "G02.3" },
            Intro: "Visits and completed events always use past simple in OET letters.",
            Example: "Example: Mrs Jones **presented** with a three-day history of cough.",
            Note: "Never use present perfect for a single completed visit.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which sentence is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Mr Patel has attended clinic three weeks ago." },
                        new { id = "b", label = "Mr Patel attended clinic three weeks ago." },
                        new { id = "c", label = "Mr Patel attends clinic three weeks ago." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'[X] ago' always takes past simple (G02.3). Present perfect with 'ago' is ungrammatical.",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Mrs Jones ___ with chest pain last Tuesday.",
                    Array.Empty<object>(),
                    "presented",
                    new[] { "presented", "attended" },
                    "Past simple for a single completed visit (G01.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The patient has presented three weeks ago.'",
                    Array.Empty<object>(),
                    "The patient presented three weeks ago.",
                    new[] { "The patient presented three weeks ago", "He presented three weeks ago" },
                    "'Ago' pins the sentence to past simple (G02.3).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-present-perfect-since-for",
            ExamTypeCode: "oet",
            Title: "Present perfect: since & for",
            Description: "Use present perfect with 'since' and 'for' for ongoing facts.",
            Category: "tenses",
            Level: "intermediate",
            EstimatedMinutes: 12,
            SortOrder: 2,
            RuleIds: new[] { "G02.1", "G02.2" },
            Intro: "'Since + starting point' and 'for + duration' require present perfect.",
            Example: "Example: She **has had** hypertension **since** 2010.",
            Note: "Never 'She is diabetic since 2015' — always 'She has been diabetic since 2015'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which sentence is correct?",
                    new object[]
                    {
                        new { id = "a", label = "She is diabetic since 2015." },
                        new { id = "b", label = "She has been diabetic since 2015." },
                        new { id = "c", label = "She was diabetic since 2015." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Since + starting point requires present perfect (G02.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "She ___ (have) hypertension for five years.",
                    Array.Empty<object>(),
                    "has had",
                    new[] { "has had" },
                    "For + duration requires present perfect (G02.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'He has hypertension for five years.'",
                    Array.Empty<object>(),
                    "He has had hypertension for five years.",
                    new[] { "He has had hypertension for five years" },
                    "For + duration -> present perfect (G02.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite with 'since': 'She became diabetic 10 years ago.'",
                    Array.Empty<object>(),
                    "She has been diabetic for 10 years.",
                    new[] { "She has been diabetic for 10 years", "She has been diabetic since 2016" },
                    "Convert 'ago' to a 'for' / 'since' construction (G02.1 / G02.2).",
                    "advanced",
                    2),
            },
            PrerequisiteLessonId: "grm-oet-tenses-past-simple"));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-surgery-past",
            ExamTypeCode: "oet",
            Title: "Surgery is always past simple",
            Description: "Resolved operations and procedures take past simple.",
            Category: "tenses",
            Level: "beginner",
            EstimatedMinutes: 8,
            SortOrder: 3,
            RuleIds: new[] { "G01.2" },
            Intro: "Resolved surgery -> past simple, without exception.",
            Example: "Example: She **underwent** a laparoscopic cholecystectomy in 2018.",
            Note: "Never: 'She has undergone a cholecystectomy in 2018.'",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "She has undergone a cholecystectomy in 2018." },
                        new { id = "b", label = "She underwent a cholecystectomy in 2018." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Surgery -> past simple (G01.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "He ___ (have) an appendicectomy at age 12.",
                    Array.Empty<object>(),
                    "had",
                    new[] { "had" },
                    "Completed surgery always past simple (G01.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She has had her tonsils removed last year.'",
                    Array.Empty<object>(),
                    "She had her tonsils removed last year.",
                    new[] { "She had her tonsils removed last year" },
                    "Past simple for surgery (G01.2).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-ongoing-present",
            ExamTypeCode: "oet",
            Title: "Present simple for ongoing facts",
            Description: "Use present simple for standing conditions.",
            Category: "tenses",
            Level: "beginner",
            EstimatedMinutes: 9,
            SortOrder: 4,
            RuleIds: new[] { "G01.3", "G01.4" },
            Intro: "Present simple for ongoing medical, social, and family history.",
            Example: "Example: She **has** hypertension. He **smokes** 20 cigarettes per day.",
            Note: "For smoking/drinking with duration: present perfect continuous.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "She ___ hypertension and type 2 diabetes.",
                    Array.Empty<object>(),
                    "has",
                    new[] { "has" },
                    "Ongoing fact (G01.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "She smokes since 20 years." },
                        new { id = "b", label = "She has been smoking for 20 years." },
                        new { id = "c", label = "She smoking for 20 years." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Smoking + duration -> present perfect continuous (G01.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She is smoking 20 cigarettes a day.'",
                    Array.Empty<object>(),
                    "She smokes 20 cigarettes a day.",
                    new[] { "She smokes 20 cigarettes a day" },
                    "Ongoing fact without duration -> present simple (G01.3).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-records-reveal",
            ExamTypeCode: "oet",
            Title: "Medical records reveal / show",
            Description: "Present simple after 'Mr X's medical records'.",
            Category: "tenses",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 5,
            RuleIds: new[] { "G01.5" },
            Intro: "'Mr Jones' medical records reveal...' -> always present simple.",
            Example: "Example: Mr Jones' medical records **reveal** a history of asthma.",
            Note: "Never past simple for this fixed construction.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "Mr Jones' records ___ a history of asthma.",
                    Array.Empty<object>(),
                    "reveal",
                    new[] { "reveal", "show", "indicate" },
                    "Present simple (G01.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Her records revealed a history of asthma.'",
                    Array.Empty<object>(),
                    "Her records reveal a history of asthma.",
                    new[] { "Her records reveal a history of asthma" },
                    "Present simple for this fixed phrasing (G01.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "His notes is showing hypertension." },
                        new { id = "b", label = "His notes show hypertension." },
                        new { id = "c", label = "His notes has shown hypertension." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Present simple, plural subject (G01.5).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-tenses-over-the-last",
            ExamTypeCode: "oet",
            Title: "'Over the last X' opens with present perfect",
            Description: "Summary paragraphs begin with present perfect; continue with past simple.",
            Category: "tenses",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 33,
            RuleIds: new[] { "G01.6" },
            Intro: "'Over the last 3 months' pins the first sentence to present perfect.",
            Example: "Example: Over the last three months, she **has been experiencing** fatigue. She **presented** twice.",
            Note: "Used to summarise multiple visits with the same complaint.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "Over the last three months, she ___ (experience) fatigue.",
                    Array.Empty<object>(),
                    "has been experiencing",
                    new[] { "has been experiencing", "has experienced" },
                    "Opens paragraph -> present perfect (G01.6).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Over the last 2 years, she developed diabetes." },
                        new { id = "b", label = "Over the last 2 years, she has developed diabetes." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Present perfect opens (G01.6).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Over the last 3 weeks, she experienced headaches.'",
                    Array.Empty<object>(),
                    "Over the last 3 weeks, she has been experiencing headaches.",
                    new[] { "Over the last 3 weeks, she has been experiencing headaches", "Over the last 3 weeks, she has experienced headaches" },
                    "Present perfect (G01.6).",
                    "intermediate",
                    1),
            }));
    }

    // ── OET PASSIVE ─────────────────────────────────────────────────────
    private static void AddOetPassiveLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-passive-admission",
            ExamTypeCode: "oet",
            Title: "Admissions in passive voice",
            Description: "Clinical admissions use 'admitted to ... with'.",
            Category: "passive_voice",
            Level: "intermediate",
            EstimatedMinutes: 11,
            SortOrder: 6,
            RuleIds: new[] { "G03.1" },
            Intro: "Admissions always use passive voice. Never 'was presented'.",
            Example: "Example: Mr Jones **was admitted** to St Mary's Hospital **with** chest pain.",
            Note: "'Was presented' is ungrammatical — 'present' is intransitive here.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Mr Jones was presented to hospital with chest pain." },
                        new { id = "b", label = "Mr Jones was admitted to hospital with chest pain." },
                        new { id = "c", label = "Mr Jones presented to hospital with chest pain." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Use 'admitted to ... with' (G03.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was presented to A&E with shortness of breath.'",
                    Array.Empty<object>(),
                    "She was admitted to A&E with shortness of breath.",
                    new[] { "She was admitted to A&E with shortness of breath", "She presented to A&E with shortness of breath" },
                    "Either 'was admitted to' or 'presented to' (G03.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "The patient ___ admitted to hospital with pneumonia.",
                    Array.Empty<object>(),
                    "was",
                    new[] { "was" },
                    "Passive past simple — 'was admitted' (G03.1).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-passive-treatment",
            ExamTypeCode: "oet",
            Title: "Treatment verbs in passive",
            Description: "Prescribe, advise, refer, review in passive.",
            Category: "passive_voice",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 7,
            RuleIds: new[] { "G03.2" },
            Intro: "Treatment verbs fit passive when the agent (staff) is implicit.",
            Example: "Example: She **was prescribed** amoxicillin 500 mg three times a day.",
            Note: "Keeps the sentence focused on the patient and the action.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite in passive: 'The doctor prescribed metformin 500 mg.'",
                    Array.Empty<object>(),
                    "Metformin 500 mg was prescribed.",
                    new[] { "Metformin 500 mg was prescribed", "Metformin 500 mg was prescribed by the doctor" },
                    "Passive moves focus to the medication (G03.2).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "fill_blank",
                    "He ___ advised on lifestyle modification.",
                    Array.Empty<object>(),
                    "was",
                    new[] { "was" },
                    "Passive: 'was advised' (G03.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which sentence sounds most clinical?",
                    new object[]
                    {
                        new { id = "a", label = "The nurse has referred her to the team." },
                        new { id = "b", label = "She was referred to the team." },
                        new { id = "c", label = "She got referred to the team." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Passive is the conventional choice (G03.2).",
                    "intermediate",
                    1),
            }));
    }

    // ── OET ARTICLES ────────────────────────────────────────────────────
    private static void AddOetArticleLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-articles-diseases",
            ExamTypeCode: "oet",
            Title: "Articles with diseases",
            Description: "Named conditions take no article.",
            Category: "articles",
            Level: "beginner",
            EstimatedMinutes: 8,
            SortOrder: 8,
            RuleIds: new[] { "G04.1", "G07.4" },
            Intro: "Named medical conditions take **no** article.",
            Example: "Example: She has **hypertension**. He was diagnosed with **type 2 diabetes mellitus**.",
            Note: "Conditions are lowercase; eponymous diseases keep the capital.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She has a hypertension.'",
                    Array.Empty<object>(),
                    "She has hypertension.",
                    new[] { "She has hypertension" },
                    "Named conditions take no article (G04.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "He developed the pneumonia last week." },
                        new { id = "b", label = "He developed pneumonia last week." },
                        new { id = "c", label = "He developed a pneumonia last week." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Zero article with disease (G04.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "She was diagnosed with ___ asthma.",
                    Array.Empty<object>(),
                    "",
                    new[] { "" },
                    "Zero article (G04.1). Leave blank.",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-articles-symptoms",
            ExamTypeCode: "oet",
            Title: "Articles with symptoms",
            Description: "Countable symptoms take 'a'; uncountable take none.",
            Category: "articles",
            Level: "beginner",
            EstimatedMinutes: 8,
            SortOrder: 9,
            RuleIds: new[] { "G04.2" },
            Intro: "'A headache', 'a cough' (countable) vs 'nausea', 'fatigue' (uncountable).",
            Example: "Example: She complained of **a headache** and **nausea**.",
            Note: "Don't write 'a nausea' or 'a fatigue'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which sentence is correct?",
                    new object[]
                    {
                        new { id = "a", label = "She reported a nausea and fatigue." },
                        new { id = "b", label = "She reported nausea and fatigue." },
                        new { id = "c", label = "She reported the nausea and fatigue." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Uncountable symptoms take no article (G04.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She reported a fatigue.'",
                    Array.Empty<object>(),
                    "She reported fatigue.",
                    new[] { "She reported fatigue" },
                    "Uncountable symptom, zero article (G04.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "He had ___ cough and fever.",
                    Array.Empty<object>(),
                    "a",
                    new[] { "a" },
                    "Countable symptom -> 'a' (G04.2).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-articles-an-investigations",
            ExamTypeCode: "oet",
            Title: "'an ECG' — vowel sounds in abbreviations",
            Description: "Use 'an' before abbreviations with a vowel initial sound.",
            Category: "articles",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 10,
            RuleIds: new[] { "G04.4" },
            Intro: "Before abbreviations whose first spoken letter is a vowel: use 'an'.",
            Example: "Example: **An ECG** was performed. **An MRI** was arranged.",
            Note: "Test: say it aloud. 'An ECG' starts with 'ee' sound -> 'an'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "A MRI was arranged." },
                        new { id = "b", label = "An MRI was arranged." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'MRI' starts with 'em' — vowel sound (G04.4).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "___ ECG was performed on admission.",
                    Array.Empty<object>(),
                    "An",
                    new[] { "An" },
                    "'ECG' starts with 'ee' -> 'an' (G04.4).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'A ENT referral was arranged.'",
                    Array.Empty<object>(),
                    "An ENT referral was arranged.",
                    new[] { "An ENT referral was arranged" },
                    "'ENT' — 'en' sound (G04.4).",
                    "beginner",
                    1),
            }));
    }
}
