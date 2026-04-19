using OetLearner.Api.Services;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    // ── IELTS GENERAL ───────────────────────────────────────────────────
    private static void AddIeltsLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-tenses-narrative",
            ExamTypeCode: "ielts",
            Title: "Narrative tenses for IELTS Task 2",
            Description: "Past simple + past continuous for background.",
            Category: "tenses",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 101,
            RuleIds: new[] { "G01.1", "G02.5" },
            Intro: "Narrative writing blends past simple (main events) with past continuous (background actions).",
            Example: "Example: While she **was studying**, the power **went** out.",
            Note: "Avoid past perfect unless necessary.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "While she ___ (study), the power went out.",
                    Array.Empty<object>(),
                    "was studying",
                    new[] { "was studying" },
                    "Past continuous for background (G01.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best?",
                    new object[]
                    {
                        new { id = "a", label = "She had eaten before she had gone out." },
                        new { id = "b", label = "She ate before going out." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Avoid past perfect when simpler tense works (G02.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'While he was walked home, it started to rain.'",
                    Array.Empty<object>(),
                    "While he was walking home, it started to rain.",
                    new[] { "While he was walking home, it started to rain" },
                    "Past continuous = was + -ING (G01.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-articles-general",
            ExamTypeCode: "ielts",
            Title: "Articles in general writing",
            Description: "Definite vs indefinite vs zero article.",
            Category: "articles",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 102,
            RuleIds: new[] { "G04.1", "G04.2" },
            Intro: "Use 'a/an' for first mention, 'the' for specific reference, zero article for general uncountables.",
            Example: "Example: I saw **a car**. **The car** was red. **Cars** are expensive.",
            Note: "Don't over-use 'the'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "I saw ___ accident this morning.",
                    Array.Empty<object>(),
                    "an",
                    new[] { "an" },
                    "First mention 'an' (G04.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Water is essential for the life." },
                        new { id = "b", label = "Water is essential for life." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Zero article with general uncountable (G04.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The education is the key to success.'",
                    Array.Empty<object>(),
                    "Education is the key to success.",
                    new[] { "Education is the key to success" },
                    "Zero article with general abstract (G04.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-linkers-cohesion",
            ExamTypeCode: "ielts",
            Title: "Linkers for cohesion",
            Description: "Band 6->7 requires varied, correctly punctuated linkers.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 103,
            RuleIds: new[] { "G08.1", "G08.2", "G08.6" },
            Intro: "Variety matters: however, therefore, moreover, furthermore, nevertheless.",
            Example: "Example: Technology has benefits; **however**, it also has costs.",
            Note: "Max 1–2 linkers per paragraph.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correctly punctuated?",
                    new object[]
                    {
                        new { id = "a", label = "It is effective, however, it is expensive." },
                        new { id = "b", label = "It is effective; however, it is expensive." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "; however, (G08.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "matching",
                    "Match the linker to its punctuation.",
                    new object[]
                    {
                        new { left = "however", right = "semicolon before, comma after" },
                        new { left = "therefore", right = "semicolon before, comma after" },
                        new { left = "in addition to", right = "no preceding punctuation" },
                    },
                    new object[]
                    {
                        new { left = "however", right = "semicolon before, comma after" },
                        new { left = "therefore", right = "semicolon before, comma after" },
                        new { left = "in addition to", right = "no preceding punctuation" },
                    },
                    Array.Empty<string>(),
                    "Linker punctuation (G08.1–G08.4).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'It is cheap, moreover, it is easy.'",
                    Array.Empty<object>(),
                    "It is cheap; moreover, it is easy.",
                    new[] { "It is cheap; moreover, it is easy" },
                    "Semicolon + comma (G08.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-conditional-second",
            ExamTypeCode: "ielts",
            Title: "Second conditional for hypotheticals",
            Description: "If + past simple, would + base.",
            Category: "conditionals",
            Level: "intermediate",
            EstimatedMinutes: 10,
            SortOrder: 104,
            RuleIds: new[] { "G06.3" },
            Intro: "Second conditional for hypothetical, contrary-to-fact situations.",
            Example: "Example: **If I won** the lottery, I **would travel** the world.",
            Note: "Use in IELTS 'Imagine that' prompts.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "If I ___ (win) the lottery, I would travel the world.",
                    Array.Empty<object>(),
                    "won",
                    new[] { "won" },
                    "Past simple in if-clause (G06.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is a second conditional?",
                    new object[]
                    {
                        new { id = "a", label = "If I win, I will buy a house." },
                        new { id = "b", label = "If I won, I would buy a house." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Second conditional (G06.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite as second conditional: 'I don't have time, so I can't help.'",
                    Array.Empty<object>(),
                    "If I had time, I would help.",
                    new[] { "If I had time, I would help", "If I had more time, I would help" },
                    "Hypothetical (G06.3).",
                    "advanced",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-sva-quantifiers",
            ExamTypeCode: "ielts",
            Title: "SVA with quantifiers",
            Description: "Each / every / one of — singular.",
            Category: "subject_verb_agreement",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 105,
            RuleIds: new[] { "G05.3" },
            Intro: "Each, every, 'one of the...' take singular verbs.",
            Example: "Example: **Each** of the students **has** received feedback.",
            Note: "A noun with 'one of the' still takes singular.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Each of the students have received feedback." },
                        new { id = "b", label = "Each of the students has received feedback." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Singular (G05.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "One of the answers ___ wrong.",
                    Array.Empty<object>(),
                    "is",
                    new[] { "is" },
                    "Singular (G05.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Every student have a textbook.'",
                    Array.Empty<object>(),
                    "Every student has a textbook.",
                    new[] { "Every student has a textbook" },
                    "Singular (G05.3).",
                    "beginner",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-passive-report",
            ExamTypeCode: "ielts",
            Title: "Passive in IELTS reports",
            Description: "'It is believed that...' academic phrasing.",
            Category: "passive_voice",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 106,
            RuleIds: new[] { "G03.3", "G03.4" },
            Intro: "Passive structures are common in IELTS for objectivity.",
            Example: "Example: **It is widely believed** that technology will transform education.",
            Note: "Mix passive and active.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is most formal for IELTS?",
                    new object[]
                    {
                        new { id = "a", label = "People think technology will transform education." },
                        new { id = "b", label = "It is widely believed that technology will transform education." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'It is believed that...' (G03.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite in passive: 'People say that pollution is a major issue.'",
                    Array.Empty<object>(),
                    "It is said that pollution is a major issue.",
                    new[] { "It is said that pollution is a major issue", "Pollution is said to be a major issue" },
                    "Passive report (G03.3).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "fill_blank",
                    "It ___ widely believed that exercise improves health.",
                    Array.Empty<object>(),
                    "is",
                    new[] { "is" },
                    "'It is...believed' (G03.3).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-register-formal",
            ExamTypeCode: "ielts",
            Title: "Formal register in IELTS essays",
            Description: "No contractions, precise vocabulary.",
            Category: "formal_register",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 107,
            RuleIds: new[] { "G07.1", "G07.5" },
            Intro: "IELTS Task 2 expects formal register — no contractions, precise vocabulary.",
            Example: "Example: **A significant number of** — not 'a lot of'.",
            Note: "Avoid colloquial, filler.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Don't forget the main point.'",
                    Array.Empty<object>(),
                    "Do not forget the main point.",
                    new[] { "Do not forget the main point" },
                    "No contractions (G07.1).",
                    "beginner",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best for IELTS Task 2?",
                    new object[]
                    {
                        new { id = "a", label = "A lot of people think..." },
                        new { id = "b", label = "A significant number of people think..." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Precise formal vocabulary (G07.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite more formally: 'Kids are bad at saving money.'",
                    Array.Empty<object>(),
                    "Young people are poor at saving money.",
                    new[] { "Young people are poor at saving money", "Children are inadequate at saving money" },
                    "Replace colloquial with formal (G07.5).",
                    "intermediate",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-ielts-concession-although",
            ExamTypeCode: "ielts",
            Title: "Although vs despite (IELTS)",
            Description: "Same rules as OET.",
            Category: "concession",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 108,
            RuleIds: new[] { "G10.1", "G10.2" },
            Intro: "Concession structures add nuance to IELTS Task 2.",
            Example: "Example: **Although** technology has benefits, it also has costs.",
            Note: "**Despite** its benefits, technology also has costs.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Despite he worked hard, he failed.'",
                    Array.Empty<object>(),
                    "Despite working hard, he failed.",
                    new[] { "Despite working hard, he failed", "Although he worked hard, he failed" },
                    "'Despite' needs noun or -ING (G10.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "Although the rain, they played." },
                        new { id = "b", label = "Although it was raining, they played." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'Although' + clause (G10.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite with 'despite': 'Although she is young, she is experienced.'",
                    Array.Empty<object>(),
                    "Despite being young, she is experienced.",
                    new[] { "Despite being young, she is experienced", "Despite her youth, she is experienced" },
                    "'Despite' + -ING/noun (G10.1).",
                    "advanced",
                    2),
            }));
    }

    // ── PTE ACADEMIC ────────────────────────────────────────────────────
    private static void AddPteLessons(List<GrammarSeedSpec> list)
    {
        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-tenses-reorder",
            ExamTypeCode: "pte",
            Title: "Tenses for Re-order Paragraphs",
            Description: "Match tense to sequence cues.",
            Category: "tenses",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 201,
            RuleIds: new[] { "G01.1", "G02.1" },
            Intro: "In PTE Re-order Paragraphs, tense consistency signals order.",
            Example: "Example: 'First, ...' -> past simple. 'Since then, ...' -> present perfect.",
            Note: "Time markers determine tense.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which follows 'Since 2020, ...'?",
                    new object[]
                    {
                        new { id = "a", label = "many people adopted remote work" },
                        new { id = "b", label = "many people have adopted remote work" },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Since + starting point -> present perfect (G02.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "First, the team ___ (collect) the data in 2022.",
                    Array.Empty<object>(),
                    "collected",
                    new[] { "collected" },
                    "Past simple with year (G01.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Since 2022, the team has collected data in 2022.'",
                    Array.Empty<object>(),
                    "In 2022, the team collected the data.",
                    new[] { "In 2022, the team collected the data" },
                    "Pick one time frame (G01.1 / G02.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-articles-academic",
            ExamTypeCode: "pte",
            Title: "Articles in Describe Image",
            Description: "Specific vs general reference.",
            Category: "articles",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 202,
            RuleIds: new[] { "G04.1", "G04.2" },
            Intro: "PTE Describe Image needs precise article use.",
            Example: "Example: **The** graph **shows** **a** significant increase.",
            Note: "'The' for the specific graph; 'a' for a general trend.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "fill_blank",
                    "___ graph shows a significant increase in sales.",
                    Array.Empty<object>(),
                    "The",
                    new[] { "The" },
                    "Specific graph 'The' (G04.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "A chart illustrates the trend." },
                        new { id = "b", label = "The chart illustrates a trend." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'The' for the specific chart (G04.2).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Chart shows increase in population.'",
                    Array.Empty<object>(),
                    "The chart shows an increase in population.",
                    new[] { "The chart shows an increase in population" },
                    "Need articles (G04.1 / G04.2).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-linkers-summary",
            ExamTypeCode: "pte",
            Title: "Linkers for Summarize Written Text",
            Description: "One-sentence summary with precise cohesion.",
            Category: "linkers",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 203,
            RuleIds: new[] { "G08.1", "G08.6" },
            Intro: "PTE SWT is a single complex sentence (5–75 words). Use linkers efficiently.",
            Example: "Example: Technology improves productivity**; however,** it raises privacy concerns, which need regulation.",
            Note: "One main linker is usually enough.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is a well-punctuated PTE summary?",
                    new object[]
                    {
                        new { id = "a", label = "Tech improves productivity, however it raises privacy concerns." },
                        new { id = "b", label = "Tech improves productivity; however, it raises privacy concerns." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "; however, (G08.1).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Combine: 'Exercise improves health. It reduces stress.' (one sentence)",
                    Array.Empty<object>(),
                    "Exercise improves health; furthermore, it reduces stress.",
                    new[] { "Exercise improves health; furthermore, it reduces stress", "Exercise improves health and reduces stress" },
                    "Semicolon + linker (G08.1).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'Cars are fast however they pollute.'",
                    Array.Empty<object>(),
                    "Cars are fast; however, they pollute.",
                    new[] { "Cars are fast; however, they pollute" },
                    "; however, (G08.1).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-register-essay",
            ExamTypeCode: "pte",
            Title: "Formal register in PTE Essay",
            Description: "No contractions, precise lexis.",
            Category: "formal_register",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 204,
            RuleIds: new[] { "G07.1", "G07.5" },
            Intro: "PTE Essay (200–300 words) expects formal academic register.",
            Example: "Example: 'It cannot be denied that...' is stronger than 'You can't say that...'",
            Note: "Avoid second-person 'you'.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'You can't deny that pollution is harmful.'",
                    Array.Empty<object>(),
                    "It cannot be denied that pollution is harmful.",
                    new[] { "It cannot be denied that pollution is harmful" },
                    "Impersonal formal (G07.1 / G07.5).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is best for PTE essay?",
                    new object[]
                    {
                        new { id = "a", label = "A lot of people think..." },
                        new { id = "b", label = "Many argue that..." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Precise formal (G07.5).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite: 'Kids shouldn't use phones too much.'",
                    Array.Empty<object>(),
                    "Young people should not use phones excessively.",
                    new[] { "Young people should not use phones excessively" },
                    "Formal register (G07.1 / G07.5).",
                    "intermediate",
                    2),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-passive-academic",
            ExamTypeCode: "pte",
            Title: "Passive in academic writing",
            Description: "Essential for PTE Summarize/Essay.",
            Category: "passive_voice",
            Level: "intermediate",
            EstimatedMinutes: 9,
            SortOrder: 205,
            RuleIds: new[] { "G03.3", "G03.4" },
            Intro: "Academic register relies on passive to shift focus.",
            Example: "Example: **Research has shown** that exercise improves cognition.",
            Note: "Mix active and passive.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "sentence_transformation",
                    "Rewrite passive: 'Researchers have shown exercise improves cognition.'",
                    Array.Empty<object>(),
                    "It has been shown that exercise improves cognition.",
                    new[] { "It has been shown that exercise improves cognition", "Exercise has been shown to improve cognition" },
                    "Passive academic report (G03.3).",
                    "intermediate",
                    2),
                new GrammarSeedExercise(
                    "mcq",
                    "Which is most formal?",
                    new object[]
                    {
                        new { id = "a", label = "People have proved that vaccines save lives." },
                        new { id = "b", label = "It has been proved that vaccines save lives." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "Passive academic (G03.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "It is widely ___ that reading improves vocabulary.",
                    Array.Empty<object>(),
                    "believed",
                    new[] { "believed", "accepted", "recognised" },
                    "'It is believed/accepted' (G03.3).",
                    "intermediate",
                    1),
            }));

        list.Add(new GrammarSeedSpec(
            Id: "grm-pte-sva-describe",
            ExamTypeCode: "pte",
            Title: "SVA for Describe Image",
            Description: "Data subjects take singular/plural verbs correctly.",
            Category: "subject_verb_agreement",
            Level: "intermediate",
            EstimatedMinutes: 8,
            SortOrder: 206,
            RuleIds: new[] { "G05.3", "G05.4" },
            Intro: "'The number of X is' -> singular. 'A number of X are' -> plural.",
            Example: "Example: **The number** of applicants **has** increased. **A number** of applicants **are** waiting.",
            Note: "Check the head noun.",
            Exercises: new[]
            {
                new GrammarSeedExercise(
                    "mcq",
                    "Which is correct?",
                    new object[]
                    {
                        new { id = "a", label = "The number of students have increased." },
                        new { id = "b", label = "The number of students has increased." },
                    },
                    "b",
                    Array.Empty<string>(),
                    "'The number of' is singular (G05.3).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "fill_blank",
                    "A number of applicants ___ waiting.",
                    Array.Empty<object>(),
                    "are",
                    new[] { "are" },
                    "'A number of' is plural (G05.4).",
                    "intermediate",
                    1),
                new GrammarSeedExercise(
                    "error_correction",
                    "Correct: 'The statistics shows a decline.'",
                    Array.Empty<object>(),
                    "The statistics show a decline.",
                    new[] { "The statistics show a decline" },
                    "'Statistics' plural (G05.4).",
                    "intermediate",
                    1),
            }));
    }
}
