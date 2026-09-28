using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    private static void SeedReferenceData(LearnerDbContext db)
    {
        db.Professions.AddRange(
            new ProfessionReference { Id = "nursing", Code = "nursing", Label = "Nursing", Status = "active", SortOrder = 1 },
            new ProfessionReference { Id = "medicine", Code = "medicine", Label = "Medicine", Status = "active", SortOrder = 2 },
            new ProfessionReference { Id = "dentistry", Code = "dentistry", Label = "Dentistry", Status = "active", SortOrder = 3 },
            new ProfessionReference { Id = "pharmacy", Code = "pharmacy", Label = "Pharmacy", Status = "active", SortOrder = 4 },
            new ProfessionReference { Id = "physiotherapy", Code = "physiotherapy", Label = "Physiotherapy", Status = "active", SortOrder = 5 },
            new ProfessionReference { Id = "radiography", Code = "radiography", Label = "Radiography", Status = "active", SortOrder = 6 },
            // Mirror of SignupProfessionCatalog (the canonical taxonomy) — ids and
            // sort order must stay identical to the catalog seeded below, or the
            // discipline filters that join on Id fall through.
            new ProfessionReference { Id = "other-allied-health", Code = "other-allied-health", Label = "Modified Allied Health Profession", Status = "active", SortOrder = 7 },
            new ProfessionReference { Id = "academic-english", Code = "academic-english", Label = "Academic / General English", Status = "active", SortOrder = 8 }
        );

        db.Subtests.AddRange(
            new SubtestReference { Id = "writing", Code = "writing", Label = "Writing", SupportsProfessionSpecificContent = true },
            new SubtestReference { Id = "speaking", Code = "speaking", Label = "Speaking", SupportsProfessionSpecificContent = true },
            new SubtestReference { Id = "reading", Code = "reading", Label = "Reading", SupportsProfessionSpecificContent = false },
            new SubtestReference { Id = "listening", Code = "listening", Label = "Listening", SupportsProfessionSpecificContent = false }
        );

        db.Criteria.AddRange(
            new CriterionReference { Id = "cri-purpose", SubtestCode = "writing", Code = "purpose", Label = "Purpose", Description = "How clearly the purpose of the letter is conveyed.", Status = "active", SortOrder = 1 },
            new CriterionReference { Id = "cri-content", SubtestCode = "writing", Code = "content", Label = "Content", Description = "Relevance and completeness of clinical content.", Status = "active", SortOrder = 2 },
            new CriterionReference { Id = "cri-conciseness-clarity", SubtestCode = "writing", Code = "conciseness_clarity", Label = "Conciseness & Clarity", Description = "Clarity of writing without unnecessary detail. Scored 0\u20137.", Status = "active", SortOrder = 3 },
            new CriterionReference { Id = "cri-genre-style", SubtestCode = "writing", Code = "genre_style", Label = "Genre & Style", Description = "Appropriate register and professional tone. Scored 0\u20137.", Status = "active", SortOrder = 4 },
            new CriterionReference { Id = "cri-organisation-layout", SubtestCode = "writing", Code = "organisation_layout", Label = "Organisation & Layout", Description = "Logical structure and formatting conventions (address, date, salutation, closure). Scored 0\u20137.", Status = "active", SortOrder = 5 },
            new CriterionReference { Id = "cri-language", SubtestCode = "writing", Code = "language", Label = "Language", Description = "Accuracy and range of grammar and vocabulary.", Status = "active", SortOrder = 6 },
            new CriterionReference { Id = "cri-intelligibility", SubtestCode = "speaking", Code = "intelligibility", Label = "Intelligibility", Description = "Pronunciation, stress, and clarity. Scored 0\u20136.", Status = "active", SortOrder = 1 },
            new CriterionReference { Id = "cri-fluency", SubtestCode = "speaking", Code = "fluency", Label = "Fluency", Description = "Smoothness, pacing, and hesitation control. Scored 0\u20136.", Status = "active", SortOrder = 2 },
            new CriterionReference { Id = "cri-appropriateness", SubtestCode = "speaking", Code = "appropriateness", Label = "Appropriateness of Language", Description = "Suitability of professional vocabulary and tone. Scored 0\u20136.", Status = "active", SortOrder = 3 },
            new CriterionReference { Id = "cri-grammar", SubtestCode = "speaking", Code = "grammar", Label = "Resources of Grammar & Expression", Description = "Range and accuracy of spoken language. Scored 0\u20136.", Status = "active", SortOrder = 4 },
            new CriterionReference { Id = "cri-relationship-building", SubtestCode = "speaking", Code = "relationshipBuilding", Label = "Relationship Building", Description = "Initiating the interaction, attentive/respectful attitude, non-judgemental approach, empathy. Scored 0\u20133.", Status = "active", SortOrder = 5 },
            new CriterionReference { Id = "cri-patient-perspective", SubtestCode = "speaking", Code = "patientPerspective", Label = "Understanding & Incorporating Patient's Perspective", Description = "Eliciting/exploring the patient's ideas, concerns, and expectations; relating explanations back to them. Scored 0\u20133.", Status = "active", SortOrder = 6 },
            new CriterionReference { Id = "cri-providing-structure", SubtestCode = "speaking", Code = "providingStructure", Label = "Providing Structure", Description = "Sequencing purposefully, signposting topic changes, organising explanations. Scored 0\u20133.", Status = "active", SortOrder = 7 },
            new CriterionReference { Id = "cri-information-gathering", SubtestCode = "speaking", Code = "informationGathering", Label = "Information Gathering", Description = "Facilitating narrative, open-then-closed questioning, avoiding compound/leading questions, clarifying, summarising. Scored 0\u20133.", Status = "active", SortOrder = 8 },
            new CriterionReference { Id = "cri-information-giving", SubtestCode = "speaking", Code = "informationGiving", Label = "Information Giving", Description = "Establishing prior knowledge, pausing, encouraging reactions, checking understanding, discovering further needs. Scored 0\u20133.", Status = "active", SortOrder = 9 }
        );

        db.ContentItems.AddRange(
            new ContentItem
            {
                Id = "wt-001",
                ContentType = "writing_task",
                SubtestCode = "writing",
                ProfessionId = "nursing",
                Title = "Discharge Summary - Post-Surgical Patient",
                Difficulty = "medium",
                EstimatedDurationMinutes = 45,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "conciseness", "content" }),
                ScenarioType = "discharge_summary",
                ModeSupportJson = JsonSupport.Serialize(new[] { "practice", "timed" }),
                PublishedRevisionId = "wt-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Patient: Mrs. Eleanor Vance, 72 years old. Admitted for elective right total knee replacement. Provide a discharge summary for the GP covering relevant history, treatment, discharge medications, and follow-up.",
                DetailJson = JsonSupport.Serialize(new
                {
                    prompt = "Write a discharge summary to the patient's GP.",
                    checklist = new[]
                    {
                        "Address the purpose clearly",
                        "Include relevant clinical information only",
                        "Maintain an appropriate professional tone",
                        "Provide clear follow-up actions"
                    }
                }),
                ModelAnswerJson = JsonSupport.Serialize(new
                {
                    paragraphs = new[]
                    {
                        new { id = "p1", text = "Dear Dr Patterson, I am writing to inform you of Mrs Eleanor Vance's recent admission and discharge following a right total knee replacement.", rationale = "States purpose immediately." },
                        new { id = "p2", text = "She was admitted on 3 June 2025 for an elective procedure. Her relevant history includes osteoarthritis, hypertension controlled on amlodipine, and type 2 diabetes managed with metformin.", rationale = "Filters history to what the GP needs." },
                        new { id = "p3", text = "Post-operatively, she mobilised with physiotherapy and was discharged in a stable condition. Please arrange staple removal in 14 days and continue follow-up for glycaemic control.", rationale = "Focuses on ongoing management." }
                    }
                })
            },
            new ContentItem
            {
                Id = "wt-002",
                ContentType = "writing_task",
                SubtestCode = "writing",
                ProfessionId = "nursing",
                Title = "Referral Letter - Cardiology Consultation",
                Difficulty = "hard",
                EstimatedDurationMinutes = 45,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "genre", "language" }),
                ScenarioType = "referral_letter",
                ModeSupportJson = JsonSupport.Serialize(new[] { "practice", "timed" }),
                PublishedRevisionId = "wt-002-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Patient: Mr David Chen, 58 years old. New onset chest pain on exertion. Write a referral letter for cardiology review.",
                DetailJson = JsonSupport.Serialize(new { prompt = "Write a referral letter to a cardiologist." }),
                ModelAnswerJson = JsonSupport.Serialize(new { paragraphs = new[] { new { id = "p1", text = "Dear Cardiologist, I am referring Mr David Chen for assessment of exertional chest pain suggestive of stable angina.", rationale = "Clear referral purpose." } } })
            },
            new ContentItem
            {
                Id = "st-001",
                ContentType = "speaking_task",
                SubtestCode = "speaking",
                ProfessionId = "nursing",
                Title = "Patient Handover - Post-Op Recovery",
                Difficulty = "medium",
                EstimatedDurationMinutes = 20,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "fluency", "appropriateness" }),
                ScenarioType = "handover",
                ModeSupportJson = JsonSupport.Serialize(new[] { "ai", "self", "exam" }),
                PublishedRevisionId = "st-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "You are handing over Mr James Wheeler, day one post right hip replacement, to the incoming nurse.",
                DetailJson = JsonSupport.Serialize(new
                {
                    profession = "Nursing",
                    setting = "Hospital surgical ward",
                    patient = "Mr. James Wheeler, 68, post right hip replacement (day 1)",
                    brief = "Provide a clinical handover.",
                    tasks = new[]
                    {
                        "Summarise the surgery and current condition",
                        "Report pain management and PRN use",
                        "Highlight mobility and DVT prophylaxis",
                        "Communicate outstanding tasks"
                    }
                })
            },
            new ContentItem
            {
                Id = "st-002",
                ContentType = "speaking_task",
                SubtestCode = "speaking",
                ProfessionId = "medicine",
                Title = "Breaking Bad News - Cancer Diagnosis",
                Difficulty = "hard",
                EstimatedDurationMinutes = 20,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "appropriateness", "grammar_expression" }),
                ScenarioType = "consultation",
                ModeSupportJson = JsonSupport.Serialize(new[] { "ai", "self", "exam" }),
                PublishedRevisionId = "st-002-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Inform a patient that a biopsy confirms invasive ductal carcinoma using a calm, empathetic structure.",
                DetailJson = JsonSupport.Serialize(new { profession = "Medicine", setting = "Outpatient room", patient = "Mrs Patricia Collins", brief = "Deliver results using the SPIKES framework." })
            },
            // Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills
            // bank. Drills are ContentItem rows with
            // ContentType = "speaking_drill" and ScenarioType encoding
            // the drill kind (phrasing | intonation | pronunciation |
            // vocabulary | chunking | empathy). They surface in
            // /v1/speaking/drills and feed the §16 16-stage course
            // pathway entry point.
            new ContentItem
            {
                Id = "sd-phrasing-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Empathic phrasing — opening the consultation",
                Difficulty = "easy",
                EstimatedDurationMinutes = 5,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "appropriateness", "relationship_building" }),
                ScenarioType = "phrasing",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-phrasing-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: practise opening the consultation with empathic phrasing.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "phrasing",
                    focus = "Opening rapport-building phrases",
                    promptLines = new[]
                    {
                        "I can see this has been a difficult few days for you.",
                        "Take your time — there's no rush.",
                        "Thank you for sharing that with me."
                    },
                })
            },
            new ContentItem
            {
                Id = "sd-intonation-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Intonation — reassurance vs uncertainty",
                Difficulty = "medium",
                EstimatedDurationMinutes = 5,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "intelligibility", "appropriateness" }),
                ScenarioType = "intonation",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-intonation-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: contrast falling vs rising tones for reassurance and questioning.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "intonation",
                    focus = "Falling tone for confident reassurance; rising tone for checking understanding.",
                })
            },
            new ContentItem
            {
                Id = "sd-pronunciation-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Pronunciation — common medication names",
                Difficulty = "medium",
                EstimatedDurationMinutes = 6,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "intelligibility" }),
                ScenarioType = "pronunciation",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-pronunciation-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: enunciate high-frequency drug names clearly.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "pronunciation",
                    focus = "Stress on the correct syllable for amoxicillin, paracetamol, ibuprofen.",
                })
            },
            new ContentItem
            {
                Id = "sd-vocabulary-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Vocabulary — translating jargon for patients",
                Difficulty = "easy",
                EstimatedDurationMinutes = 5,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "appropriateness", "information_giving" }),
                ScenarioType = "vocabulary",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-vocabulary-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: replace technical jargon with patient-friendly language.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "vocabulary",
                    focus = "Hypertension → high blood pressure; myocardial infarction → heart attack.",
                })
            },
            new ContentItem
            {
                Id = "sd-chunking-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Chunking — pacing complex explanations",
                Difficulty = "medium",
                EstimatedDurationMinutes = 6,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "fluency", "intelligibility" }),
                ScenarioType = "chunking",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-chunking-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: pause between thought groups when explaining a treatment plan.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "chunking",
                    focus = "Group ideas with deliberate micro-pauses (≈300 ms).",
                })
            },
            new ContentItem
            {
                Id = "sd-empathy-001",
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = "Empathy — acknowledging fear and concern",
                Difficulty = "medium",
                EstimatedDurationMinutes = 5,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "relationship_building", "patient_perspective" }),
                ScenarioType = "empathy",
                ModeSupportJson = JsonSupport.Serialize(new[] { "self" }),
                PublishedRevisionId = "sd-empathy-001-r1",
                Status = ContentStatus.Published,
                CaseNotes = "Drill: acknowledge concerns explicitly before redirecting.",
                DetailJson = JsonSupport.Serialize(new
                {
                    drillKind = "empathy",
                    focus = "Name + normalise + invite — \"That sounds frightening — many people in your situation feel the same. Tell me more...\"",
                })
            },
            new ContentItem
            {
                Id = "rt-001",
                ContentType = "reading_task",
                SubtestCode = "reading",
                ProfessionId = null,
                Title = "Health Policy - Hospital-Acquired Infections",
                Difficulty = "medium",
                EstimatedDurationMinutes = 30,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "detail_extraction", "inference" }),
                ScenarioType = "part_c",
                ModeSupportJson = JsonSupport.Serialize(new[] { "practice", "exam" }),
                PublishedRevisionId = "rt-001-r1",
                Status = ContentStatus.Published,
                DetailJson = JsonSupport.Serialize(new
                {
                    part = "C",
                    timeLimitSeconds = 900,
                    texts = new[] { new { id = "rtxt-1", title = "Hospital-Acquired Infections: Prevention Strategies", content = "Hospital-acquired infections remain one of the most significant challenges in modern healthcare..." } },
                    questions = new object[]
                    {
                        new { id = "rq-1", number = 1, text = "What proportion of hospital patients will develop an HAI?", type = "short_answer", options = (string[]?)null, correctAnswer = "approximately 1 in 10", explanation = "The passage states that around one in ten patients develops a hospital-acquired infection." },
                        new { id = "rq-2", number = 2, text = "What does the WHO identify as the most important prevention measure?", type = "short_answer", options = (string[]?)null, correctAnswer = "hand hygiene", explanation = "The WHO identifies hand hygiene as the single most important prevention measure." },
                        new { id = "rq-3", number = 3, text = "What is described as a structured way of improving care processes?", type = "mcq", options = new[] { "Antimicrobial stewardship", "Bundles", "Staff education" }, correctAnswer = "Bundles", explanation = "The text defines bundles as a structured way of improving care processes." }
                    }
                })
            },
            new ContentItem
            {
                Id = "lt-001",
                ContentType = "listening_task",
                SubtestCode = "listening",
                ProfessionId = null,
                Title = "Consultation: Asthma Management Review",
                Difficulty = "medium",
                EstimatedDurationMinutes = 25,
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "detail_capture", "distractor_control" }),
                ScenarioType = "consultation",
                ModeSupportJson = JsonSupport.Serialize(new[] { "practice", "exam" }),
                PublishedRevisionId = "lt-001-r1",
                Status = ContentStatus.Published,
                DetailJson = JsonSupport.Serialize(new
                {
                    audioUrl = "/media/listening/lt-001.mp3",
                    durationSeconds = 240,
                    questions = new object[]
                    {
                        new { id = "lq-1", number = 1, text = "What is the patient's main concern?", type = "mcq", options = new[] { "Increasing breathlessness at night", "Side effects", "Difficulty using inhaler" }, correctAnswer = "Increasing breathlessness at night", explanation = "The patient specifically reports worsening breathlessness overnight.", allowTranscriptReveal = true, transcriptExcerpt = "Patient: I've been waking up at night feeling quite breathless.", distractorExplanation = (string?)null },
                        new { id = "lq-2", number = 2, text = "How often is the reliever inhaler used?", type = "short_answer", options = (string[]?)null, correctAnswer = "3-4 times per week", explanation = "The patient states a frequency of three or four times per week.", allowTranscriptReveal = true, transcriptExcerpt = "Doctor: How often are you using your blue inhaler? Patient: Maybe three or four times a week.", distractorExplanation = "The patient also says sometimes more after walking, which can mislead you into thinking it is daily use." },
                        new { id = "lq-3", number = 3, text = "What treatment change is recommended?", type = "mcq", options = new[] { "Increase preventer dose", "Combination inhaler", "Refer specialist" }, correctAnswer = "Combination inhaler", explanation = "The clinician recommends switching to a combination inhaler.", allowTranscriptReveal = false, transcriptExcerpt = (string?)null, distractorExplanation = (string?)null }
                    }
                })
            }
        );

        // The LiveKit/live-voice rewrite (22 Sep 2026) made every learner
        // Speaking-attempt path (CreateSpeakingAttemptAsync,
        // GetLegacyFreeSpeakingTaskAsync) resolve through a RolePlayCard row,
        // not the bare ContentItem — st-001/st-002 never had one, so any test
        // POSTing /v1/speaking/attempts against them 404'd. Mirrors the real
        // ContentItem's profession/title/case-notes so learner-facing reads
        // stay consistent between the two rows.
        db.RolePlayCards.AddRange(
            new RolePlayCard
            {
                Id = "st-001",
                ContentItemId = "st-001",
                ProfessionId = "nursing",
                ScenarioTitle = "Patient Handover - Post-Op Recovery",
                Setting = "Hospital surgical ward",
                CandidateRole = "Nurse",
                InterlocutorRole = "Incoming nurse",
                PatientName = "Mr James Wheeler",
                PatientAge = "68",
                Background = "Mr James Wheeler, day one post right hip replacement.",
                Task1 = "Summarise the surgery and current condition",
                Task2 = "Report pain management and PRN use",
                Task3 = "Highlight mobility and DVT prophylaxis",
                Task4 = "Communicate outstanding tasks",
                Difficulty = "core",
                PrimaryCategory = "First Visit",
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "fluency", "appropriateness" }),
                Status = ContentStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            },
            new RolePlayCard
            {
                Id = "st-002",
                ContentItemId = "st-002",
                ProfessionId = "medicine",
                ScenarioTitle = "Breaking Bad News - Cancer Diagnosis",
                Setting = "Outpatient room",
                CandidateRole = "Doctor",
                InterlocutorRole = "Patient",
                PatientName = "Mrs Patricia Collins",
                Background = "Biopsy confirms invasive ductal carcinoma.",
                Task1 = "Deliver results using the SPIKES framework",
                Difficulty = "core",
                PrimaryCategory = "First Visit",
                CriteriaFocusJson = JsonSupport.Serialize(new[] { "appropriateness", "grammar_expression" }),
                Status = ContentStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow,
            }
        );
    }
}
