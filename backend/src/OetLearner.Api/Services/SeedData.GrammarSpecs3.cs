using OetLearner.Api.Services;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    // ── OET LINKERS ─────────────────────────────────────────────────────
    private static void AddOetLinkerLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-linker-however",
            ExamTypeCode: "oet",
            Title: "'However' punctuation",
            Description: "; however, — semicolon before, comma after.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 20,
            RuleIds: new[] { "G08.1" },
            Intro: "'However' joining two independent clauses takes a semicolon before and a comma after.",
            Example: "Example: She completed the course**; however,** her symptoms persisted.",
            Note: "Never comma-splice: 'She completed the course, however, her symptoms persisted.' is wrong.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correctly punctuated?",
                    new object[]
                    {
                        new { id = "a", label = "She completed the course, however, her symptoms persisted." },
                        new { id = "b", label = "She completed the course; however, her symptoms persisted." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "; however, (G08.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The antibiotics were prescribed, however the symptoms persisted.'",
                    Array.Empty<object>(),
                    "The antibiotics were prescribed; however, the symptoms persisted.",
                    new[] { "The antibiotics were prescribed; however, the symptoms persisted" },
                    "; however, (G08.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Her BP normalised ___ however, she remained symptomatic.",
                    Array.Empty<object>(),
                    ";",
                    new[] { ";" },
                    "Semicolon before 'however' (G08.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-linker-therefore",
            ExamTypeCode: "oet",
            Title: "'Therefore' and 'thus' punctuation",
            Description: "Same pattern as 'however'.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 21,
            RuleIds: new[] { "G08.2" },
            Intro: "'Therefore' and 'thus' joining clauses take ; then ,.",
            Example: "Example: Her HbA1c rose to 9.2%**; therefore,** insulin was initiated.",
            Note: "Don't stack '; however, therefore,' — pick one.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Her HbA1c rose, therefore insulin was initiated." },
                        new { id = "b", label = "Her HbA1c rose; therefore, insulin was initiated." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "; therefore, (G08.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was non-responsive, thus the team escalated.'",
                    Array.Empty<object>(),
                    "She was non-responsive; thus, the team escalated.",
                    new[] { "She was non-responsive; thus, the team escalated" },
                    "; thus, (G08.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Her HbA1c rose to 9.2% ___ therefore, insulin was initiated.",
                    Array.Empty<object>(),
                    ";",
                    new[] { ";" },
                    "Semicolon (G08.2).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-linker-in-addition",
            ExamTypeCode: "oet",
            Title: "'In addition' vs 'in addition to'",
            Description: "Different punctuation.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 22,
            RuleIds: new[] { "G08.3", "G08.4" },
            Intro: "'In addition' (adverbial) -> ; in addition, + clause. 'In addition to' + noun/-ING, no preceding punctuation.",
            Example: "Example 1: She was advised on lifestyle**; in addition,** dietetic review was arranged. Example 2: **In addition to** lifestyle advice, metformin was commenced.",
            Note: "Different roles, different punctuation.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "In addition lifestyle was advised, metformin was commenced." },
                        new { id = "b", label = "In addition to lifestyle advice, metformin was commenced." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'In addition to' + noun (G08.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was advised on lifestyle, in addition dietetic review was arranged.'",
                    Array.Empty<object>(),
                    "She was advised on lifestyle; in addition, dietetic review was arranged.",
                    new[] { "She was advised on lifestyle; in addition, dietetic review was arranged" },
                    "; in addition, (G08.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "___ to metformin, she was prescribed atorvastatin.",
                    Array.Empty<object>(),
                    "In addition",
                    new[] { "In addition" },
                    "'In addition to' + noun (G08.4).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-linker-density",
            ExamTypeCode: "oet",
            Title: "Max 1–2 linkers per paragraph",
            Description: "Over-linking drops your Conciseness score.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 7,
            SortOrder: 34,
            RuleIds: new[] { "G08.6", "G08.7" },
            Intro: "Each paragraph should use at most 1–2 inter-sentence linkers. Target 15–25 words per sentence.",
            Example: "Example: 'She was admitted; however, she was discharged 48 hours later.'",
            Note: "Don't chain three or more linkers.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was admitted; however, she improved; therefore, she was discharged; in addition, she was given home care.'",
                    Array.Empty<object>(),
                    "She was admitted and improved rapidly. She was discharged with home care instructions.",
                    new[] { "She was admitted and improved rapidly. She was discharged with home care instructions" },
                    "Too many linkers (G08.6).",
                    "advanced",
                    2),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is cleanest?",
                    new object[]
                    {
                        new { id = "a", label = "She was admitted; however, she improved; in addition, she was discharged." },
                        new { id = "b", label = "She was admitted and improved; she was discharged the following day." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Max 1–2 linkers (G08.6).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Target sentence length in OET letters: ___ words.",
                    Array.Empty<object>(),
                    "15-25",
                    new[] { "15-25", "15 to 25" },
                    "Sentence length guide (G08.7).",
                    "beginner",
                    1),
            }));
    }

    // ── OET PREPOSITIONS ────────────────────────────────────────────────
    private static void AddOetPrepositionLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-preposition-treatment-for",
            ExamTypeCode: "oet",
            Title: "Treatment FOR — not FROM",
            Description: "Always 'treated for', 'admitted for', 'referred for'.",
            Category: "prepositions",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 23,
            RuleIds: new[] { "G09.1" },
            Intro: "Always 'treatment FOR [condition]'. Never 'treatment FROM'.",
            Example: "Example: She was **treated for** pneumonia. He was **admitted for** observation.",
            Note: "'Treated from' is grammatically wrong.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was treated from pneumonia.'",
                    Array.Empty<object>(),
                    "She was treated for pneumonia.",
                    new[] { "She was treated for pneumonia" },
                    "'Treated for' (G09.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "He was admitted from chest pain." },
                        new { id = "b", label = "He was admitted with chest pain." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'Admitted with' + symptom (G09.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "The patient was referred ___ cardiology review.",
                    Array.Empty<object>(),
                    "for",
                    new[] { "for" },
                    "'Referred for' (G09.1).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-preposition-on-in-at",
            ExamTypeCode: "oet",
            Title: "On / in / at with time",
            Description: "On + date; in + month/year; at + clock time.",
            Category: "prepositions",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 24,
            RuleIds: new[] { "G09.2" },
            Intro: "Use 'on' with a date, 'in' with a month or year, 'at' with a clock time.",
            Example: "Example: She was admitted **on** 15 March. The procedure was performed **in** 2018.",
            Note: "The appointment is **at** 10:30 a.m.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "She was admitted ___ 15 March.",
                    Array.Empty<object>(),
                    "on",
                    new[] { "on" },
                    "'On' + specific date (G09.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "The appointment is on 10:30 a.m." },
                        new { id = "b", label = "The appointment is at 10:30 a.m." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'At' + clock time (G09.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The operation was performed on 2018.'",
                    Array.Empty<object>(),
                    "The operation was performed in 2018.",
                    new[] { "The operation was performed in 2018" },
                    "'In' + year (G09.2).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-preposition-duration-hyphen",
            ExamTypeCode: "oet",
            Title: "Duration as adjective: hyphenate + singular",
            Description: "'A six-month history', not 'a six months history'.",
            Category: "prepositions",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 25,
            RuleIds: new[] { "G09.4" },
            Intro: "When a duration acts as an adjective (before a noun), hyphenate and use the singular.",
            Example: "Example: **A three-week history** of cough. **A six-month course** of antibiotics.",
            Note: "Standalone duration takes plural: 'For 6 months, she has had acne.'",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She reported a three weeks history of cough.'",
                    Array.Empty<object>(),
                    "She reported a three-week history of cough.",
                    new[] { "She reported a three-week history of cough" },
                    "Hyphenate + singular (G09.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "a 6-month history of acne" },
                        new { id = "b", label = "a 6-months history of acne" },
                    },
                    "a",
                    Array.Empty<string>(),
                    "Duration adjective, hyphen, singular (G09.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "A ___ (two-week/two-weeks) history of fever.",
                    Array.Empty<object>(),
                    "two-week",
                    new[] { "two-week" },
                    "Duration adjective (G09.4).",
                    "intermediate",
                    1),
            }));
    }

    // ── OET CONCESSION ──────────────────────────────────────────────────
    private static void AddOetConcessionLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-concession-despite-although",
            ExamTypeCode: "oet",
            Title: "Despite vs although",
            Description: "Despite + noun/-ING; although + full clause.",
            Category: "concession",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 26,
            RuleIds: new[] { "G10.1", "G10.2" },
            Intro: "'Despite' takes a noun or -ING. 'Although' takes a full clause.",
            Example: "Example 1: **Despite the medication**, her symptoms persisted. Example 2: **Although the medication was prescribed**, her symptoms persisted.",
            Note: "Don't mix: 'Despite she took' is wrong.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Despite she took the antibiotics, she continued to deteriorate." },
                        new { id = "b", label = "Despite taking the antibiotics, she continued to deteriorate." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'Despite' + -ING (G10.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Although the medication, her symptoms persisted.'",
                    Array.Empty<object>(),
                    "Despite the medication, her symptoms persisted.",
                    new[] { "Despite the medication, her symptoms persisted", "Although the medication was prescribed, her symptoms persisted" },
                    "'Although' needs a full clause (G10.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite with 'despite': 'Although she took the antibiotics, she deteriorated.'",
                    Array.Empty<object>(),
                    "Despite taking the antibiotics, she deteriorated.",
                    new[] { "Despite taking the antibiotics, she deteriorated" },
                    "'Despite' + -ING (G10.1).",
                    "advanced",
                    2),
            }));
    }

    // ── OET NUMERACY ────────────────────────────────────────────────────
    private static void AddOetNumeracyLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-numeracy-latin",
            ExamTypeCode: "oet",
            Title: "Translate Latin abbreviations",
            Description: "od/bd/tds/qds/stat/prn must be translated.",
            Category: "clinical_numeracy",
            Level: "beginner",
            EstimatedMinutes: 8,
            SortOrder: 27,
            RuleIds: new[] { "G12.1" },
            Intro: "od/om -> once a day; bd/bid -> twice a day; tds/tid -> three times a day; qds/qid -> four times a day; stat -> immediately; prn -> as needed.",
            Example: "Example: 'metformin 500 mg tds' -> 'metformin 500 mg **three times a day**'.",
            Note: "Latin is fine in case notes but never in the letter body.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Translate: 'metformin 500 mg tds'",
                    Array.Empty<object>(),
                    "metformin 500 mg three times a day",
                    new[] { "metformin 500 mg three times a day" },
                    "'tds' -> 'three times a day' (G12.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Paracetamol 500 mg ___ for pain (translate 'prn').",
                    Array.Empty<object>(),
                    "as needed",
                    new[] { "as needed" },
                    "'prn' -> 'as needed' (G12.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "matching",
                    "Match each Latin abbreviation to its English equivalent.",
                    new object[]
                    {
                        new { left = "od", right = "once a day" },
                        new { left = "bd", right = "twice a day" },
                        new { left = "stat", right = "immediately" },
                        new { left = "prn", right = "as needed" },
                    },
                    new object[]
                    {
                        new { left = "od", right = "once a day" },
                        new { left = "bd", right = "twice a day" },
                        new { left = "stat", right = "immediately" },
                        new { left = "prn", right = "as needed" },
                    },
                    Array.Empty<string>(),
                    "Latin -> English (G12.1).",
                    "intermediate",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-numeracy-units",
            ExamTypeCode: "oet",
            Title: "Values always paired with units",
            Description: "Glucose 7.8 mmol/L, BP 150/90 mmHg.",
            Category: "clinical_numeracy",
            Level: "beginner",
            EstimatedMinutes: 7,
            SortOrder: 28,
            RuleIds: new[] { "G12.2", "G12.6" },
            Intro: "Every clinical value carries its unit. Write 'oral' in full (never 'PO').",
            Example: "Example: glucose **of 7.8 mmol/L**; BP **150/90 mmHg**; T° **38.5 °C**.",
            Note: "Never write the reference range inside the letter.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Glucose was 7.8.'",
                    Array.Empty<object>(),
                    "Glucose was 7.8 mmol/L.",
                    new[] { "Glucose was 7.8 mmol/L" },
                    "Pair value with unit (G12.2).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "amoxicillin PO 500 mg" },
                        new { id = "b", label = "oral amoxicillin 500 mg" },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'Oral' in full — not 'PO' (G12.6).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "BP was 150/90 ___.",
                    Array.Empty<object>(),
                    "mmHg",
                    new[] { "mmHg" },
                    "Always paired with unit (G12.2).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-numeracy-dates",
            ExamTypeCode: "oet",
            Title: "Dates: full year, leading zero",
            Description: "Write 01/03/2026, not 1/3/26.",
            Category: "clinical_numeracy",
            Level: "beginner",
            EstimatedMinutes: 6,
            SortOrder: 35,
            RuleIds: new[] { "G12.4", "G12.5" },
            Intro: "Never abbreviate the year. Numbers 1–9 in dates always carry a leading zero.",
            Example: "Example: **01/03/2026**, **05 March 2026**.",
            Note: "Never: 1/3/26.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The operation was on 5/3/26.'",
                    Array.Empty<object>(),
                    "The operation was on 05/03/2026.",
                    new[] { "The operation was on 05/03/2026", "The operation was on 05 March 2026" },
                    "Leading zero + full year (G12.4/G12.5).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "The appointment was on 3/5/26." },
                        new { id = "b", label = "The appointment was on 03/05/2026." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Leading zero + full year (G12.4/G12.5).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "Write the date as: ___ March 2026 (single-digit day).",
                    Array.Empty<object>(),
                    "05",
                    new[] { "05" },
                    "Leading zero (G12.5).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-numeracy-numbers-in-text",
            ExamTypeCode: "oet",
            Title: "Numbers 1–9 words; 10+ numerals",
            Description: "Except with units: always numerals.",
            Category: "clinical_numeracy",
            Level: "beginner",
            EstimatedMinutes: 6,
            SortOrder: 36,
            RuleIds: new[] { "G12.3" },
            Intro: "In running text: numbers 1–9 are words; 10+ are numerals. With units: always numerals.",
            Example: "Example: 'She had **two** previous admissions and **12** consultations. Her BP was **150/90 mmHg**.'",
            Note: "Don't mix 'two' and '2' in the same sentence.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She had 2 previous admissions and twelve consultations.'",
                    Array.Empty<object>(),
                    "She had two previous admissions and 12 consultations.",
                    new[] { "She had two previous admissions and 12 consultations" },
                    "Words 1–9; numerals 10+ (G12.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "BP was one hundred and fifty over ninety mmHg." },
                        new { id = "b", label = "BP was 150/90 mmHg." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Units -> numerals (G12.3).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "She had ___ (3) previous admissions.",
                    Array.Empty<object>(),
                    "three",
                    new[] { "three" },
                    "Numbers 1–9 as words (G12.3).",
                    "beginner",
                    1),
            }));
    }

    // ── OET REPORTED SPEECH ─────────────────────────────────────────────
    private static void AddOetReportedSpeechLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-reported-speech",
            ExamTypeCode: "oet",
            Title: "Reported speech: -ING or noun",
            Description: "'She reported having headaches' — not 'She said that she had headaches'.",
            Category: "reported_speech",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 31,
            RuleIds: new[] { "G11.1", "G11.2", "G11.3" },
            Intro: "After 'reported', 'mentioned', 'stated', 'described' — use -ING or a noun phrase.",
            Example: "Example: She **reported having** headaches for two weeks.",
            Note: "Avoid nested 'had had' constructions.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She reported that she had had headaches.'",
                    Array.Empty<object>(),
                    "She reported having headaches.",
                    new[] { "She reported having headaches", "She reported a history of headaches" },
                    "Avoid 'had had' (G11.1 / G11.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "She said she had had chest pain." },
                        new { id = "b", label = "She reported a history of chest pain." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Noun phrase (G11.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite: 'She said that she had had chest pain for a month.'",
                    Array.Empty<object>(),
                    "She reported a one-month history of chest pain.",
                    new[] { "She reported a one-month history of chest pain", "She reported having chest pain for a month" },
                    "Noun phrase with duration (G11.1 / G09.4).",
                    "advanced",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-oet-reported-advised-counselled",
            ExamTypeCode: "oet",
            Title: "Advised to / counselled regarding",
            Description: "Replace 'told' or 'instructed' with these verbs.",
            Category: "reported_speech",
            Level: "intermediate",
            EstimatedMinutes: 7,
            SortOrder: 32,
            RuleIds: new[] { "G11.3" },
            Intro: "Use 'advised to adopt' and 'counselled regarding'.",
            Example: "Example: She was **advised to adopt** lifestyle modifications. He was **counselled regarding** his alcohol intake.",
            Note: "Never 'instructed to adopt'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'She was instructed to adopt lifestyle changes.'",
                    Array.Empty<object>(),
                    "She was advised to adopt lifestyle changes.",
                    new[] { "She was advised to adopt lifestyle changes" },
                    "'Advised to' (G11.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "The patient was told to stop drinking." },
                        new { id = "b", label = "He was counselled regarding his alcohol intake." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Counselled regarding (G11.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "She was ___ to adopt lifestyle changes.",
                    Array.Empty<object>(),
                    "advised",
                    new[] { "advised", "counselled" },
                    "Formal verb (G11.3).",
                    "beginner",
                    1),
            }));
    }
}
