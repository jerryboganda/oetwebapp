using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    private static void SeedExamFamilies(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        db.ExamFamilies.AddRange(
            new ExamFamily
            {
                Code = "oet",
                Label = "OET",
                ScoringModel = "0-500-letter",
                Description = "Occupational English Test — healthcare professional English proficiency assessment.",
                SubtestConfigJson = JsonSupport.Serialize(new[]
                {
                    new { code = "writing", label = "Writing", duration = 45, isProfessionSpecific = true },
                    new { code = "speaking", label = "Speaking", duration = 20, isProfessionSpecific = true },
                    new { code = "reading", label = "Reading", duration = 60, isProfessionSpecific = false },
                    new { code = "listening", label = "Listening", duration = 45, isProfessionSpecific = false }
                }),
                CriteriaConfigJson = JsonSupport.Serialize(new object[]
                {
                    new { subtest = "writing", criteria = new[] { "purpose", "content", "conciseness", "genre", "organization", "language" } },
                    new { subtest = "speaking", criteria = new[] { "intelligibility", "fluency", "appropriateness", "grammar", "relationshipBuilding", "patientPerspective", "providingStructure", "informationGathering", "informationGiving" } }
                }),
                SortOrder = 1,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new ExamFamily
            {
                Code = "ielts",
                Label = "IELTS Academic",
                ScoringModel = "0-9-band",
                Description = "International English Language Testing System — Academic module for university and professional registration.",
                SubtestConfigJson = JsonSupport.Serialize(new[]
                {
                    new { code = "writing", label = "Writing", duration = 60, isProfessionSpecific = false },
                    new { code = "speaking", label = "Speaking", duration = 14, isProfessionSpecific = false },
                    new { code = "reading", label = "Reading", duration = 60, isProfessionSpecific = false },
                    new { code = "listening", label = "Listening", duration = 30, isProfessionSpecific = false }
                }),
                CriteriaConfigJson = JsonSupport.Serialize(new object[]
                {
                    new { subtest = "writing", criteria = new[] { "task_achievement", "coherence_cohesion", "lexical_resource", "grammatical_range" } },
                    new { subtest = "speaking", criteria = new[] { "fluency_coherence", "lexical_resource", "grammatical_range", "pronunciation" } }
                }),
                SortOrder = 2,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new ExamFamily
            {
                Code = "pte",
                Label = "PTE Academic",
                ScoringModel = "10-90",
                Description = "Pearson Test of English Academic — computer-based, AI-scored English proficiency test.",
                SubtestConfigJson = JsonSupport.Serialize(new[]
                {
                    new { code = "speaking_writing", label = "Speaking & Writing", duration = 67, isProfessionSpecific = false },
                    new { code = "reading", label = "Reading", duration = 30, isProfessionSpecific = false },
                    new { code = "listening", label = "Listening", duration = 43, isProfessionSpecific = false }
                }),
                CriteriaConfigJson = JsonSupport.Serialize(new object[]
                {
                    new { subtest = "speaking_writing", criteria = new[] { "oral_fluency", "pronunciation", "content", "form", "grammar", "vocabulary", "spelling" } },
                    new { subtest = "reading", criteria = new[] { "content", "form" } },
                    new { subtest = "listening", criteria = new[] { "content", "form" } }
                }),
                SortOrder = 3,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
    }

    private static void SeedExamTypes(LearnerDbContext db)
    {
        db.ExamTypes.AddRange(
            new ExamType
            {
                Code = "oet",
                Label = "OET",
                Description = "Occupational English Test for healthcare professionals.",
                SubtestDefinitionsJson = """[{"code":"listening","label":"Listening","durationMinutes":45},{"code":"reading","label":"Reading","durationMinutes":60},{"code":"writing","label":"Writing","durationMinutes":45},{"code":"speaking","label":"Speaking","durationMinutes":20}]""",
                ScoringSystemJson = """{"scale":"A-E","passing":"B","grades":[{"grade":"A","label":"Expert"},{"grade":"B","label":"Good"},{"grade":"C","label":"Borderline"},{"grade":"D","label":"Limited"},{"grade":"E","label":"Very Limited"}]}""",
                TimingsJson = """{"listening":45,"reading":60,"writing":45,"speaking":20}""",
                ProfessionIdsJson = """["medicine","nursing","pharmacy","dentistry","physiotherapy","occupational_therapy","radiography","optometry","veterinary","dietetics","podiatry","speech_pathology","social_work"]""",
                Status = "active",
                SortOrder = 1
            },
            new ExamType
            {
                Code = "ielts",
                Label = "IELTS Academic",
                Description = "International English Language Testing System (Academic) for higher education and professional registration.",
                SubtestDefinitionsJson = """[{"code":"listening","label":"Listening","durationMinutes":40},{"code":"reading","label":"Reading","durationMinutes":60},{"code":"writing","label":"Writing","durationMinutes":60},{"code":"speaking","label":"Speaking","durationMinutes":15}]""",
                ScoringSystemJson = """{"scale":"0-9","passing":6.5,"bandIncrement":0.5}""",
                TimingsJson = """{"listening":40,"reading":60,"writing":60,"speaking":15}""",
                ProfessionIdsJson = "[]",
                Status = "planned",
                SortOrder = 2
            },
            new ExamType
            {
                Code = "pte",
                Label = "PTE Academic",
                Description = "Pearson Test of English Academic — a computer-based test accepted by universities and governments worldwide.",
                SubtestDefinitionsJson = """[{"code":"speaking_writing","label":"Speaking & Writing","durationMinutes":77},{"code":"reading","label":"Reading","durationMinutes":32},{"code":"listening","label":"Listening","durationMinutes":45}]""",
                ScoringSystemJson = """{"scale":"10-90","passing":50}""",
                TimingsJson = """{"speaking_writing":77,"reading":32,"listening":45}""",
                ProfessionIdsJson = "[]",
                Status = "planned",
                SortOrder = 3
            },
            new ExamType
            {
                Code = "cambridge",
                Label = "Cambridge English",
                Description = "Cambridge English Qualifications (B2 First, C1 Advanced, C2 Proficiency) — globally recognised by universities and employers.",
                SubtestDefinitionsJson = """[{"code":"reading_use","label":"Reading & Use of English","durationMinutes":75},{"code":"writing","label":"Writing","durationMinutes":80},{"code":"listening","label":"Listening","durationMinutes":40},{"code":"speaking","label":"Speaking","durationMinutes":15}]""",
                ScoringSystemJson = """{"scale":"CEFR","levels":["B2","C1","C2"],"scores":{"B2":{"min":160,"max":179},"C1":{"min":180,"max":199},"C2":{"min":200,"max":230}}}""",
                TimingsJson = """{"reading_use":75,"writing":80,"listening":40,"speaking":15}""",
                ProfessionIdsJson = "[]",
                Status = "planned",
                SortOrder = 4
            },
            new ExamType
            {
                Code = "toefl",
                Label = "TOEFL iBT",
                Description = "Test of English as a Foreign Language (internet-based) — the world's most widely accepted English proficiency test.",
                SubtestDefinitionsJson = """[{"code":"reading","label":"Reading","durationMinutes":54},{"code":"listening","label":"Listening","durationMinutes":41},{"code":"speaking","label":"Speaking","durationMinutes":17},{"code":"writing","label":"Writing","durationMinutes":50}]""",
                ScoringSystemJson = """{"scale":"0-120","sectionMax":30,"passing":80}""",
                TimingsJson = """{"reading":54,"listening":41,"speaking":17,"writing":50}""",
                ProfessionIdsJson = "[]",
                Status = "planned",
                SortOrder = 5
            }
        );
    }

    private static void SeedAchievements(LearnerDbContext db)
    {
        db.Achievements.AddRange(
            // ── Practice achievements ──
            new Achievement { Id = "ach-001", Code = "first_attempt", Label = "First Step", Description = "Complete your very first practice attempt.", Category = "practice", XPReward = 25, CriteriaJson = """{"type":"attempt_count","threshold":1}""", SortOrder = 10, Status = "active" },
            new Achievement { Id = "ach-002", Code = "attempts_10", Label = "Getting Started", Description = "Complete 10 practice attempts.", Category = "practice", XPReward = 50, CriteriaJson = """{"type":"attempt_count","threshold":10}""", SortOrder = 20, Status = "active" },
            new Achievement { Id = "ach-003", Code = "attempts_50", Label = "Committed Learner", Description = "Complete 50 practice attempts.", Category = "practice", XPReward = 100, CriteriaJson = """{"type":"attempt_count","threshold":50}""", SortOrder = 30, Status = "active" },
            new Achievement { Id = "ach-004", Code = "attempts_100", Label = "Century Mark", Description = "Complete 100 practice attempts.", Category = "practice", XPReward = 200, CriteriaJson = """{"type":"attempt_count","threshold":100}""", SortOrder = 40, Status = "active" },
            new Achievement { Id = "ach-005", Code = "attempts_500", Label = "Practice Champion", Description = "Complete 500 practice attempts.", Category = "practice", XPReward = 500, CriteriaJson = """{"type":"attempt_count","threshold":500}""", SortOrder = 50, Status = "active" },
            // ── Streak achievements ──
            new Achievement { Id = "ach-010", Code = "streak_3", Label = "3-Day Streak", Description = "Maintain a 3-day study streak.", Category = "streak", XPReward = 30, CriteriaJson = """{"type":"streak_days","threshold":3}""", SortOrder = 100, Status = "active" },
            new Achievement { Id = "ach-011", Code = "streak_7", Label = "Week Warrior", Description = "Maintain a 7-day study streak.", Category = "streak", XPReward = 75, CriteriaJson = """{"type":"streak_days","threshold":7}""", SortOrder = 110, Status = "active" },
            new Achievement { Id = "ach-012", Code = "streak_14", Label = "Fortnight Focus", Description = "Maintain a 14-day study streak.", Category = "streak", XPReward = 150, CriteriaJson = """{"type":"streak_days","threshold":14}""", SortOrder = 120, Status = "active" },
            new Achievement { Id = "ach-013", Code = "streak_30", Label = "Monthly Dedication", Description = "Maintain a 30-day study streak.", Category = "streak", XPReward = 300, CriteriaJson = """{"type":"streak_days","threshold":30}""", SortOrder = 130, Status = "active" },
            new Achievement { Id = "ach-014", Code = "streak_100", Label = "Century Streak", Description = "Maintain a 100-day study streak.", Category = "streak", XPReward = 1000, CriteriaJson = """{"type":"streak_days","threshold":100}""", SortOrder = 140, Status = "active" },
            // ── Score milestone achievements ──
            new Achievement { Id = "ach-020", Code = "first_grade_b", Label = "Grade B Unlocked", Description = "Achieve an OET Grade B on any subtest for the first time.", Category = "milestone", XPReward = 150, CriteriaJson = """{"type":"first_grade","grade":"B","examTypeCode":"oet"}""", SortOrder = 200, Status = "active" },
            new Achievement { Id = "ach-021", Code = "all_b_grade", Label = "All B's", Description = "Achieve Grade B or above on all four OET subtests in practice.", Category = "milestone", XPReward = 500, CriteriaJson = """{"type":"all_subtests_grade","grade":"B","examTypeCode":"oet"}""", SortOrder = 210, Status = "active" },
            new Achievement { Id = "ach-022", Code = "grade_a_writing", Label = "Writing Ace", Description = "Achieve OET Grade A in Writing.", Category = "milestone", XPReward = 250, CriteriaJson = """{"type":"first_grade","grade":"A","subtest":"writing","examTypeCode":"oet"}""", SortOrder = 220, Status = "active" },
            new Achievement { Id = "ach-023", Code = "grade_a_speaking", Label = "Speaking Star", Description = "Achieve OET Grade A in Speaking.", Category = "milestone", XPReward = 250, CriteriaJson = """{"type":"first_grade","grade":"A","subtest":"speaking","examTypeCode":"oet"}""", SortOrder = 230, Status = "active" },
            new Achievement { Id = "ach-024", Code = "score_improvement", Label = "On the Rise", Description = "Improve your score on the same subtest across three consecutive attempts.", Category = "milestone", XPReward = 100, CriteriaJson = """{"type":"consecutive_improvement","count":3}""", SortOrder = 240, Status = "active" },
            // ── Mastery achievements ──
            new Achievement { Id = "ach-030", Code = "vocab_50", Label = "Word Collector", Description = "Add 50 vocabulary terms to your study list.", Category = "mastery", XPReward = 50, CriteriaJson = """{"type":"vocab_added","threshold":50}""", SortOrder = 300, Status = "active" },
            new Achievement { Id = "ach-031", Code = "vocab_100", Label = "Vocabulary Builder", Description = "Add 100 vocabulary terms to your study list.", Category = "mastery", XPReward = 100, CriteriaJson = """{"type":"vocab_added","threshold":100}""", SortOrder = 310, Status = "active" },
            new Achievement { Id = "ach-032", Code = "vocab_mastered_25", Label = "Word Master", Description = "Master 25 vocabulary terms.", Category = "mastery", XPReward = 100, CriteriaJson = """{"type":"vocab_mastered","threshold":25}""", SortOrder = 320, Status = "active" },
            new Achievement { Id = "ach-033", Code = "review_sessions_10", Label = "Spaced Repetition Pro", Description = "Complete 10 spaced repetition review sessions.", Category = "mastery", XPReward = 75, CriteriaJson = """{"type":"review_sessions","threshold":10}""", SortOrder = 330, Status = "active" },
            new Achievement { Id = "ach-034", Code = "pronunciation_drill_5", Label = "Articulate", Description = "Complete 5 pronunciation drill sessions.", Category = "mastery", XPReward = 60, CriteriaJson = """{"type":"pronunciation_drills","threshold":5}""", SortOrder = 340, Status = "active" },
            new Achievement { Id = "ach-035", Code = "conversation_sessions_5", Label = "Conversationalist", Description = "Complete 5 AI conversation practice sessions.", Category = "mastery", XPReward = 100, CriteriaJson = """{"type":"conversation_sessions","threshold":5}""", SortOrder = 350, Status = "active" },
            new Achievement { Id = "ach-036", Code = "grammar_lessons_5", Label = "Grammar Guru", Description = "Complete 5 grammar lessons.", Category = "mastery", XPReward = 75, CriteriaJson = """{"type":"grammar_lessons_completed","threshold":5}""", SortOrder = 360, Status = "active" },
            // ── Social achievements ──
            new Achievement { Id = "ach-040", Code = "first_forum_post", Label = "Community Member", Description = "Post your first message in the community forums.", Category = "social", XPReward = 25, CriteriaJson = """{"type":"forum_posts","threshold":1}""", SortOrder = 400, Status = "active" },
            new Achievement { Id = "ach-041", Code = "forum_posts_10", Label = "Active Contributor", Description = "Post 10 messages in the community forums.", Category = "social", XPReward = 75, CriteriaJson = """{"type":"forum_posts","threshold":10}""", SortOrder = 410, Status = "active" },
            new Achievement { Id = "ach-042", Code = "first_referral", Label = "Referral Pioneer", Description = "Successfully refer a friend to the platform.", Category = "social", XPReward = 100, CriteriaJson = """{"type":"referrals_converted","threshold":1}""", SortOrder = 420, Status = "active" },
            new Achievement { Id = "ach-043", Code = "referrals_5", Label = "Brand Ambassador", Description = "Successfully refer 5 friends to the platform.", Category = "social", XPReward = 300, CriteriaJson = """{"type":"referrals_converted","threshold":5}""", SortOrder = 430, Status = "active" },
            // ── Milestone XP achievements ──
            new Achievement { Id = "ach-050", Code = "xp_500", Label = "Rising Star", Description = "Earn 500 total XP.", Category = "milestone", XPReward = 0, CriteriaJson = """{"type":"total_xp","threshold":500}""", SortOrder = 500, Status = "active" },
            new Achievement { Id = "ach-051", Code = "xp_1000", Label = "Level Up", Description = "Earn 1,000 total XP.", Category = "milestone", XPReward = 0, CriteriaJson = """{"type":"total_xp","threshold":1000}""", SortOrder = 510, Status = "active" },
            new Achievement { Id = "ach-052", Code = "xp_5000", Label = "XP Champion", Description = "Earn 5,000 total XP.", Category = "milestone", XPReward = 0, CriteriaJson = """{"type":"total_xp","threshold":5000}""", SortOrder = 520, Status = "active" },
            new Achievement { Id = "ach-053", Code = "xp_10000", Label = "Elite Learner", Description = "Earn 10,000 total XP.", Category = "milestone", XPReward = 0, CriteriaJson = """{"type":"total_xp","threshold":10000}""", SortOrder = 530, Status = "active" },
            new Achievement { Id = "ach-054", Code = "leaderboard_top10", Label = "Top 10", Description = "Reach the top 10 on the weekly leaderboard.", Category = "social", XPReward = 150, CriteriaJson = """{"type":"leaderboard_rank","threshold":10,"period":"weekly"}""", SortOrder = 540, Status = "active" },
            new Achievement { Id = "ach-055", Code = "mock_exam_first", Label = "Mock Exam Taker", Description = "Complete your first full mock exam.", Category = "practice", XPReward = 75, CriteriaJson = """{"type":"mock_exams","threshold":1}""", SortOrder = 60, Status = "active" }
        );
    }

    private static void SeedVocabularyTerms(LearnerDbContext db)
    {
        db.VocabularyTerms.AddRange(
            // ── Clinical Communication ──
            new VocabularyTerm { Id = "vt-001", Term = "analgesia", Definition = "The absence of pain or the relief of pain without loss of consciousness.", ExampleSentence = "The patient was given analgesia to manage post-operative pain.", ContextNotes = "Commonly used when referring to pain management in clinical settings.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["pain relief","pain management"]""", CollocationsJson = """["provide analgesia","adequate analgesia","post-operative analgesia"]""", RelatedTermsJson = """["analgesic","nociception","pain score"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-002", Term = "dyspnoea", Definition = "Difficulty or laboured breathing; shortness of breath.", ExampleSentence = "The patient presented with acute dyspnoea on exertion.", ContextNotes = "Preferred clinical spelling in Australian/UK English (vs. 'dyspnea' in US English).", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["shortness of breath","breathlessness","SOB"]""", CollocationsJson = """["acute dyspnoea","dyspnoea on exertion","nocturnal dyspnoea"]""", RelatedTermsJson = """["tachypnoea","orthopnoea","respiratory distress"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-003", Term = "haemoptysis", Definition = "The coughing up of blood or blood-stained mucus from the bronchi, larynx, trachea, or lungs.", ExampleSentence = "The referral letter noted intermittent haemoptysis over the past three weeks.", ContextNotes = "Important OET writing term; always flag as a red flag symptom.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["coughing up blood"]""", CollocationsJson = """["frank haemoptysis","intermittent haemoptysis","massive haemoptysis"]""", RelatedTermsJson = """["haematemesis","epistaxis","pulmonary embolism"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-004", Term = "oedema", Definition = "Swelling caused by excess fluid trapped in the body's tissues.", ExampleSentence = "Peripheral oedema was noted bilaterally up to the knees.", ContextNotes = "Australian/UK spelling. US spelling is 'edema'.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["edema","swelling","fluid retention"]""", CollocationsJson = """["peripheral oedema","pitting oedema","pulmonary oedema","bilateral oedema"]""", RelatedTermsJson = """["ascites","pleural effusion","lymphoedema"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-005", Term = "tachycardia", Definition = "Abnormally rapid heart rate, typically defined as over 100 beats per minute in adults.", ExampleSentence = "The patient was in sinus tachycardia at 118 bpm on admission.", ContextNotes = "Can be physiological (exercise) or pathological; specify subtype in clinical notes.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["rapid heart rate","fast pulse"]""", CollocationsJson = """["sinus tachycardia","ventricular tachycardia","resting tachycardia"]""", RelatedTermsJson = """["bradycardia","arrhythmia","palpitations"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-006", Term = "hypotension", Definition = "Abnormally low blood pressure, generally defined as systolic BP below 90 mmHg.", ExampleSentence = "Postural hypotension was identified as the likely cause of the patient's recurrent falls.", ContextNotes = "Distinguish orthostatic/postural hypotension from general hypotension in referrals.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["low blood pressure"]""", CollocationsJson = """["postural hypotension","orthostatic hypotension","severe hypotension"]""", RelatedTermsJson = """["hypertension","syncope","vasodilation"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-007", Term = "contraindicated", Definition = "Not recommended or inadvisable due to the potential for harm in a specific situation.", ExampleSentence = "NSAIDs are contraindicated in patients with active peptic ulcer disease.", ContextNotes = "Passive form 'is contraindicated' is standard in clinical writing.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["not recommended","inadvisable","unsafe"]""", CollocationsJson = """["contraindicated in","absolutely contraindicated","relatively contraindicated"]""", RelatedTermsJson = """["precaution","adverse reaction","side effect"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-008", Term = "prognosis", Definition = "A forecast of the likely course and outcome of a disease or condition.", ExampleSentence = "The prognosis for stage II breast cancer with current treatment protocols is generally favourable.", ContextNotes = "Distinguish from 'diagnosis' (identification of condition) in OET speaking scenarios.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["outlook","forecast","expected outcome"]""", CollocationsJson = """["poor prognosis","favourable prognosis","guarded prognosis","overall prognosis"]""", RelatedTermsJson = """["diagnosis","diagnosis and management","treatment outcome"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-009", Term = "exacerbation", Definition = "A worsening or increase in severity of a disease or its symptoms.", ExampleSentence = "The patient was admitted with an acute exacerbation of COPD.", ContextNotes = "Frequently appears in OET writing tasks for chronic disease referrals.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["flare-up","deterioration","worsening"]""", CollocationsJson = """["acute exacerbation","exacerbation of COPD","prevent exacerbation"]""", RelatedTermsJson = """["remission","relapse","deterioration"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-010", Term = "palliative", Definition = "Providing relief from the symptoms of a serious illness without curing it; relating to end-of-life care.", ExampleSentence = "The patient and family elected to pursue palliative care rather than further curative treatment.", ContextNotes = "Sensitive term; use with care in OET speaking scenarios involving end-of-life discussions.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["comfort care","supportive care","end-of-life care"]""", CollocationsJson = """["palliative care","palliative approach","palliative management","palliative intent"]""", RelatedTermsJson = """["hospice","curative","terminal illness"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-011", Term = "aetiology", Definition = "The cause, set of causes, or manner of causation of a disease or condition.", ExampleSentence = "The aetiology of the patient's hypertension was considered to be multifactorial.", ContextNotes = "Australian/UK spelling; US English uses 'etiology'.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["cause","etiology","origin"]""", CollocationsJson = """["unknown aetiology","multifactorial aetiology","aetiology of"]""", RelatedTermsJson = """["pathophysiology","risk factor","precipitating factor"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-012", Term = "pyrexia", Definition = "Fever; an abnormally high body temperature, typically above 38°C.", ExampleSentence = "The child presented with pyrexia of unknown origin for five days.", ContextNotes = "Formal clinical term; 'fever' is acceptable in patient-facing communication.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["fever","febrile","elevated temperature"]""", CollocationsJson = """["pyrexia of unknown origin","low-grade pyrexia","high pyrexia"]""", RelatedTermsJson = """["hyperpyrexia","hypothermia","sepsis"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-013", Term = "diuresis", Definition = "Increased or excessive production of urine.", ExampleSentence = "Forced diuresis was initiated to manage the patient's fluid overload.", ContextNotes = "Often used in renal and cardiac nursing contexts.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["urine output","polyuria"]""", CollocationsJson = """["forced diuresis","osmotic diuresis","inadequate diuresis"]""", RelatedTermsJson = """["oliguria","anuria","fluid balance"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-014", Term = "ambulate", Definition = "To walk or be able to walk; to move around under one's own power.", ExampleSentence = "The physiotherapist documented that the patient could ambulate 20 metres with a walking frame.", ContextNotes = "Common in rehabilitation and nursing notes.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["walk","mobilise","move independently"]""", CollocationsJson = """["ambulate independently","ambulate with assistance","unable to ambulate"]""", RelatedTermsJson = """["mobilisation","gait","transfers"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-015", Term = "haemorrhage", Definition = "Escape of blood from a ruptured blood vessel; heavy bleeding.", ExampleSentence = "Emergency surgery was required due to post-partum haemorrhage.", ContextNotes = "Australian/UK spelling; US spelling is 'hemorrhage'.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["bleeding","hemorrhage","blood loss"]""", CollocationsJson = """["postpartum haemorrhage","intracranial haemorrhage","subarachnoid haemorrhage","major haemorrhage"]""", RelatedTermsJson = """["coagulopathy","transfusion","haemostasis"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-016", Term = "nausea", Definition = "A feeling of sickness with an inclination to vomit.", ExampleSentence = "The patient reported persistent nausea and one episode of vomiting following chemotherapy.", ContextNotes = "Distinguish from vomiting (emesis); both may need to be reported in OET writing tasks.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["queasiness","sickness","feeling sick"]""", CollocationsJson = """["nausea and vomiting","persistent nausea","intractable nausea","post-operative nausea"]""", RelatedTermsJson = """["emesis","antiemetic","dyspepsia"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-017", Term = "aspiration", Definition = "The inhalation of food, liquid, or foreign material into the airway; or the act of drawing fluid out of a body cavity.", ExampleSentence = "The patient was at high risk of aspiration due to impaired swallowing function.", ContextNotes = "Dual meaning — context determines which sense is intended.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["inhalation","silent aspiration"]""", CollocationsJson = """["aspiration risk","aspiration pneumonia","silent aspiration","aspiration of fluid"]""", RelatedTermsJson = """["dysphagia","choking","pneumonia"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-018", Term = "arrhythmia", Definition = "An irregular or abnormal heart rhythm.", ExampleSentence = "The ECG confirmed a new arrhythmia that required urgent cardiology review.", ContextNotes = "Umbrella term encompassing tachycardia, bradycardia, fibrillation, etc.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["dysrhythmia","irregular heartbeat","cardiac dysrhythmia"]""", CollocationsJson = """["cardiac arrhythmia","arrhythmia management","ventricular arrhythmia","atrial arrhythmia"]""", RelatedTermsJson = """["atrial fibrillation","tachycardia","bradycardia","ECG"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-019", Term = "benign", Definition = "Not malignant; (of a tumour) not cancerous and not tending to spread.", ExampleSentence = "The biopsy results confirmed the lesion was benign.", ContextNotes = "Contrast with 'malignant' — critical distinction in OET writing referral letters.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["non-cancerous","non-malignant","harmless"]""", CollocationsJson = """["benign tumour","benign lesion","benign condition","benign prostatic hyperplasia"]""", RelatedTermsJson = """["malignant","carcinoma","neoplasm"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-020", Term = "prophylaxis", Definition = "Treatment given to prevent disease rather than to treat an existing disease.", ExampleSentence = "The patient was commenced on antibiotic prophylaxis prior to the dental procedure.", ContextNotes = "Common in OET writing for surgical and high-risk patient referrals.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["prevention","preventive treatment","precaution"]""", CollocationsJson = """["antibiotic prophylaxis","DVT prophylaxis","primary prophylaxis","post-exposure prophylaxis"]""", RelatedTermsJson = """["indication","contraindication","treatment"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-021", Term = "iatrogenic", Definition = "Caused by medical examination or treatment.", ExampleSentence = "The iatrogenic pneumothorax occurred following central line insertion.", ContextNotes = "Important term for documenting adverse events in clinical notes.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["treatment-induced","medically caused"]""", CollocationsJson = """["iatrogenic injury","iatrogenic complication","iatrogenic disease"]""", RelatedTermsJson = """["adverse event","complication","harm"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-022", Term = "titrate", Definition = "To adjust the dose of a drug to the required level in a measured way.", ExampleSentence = "The opioid dose was titrated upwards according to the patient's pain response.", ContextNotes = "Frequently used in pain management and ICU nursing documentation.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["adjust","dose-adjust","calibrate"]""", CollocationsJson = """["titrate to effect","titrate dose","up-titrate","down-titrate"]""", RelatedTermsJson = """["analgesic","dosing","therapeutic range"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-023", Term = "triage", Definition = "The process of determining the priority of patients' treatment based on urgency of need.", ExampleSentence = "The emergency department nurse triaged the patient as category 2 (emergency).", ContextNotes = "Common in emergency nursing OET speaking and writing tasks.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["priority assessment","sorting"]""", CollocationsJson = """["emergency triage","triage category","triage nurse","triage assessment"]""", RelatedTermsJson = """["primary survey","ABCDE assessment","acuity"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-024", Term = "consent", Definition = "Permission granted for medical treatment or procedure, especially informed consent.", ExampleSentence = "Written informed consent was obtained before the procedure.", ContextNotes = "Critical ethical and legal concept in OET healthcare scenarios.", ExamTypeCode = "oet", Category = "clinical_communication", SynonymsJson = """["agreement","permission","authorisation"]""", CollocationsJson = """["informed consent","written consent","consent form","capacity to consent","consent obtained"]""", RelatedTermsJson = """["capacity","autonomy","refusal"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-025", Term = "idiopathic", Definition = "Relating to a disease or condition that arises spontaneously and for which no cause is known.", ExampleSentence = "The diagnosis was idiopathic pulmonary fibrosis.", ContextNotes = "Use when no identifiable cause has been determined despite investigation.", ExamTypeCode = "oet", Category = "medical", SynonymsJson = """["unknown cause","of unknown origin"]""", CollocationsJson = """["idiopathic disease","idiopathic pulmonary fibrosis","idiopathic hypertension"]""", RelatedTermsJson = """["aetiology","primary","secondary"]""", Status = "active" }
        );

        // Extend with the full OET medical vocabulary bank while keeping the
        // original canonical demo terms stable and avoiding duplicate terms.
        var seededKeys = db.ChangeTracker
            .Entries<VocabularyTerm>()
            .Select(entry => VocabularySeedKey(entry.Entity.Term, entry.Entity.ExamTypeCode, entry.Entity.ProfessionId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        db.VocabularyTerms.AddRange(
            BuildOetVocabularyBank()
                .Where(term => seededKeys.Add(VocabularySeedKey(term.Term, term.ExamTypeCode, term.ProfessionId))));
    }

    private static void SeedForumCategories(LearnerDbContext db)
    {
        db.ForumCategories.AddRange(
            new ForumCategory { Id = "fcat-001", ExamTypeCode = null, Name = "General Discussion", Description = "General conversations about studying, test preparation strategies, and learner experiences.", SortOrder = 10, Status = "active" },
            new ForumCategory { Id = "fcat-002", ExamTypeCode = "oet", Name = "OET Writing", Description = "Discuss OET Writing tasks, referral letter structures, and get peer feedback.", SortOrder = 20, Status = "active" },
            new ForumCategory { Id = "fcat-003", ExamTypeCode = "oet", Name = "OET Speaking", Description = "OET Speaking roleplays, scenario tips, and fluency improvement strategies.", SortOrder = 30, Status = "active" },
            new ForumCategory { Id = "fcat-004", ExamTypeCode = "oet", Name = "OET Reading", Description = "Techniques for OET Reading, time management, and comprehension strategies.", SortOrder = 40, Status = "active" },
            new ForumCategory { Id = "fcat-005", ExamTypeCode = "oet", Name = "OET Listening", Description = "Tips and resources for the OET Listening subtest.", SortOrder = 50, Status = "active" },
            new ForumCategory { Id = "fcat-006", ExamTypeCode = null, Name = "Study Groups", Description = "Find and organise study partners and virtual study groups.", SortOrder = 60, Status = "active" },
            new ForumCategory { Id = "fcat-007", ExamTypeCode = null, Name = "Exam Experiences", Description = "Share your exam day experiences, scores, and re-sit strategies.", SortOrder = 70, Status = "active" },
            new ForumCategory { Id = "fcat-008", ExamTypeCode = null, Name = "Resources & Tools", Description = "Share helpful resources, books, videos, and preparation tools.", SortOrder = 80, Status = "active" },
            new ForumCategory { Id = "fcat-009", ExamTypeCode = null, Name = "Ask a Tutor", Description = "Post your OET preparation questions and get verified answers from certified tutor reviewers.", SortOrder = 5, Status = "active" }
        );
    }

    private static void SeedPronunciationDrills(LearnerDbContext db)
    {
        // ─────────────────────────────────────────────────────────────────────
        // 60+ pronunciation drills across 4 pillars × 7 professions where
        // meaningful. Every drill ships with:
        //   - A `PrimaryRuleId` referencing /rulebooks/pronunciation/<profession>/rulebook.v1.json
        //   - Profession tag ("all" for phoneme drills, "medicine"/"nursing"/… for targeted ones)
        //   - Focus category: phoneme | cluster | stress | intonation | prosody
        //   - Difficulty: easy | medium | hard
        // The content-team can edit freely in /admin/pronunciation; the seed
        // exists so fresh databases have ~60 publishable drills from day one.
        // ─────────────────────────────────────────────────────────────────────
        var drills = new List<PronunciationDrill>();

        PronunciationDrill D(string id, string phoneme, string label, string profession, string focus,
            string primaryRuleId, string difficulty, string exampleWords, string minimalPairs,
            string sentences, string tipsHtml, int order = 0) => new()
        {
            Id = id,
            TargetPhoneme = phoneme,
            Label = label,
            Profession = profession,
            Focus = focus,
            PrimaryRuleId = primaryRuleId,
            Difficulty = difficulty,
            ExampleWordsJson = exampleWords,
            MinimalPairsJson = minimalPairs,
            SentencesJson = sentences,
            TipsHtml = tipsHtml,
            Status = "active",
            OrderIndex = order,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // ── Phoneme pack (20 drills) ─────────────────────────────────────────
        drills.Add(D("pd-001", "\u03B8", "th (voiceless) - as in 'think'", "all", "phoneme", "P01.1", "medium",
            """["think","therapy","three","breath","tooth","both","method","author","thyroid","pathology"]""",
            """[{"a":"think","b":"sink"},{"a":"three","b":"free"},{"a":"bath","b":"bass"},{"a":"thin","b":"tin"}]""",
            """["The therapist recommended three therapeutic exercises.","Breathe through your mouth during the assessment.","The pathologist confirmed the diagnosis on Thursday."]""",
            "<p>Place the tip of your tongue lightly between your upper and lower front teeth. Blow air through gently - do <strong>not</strong> voice this sound.</p>", 1));

        drills.Add(D("pd-002", "\u00F0", "th (voiced) - as in 'this'", "all", "phoneme", "P01.2", "medium",
            """["this","the","that","breathe","soothe","other","mother","whether","smoothly","rhythm"]""",
            """[{"a":"this","b":"miss"},{"a":"then","b":"den"},{"a":"breathe","b":"breed"},{"a":"they","b":"day"}]""",
            """["Breathe in deeply through the nose.","The other doctor confirmed the diagnosis.","Although the patient denied symptoms, further investigation was warranted."]""",
            "<p>Same tongue position as voiceless th, but <strong>add voice</strong> by vibrating your vocal cords. You should feel vibration in your throat.</p>", 2));

        drills.Add(D("pd-003", "v", "v — as in 'vital'", "all", "phoneme", "P01.3", "easy",
            """["vital","valve","intravenous","invasive","vomit","vast","fever","verbal","evaluate","previous"]""",
            """[{"a":"vein","b":"wane"},{"a":"vine","b":"wine"},{"a":"vest","b":"west"},{"a":"very","b":"berry"}]""",
            """["Vital signs were stable on arrival.","The vascular surgeon reviewed the patient.","Intravenous fluids were administered at a very slow rate."]""",
            "<p>Lightly bite your lower lip with your upper teeth, then push air through while vibrating your vocal cords. Avoid closing the lips completely.</p>", 3));

        drills.Add(D("pd-004", "w", "w — as in 'wound'", "all", "phoneme", "P01.4", "easy",
            """["wound","ward","weight","swallow","withdraw","weakness","well-being","away","warm","worry"]""",
            """[{"a":"wet","b":"vet"},{"a":"wine","b":"vine"},{"a":"west","b":"vest"},{"a":"while","b":"vile"}]""",
            """["The wound was well-healed at the two-week review.","The patient's weight had reduced significantly.","Withdrawal of treatment was discussed with the family."]""",
            "<p>Round your lips tightly into a small circle, then open them quickly while pushing air out and voicing. Do not use your teeth.</p>", 4));

        drills.Add(D("pd-005", "ɪ", "Short i — as in 'symptom'", "all", "phoneme", "P02.1", "easy",
            """["symptom","physical","clinic","insulin","infusion","intravenous","inhibitor","risk","limit","rigid"]""",
            """[{"a":"bit","b":"beat"},{"a":"sit","b":"seat"},{"a":"ship","b":"sheep"},{"a":"fill","b":"feel"}]""",
            """["The clinical symptoms included intermittent nausea.","Physical examination findings were significant.","The patient was given insulin via infusion."]""",
            "<p>This is a short, relaxed vowel. The tongue is high and forward, but <strong>more relaxed</strong> than the long 'ee' (iː) sound. Keep it brief.</p>", 5));

        drills.Add(D("pd-006", "æ", "Short a (trap vowel) — as in 'catheter'", "all", "phoneme", "P02.3", "medium",
            """["catheter","analgesia","abdominal","fracture","clamp","traction","anaphylaxis","anaemia","allergy","cannula"]""",
            """[{"a":"bad","b":"bed"},{"a":"band","b":"bend"},{"a":"can","b":"ken"},{"a":"mass","b":"mess"}]""",
            """["A nasogastric catheter was inserted.","The abdominal examination was unremarkable.","Anaphylaxis protocol was activated immediately."]""",
            "<p>Open your jaw wide, spread your lips, and position your tongue low and forward. This is the 'flat' a sound — do not let it sound like 'eh'.</p>", 6));

        drills.Add(D("pd-007", "ɜː", "er vowel — as in 'nurse'", "all", "phoneme", "P02.4", "medium",
            """["nurse","word","alert","observed","referred","further","concerns","determinant","burn","worse"]""",
            """[{"a":"word","b":"ward"},{"a":"nurse","b":"Norse"},{"a":"hurt","b":"heart"},{"a":"stern","b":"stain"}]""",
            """["The nurse observed the patient throughout the turn.","Further assessment was required.","The patient was referred for urgent evaluation."]""",
            "<p>This is a mid-central vowel. Your tongue should be in the middle of your mouth, relaxed. In Australian and British English, this vowel is <strong>non-rhotic</strong> — do not pronounce the 'r'.</p>", 7));

        drills.Add(D("pd-011", "iː", "Long ee — as in 'fever'", "all", "phoneme", "P02.2", "easy",
            """["fever","severe","anaemia","paediatric","seizure","relieve","need","meet","clean","see"]""",
            """[{"a":"fever","b":"favour"},{"a":"need","b":"nod"},{"a":"seat","b":"sit"}]""",
            """["A low-grade fever persisted for three days.","The paediatric team was consulted immediately.","Her symptoms were relieved with simple analgesia."]""",
            "<p>A long, tense high-front vowel. Smile slightly; keep the tongue high and forward. Hold the sound longer than the short /ɪ/.</p>", 11));

        drills.Add(D("pd-012", "ʌ", "strut vowel — as in 'blood'", "all", "phoneme", "P02.5", "medium",
            """["blood","gut","lung","onset","suffer","numb","cough","tough","mother","under"]""",
            """[{"a":"cut","b":"cat"},{"a":"luck","b":"lock"}]""",
            """["The sudden onset of chest pain required urgent assessment.","She was suffering from chronic gut discomfort.","A tough course of antibiotics was prescribed."]""",
            "<p>Open the jaw moderately, tongue central and low. A short, relaxed sound — not the 'o' of 'hot'.</p>", 12));

        drills.Add(D("pd-013", "ə", "schwa — reduced vowel in long words", "all", "phoneme", "P02.6", "medium",
            """["doctor","hospital","patient","surgeon","medicine","cardiac","therapy","suffer","alert","signal"]""",
            "[]",
            """["The doctor reassured the patient before the procedure.","Each hospital follows its own admission protocol.","A cardiac monitor was attached on arrival."]""",
            "<p>The most common English vowel. Occurs in <strong>unstressed</strong> syllables — relax the mouth and produce a short neutral 'uh'. Failing to reduce unstressed vowels makes speech sound unnatural.</p>", 13));

        drills.Add(D("pd-014", "r vs l", "r and l contrast", "all", "phoneme", "P01.5", "hard",
            """["rash","lesion","right","light","rate","late","rashes","lateral","referred","reported"]""",
            """[{"a":"rash","b":"lash"},{"a":"rate","b":"late"},{"a":"right","b":"light"},{"a":"fresh","b":"flesh"}]""",
            """["A widespread rash was observed on the lateral aspect of the arm.","She reported the light-headedness began last night.","He was referred to the right-sided specialist clinic."]""",
            "<p><strong>/r/</strong>: tongue tip curls toward the roof of the mouth but never taps. <strong>/l/</strong>: tongue tip touches the ridge behind the upper teeth and stays there.</p>", 14));

        drills.Add(D("pd-015", "p/t/k", "Aspirated voiceless stops", "all", "phoneme", "P01.6", "medium",
            """["penicillin","paracetamol","potassium","tramadol","ketamine","cannula","tablet","tumour","patient","tense"]""",
            "[]",
            """["Paracetamol was prescribed for the pain.","The patient reported a painful tense abdomen.","A small tumour was identified on imaging."]""",
            "<p>In word-initial positions, English /p/ /t/ /k/ are <strong>aspirated</strong> — a small puff of air follows. Hold a tissue near your mouth; it should flutter on 'pin' but not on 'spin'.</p>", 15));

        drills.Add(D("pd-016", "ŋ", "ng — as in 'lung'", "all", "phoneme", "P01.8", "easy",
            """["lung","ringing","swelling","breathing","bleeding","coughing","tingling","rounding"]""",
            """[{"a":"sing","b":"sin"},{"a":"ring","b":"rim"}]""",
            """["Shortness of breath on lung examination was noted.","The patient reported ongoing swelling of the ankle.","Coughing worsened at night."]""",
            "<p>Back of the tongue touches the soft palate; air passes through the nose. Do NOT add a separate 'g' at the end.</p>", 16));

        drills.Add(D("pd-017", "ʊ vs uː", "FOOT vs GOOSE", "all", "phoneme", "P02.7", "medium",
            """["full","fool","good","food","look","soup","book","root","put","cool"]""",
            """[{"a":"full","b":"fool"},{"a":"pull","b":"pool"},{"a":"good","b":"gooed"}]""",
            """["A full course of treatment was completed.","The patient refused food post-operatively.","Please look at the dosing schedule closely."]""",
            "<p>/ʊ/ is short and relaxed ('good'); /uː/ is long and tense ('food'). Do not merge them.</p>", 17));

        drills.Add(D("pd-018", "ʧ", "ch — as in 'chest'", "all", "phoneme", "P01.6", "easy",
            """["chest","cheek","choke","chart","reach","inch","nature","picture"]""",
            """[{"a":"chest","b":"jest"},{"a":"cheap","b":"jeep"}]""",
            """["Chest pain radiated to the left arm.","A chart review showed recent abnormal results.","Please reach for the emergency button if needed."]""",
            "<p>Voiceless affricate: begin with a /t/ closure, then release into /ʃ/. Lips slightly rounded.</p>", 18));

        drills.Add(D("pd-019", "ʒ", "zh — as in 'measure'", "all", "phoneme", "P01.2", "hard",
            """["measure","pleasure","vision","decision","casual","usual","treasure"]""",
            "[]",
            """["Careful measurement of the wound was recorded.","The final decision rested with the consultant.","Usual treatment was ineffective in this case."]""",
            "<p>Voiced counterpart of /ʃ/ (as in 'shoe'). Lips slightly protrude; tongue raised to roof of mouth, with voicing.</p>", 19));

        drills.Add(D("pd-020", "ʃ", "sh — as in 'shot'", "all", "phoneme", "P01.6", "easy",
            """["shot","should","sharp","shock","tissue","pressure","infection","session"]""",
            """[{"a":"shot","b":"sot"},{"a":"sheep","b":"seep"}]""",
            """["A tetanus shot was administered.","A sharp pain occurred with deep inspiration.","The pressure ulcer required daily dressing."]""",
            "<p>Voiceless fricative: lips slightly rounded, tongue raised behind the alveolar ridge. No voicing.</p>", 20));

        // Final consonants
        drills.Add(D("pd-021", "final-consonants", "Word-final consonants — must not drop", "all", "phoneme", "P01.7", "hard",
            """["heart","chest","breath","arrest","discharge","admit","referred","patient","prompt","impact"]""",
            """[{"a":"heart","b":"hear"},{"a":"chest","b":"ches"}]""",
            """["The patient was admitted for chest pain.","An arrest team was activated promptly.","The discharge summary was sent to the GP."]""",
            "<p>Final /t/ /d/ /s/ /z/ /k/ carry meaning in medical English. Finish every word fully.</p>", 21));

        // ── Consonant clusters (6 drills) ───────────────────────────────────
        drills.Add(D("pd-030", "spr/str/spl/skr", "3-consonant initial clusters", "all", "cluster", "P03.1", "hard",
            """["stroke","strain","spleen","splint","splash","stress","screen","script","strict","street"]""",
            "[]",
            """["An acute stroke was diagnosed on imaging.","The patient's spleen was enlarged.","A strict low-salt diet was advised."]""",
            "<p>Do not insert a vowel between the consonants. 'stroke' is one syllable, NOT 'su-tro-ke'. Practise slowly, then at speed.</p>", 30));

        drills.Add(D("pd-031", "kt/pt/kst", "Word-final consonant clusters", "all", "cluster", "P03.2", "medium",
            """["infect","impact","script","concept","prompt","text","context","fact","exact","act"]""",
            "[]",
            """["A prompt referral was made.","The clinical context was carefully considered.","An infection control plan was enacted."]""",
            "<p>Release every final consonant. Many learners drop the last stop — an infected vs an infect matters clinically.</p>", 31));

        drills.Add(D("pd-032", "nt/nd/ns", "Nasal + stop clusters in past tense", "all", "cluster", "P03.3", "medium",
            """["examined","assessed","consulted","scanned","referred","admitted","discharged","prescribed"]""",
            "[]",
            """["The patient was examined and promptly admitted.","A CT was performed and the results were discussed.","The GP was consulted before discharge."]""",
            "<p>Past-tense /-d/ only adds a syllable when the stem ends in /t/ or /d/ ('admitted'). Otherwise it's a single cluster: 'examined' = /ɪɡˈzæmɪnd/, NOT /ɪɡˈzæmɪnɪd/.</p>", 32));

        drills.Add(D("pd-033", "dr/tr", "dr/tr clusters in clinical words", "all", "cluster", "P03.1", "easy",
            """["drip","drug","drop","dressing","trauma","trial","tract","trolley"]""",
            "[]",
            """["A saline drip was started.","The trauma team was paged immediately.","The urinary tract was clear on imaging."]""",
            "<p>Start /d/ or /t/ with the tongue already near the roof of the mouth, then glide into /r/. No pause between consonants.</p>", 33));

        drills.Add(D("pd-034", "sk/sp/st", "s-stop clusters", "all", "cluster", "P03.1", "easy",
            """["scan","scope","stent","step","stop","spasm","sputum","stable"]""",
            "[]",
            """["A CT scan was requested.","The stent was placed under sedation.","Stable observations were maintained overnight."]""",
            "<p>Start with /s/ friction, move smoothly into the stop. Do not add a vowel before /s/.</p>", 34));

        drills.Add(D("pd-035", "ks/kts", "-tics / -tics clusters", "medicine", "cluster", "P03.2", "hard",
            """["optics","antibiotics","paediatrics","genetics","dynamics","statistics"]""",
            "[]",
            """["Antibiotics were empirically prescribed.","Paediatrics was consulted for the child's fever.","Genetics review was arranged."]""",
            "<p>Suffix -tics is /tɪks/ — a full syllable. Stress falls on the syllable before: antiBIOtics, paediATRics.</p>", 35));

        // ── Word stress pack (15 drills) ────────────────────────────────────
        drills.Add(D("pd-040", "stress", "Penultimate stress: -tion / -sion / -cian", "all", "stress", "P04.2", "medium",
            """["examiNAtion","interVENtion","phySIcian","conDItion","inFECtion","operAtion","susPIcion","preSCRIPtion","progresSION","conSULtaTION"]""",
            "[]",
            """["A thorough physical examination was performed.","The intervention was tolerated well.","The condition resolved after treatment."]""",
            "<p>Words ending -tion, -sion, -cian, -cious stress the syllable <strong>immediately before</strong> the suffix. Say the stressed syllable louder, longer, and higher.</p>", 40));

        drills.Add(D("pd-041", "stress", "Antepenultimate stress: -ology / -ography", "all", "stress", "P04.1", "hard",
            """["paTHOLogy","carDIology","raDIology","neuROLogy","epidemiOLogy","gastroenterOLogy"]""",
            "[]",
            """["Pathology was consulted for specimen analysis.","Cardiology review was arranged.","Neurology performed the full workup."]""",
            "<p>Words ending -ology, -ologist, -ography stress the syllable <strong>three from the end</strong>. pathOLogy, NOT patholOgy.</p>", 41));

        drills.Add(D("pd-042", "stress", "Penultimate stress: -ic / -ical / -ity", "all", "stress", "P04.3", "medium",
            """["spe-CI-fic","cli-NI-cal","se-VE-ri-ty","mor-BI-di-ty","mor-TA-li-ty","a-CU-i-ty","CHRO-nic"]""",
            "[]",
            """["The specific cause remained unclear.","Clinical findings supported the diagnosis.","Severity was graded using a validated tool."]""",
            "<p>Suffixes -ic / -ical / -ity pull stress onto the syllable just before them: speCIfic, cliNIcal, seVErity.</p>", 42));

        drills.Add(D("pd-043", "stress", "Drug-name stress — common generics", "pharmacy", "stress", "P04.4", "hard",
            """["PA-racetamol","I-buprofen","MOR-phine","IN-sulin","TRA-madol","WAR-farin","MET-formin","a-MO-xi-ci-llin"]""",
            "[]",
            """["Paracetamol and ibuprofen were prescribed for pain relief.","Morphine was titrated to effect.","Warfarin was adjusted according to the INR."]""",
            "<p>Most generic drug names stress the first syllable. Amoxicillin is an exception — stress falls on -CIL-.</p>", 43));

        drills.Add(D("pd-044", "stress", "Medical Latin stress — -itis / -osis", "all", "stress", "P08.1", "medium",
            """["arthri-TIS","bronchi-TIS","tendini-TIS","cirrho-SIS","psycho-SIS","stenos-IS","dermati-TIS"]""",
            "[]",
            """["Rheumatoid arthritis flared following the infection.","Acute bronchitis was diagnosed.","Liver cirrhosis had progressed despite therapy."]""",
            "<p>Conditions ending -itis or -osis stress that suffix. The pattern carries from Latin/Greek roots.</p>", 44));

        drills.Add(D("pd-045", "stress", "Noun/verb stress shift", "all", "stress", "P04.5", "medium",
            """["REcord/reCORD","INcrease/inCREASE","DIScharge/disCHARGE","CONduct/conDUCT","SUBject/subJECT","OBject/obJECT"]""",
            "[]",
            """["The patient's record was updated.","Please record the findings in the notes.","She was discharged on the third day."]""",
            "<p>Two-syllable words function as noun (stress first) or verb (stress second). 'The DIScharge was unremarkable' vs 'She was disCHARGED yesterday'.</p>", 45));

        // Nursing-specific
        drills.Add(D("pd-046", "stress", "Nursing handover vocabulary stress", "nursing", "stress", "P04.1", "medium",
            """["meDIcation","observAtion","adMINistrAtion","PREscription","docuMENtATION","ID-n-ti-fi-CA-tion"]""",
            "[]",
            """["The medication round was completed on time.","Frequent observation was maintained overnight.","Documentation was updated in the progress notes."]""",
            "<p>Polysyllabic nursing vocabulary needs clear primary + secondary stress. Mark the strong beats when practising.</p>", 46));

        // ── Intonation pack (10 drills) ─────────────────────────────────────
        drills.Add(D("pd-050", "intonation", "Rising intonation — yes/no questions", "all", "intonation", "P06.1", "medium",
            "[]", "[]",
            """["Are you experiencing any chest pain?","Have you taken your medication today?","Do you have any allergies?","Would you like me to explain the procedure?"]""",
            "<p>Yes/no questions rise on the final stressed syllable. Rising intonation signals you are waiting for an answer.</p>", 50));

        drills.Add(D("pd-051", "intonation", "Falling intonation — wh-questions", "all", "intonation", "P06.2", "medium",
            "[]", "[]",
            """["What brings you in today?","Where is the pain located?","How long have you felt unwell?","When did the symptoms start?"]""",
            "<p>Wh-questions take a decisive fall on the final stressed content word.</p>", 51));

        drills.Add(D("pd-052", "intonation", "Falling intonation — clinical instructions", "all", "intonation", "P06.3", "medium",
            "[]", "[]",
            """["You should take this twice a day.","We will admit you for observation.","Please avoid alcohol while on this medication.","I recommend we proceed with the investigation."]""",
            "<p>Clinical instructions require falling intonation to convey authority. Rising makes you sound tentative.</p>", 52));

        drills.Add(D("pd-053", "intonation", "Reassurance — rise then fall", "all", "intonation", "P06.4", "hard",
            "[]", "[]",
            """["This is going to be completely fine.","You are in safe hands.","We will take good care of you.","There's no need to worry about this result."]""",
            "<p>Reassuring a patient uses a gentle rise-then-fall. Monotone reassurance sounds insincere.</p>", 53));

        drills.Add(D("pd-054", "intonation", "Listing intonation — rise until last", "all", "intonation", "P06.5", "medium",
            "[]", "[]",
            """["We will order an ECG, a chest X-ray, and blood tests.","Common side effects include nausea, dizziness, and fatigue.","The pain is sharp, constant, and radiates to the back."]""",
            "<p>Each item rises; the last falls. Signals 'I am completing a list'.</p>", 54));

        // ── Prosody / rhythm ────────────────────────────────────────────────
        drills.Add(D("pd-060", "prosody", "Stressed content, reduced function words", "all", "prosody", "P05.1", "hard",
            "[]", "[]",
            """["She has been referred to the specialist.","I'd like to check your blood pressure now.","The results of the tests are back.","We can arrange a follow-up next week."]""",
            "<p>Content words (nouns, main verbs) are long and loud. Function words (is, the, to, for, of) reduce to schwa. Avoid machine-like word-by-word delivery.</p>", 60));

        drills.Add(D("pd-061", "prosody", "Pausing at clause boundaries", "all", "prosody", "P05.2", "medium",
            "[]", "[]",
            """["After reviewing the results, | we have decided to adjust your medication.","If the symptoms persist, | please return to the clinic.","As you know, | your blood pressure has been elevated."]""",
            "<p>Pause at commas and clause boundaries — not mid-clause. Pauses aid intelligibility; mid-clause hesitation harms fluency scoring.</p>", 61));

        drills.Add(D("pd-062", "prosody", "Linking — final consonant to next vowel", "all", "prosody", "P07.1", "medium",
            "[]", "[]",
            """["Take_it three times_a day.","I'd_like to check_on the wound.","Not_at_all — please continue_as_instructed."]""",
            "<p>Link a word-final consonant directly into the next word's initial vowel. Never chop words apart.</p>", 62));

        // ── Profession-specific drills ─────────────────────────────────────
        drills.Add(D("pd-070", "medication", "Medication names — pharmacy pack", "pharmacy", "stress", "P04.4", "hard",
            """["amoxiCILlin","PARAcetamol","proPRAnolol","saBUtamol","IBuprofen","CEFtriaxone","OMEprazole","diAZepam"]""",
            "[]",
            """["Amoxicillin 500 mg three times daily was prescribed.","Paracetamol is considered first-line for simple analgesia.","Propranolol may be considered for long-term control."]""",
            "<p>Generic drug names carry idiosyncratic stress. Practise the stress pattern of each drug you dispense every day.</p>", 70));

        drills.Add(D("pd-071", "handover", "Nursing handover rhythm", "nursing", "prosody", "P05.2", "hard",
            "[]", "[]",
            """["Situation: | 67-year-old male, | day two post-op.","Background: | elective cholecystectomy, | no complications overnight.","Assessment: | vitals stable, | pain well-controlled on regular paracetamol.","Recommendation: | continue current plan, | mobilise as tolerated."]""",
            "<p>SBAR handover benefits from measured pace, clear pauses, and stress on the key clinical content. Do not rush.</p>", 71));

        drills.Add(D("pd-072", "dental", "Dental terminology", "dentistry", "phoneme", "P08.1", "medium",
            """["cavity","filling","crown","extraction","anaesthetic","periodontal","molar","gingivitis"]""",
            "[]",
            """["A cavity was identified on the upper left molar.","Anaesthetic was administered before the extraction.","Gingivitis management was discussed."]""",
            "<p>Dental words are often of Latin origin — watch for the stress patterns of -itis and -ontal endings.</p>", 72));

        drills.Add(D("pd-073", "physio", "Physiotherapy terminology", "physiotherapy", "phoneme", "P01.3", "medium",
            """["mobility","strain","sprain","rehabilitation","flexion","extension","physiotherapy","gait"]""",
            "[]",
            """["Mobility was assessed using a validated tool.","A programme of rehabilitation was commenced.","Gait analysis revealed mild left-sided weakness."]""",
            "<p>Physiotherapy vocabulary leans heavily on Latin anatomical roots. Take care to pronounce the 'th' in 'physiotherapy' correctly.</p>", 73));

        drills.Add(D("pd-074", "speech", "Speech pathology terminology", "speech-pathology", "phoneme", "P01.1", "hard",
            """["articulation","dysphagia","aphasia","phonology","larynx","voicing","pitch","resonance"]""",
            "[]",
            """["Articulation therapy targeted fricative consonants.","Dysphagia was assessed using a modified barium swallow.","Phonological awareness was the focus of week-two sessions."]""",
            "<p>Many speech-pathology terms contain the very sounds the learner is practising — be especially careful with /θ/, /ʃ/, /dʒ/.</p>", 74));

        drills.Add(D("pd-075", "ot", "Occupational therapy terminology", "occupational-therapy", "phoneme", "P01.1", "medium",
            """["function","dexterity","assistive","adaptation","grading","cognition","sensory","activities"]""",
            "[]",
            """["Fine motor dexterity was below age-matched norms.","Assistive equipment was trialled in the home.","Activities of daily living were graded in complexity."]""",
            "<p>Watch the schwa reduction in unstressed syllables: 'funcTION' = /ˈfʌŋkʃən/, not /ˈfʌŋkʃɒn/.</p>", 75));

        db.PronunciationDrills.AddRange(drills);
    }

    private static byte[] BuildDemoWaveFile()
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const double durationSeconds = 1.2;
        const double frequencyHz = 440;
        const double amplitude = 0.2;

        var sampleCount = (int)(sampleRate * durationSeconds);
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataLength = sampleCount * blockAlign;

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * index / sampleRate) * short.MaxValue * amplitude);
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
