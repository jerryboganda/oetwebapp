using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static class SeedData
{
    public static async Task EnsureReferenceDataAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Professions.AnyAsync(cancellationToken))
        {
            return;
        }

        SeedReferenceData(db);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureDemoDataAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(x => x.Id == "mock-user-001", cancellationToken))
        {
            return;
        }

        SeedDemoUser(db);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureBootstrapAuthAsync(LearnerDbContext db, BootstrapOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ExpertEmail) || string.IsNullOrWhiteSpace(options.ExpertPassword))
        {
            return;
        }

        var email = options.ExpertEmail.Trim().ToLowerInvariant();
        var expertId = "expert-001";
        var displayName = string.IsNullOrWhiteSpace(options.ExpertDisplayName)
            ? "Expert Reviewer"
            : options.ExpertDisplayName.Trim();

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(candidate => candidate.Id == expertId, cancellationToken);
        if (expert is null)
        {
            expert = new ExpertUser
            {
                Id = expertId,
                DisplayName = displayName,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow,
                Timezone = "UTC",
                IsActive = true
            };
            db.ExpertUsers.Add(expert);
        }
        else
        {
            expert.DisplayName = displayName;
            expert.Email = email;
            expert.IsActive = true;
        }

        var account = await db.AuthAccounts.FirstOrDefaultAsync(candidate => candidate.Email == email || (candidate.SubjectId == expertId && candidate.Role == "expert"), cancellationToken);
        var passwordHash = AuthService.HashPassword(options.ExpertPassword);

        if (account is null)
        {
            db.AuthAccounts.Add(new AuthAccount
            {
                Id = $"auth-{Guid.NewGuid():N}",
                SubjectId = expertId,
                Role = "expert",
                Email = email,
                DisplayName = displayName,
                PasswordHash = passwordHash,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            account.SubjectId = expertId;
            account.Role = "expert";
            account.Email = email;
            account.DisplayName = displayName;
            account.PasswordHash = passwordHash;
            account.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void SeedDemoUser(LearnerDbContext db)
    {
        SeedDemoUserCore(db);
        SeedDemoUserData(db);
    }

    private static void SeedDemoUserCore(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = "mock-user-001";

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Role = "learner",
            DisplayName = "Faisal Maqsood",
            Email = "learner@oet-prep.dev",
            Timezone = "Australia/Sydney",
            Locale = "en-AU",
            CurrentPlanId = "premium-monthly",
            ActiveProfessionId = "nursing",
            OnboardingCurrentStep = 4,
            OnboardingStepCount = 4,
            OnboardingCompleted = true,
            OnboardingStartedAt = now.AddDays(-30),
            OnboardingCompletedAt = now.AddDays(-29),
            CreatedAt = now.AddMonths(-3),
            LastActiveAt = now.AddMinutes(-10)
        });

        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = "nursing",
            TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            OverallGoal = "Reach a B grade equivalent across all sub-tests before migration to Australia.",
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            PreviousAttempts = 1,
            WeakSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }),
            StudyHoursPerWeek = 10,
            TargetCountry = "Australia",
            TargetOrganization = "AHPRA",
            DraftStateJson = JsonSupport.Serialize(new Dictionary<string, object?> { ["source"] = "seed" }),
            SubmittedAt = now.AddDays(-28),
            UpdatedAt = now.AddDays(-2)
        });

        db.Settings.Add(new LearnerSettings
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfileJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["displayName"] = "Faisal Maqsood",
                ["email"] = "learner@oet-prep.dev",
                ["profession"] = "nursing",
                ["timezone"] = "Australia/Sydney"
            }),
            NotificationsJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["emailReminders"] = true,
                ["reviewUpdates"] = true,
                ["billingAlerts"] = true
            }),
            PrivacyJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["audioConsentAccepted"] = true,
                ["analyticsOptIn"] = true
            }),
            AccessibilityJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["reducedMotion"] = false,
                ["highContrast"] = false,
                ["fontScale"] = 1.0
            }),
            AudioJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["preferredInputDevice"] = "system-default",
                ["allowCellularUploads"] = true,
                ["lowBandwidthMode"] = false
            }),
            StudyJson = JsonSupport.Serialize(new Dictionary<string, object?>
            {
                ["weeklyStudyHours"] = 10,
                ["preferredSessionLengthMinutes"] = 45,
                ["weekendFocus"] = true
            })
        });
    }

    private static void SeedReferenceData(LearnerDbContext db)
    {
        db.Professions.AddRange(
            new ProfessionReference { Id = "nursing", Code = "nursing", Label = "Nursing", Status = "active", SortOrder = 1 },
            new ProfessionReference { Id = "medicine", Code = "medicine", Label = "Medicine", Status = "active", SortOrder = 2 },
            new ProfessionReference { Id = "dentistry", Code = "dentistry", Label = "Dentistry", Status = "active", SortOrder = 3 },
            new ProfessionReference { Id = "pharmacy", Code = "pharmacy", Label = "Pharmacy", Status = "active", SortOrder = 4 },
            new ProfessionReference { Id = "physiotherapy", Code = "physiotherapy", Label = "Physiotherapy", Status = "active", SortOrder = 5 }
        );

        db.Subtests.AddRange(
            new SubtestReference { Id = "writing", Code = "writing", Label = "Writing", SupportsProfessionSpecificContent = true },
            new SubtestReference { Id = "speaking", Code = "speaking", Label = "Speaking", SupportsProfessionSpecificContent = true },
            new SubtestReference { Id = "reading", Code = "reading", Label = "Reading", SupportsProfessionSpecificContent = false },
            new SubtestReference { Id = "listening", Code = "listening", Label = "Listening", SupportsProfessionSpecificContent = false }
        );

        db.Criteria.AddRange(
            new CriterionReference { Id = "cri-purpose", SubtestCode = "writing", Code = "purpose", Label = "Purpose", Description = "How clearly the purpose of the letter is conveyed.", SortOrder = 1 },
            new CriterionReference { Id = "cri-content", SubtestCode = "writing", Code = "content", Label = "Content", Description = "Relevance and completeness of clinical content.", SortOrder = 2 },
            new CriterionReference { Id = "cri-conciseness", SubtestCode = "writing", Code = "conciseness", Label = "Conciseness & Clarity", Description = "Clarity of writing without unnecessary detail.", SortOrder = 3 },
            new CriterionReference { Id = "cri-genre", SubtestCode = "writing", Code = "genre", Label = "Genre & Style", Description = "Appropriate register and professional tone.", SortOrder = 4 },
            new CriterionReference { Id = "cri-organization", SubtestCode = "writing", Code = "organization", Label = "Organisation & Layout", Description = "Logical structure and formatting.", SortOrder = 5 },
            new CriterionReference { Id = "cri-language", SubtestCode = "writing", Code = "language", Label = "Language", Description = "Accuracy and range of grammar and vocabulary.", SortOrder = 6 },
            new CriterionReference { Id = "cri-intelligibility", SubtestCode = "speaking", Code = "intelligibility", Label = "Intelligibility", Description = "Pronunciation, stress, and clarity.", SortOrder = 1 },
            new CriterionReference { Id = "cri-fluency", SubtestCode = "speaking", Code = "fluency", Label = "Fluency", Description = "Smoothness, pacing, and hesitation control.", SortOrder = 2 },
            new CriterionReference { Id = "cri-appropriateness", SubtestCode = "speaking", Code = "appropriateness", Label = "Appropriateness of Language", Description = "Suitability of professional vocabulary and tone.", SortOrder = 3 },
            new CriterionReference { Id = "cri-grammar-expression", SubtestCode = "speaking", Code = "grammar_expression", Label = "Resources of Grammar and Expression", Description = "Range and accuracy of spoken language.", SortOrder = 4 }
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
                    brief = "Provide a safe, concise handover for the next shift.",
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
    }

    private static void SeedDemoUserData(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = "mock-user-001";

        db.StudyPlans.Add(new StudyPlan
        {
            Id = "plan-001",
            UserId = userId,
            Version = 1,
            GeneratedAt = now.AddDays(-1),
            State = AsyncState.Completed,
            Checkpoint = "Full mock scheduled in 7 days",
            WeakSkillFocus = "Writing conciseness and speaking fluency"
        });

        db.StudyPlanItems.AddRange(
            new StudyPlanItem { Id = "spi-001", StudyPlanId = "plan-001", Title = "Writing: Discharge Summary Practice", SubtestCode = "writing", DurationMinutes = 45, Rationale = "Your Conciseness & Clarity score was below target. This task narrows the detail to what the GP needs.", DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = StudyPlanItemStatus.NotStarted, Section = "today", ContentId = "wt-001", ItemType = "practice" },
            new StudyPlanItem { Id = "spi-002", StudyPlanId = "plan-001", Title = "Speaking: Patient Handover Role Play", SubtestCode = "speaking", DurationMinutes = 20, Rationale = "Fluency markers were flagged in your last speaking attempt. This role play reinforces structure and confident delivery.", DueDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = StudyPlanItemStatus.NotStarted, Section = "today", ContentId = "st-001", ItemType = "roleplay" },
            new StudyPlanItem { Id = "spi-003", StudyPlanId = "plan-001", Title = "Reading: Part C Detail Extraction", SubtestCode = "reading", DurationMinutes = 30, Rationale = "Recent reading errors came from missing exact figures and ranges.", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), Status = StudyPlanItemStatus.NotStarted, Section = "thisWeek", ContentId = "rt-001", ItemType = "practice" },
            new StudyPlanItem { Id = "spi-004", StudyPlanId = "plan-001", Title = "Listening: Number & Frequency Drill", SubtestCode = "listening", DurationMinutes = 15, Rationale = "Your last listening result showed distractor confusion on number and frequency cues.", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), Status = StudyPlanItemStatus.NotStarted, Section = "thisWeek", ContentId = "lt-001", ItemType = "drill" },
            new StudyPlanItem { Id = "spi-005", StudyPlanId = "plan-001", Title = "Full OET Mock Test", SubtestCode = "writing", DurationMinutes = 180, Rationale = "Checkpoint to measure readiness progression across all sub-tests.", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), Status = StudyPlanItemStatus.NotStarted, Section = "nextCheckpoint", ContentId = "mock-full-001", ItemType = "mock" }
        );

        db.Attempts.AddRange(
            new Attempt
            {
                Id = "wa-001",
                UserId = userId,
                ContentId = "wt-001",
                SubtestCode = "writing",
                Context = "practice",
                Mode = "timed",
                State = AttemptState.Completed,
                StartedAt = now.AddDays(-4),
                SubmittedAt = now.AddDays(-4).AddMinutes(45),
                CompletedAt = now.AddDays(-4).AddHours(1),
                ElapsedSeconds = 2700,
                DraftVersion = 3,
                ComparisonGroupId = "writing-core-001",
                DeviceType = "desktop",
                LastClientSyncAt = now.AddDays(-4).AddMinutes(44),
                DraftContent = "Dear Dr Patterson, I am writing to inform you of Mrs Eleanor Vance's recent admission and discharge...",
                ChecklistJson = JsonSupport.Serialize(new Dictionary<string, bool>
                {
                    ["Addressed the purpose clearly"] = true,
                    ["Included all relevant clinical information"] = true,
                    ["Maintained professional tone"] = true,
                    ["Proofread for grammar and spelling"] = false
                })
            },
            new Attempt
            {
                Id = "sa-001",
                UserId = userId,
                ContentId = "st-001",
                SubtestCode = "speaking",
                Context = "practice",
                Mode = "ai",
                State = AttemptState.Completed,
                StartedAt = now.AddDays(-3),
                SubmittedAt = now.AddDays(-3).AddMinutes(20),
                CompletedAt = now.AddDays(-3).AddMinutes(25),
                ElapsedSeconds = 1200,
                DeviceType = "desktop",
                AudioUploadState = UploadState.Uploaded,
                AudioObjectKey = "audio/sa-001.webm",
                TranscriptJson = JsonSupport.Serialize(new object[]
                {
                    new { id = "tl-1", speaker = "nurse", text = "Good evening, I'm handing over the care of Mr James Wheeler in bed 4.", startTime = 0, endTime = 8, markers = (object[]?)null },
                    new { id = "tl-2", speaker = "nurse", text = "Um, his relevant background includes atrial fibrillation and reflux disease.", startTime = 9, endTime = 17, markers = new[] { new { id = "m-1", type = "fluency", startTime = 9, endTime = 10, text = "Um", suggestion = "Reduce filler words for smoother handover pacing." } } },
                    new { id = "tl-3", speaker = "nurse", text = "He mobilised with physiotherapy this afternoon and tolerated sitting out of bed for fifteen minutes.", startTime = 18, endTime = 31, markers = (object[]?)null }
                }),
                AnalysisJson = JsonSupport.Serialize(new
                {
                    phrasing = new[]
                    {
                        new { id = "ps-1", originalPhrase = "Um, his relevant background includes", issueExplanation = "Filler words interrupt fluency.", strongerAlternative = "His relevant background includes", drillPrompt = "Repeat the handover without the filler at the opening." }
                    },
                    waveformPeaks = new[] { 6, 12, 8, 14, 9, 5, 11 }
                })
            },
            new Attempt
            {
                Id = "ra-001",
                UserId = userId,
                ContentId = "rt-001",
                SubtestCode = "reading",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = now.AddDays(-2),
                SubmittedAt = now.AddDays(-2).AddMinutes(25),
                CompletedAt = now.AddDays(-2).AddMinutes(25),
                ElapsedSeconds = 1500,
                AnswersJson = JsonSupport.Serialize(new Dictionary<string, string?>
                {
                    ["rq-1"] = "approximately 1 in 10",
                    ["rq-2"] = "hand hygiene",
                    ["rq-3"] = "Bundles"
                })
            },
            new Attempt
            {
                Id = "la-001",
                UserId = userId,
                ContentId = "lt-001",
                SubtestCode = "listening",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = now.AddDays(-1),
                SubmittedAt = now.AddDays(-1).AddMinutes(18),
                CompletedAt = now.AddDays(-1).AddMinutes(18),
                ElapsedSeconds = 1080,
                AnswersJson = JsonSupport.Serialize(new Dictionary<string, string?>
                {
                    ["lq-1"] = "Increasing breathlessness at night",
                    ["lq-2"] = "daily",
                    ["lq-3"] = "Combination inhaler"
                })
            }
        );

        db.Evaluations.AddRange(
            new Evaluation
            {
                Id = "we-001",
                AttemptId = "wa-001",
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = "330-360",
                GradeRange = "B-B+",
                ConfidenceBand = ConfidenceBand.Medium,
                StrengthsJson = JsonSupport.Serialize(new[] { "Clinical information was selected accurately", "Follow-up actions are mostly clear" }),
                IssuesJson = JsonSupport.Serialize(new[] { "Some details remain more extensive than the GP needs", "Proofreading should be more systematic" }),
                CriterionScoresJson = JsonSupport.Serialize(new[]
                {
                    new { criterionCode = "purpose", scoreRange = "4-5/6", confidenceBand = "medium", explanation = "The letter purpose is clear early." },
                    new { criterionCode = "content", scoreRange = "5/6", confidenceBand = "high", explanation = "Important postoperative details are included." },
                    new { criterionCode = "conciseness", scoreRange = "3-4/6", confidenceBand = "medium", explanation = "Some lower-value detail still appears." },
                    new { criterionCode = "genre", scoreRange = "4/6", confidenceBand = "medium", explanation = "Register is mostly appropriate." },
                    new { criterionCode = "organization", scoreRange = "4-5/6", confidenceBand = "medium", explanation = "Structure is easy to follow." },
                    new { criterionCode = "language", scoreRange = "4/6", confidenceBand = "medium", explanation = "Minor grammar issues remain." }
                }),
                FeedbackItemsJson = JsonSupport.Serialize(new[]
                {
                    new { feedbackItemId = "wf-1", criterionCode = "conciseness", type = "anchored_comment", anchor = new { snippet = "under spinal anaesthesia", position = 112 }, message = "This detail may be unnecessary for the receiving GP unless it changes ongoing care.", severity = "medium", suggestedFix = "Focus on post-discharge relevance." },
                    new { feedbackItemId = "wf-2", criterionCode = "language", type = "anchored_comment", anchor = new { snippet = "please arrange", position = 380 }, message = "Good direct handover of the GP action item.", severity = "low", suggestedFix = "Keep this level of clarity." }
                }),
                GeneratedAt = now.AddDays(-4).AddHours(1),
                ModelExplanationSafe = "This is a training estimate based on criterion-level patterns in your response, not an official OET score.",
                LearnerDisclaimer = "Estimated performance only. Use expert review for a higher-trust external check.",
                StatusReasonCode = "completed",
                StatusMessage = "Evaluation completed successfully.",
                LastTransitionAt = now.AddDays(-4).AddHours(1)
            },
            new Evaluation
            {
                Id = "se-001",
                AttemptId = "sa-001",
                SubtestCode = "speaking",
                State = AsyncState.Completed,
                ScoreRange = "330-360",
                GradeRange = null,
                ConfidenceBand = ConfidenceBand.Medium,
                StrengthsJson = JsonSupport.Serialize(new[] { "Logical handover structure", "Good use of clinical terminology" }),
                IssuesJson = JsonSupport.Serialize(new[] { "Filler words interrupt flow", "One phrase became slightly informal" }),
                CriterionScoresJson = JsonSupport.Serialize(new[]
                {
                    new { criterionCode = "intelligibility", scoreRange = "4-5/6", confidenceBand = "high", explanation = "Mostly clear and easy to follow." },
                    new { criterionCode = "fluency", scoreRange = "3-4/6", confidenceBand = "medium", explanation = "Pauses and filler words affected smoothness." },
                    new { criterionCode = "appropriateness", scoreRange = "4/6", confidenceBand = "medium", explanation = "Tone remained mostly professional." },
                    new { criterionCode = "grammar_expression", scoreRange = "4/6", confidenceBand = "medium", explanation = "Generally accurate with room for richer expression." }
                }),
                FeedbackItemsJson = JsonSupport.Serialize(new[]
                {
                    new { feedbackItemId = "sf-1", criterionCode = "fluency", type = "transcript_marker", anchor = new { lineId = "tl-2", startTime = 9, endTime = 10 }, message = "Remove the filler to open with more authority.", severity = "medium", suggestedFix = "Start directly with the patient's background." }
                }),
                GeneratedAt = now.AddDays(-3).AddMinutes(25),
                ModelExplanationSafe = "This is a learner-safe estimate derived from speech clarity, fluency markers, and professional language patterns.",
                LearnerDisclaimer = "Training estimate only. Audio quality and task complexity can affect confidence.",
                StatusReasonCode = "completed",
                StatusMessage = "Speaking evaluation completed successfully.",
                LastTransitionAt = now.AddDays(-3).AddMinutes(25)
            },
            new Evaluation
            {
                Id = "re-001",
                AttemptId = "ra-001",
                SubtestCode = "reading",
                State = AsyncState.Completed,
                ScoreRange = "67%",
                GradeRange = "C+",
                ConfidenceBand = ConfidenceBand.High,
                StrengthsJson = JsonSupport.Serialize(new[] { "You identified the main prevention measure correctly", "Your detail extraction on item 1 was accurate" }),
                IssuesJson = JsonSupport.Serialize(new[] { "Watch for distractor ranges and category labels" }),
                CriterionScoresJson = JsonSupport.Serialize(new[] { new { criterionCode = "detail_extraction", scoreRange = "2/3", confidenceBand = "high", explanation = "Mostly accurate on explicit details." } }),
                FeedbackItemsJson = JsonSupport.Serialize(new[] { new { feedbackItemId = "rf-1", criterionCode = "detail_extraction", type = "answer_feedback", anchor = new { questionId = "rq-3" }, message = "Question 3 tested your recognition of the term 'bundles'.", severity = "medium", suggestedFix = "Highlight named concepts while reading." } }),
                GeneratedAt = now.AddDays(-2).AddMinutes(25),
                ModelExplanationSafe = "This reading result is computed from answer accuracy.",
                LearnerDisclaimer = "Practice estimate only.",
                StatusReasonCode = "completed",
                StatusMessage = "Reading result ready.",
                LastTransitionAt = now.AddDays(-2).AddMinutes(25)
            },
            new Evaluation
            {
                Id = "le-001",
                AttemptId = "la-001",
                SubtestCode = "listening",
                State = AsyncState.Completed,
                ScoreRange = "66%",
                GradeRange = "C+",
                ConfidenceBand = ConfidenceBand.High,
                StrengthsJson = JsonSupport.Serialize(new[] { "Main concern was captured correctly", "Treatment recommendation was identified" }),
                IssuesJson = JsonSupport.Serialize(new[] { "A frequency distractor caused one incorrect answer" }),
                CriterionScoresJson = JsonSupport.Serialize(new[] { new { criterionCode = "detail_capture", scoreRange = "2/3", confidenceBand = "high", explanation = "Strong on the main idea, less secure on precise frequency." } }),
                FeedbackItemsJson = JsonSupport.Serialize(new[] { new { feedbackItemId = "lf-1", criterionCode = "detail_capture", type = "answer_feedback", anchor = new { questionId = "lq-2" }, message = "The patient said three to four times per week, not daily.", severity = "medium", suggestedFix = "Listen closely for exact numerical detail." } }),
                GeneratedAt = now.AddDays(-1).AddMinutes(18),
                ModelExplanationSafe = "This listening result is based on answer accuracy and transcript alignment.",
                LearnerDisclaimer = "Practice estimate only.",
                StatusReasonCode = "completed",
                StatusMessage = "Listening result ready.",
                LastTransitionAt = now.AddDays(-1).AddMinutes(18)
            }
        );

        db.ReadinessSnapshots.Add(new ReadinessSnapshot
        {
            Id = "rs-001",
            UserId = userId,
            ComputedAt = now.AddHours(-6),
            Version = 1,
            PayloadJson = JsonSupport.Serialize(new
            {
                targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)).ToString("yyyy-MM-dd"),
                weeksRemaining = 13,
                overallRisk = "moderate",
                recommendedStudyHours = 12,
                weakestLink = "Writing - Conciseness & Clarity",
                subTests = new[]
                {
                    new { id = "rd-w", name = "Writing", readiness = 62, target = 80, status = "Needs attention", isWeakest = true },
                    new { id = "rd-s", name = "Speaking", readiness = 68, target = 80, status = "On track", isWeakest = false },
                    new { id = "rd-r", name = "Reading", readiness = 82, target = 80, status = "Target met", isWeakest = false },
                    new { id = "rd-l", name = "Listening", readiness = 76, target = 80, status = "Almost there", isWeakest = false }
                },
                blockers = new[]
                {
                    new { id = 1, title = "Writing conciseness remains below threshold", description = "Recent writing evidence shows extra detail that weakens GP-focused communication." },
                    new { id = 2, title = "Speaking fluency markers still appear", description = "Filler words and soft starts reduce handover authority." }
                },
                evidence = new { mocksCompleted = 2, practiceQuestions = 48, expertReviews = 1, recentTrend = "Improving", lastUpdated = now.AddHours(-6) }
            })
        });

        db.DiagnosticSessions.Add(new DiagnosticSession
        {
            Id = "diag-001",
            UserId = userId,
            State = AttemptState.Completed,
            StartedAt = now.AddDays(-20),
            CompletedAt = now.AddDays(-19)
        });

        db.DiagnosticSubtests.AddRange(
            new DiagnosticSubtestStatus { Id = "diag-st-1", DiagnosticSessionId = "diag-001", SubtestCode = "writing", State = AttemptState.Completed, EstimatedDurationMinutes = 45, CompletedAt = now.AddDays(-19), AttemptId = "wa-001" },
            new DiagnosticSubtestStatus { Id = "diag-st-2", DiagnosticSessionId = "diag-001", SubtestCode = "speaking", State = AttemptState.Completed, EstimatedDurationMinutes = 20, CompletedAt = now.AddDays(-19), AttemptId = "sa-001" },
            new DiagnosticSubtestStatus { Id = "diag-st-3", DiagnosticSessionId = "diag-001", SubtestCode = "reading", State = AttemptState.Completed, EstimatedDurationMinutes = 30, CompletedAt = now.AddDays(-19), AttemptId = "ra-001" },
            new DiagnosticSubtestStatus { Id = "diag-st-4", DiagnosticSessionId = "diag-001", SubtestCode = "listening", State = AttemptState.Completed, EstimatedDurationMinutes = 25, CompletedAt = now.AddDays(-19), AttemptId = "la-001" }
        );

        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-001",
            UserId = userId,
            PlanId = "premium-monthly",
            Status = SubscriptionStatus.Active,
            NextRenewalAt = now.AddDays(30),
            StartedAt = now.AddMonths(-2),
            ChangedAt = now.AddMonths(-1),
            PriceAmount = 49.99m,
            Currency = "AUD",
            Interval = "monthly"
        });

        db.Wallets.Add(new Wallet
        {
            Id = "wallet-001",
            UserId = userId,
            CreditBalance = 3,
            LastUpdatedAt = now.AddDays(-1),
            LedgerSummaryJson = JsonSupport.Serialize(new[]
            {
                new { id = "wl-1", type = "credit_purchase", delta = 5, balanceAfter = 5, createdAt = now.AddDays(-14), note = "Purchased review credits" },
                new { id = "wl-2", type = "credit_consumed", delta = -2, balanceAfter = 3, createdAt = now.AddDays(-7), note = "Expert reviews requested" }
            })
        });

        db.Invoices.AddRange(
            new Invoice { Id = "inv-001", UserId = userId, IssuedAt = now.AddMonths(-2), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription" },
            new Invoice { Id = "inv-002", UserId = userId, IssuedAt = now.AddMonths(-1), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription" },
            new Invoice { Id = "inv-003", UserId = userId, IssuedAt = now.AddDays(-5), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription" }
        );

        db.ReviewRequests.AddRange(
            new ReviewRequest
            {
                Id = "review-001",
                AttemptId = "wa-001",
                SubtestCode = "writing",
                State = ReviewRequestState.Completed,
                TurnaroundOption = "standard",
                FocusAreasJson = JsonSupport.Serialize(new[] { "conciseness", "genre" }),
                LearnerNotes = "Please focus on conciseness and layout.",
                PaymentSource = "credits",
                PriceSnapshot = 1m,
                CreatedAt = now.AddDays(-3),
                CompletedAt = now.AddDays(-1),
                EligibilitySnapshotJson = JsonSupport.Serialize(new { canRequestReview = true, reasonCodes = Array.Empty<string>() })
            },
            new ReviewRequest
            {
                Id = "review-queue-001",
                AttemptId = "sa-001",
                SubtestCode = "speaking",
                State = ReviewRequestState.Queued,
                TurnaroundOption = "express",
                FocusAreasJson = JsonSupport.Serialize(new[] { "fluency" }),
                LearnerNotes = "Please focus on flow and clarity.",
                PaymentSource = "credits",
                PriceSnapshot = 2m,
                CreatedAt = now.AddHours(-8),
                EligibilitySnapshotJson = JsonSupport.Serialize(new { canRequestReview = true, reasonCodes = Array.Empty<string>() })
            },
            new ReviewRequest
            {
                Id = "review-queue-002",
                AttemptId = "wa-001",
                SubtestCode = "writing",
                State = ReviewRequestState.Queued,
                TurnaroundOption = "standard",
                FocusAreasJson = JsonSupport.Serialize(new[] { "content", "language" }),
                LearnerNotes = "Please focus on clinical relevance and language control.",
                PaymentSource = "credits",
                PriceSnapshot = 1m,
                CreatedAt = now.AddHours(-6),
                EligibilitySnapshotJson = JsonSupport.Serialize(new { canRequestReview = true, reasonCodes = Array.Empty<string>() })
            });

        db.MockAttempts.Add(new MockAttempt
        {
            Id = "mock-attempt-001",
            UserId = userId,
            ConfigJson = JsonSupport.Serialize(new { type = "full", mode = "exam", profession = "nursing", includeReview = false, strictTimer = true }),
            State = AttemptState.Completed,
            StartedAt = now.AddDays(-10),
            SubmittedAt = now.AddDays(-10).AddHours(3),
            CompletedAt = now.AddDays(-10).AddHours(4),
            ReportId = "mock-report-001"
        });

        db.MockReports.Add(new MockReport
        {
            Id = "mock-report-001",
            MockAttemptId = "mock-attempt-001",
            State = AsyncState.Completed,
            GeneratedAt = now.AddDays(-10).AddHours(4),
            PayloadJson = JsonSupport.Serialize(new
            {
                id = "mock-report-001",
                title = "Full OET Mock Test #1",
                date = now.AddDays(-10).ToString("yyyy-MM-dd"),
                overallScore = "340",
                summary = "Solid performance overall, with writing and speaking still needing targeted improvement.",
                subTests = new[]
                {
                    new { id = "ms-r", name = "Reading", score = "370", rawScore = "38/42" },
                    new { id = "ms-l", name = "Listening", score = "350", rawScore = "35/42" },
                    new { id = "ms-w", name = "Writing", score = "320", rawScore = "24/36" },
                    new { id = "ms-s", name = "Speaking", score = "330", rawScore = "N/A" }
                },
                weakestCriterion = new { subtest = "Writing", criterion = "Conciseness & Clarity", description = "Writing still contains unnecessary clinical detail for the intended reader." },
                priorComparison = new { exists = false, priorMockName = string.Empty, overallTrend = "flat", details = "This is your first full mock in the current training block." }
            })
        });

        // ─── Expert Console Seed Data ───

        db.ExpertUsers.Add(new ExpertUser
        {
            Id = "expert-001",
            Role = "expert",
            DisplayName = "Expert Reviewer",
            Email = "expert@oet-prep.dev",
            SpecialtiesJson = JsonSupport.Serialize(new[] { "nursing", "medicine" }),
            Timezone = "Australia/Sydney",
            IsActive = true,
            CreatedAt = now.AddMonths(-6)
        });

        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = "era-001",
            ReviewRequestId = "review-001",
            AssignedReviewerId = "expert-001",
            AssignedAt = now.AddDays(-2),
            ClaimState = ExpertAssignmentState.Claimed
        });

        db.ExpertReviewDrafts.Add(new ExpertReviewDraft
        {
            Id = "erd-001",
            ReviewRequestId = "review-001",
            ReviewerId = "expert-001",
            Version = 1,
            State = "submitted",
            RubricEntriesJson = JsonSupport.Serialize(new Dictionary<string, int>
            {
                ["purpose"] = 4, ["content"] = 5, ["conciseness"] = 3, ["genre"] = 4, ["organization"] = 4, ["language"] = 4
            }),
            CriterionCommentsJson = JsonSupport.Serialize(new Dictionary<string, string>
            {
                ["purpose"] = "Clear opening statement.",
                ["content"] = "All key details are relevant.",
                ["conciseness"] = "Some extraneous clinical detail remains.",
                ["genre"] = "Appropriate register.",
                ["organization"] = "Well structured.",
                ["language"] = "Minor grammatical issues."
            }),
            FinalCommentDraft = "Clear improvement in structure and clinical filtering.",
            DraftSavedAt = now.AddDays(-1)
        });

        db.ExpertCalibrationCases.AddRange(
            new ExpertCalibrationCase
            {
                Id = "cal-001",
                SubtestCode = "writing",
                ProfessionId = "nursing",
                Title = "Writing Calibration - Referral Letter",
                BenchmarkLabel = "Benchmark A",
                BenchmarkScore = 4,
                Difficulty = "medium",
                Status = CalibrationCaseStatus.Completed,
                CreatedAt = now.AddDays(-2)
            },
            new ExpertCalibrationCase
            {
                Id = "cal-002",
                SubtestCode = "speaking",
                ProfessionId = "medicine",
                Title = "Speaking Calibration - Handover",
                BenchmarkLabel = "Benchmark B",
                BenchmarkScore = 5,
                Difficulty = "medium",
                Status = CalibrationCaseStatus.Pending,
                CreatedAt = now.AddDays(-1)
            }
        );

        db.ExpertCalibrationResults.Add(new ExpertCalibrationResult
        {
            Id = "ecr-001",
            CalibrationCaseId = "cal-001",
            ReviewerId = "expert-001",
            SubmittedRubricJson = JsonSupport.Serialize(new Dictionary<string, int> { ["purpose"] = 4, ["content"] = 5, ["conciseness"] = 3, ["genre"] = 4, ["organization"] = 4, ["language"] = 4 }),
            ReviewerScore = 4,
            AlignmentScore = 100.0,
            Notes = "Agreed with benchmark scoring.",
            SubmittedAt = now.AddDays(-2).AddHours(2)
        });

        db.ExpertCalibrationNotes.AddRange(
            new ExpertCalibrationNote { Id = "ecn-001", Type = CalibrationNoteType.Completed, Message = "Completed Writing Calibration - Referral Letter. Alignment: Aligned.", CaseId = "cal-001", ReviewerId = "expert-001", CreatedAt = now.AddDays(-2).AddHours(2) },
            new ExpertCalibrationNote { Id = "ecn-002", Type = CalibrationNoteType.Comment, Message = "Content criterion scoring felt borderline — reviewed benchmark rubric notes for clarification.", CaseId = "cal-001", ReviewerId = "expert-001", CreatedAt = now.AddDays(-2).AddHours(3) },
            new ExpertCalibrationNote { Id = "ecn-003", Type = CalibrationNoteType.System, Message = "New calibration case assigned: Speaking Calibration - Handover (cal-002).", CaseId = "cal-002", ReviewerId = null, CreatedAt = now.AddDays(-1) }
        );

        db.ExpertAvailabilities.Add(new ExpertAvailability
        {
            Id = "ea-001",
            ReviewerId = "expert-001",
            Timezone = "Australia/Sydney",
            DaysJson = JsonSupport.Serialize(new Dictionary<string, object>
            {
                ["monday"] = new { active = true, start = "09:00", end = "17:00" },
                ["tuesday"] = new { active = true, start = "09:00", end = "17:00" },
                ["wednesday"] = new { active = true, start = "09:00", end = "17:00" },
                ["thursday"] = new { active = true, start = "09:00", end = "17:00" },
                ["friday"] = new { active = true, start = "09:00", end = "16:00" },
                ["saturday"] = new { active = false, start = "09:00", end = "12:00" },
                ["sunday"] = new { active = false, start = "09:00", end = "12:00" }
            }),
            EffectiveFrom = now.AddMonths(-3)
        });

        db.ExpertMetricSnapshots.Add(new ExpertMetricSnapshot
        {
            Id = "ems-001",
            ReviewerId = "expert-001",
            WindowStart = now.AddDays(-30),
            WindowEnd = now,
            CompletedReviews = 184,
            DraftReviews = 2,
            AvgTurnaroundHours = 18.5,
            SlaHitRate = 97.0,
            CalibrationScore = 92.0,
            ReworkRate = 4.0,
            CompletionDataJson = JsonSupport.Serialize(new[]
            {
                new { day = "Mon", count = 5 },
                new { day = "Tue", count = 4 },
                new { day = "Wed", count = 6 },
                new { day = "Thu", count = 7 },
                new { day = "Fri", count = 5 },
                new { day = "Sat", count = 3 },
                new { day = "Sun", count = 2 }
            })
        });

        // ─── Admin / CMS Seed Data ───

        db.FeatureFlags.AddRange(
            new FeatureFlag { Id = "flg-001", Name = "AI Scoring V2", Key = "ai_scoring_v2", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable AI scoring V2 pipeline for all subtests.", Owner = "Platform Team", CreatedAt = now.AddDays(-60), UpdatedAt = now.AddDays(-5) },
            new FeatureFlag { Id = "flg-002", Name = "Mock Exam Timer", Key = "mock_exam_timer", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Show countdown timers in full mock exams.", Owner = "Product", CreatedAt = now.AddDays(-45), UpdatedAt = now.AddDays(-10) },
            new FeatureFlag { Id = "flg-003", Name = "Expert Double Review", Key = "expert_double_review", FlagType = FeatureFlagType.Experiment, Enabled = false, RolloutPercentage = 25, Description = "A/B test: assign two expert reviewers to each writing submission.", Owner = "QA Team", CreatedAt = now.AddDays(-14), UpdatedAt = now.AddDays(-1) },
            new FeatureFlag { Id = "flg-004", Name = "Maintenance Banner", Key = "maintenance_banner", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Show maintenance notice banner across the platform.", Owner = "DevOps", CreatedAt = now.AddDays(-90), UpdatedAt = now.AddDays(-30) }
        );

        db.AIConfigVersions.AddRange(
            new AIConfigVersion { Id = "aic-001", Model = "gpt-4o", Provider = "OpenAI", TaskType = "writing", Status = AIConfigStatus.Active, Accuracy = 94.2, ConfidenceThreshold = 0.85, RoutingRule = "default", PromptLabel = "Writing Eval v3.2", CreatedBy = "Admin", CreatedAt = now.AddDays(-30) },
            new AIConfigVersion { Id = "aic-002", Model = "gpt-4o", Provider = "OpenAI", TaskType = "speaking", Status = AIConfigStatus.Active, Accuracy = 91.8, ConfidenceThreshold = 0.80, RoutingRule = "default", PromptLabel = "Speaking Eval v2.1", CreatedBy = "Admin", CreatedAt = now.AddDays(-25) },
            new AIConfigVersion { Id = "aic-003", Model = "claude-3.5-sonnet", Provider = "Anthropic", TaskType = "writing", Status = AIConfigStatus.Testing, Accuracy = 95.1, ConfidenceThreshold = 0.88, RoutingRule = "experiment:claude_writing", ExperimentFlag = "claude_writing_test", PromptLabel = "Writing Eval v4.0-beta", CreatedBy = "Admin", CreatedAt = now.AddDays(-7) },
            new AIConfigVersion { Id = "aic-004", Model = "gpt-3.5-turbo", Provider = "OpenAI", TaskType = "reading", Status = AIConfigStatus.Deprecated, Accuracy = 88.5, ConfidenceThreshold = 0.75, RoutingRule = "legacy", PromptLabel = "Reading Eval v1.0", CreatedBy = "Admin", CreatedAt = now.AddDays(-120) }
        );

        db.AuditEvents.AddRange(
            new AuditEvent { Id = "aud-001", OccurredAt = now.AddHours(-2), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Published", ResourceType = "Content", ResourceId = "writing-referral-01", Details = "Published writing content" },
            new AuditEvent { Id = "aud-002", OccurredAt = now.AddHours(-5), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Created", ResourceType = "Flag", ResourceId = "flg-003", Details = "Created feature flag: Expert Double Review" },
            new AuditEvent { Id = "aud-003", OccurredAt = now.AddDays(-1), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Updated", ResourceType = "AIConfig", ResourceId = "aic-001", Details = "Updated AI config: gpt-4o writing" },
            new AuditEvent { Id = "aud-004", OccurredAt = now.AddDays(-2), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Assigned Review", ResourceType = "ReviewRequest", ResourceId = "review-001", Details = "Assigned review to expert-001" }
        );

        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-premium-monthly", Name = "Premium Monthly", Price = 49.99m, Currency = "AUD", Interval = "monthly", ActiveSubscribers = 1250, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-premium-yearly", Name = "Premium Yearly", Price = 399.99m, Currency = "AUD", Interval = "yearly", ActiveSubscribers = 820, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-basic-monthly", Name = "Basic Monthly", Price = 19.99m, Currency = "AUD", Interval = "monthly", ActiveSubscribers = 3400, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-18), UpdatedAt = now.AddDays(-10) },
            new BillingPlan { Id = "plan-legacy-trial", Name = "Legacy Trial", Price = 0m, Currency = "AUD", Interval = "monthly", ActiveSubscribers = 0, Status = BillingPlanStatus.Legacy, CreatedAt = now.AddMonths(-24), UpdatedAt = now.AddMonths(-6) }
        );

        db.ContentRevisions.AddRange(
            new ContentRevision { Id = "rev-w01-1", ContentItemId = "writing-referral-01", RevisionNumber = 1, State = "draft", ChangeNote = "Initial creation", SnapshotJson = "{}", CreatedBy = "Admin", CreatedAt = now.AddDays(-30) },
            new ContentRevision { Id = "rev-w01-2", ContentItemId = "writing-referral-01", RevisionNumber = 2, State = "published", ChangeNote = "Published after QA review", SnapshotJson = "{}", CreatedBy = "Admin", CreatedAt = now.AddDays(-28) }
        );

    }
}
