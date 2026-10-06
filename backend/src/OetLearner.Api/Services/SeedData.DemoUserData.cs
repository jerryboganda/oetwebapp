using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
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
                AudioObjectKey = "audio/sa-001.wav",
                AudioMetadataJson = JsonSupport.Serialize(new Dictionary<string, object?> { ["contentType"] = "audio/wav" }),
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

        // ─── Writing V2 reviewable submission (WS-F5 tutor/expert marking surface) ───
        // A self-contained WritingScenario + content checklist + model-answer exemplar +
        // a graded WritingSubmission so the V2 marking workspace
        // (GET /v1/writing/tutor/reviews/{id}/context, behind
        // /expert/review/writing/{submissionId} and /tutor/writing/reviews/{submissionId})
        // has a deterministic, reviewable target in E2E. Fixed GUIDs keep the route
        // stable for the expert-review specs. The whole demo seed is guarded by the
        // mock-user-001 existence check in EnsureDemoDataAsync, so plain Add(...) is safe.
        var writingMarkingScenarioId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var writingMarkingSubmissionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var writingMarkingGradeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var writingMarkingAssignmentId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        const string writingMarkingLetter =
            "Dear Dr Patterson,\n\n" +
            "Re: Mrs Eleanor Vance, 72 years old\n\n" +
            "I am writing to refer Mrs Vance, who was admitted on 2 June following a fall at home and " +
            "is now ready for discharge into your care. She was diagnosed with a fractured left neck of " +
            "femur and underwent a hemiarthroplasty on 3 June, which she tolerated well.\n\n" +
            "Her recovery has been uncomplicated. She is mobilising with a frame and physiotherapy, and " +
            "her wound is clean and dry. Please review her in one week to monitor wound healing and " +
            "anticoagulation, and arrange ongoing physiotherapy in the community.\n\n" +
            "Thank you for your continued care.\n\n" +
            "Yours sincerely,\nThe Charge Nurse";

        db.WritingScenarios.Add(new WritingScenario
        {
            Id = writingMarkingScenarioId,
            Title = "Discharge referral — Mrs Eleanor Vance",
            LetterType = "referral",
            Profession = "nursing",
            TopicsJson = JsonSupport.Serialize(new[] { "discharge", "orthopaedics" }),
            Difficulty = 3,
            IsDiagnostic = false,
            Status = "published",
            Version = 1,
            AuthorId = "admin-seed",
            PublishedAt = now.AddDays(-30),
            CreatedAt = now.AddDays(-30),
            InternalCode = "NUR-WR-S01",
            TaskPromptMarkdown =
                "Using the case notes, write a referral letter to the patient's general practitioner, " +
                "Dr Patterson, summarising the admission and the follow-up care required after discharge.",
            WriterRole = "You are the charge nurse on the orthopaedic ward at Newtown General Hospital.",
            TodayDate = "10 June 2026",
            ExpectedPurpose = "Refer the patient to the GP for ongoing care after a hip-fracture admission.",
            ExpectedAction = "Review wound and anticoagulation in one week; arrange community physiotherapy.",
            FixedInstructionsJson = JsonSupport.Serialize(new[]
            {
                "Use the conventions of a formal referral letter.",
                "Write 180–200 words.",
                "Do not use note form."
            }),
            WordGuideMin = 180,
            WordGuideMax = 200,
            ReadingTimeSeconds = 300,
            WritingTimeSeconds = 2400,
            SimulationModes = "both",
            MarkingMode = "tutor",
            ContentOwnerId = "admin-seed",
            UpdatedAt = now.AddDays(-30)
        });

        var writingMarkingCaseNotes = new[]
        {
            "Mrs Eleanor Vance, 72 years old, was admitted on 2 June following a fall at home.",
            "She had a fractured left neck of femur and underwent a hemiarthroplasty on 3 June.",
            "Her recovery has been uncomplicated. She is mobilising with a frame and physiotherapy, and her wound is clean and dry.",
            "Dr Patterson should review wound healing and anticoagulation in one week and arrange ongoing physiotherapy in the community."
        };
        for (var index = 0; index < writingMarkingCaseNotes.Length; index++)
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = writingMarkingScenarioId,
                Ordinal = index + 1,
                SentenceText = writingMarkingCaseNotes[index],
                RelevanceLabel = "relevant",
                CreatedAt = now.AddDays(-30)
            });
        }

        db.WritingMocks.Add(new WritingMock
        {
            Id = Guid.NewGuid(),
            ScenarioId = writingMarkingScenarioId,
            Title = "Discharge referral - Mrs Eleanor Vance",
            Difficulty = 3,
            Status = "published",
            CreatedAt = now.AddDays(-30)
        });

        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = writingMarkingSubmissionId,
            UserId = userId,
            ScenarioId = writingMarkingScenarioId,
            Mode = "exam",
            LetterContent = writingMarkingLetter,
            LetterContentHash = "seed-writing-marking-001",
            WordCount = 168,
            TimeSpentSeconds = 1980,
            StartedAt = now.AddDays(-2),
            SubmittedAt = now.AddDays(-2).AddMinutes(33),
            IsRevision = false,
            Status = "graded",
            GradingTier = "batched",
            InputSource = "typed",
            CreatedAt = now.AddDays(-2)
        });

        db.WritingGrades.Add(new WritingGrade
        {
            Id = writingMarkingGradeId,
            SubmissionId = writingMarkingSubmissionId,
            C1Purpose = 2,
            C2Content = 5,
            C3Conciseness = 4,
            C4Genre = 5,
            C5Organisation = 5,
            C6Language = 5,
            RawTotal = 26,
            EstimatedBand = 26,
            BandLabel = "C+",
            PerCriterionFeedbackJson = JsonSupport.Serialize(new Dictionary<string, string>
            {
                ["c1Purpose"] = "The purpose of the referral is clear in the opening lines.",
                ["c2Content"] = "Most key clinical details are included accurately.",
                ["c3Conciseness"] = "Some lower-value detail could be trimmed for the GP.",
                ["c4Genre"] = "Register is appropriate for a referral letter.",
                ["c5Organisation"] = "Information is logically sequenced.",
                ["c6Language"] = "Generally well controlled with minor slips."
            }),
            TopThreePrioritiesJson = JsonSupport.Serialize(new[]
            {
                "Tighten the discharge summary to the GP-relevant essentials.",
                "Make the requested follow-up actions more explicit.",
                "Proofread for minor grammatical slips."
            }),
            ConfidenceFlag = "medium",
            ModelUsed = "seed-heuristic",
            CanonVersion = "v1",
            GradedAt = now.AddDays(-2).AddMinutes(35),
            CreatedAt = now.AddDays(-2).AddMinutes(35)
        });

        // A tutor-review assignment claimed by the seeded expert so the seeded
        // submission surfaces in the writing review queue (GET /v1/tutors/writing/queue,
        // which lists both "pending" and "claimed" rows) — backing both the expert
        // writing queue (/expert/queue/assigned) and /tutor/writing/queue — AND so the
        // expert can load the marking context directly via
        // /expert/review/writing/{submissionId}. GetContextAsync gates access through
        // CanAccessSubmissionAsync, which requires an assignment for THIS tutor with
        // status "claimed"/"submitted"; an unclaimed (empty-tutor, "pending") row would
        // 403 the direct route load and blank the marking workspace. TutorId must match
        // the JWT NameIdentifier the API resolves (the expert's auth account id), not the
        // ExpertUser id. The claim endpoint is idempotent for the current tutor, so the
        // queue → review navigation spec can still re-claim it; SubmitMarkingReviewAsync
        // never touches this row, so it stays queue-visible across review submissions.
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = writingMarkingAssignmentId,
            SubmissionId = writingMarkingSubmissionId,
            TutorId = ExpertAuthAccountId,
            Status = "claimed",
            ClaimedAt = now.AddDays(-2).AddMinutes(33),
            DueAt = now.AddDays(-2).AddMinutes(33).AddHours(36)
        });

        db.Evaluations.AddRange(
            new Evaluation
            {
                Id = "we-001",
                AttemptId = "wa-001",
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = "330-360",
                GradeRange = "C+-B",
                ConfidenceBand = ConfidenceBand.Medium,
                StrengthsJson = JsonSupport.Serialize(new[] { "Clinical information was selected accurately", "Follow-up actions are mostly clear" }),
                IssuesJson = JsonSupport.Serialize(new[] { "Some details remain more extensive than the GP needs", "Proofreading should be more systematic" }),
                CriterionScoresJson = JsonSupport.Serialize(new[]
                {
                    new { criterionCode = "purpose", scoreRange = "2/3", confidenceBand = "medium", explanation = "The letter purpose is clear early. Purpose is scored 0\u20133 only." },
                    new { criterionCode = "content", scoreRange = "5/7", confidenceBand = "high", explanation = "Important postoperative details are included." },
                    new { criterionCode = "conciseness_clarity", scoreRange = "4/7", confidenceBand = "medium", explanation = "Some lower-value detail still appears." },
                    new { criterionCode = "genre_style", scoreRange = "4/7", confidenceBand = "medium", explanation = "Register is mostly appropriate." },
                    new { criterionCode = "organisation_layout", scoreRange = "5/7", confidenceBand = "medium", explanation = "Structure is easy to follow." },
                    new { criterionCode = "language", scoreRange = "4/7", confidenceBand = "medium", explanation = "Minor grammar issues remain." }
                }),
                FeedbackItemsJson = JsonSupport.Serialize(new[]
                {
                    new { feedbackItemId = "wf-1", criterionCode = "conciseness_clarity", type = "anchored_comment", anchor = new { snippet = "under spinal anaesthesia", position = 112 }, message = "This detail may be unnecessary for the receiving GP unless it changes ongoing care.", severity = "medium", suggestedFix = "Focus on post-discharge relevance." },
                    new { feedbackItemId = "wf-2", criterionCode = "language", type = "anchored_comment", anchor = new { snippet = "please arrange", position = 380 }, message = "Good direct handover of the GP action item.", severity = "low", suggestedFix = "Keep this level of clarity." }
                }),
                GeneratedAt = now.AddDays(-4).AddHours(1),
                ModelExplanationSafe = "This is a training estimate based on criterion-level patterns in your response, not an official OET score.",
                LearnerDisclaimer = "Estimated performance only. Use tutor review for a higher-trust external check.",
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

        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "sub-item-001",
            SubscriptionId = "sub-001",
            ItemType = "addon",
            ItemCode = "credits-3",
            Quantity = 1,
            Status = SubscriptionItemStatus.Active,
            StartsAt = now.AddMonths(-1),
            EndsAt = now.AddMonths(1),
            QuoteId = "quote-seeded-001",
            CheckoutSessionId = "checkout-seeded-001",
            CreatedAt = now.AddMonths(-1),
            UpdatedAt = now
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
                new { id = "wl-2", type = "credit_consumed", delta = -2, balanceAfter = 3, createdAt = now.AddDays(-7), note = "Tutor reviews requested" }
            })
        });

        db.AiPackageCreditAccounts.Add(new AiPackageCreditAccount
        {
            Id = "aipkg-demo-001",
            UserId = userId,
            WritingOnlyCredits = 3,
            ListeningTestsRemaining = 0,
            ReadingTestsRemaining = 0,
            ExpiresAt = now.AddMonths(1),
            CreatedAt = now,
            UpdatedAt = now
        });

        db.Invoices.AddRange(
            new Invoice { Id = "inv-001", UserId = userId, IssuedAt = now.AddMonths(-2), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription", Source = InvoiceSources.AdminGrant, ReconciledAt = now },
            new Invoice { Id = "inv-002", UserId = userId, IssuedAt = now.AddMonths(-1), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription", Source = InvoiceSources.AdminGrant, ReconciledAt = now },
            new Invoice { Id = "inv-003", UserId = userId, IssuedAt = now.AddDays(-5), Amount = 49.99m, Currency = "AUD", Status = "Paid", Description = "Premium Monthly subscription", Source = InvoiceSources.AdminGrant, ReconciledAt = now }
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
            DisplayName = "Dr. Ahmed Hesham",
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
                ["purpose"] = 2, ["content"] = 5, ["conciseness_clarity"] = 5, ["genre_style"] = 5, ["organisation_layout"] = 5, ["language"] = 5
            }),
            CriterionCommentsJson = JsonSupport.Serialize(new Dictionary<string, string>
            {
                ["purpose"] = "Clear opening statement.",
                ["content"] = "All key details are relevant.",
                ["conciseness_clarity"] = "Some extraneous clinical detail remains.",
                ["genre_style"] = "Appropriate register.",
                ["organisation_layout"] = "Well structured.",
                ["language"] = "Minor grammatical issues."
            }),
            FinalCommentDraft = "Clear improvement in structure and clinical filtering.",
            ScratchpadJson = JsonSupport.Serialize("Double-check whether the safety-net advice is explicit enough for community follow-up."),
            ChecklistItemsJson = JsonSupport.Serialize(new[]
            {
                new { id = "purpose", label = "Purpose is explicit in the opening lines.", @checked = true },
                new { id = "content", label = "Only clinically relevant post-operative facts remain.", @checked = true },
                new { id = "safety-net", label = "Follow-up and escalation advice are obvious to the receiving clinician.", @checked = false }
            }),
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
                CaseArtifactsJson = JsonSupport.Serialize(new[]
                {
                    new { kind = "case_notes", title = "Case Notes", content = "Post-operative referral after laparoscopic cholecystectomy with persistent abdominal pain, mild wound ooze, and delayed recovery." },
                    new { kind = "learner_response", title = "Candidate Response", content = "Dear Dr Patel, thank you for reviewing Mrs Khan, who now has worsening abdominal pain and concerns about wound healing after surgery." },
                    new { kind = "benchmark_focus", title = "Benchmark Focus", content = "This case rewards concise referral purpose, careful filtering of low-value detail, and a clinically useful closing request." }
                }),
                ReferenceRubricJson = JsonSupport.Serialize(new[]
                {
                    new { criterion = "purpose", benchmarkScore = 2, rationale = "Referral purpose is established immediately. Scored 0\u20133." },
                    new { criterion = "content", benchmarkScore = 5, rationale = "The strongest benchmark keeps only information that changes follow-up urgency." },
                    new { criterion = "conciseness_clarity", benchmarkScore = 5, rationale = "A few extra procedural details reduce efficiency; strong benchmarks cut them." },
                    new { criterion = "genre_style", benchmarkScore = 5, rationale = "Professional referral register is sustained." },
                    new { criterion = "organisation_layout", benchmarkScore = 5, rationale = "Information flows from reason for referral to current concerns and request." },
                    new { criterion = "language", benchmarkScore = 5, rationale = "Minor slips remain, but overall control is strong." }
                }),
                ReferenceNotesJson = JsonSupport.Serialize(new[]
                {
                    "Benchmark prioritises reason for referral, current complication, and required follow-up action in the opening half.",
                    "Lower-value surgical history should stay compressed unless it changes the receiving clinician's decision.",
                    "Language quality matters, but information selection is the main differentiator in this case."
                }),
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
                CaseArtifactsJson = JsonSupport.Serialize(new[]
                {
                    new { kind = "role_card", title = "Role Card", content = "You are handing over a patient with escalating post-operative pain and new abnormal observations to the on-call doctor." },
                    new { kind = "candidate_transcript", title = "Candidate Transcript", content = "Doctor, I am calling about a patient whose pain has worsened despite analgesia. I need your review of the escalation plan and monitoring priorities." },
                    new { kind = "benchmark_focus", title = "Benchmark Focus", content = "The benchmark rewards clear escalation language, prioritisation, and confident, clinically safe handover structure." }
                }),
                ReferenceRubricJson = JsonSupport.Serialize(new[]
                {
                    new { criterion = "intelligibility", benchmarkScore = 5, rationale = "Speech is consistently easy to follow." },
                    new { criterion = "fluency", benchmarkScore = 5, rationale = "The benchmark delivery stays steady with minimal hesitation." },
                    new { criterion = "appropriateness", benchmarkScore = 4, rationale = "Register is professional with one slightly abrupt reassurance phrase." },
                    new { criterion = "grammar", benchmarkScore = 5, rationale = "Grammar and expression stay controlled throughout the handover." },
                    new { criterion = "relationshipBuilding", benchmarkScore = 2, rationale = "Respectful and attentive tone; empathy shown to the on-call doctor's priorities." },
                    new { criterion = "patientPerspective", benchmarkScore = 2, rationale = "Candidate relays the patient's concerns and picks up the deterioration cue." },
                    new { criterion = "providingStructure", benchmarkScore = 3, rationale = "Opens with a concise summary, signposts escalation, and closes with a clear follow-up request." },
                    new { criterion = "informationGathering", benchmarkScore = 2, rationale = "Open question used to confirm plan; avoids compound questioning." },
                    new { criterion = "informationGiving", benchmarkScore = 3, rationale = "Prioritisation, escalation, and safety-netting are explicit." }
                }),
                ReferenceNotesJson = JsonSupport.Serialize(new[]
                {
                    "Benchmark opens with a concise summary before the detailed escalation points.",
                    "Full marks require explicit prioritisation and a clear follow-up request.",
                    "Minor warmth or phrasing issues are acceptable if the handover remains clinically safe and well organised."
                }),
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
            SubmittedRubricJson = JsonSupport.Serialize(new Dictionary<string, int> { ["purpose"] = 2, ["content"] = 5, ["conciseness_clarity"] = 5, ["genre_style"] = 5, ["organisation_layout"] = 5, ["language"] = 5 }),
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
            // ── Existing operational flags ──
            new FeatureFlag { Id = "flg-001", Name = "AI Scoring V2", Key = "ai_scoring_v2", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable AI scoring V2 pipeline for all subtests.", Owner = "Platform Team", CreatedAt = now.AddDays(-60), UpdatedAt = now.AddDays(-5) },
            new FeatureFlag { Id = "flg-002", Name = "Mock Exam Timer", Key = "mock_exam_timer", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Show countdown timers in full mock exams.", Owner = "Product", CreatedAt = now.AddDays(-45), UpdatedAt = now.AddDays(-10) },
            new FeatureFlag { Id = "flg-003", Name = "Tutor Double Review", Key = "expert_double_review", FlagType = FeatureFlagType.Experiment, Enabled = false, RolloutPercentage = 25, Description = "A/B test: assign two tutor reviewers to each writing submission.", Owner = "QA Team", CreatedAt = now.AddDays(-14), UpdatedAt = now.AddDays(-1) },
            new FeatureFlag { Id = "flg-004", Name = "Maintenance Banner", Key = "maintenance_banner", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Show maintenance notice banner across the platform.", Owner = "DevOps", CreatedAt = now.AddDays(-90), UpdatedAt = now.AddDays(-30) },
            // ── Phase 1 new feature flags ──
            new FeatureFlag { Id = "flg-005", Name = "Multi-Exam Foundation", Key = "multi_exam_foundation", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable multi-exam support (IELTS, PTE, Cambridge, TOEFL) alongside OET.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-006", Name = "Adaptive Difficulty", Key = "adaptive_difficulty", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable Elo-based adaptive difficulty engine for content recommendations.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-007", Name = "Spaced Repetition", Key = "spaced_repetition", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable SM-2 spaced repetition review system for mistakes and weak areas.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-008", Name = "Gamification", Key = "gamification", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable XP, streaks, achievements, and leaderboard gamification mechanics.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-009", Name = "Vocabulary Builder", Key = "vocabulary_builder", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable medical and academic vocabulary builder with flashcards and quizzes.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-010", Name = "AI Content Generation", Key = "ai_content_generation", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Enable admin AI content generation tool.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            // ── Phase 2 new feature flags ──
            new FeatureFlag { Id = "flg-011", Name = "AI Conversation Practice", Key = "ai_conversation", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable AI roleplay conversation partner for speaking practice.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-012", Name = "AI Writing Coach", Key = "ai_writing_coach", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable real-time AI writing suggestions in the writing editor.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-013", Name = "Pronunciation Analysis", Key = "pronunciation_analysis", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Phoneme-level pronunciation analysis, drills, recording UX, and ASR scoring.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-014", Name = "Performance Prediction", Key = "performance_prediction", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable predicted score forecasting based on practice history.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            // ── Phase 3 new feature flags ──
            new FeatureFlag { Id = "flg-015", Name = "Grammar Lessons", Key = "grammar_lessons", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable structured grammar lesson modules.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-016", Name = "Video Lessons", Key = "video_lessons", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable video lesson catalogue.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-017", Name = "Strategy Guides", Key = "strategy_guides", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable written OET exam strategy guides.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-018", Name = "Community Forums", Key = "community_forums", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable community discussion forums and study groups.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-019", Name = "Live Tutoring", Key = "live_tutoring", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable one-on-one live tutoring sessions with experts.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-020", Name = "Certificates", Key = "certificates", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable achievement certificate generation and download.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            // Gradual rollout: 10% → monitor → 50% → 100% over 6 weeks.
            new FeatureFlag { Id = "flg-021", Name = "Referral Program", Key = "referral_program", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 10, Description = "Enable referral program with credit rewards.", Owner = "Growth", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-022", Name = "Sponsor Dashboard", Key = "sponsor_cohorts", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable sponsor dashboard and cohort management.", Owner = "Enterprise", CreatedAt = now, UpdatedAt = now },
            // ── Phase 4 new feature flags ──
            new FeatureFlag { Id = "flg-023", Name = "Exam Booking", Key = "exam_booking", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable exam booking integration with official booking portals.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-024", Name = "Content Marketplace", Key = "content_marketplace", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable content contributor marketplace.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-025", Name = "Offline Mode", Key = "offline_mode", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable offline practice mode for mobile.", Owner = "Mobile Team", CreatedAt = now, UpdatedAt = now },
            // ── AI Learning Companion (persona "Sami") — docs/ai-learning-companion/ ──
            // Independent switches so an incident can disable retrieval, actions or
            // credit consumption WITHOUT killing the whole surface, and without a
            // deploy. All default OFF; the owner enables them deliberately.
            new FeatureFlag { Id = "flg-026", Name = "AI Learning Companion", Key = "ai_learning_companion", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Master switch for the AI Learning Companion learner surface (floating panel + full tutor route).", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-027", Name = "Companion Retrieval", Key = "companion_retrieval", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Kill switch for companion knowledge retrieval. Off = companion answers without grounded sources instead of going down.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-028", Name = "Companion Actions", Key = "companion_actions", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Kill switch for companion typed platform actions (open resource, add to plan, checkout).", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-029", Name = "Companion Credit Consumption", Key = "companion_credits", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Freeze new companion AI Credit consumption during a ledger incident. Balances are preserved; only new charges stop.", Owner = "Finance", CreatedAt = now, UpdatedAt = now },
            // TO VERIFY TV-006 / TV-007: numeric Writing/Speaking band claims by the
            // companion stay disabled until approved calibration exists. Criterion
            // feedback is always allowed. Do not enable without Pedagogy/AI QA sign-off.
            new FeatureFlag { Id = "flg-030", Name = "Companion Score Display", Key = "companion_score_display", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "GATED (TV-006/TV-007): allow the companion to state numeric Writing/Speaking band estimates. Requires approved calibration.", Owner = "Pedagogy", CreatedAt = now, UpdatedAt = now },
            // ── Owner Agent Console (agent-console/CONTRACT.md §5) ──
            // Kill switch read uncached + fail-closed by OwnerAgentFeatureGate: missing or
            // disabled row ⇒ every /v1/owner-agent route answers 503. Default OFF; the owner
            // enables it deliberately (production has no demo seed, so create the row with
            // this key from /admin/flags there).
            new FeatureFlag { Id = "flg-031", Name = "Owner Agent Console", Key = "owner_agent_console", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Kill switch for the owner-only agent console (/admin/agent-console). Off = every /v1/owner-agent route returns 503.", Owner = "Owner", CreatedAt = now, UpdatedAt = now },
            // ── Secondary Writing reviewer shadow switch (writing.grade.review; WritingGradeReviewer) ──
            // Read uncached, newest row wins, unreadable = off. Only the SHADOW row is seeded (Enabled = false: inert).
            // The enforced reviewer's off switch is the key writing_ai_reviewer with Enabled = false and is deliberately
            // NOT seeded: a disabled row means OFF, so seeding it would switch the reviewer off wherever this seed ran.
            // With no such row the reviewer is on whenever the writing-codex-sub provider row is active. Production has
            // no demo seed: create the rows from /admin/flags there.
            new FeatureFlag { Id = "flg-032", Name = "Writing Secondary Reviewer (Shadow)", Key = "writing_ai_reviewer_shadow", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Shadow mode for the secondary Writing reviewer: runs it and records proposals in admin notes only; results are never changed or held.", Owner = "Owner", CreatedAt = now, UpdatedAt = now }
        );

        db.AIConfigVersions.AddRange(
            new AIConfigVersion { Id = "aic-001", Model = "claude-sonnet-5", Provider = "DigitalOcean Serverless (Anthropic)", TaskType = "writing", Status = AIConfigStatus.Active, Accuracy = 94.2, ConfidenceThreshold = 0.85, RoutingRule = "default", PromptLabel = "Writing Eval v3.2", CreatedBy = "Admin", CreatedAt = now.AddDays(-30) },
            new AIConfigVersion { Id = "aic-002", Model = "claude-sonnet-5", Provider = "DigitalOcean Serverless (Anthropic)", TaskType = "speaking", Status = AIConfigStatus.Active, Accuracy = 91.8, ConfidenceThreshold = 0.80, RoutingRule = "default", PromptLabel = "Speaking Eval v2.1", CreatedBy = "Admin", CreatedAt = now.AddDays(-25) },
            new AIConfigVersion { Id = "aic-003", Model = "claude-3.5-sonnet", Provider = "Anthropic", TaskType = "writing", Status = AIConfigStatus.Testing, Accuracy = 95.1, ConfidenceThreshold = 0.88, RoutingRule = "experiment:claude_writing", ExperimentFlag = "claude_writing_test", PromptLabel = "Writing Eval v4.0-beta", CreatedBy = "Admin", CreatedAt = now.AddDays(-7) },
            new AIConfigVersion { Id = "aic-004", Model = "gpt-3.5-turbo", Provider = "OpenAI", TaskType = "reading", Status = AIConfigStatus.Deprecated, Accuracy = 88.5, ConfidenceThreshold = 0.75, RoutingRule = "legacy", PromptLabel = "Reading Eval v1.0", CreatedBy = "Admin", CreatedAt = now.AddDays(-120) }
        );

        db.AuditEvents.AddRange(
            new AuditEvent { Id = "aud-001", OccurredAt = now.AddHours(-2), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Published", ResourceType = "Content", ResourceId = "writing-referral-01", Details = "Published writing content" },
            new AuditEvent { Id = "aud-002", OccurredAt = now.AddHours(-5), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Created", ResourceType = "Flag", ResourceId = "flg-003", Details = "Created feature flag: Expert Double Review" },
            new AuditEvent { Id = "aud-003", OccurredAt = now.AddDays(-1), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Updated", ResourceType = "AIConfig", ResourceId = "aic-001", Details = "Updated AI config: gpt-4o writing" },
            new AuditEvent { Id = "aud-004", OccurredAt = now.AddDays(-2), ActorId = "admin-user-001", ActorName = "Admin User", Action = "Assigned Review", ResourceType = "ReviewRequest", ResourceId = "review-001", Details = "Assigned review to expert-001" }
        );

        var billingPlanIds = new[]
        {
            "plan-basic-monthly",
            "plan-premium-monthly",
            "plan-premium-yearly",
            "plan-intensive-monthly",
            "plan-legacy-trial"
        };

        var existingBillingPlans = db.BillingPlans
            .Where(plan => billingPlanIds.Contains(plan.Id))
            .ToList();
        if (existingBillingPlans.Count > 0)
        {
            db.BillingPlans.RemoveRange(existingBillingPlans);
        }

        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-premium-monthly", Code = "premium-monthly", Name = "Premium Monthly", Description = "Adds productive-skill review capacity and richer mock support for active preparation.", Price = 49.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 20, IncludedCredits = 3, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true, ai = new { quotaPlanCode = "pro" } }), ActiveSubscribers = 0, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-premium-yearly", Code = "premium-yearly", Name = "Premium Yearly", Description = "Annual premium access with the same learner benefits and stronger retention value.", Price = 399.99m, Currency = "AUD", Interval = "yearly", DurationMonths = 12, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 30, IncludedCredits = 6, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true, ai = new { quotaPlanCode = "pro" } }), ActiveSubscribers = 0, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-basic-monthly", Code = "basic-monthly", Name = "Basic Monthly", Description = "Core OET practice with AI evaluation and learner analytics.", Price = 19.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 10, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true, ai = new { quotaPlanCode = "starter" } }), ActiveSubscribers = 0, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-18), UpdatedAt = now.AddDays(-10) },
            new BillingPlan { Id = "plan-intensive-monthly", Code = "intensive-monthly", Name = "Intensive Monthly", Description = "Higher review capacity for repeated writing and speaking feedback before the exam window.", Price = 79.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 40, IncludedCredits = 8, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true, ai = new { quotaPlanCode = "pro" } }), ActiveSubscribers = 0, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-10), UpdatedAt = now.AddDays(-3) },
            new BillingPlan { Id = "plan-legacy-trial", Code = "legacy-trial", Name = "Legacy Trial", Description = "Legacy trial plan retained for compatibility.", Price = 0m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = false, IsRenewable = false, TrialDays = 14, DisplayOrder = 0, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true, ai = new { quotaPlanCode = "free" } }), ActiveSubscribers = 0, Status = BillingPlanStatus.Legacy, CreatedAt = now.AddMonths(-24), UpdatedAt = now.AddMonths(-6) }
        );

        var billingAddOnIds = new[]
        {
            "addon-credits-3",
            "addon-credits-5",
            "addon-priority-review"
        };

        var existingBillingAddOns = db.BillingAddOns
            .Where(addOn => billingAddOnIds.Contains(addOn.Id))
            .ToList();
        if (existingBillingAddOns.Count > 0)
        {
            db.BillingAddOns.RemoveRange(existingBillingAddOns);
        }

        db.BillingAddOns.AddRange(
            // Unified credit-economy pricing (~$3/credit anchor).  Eliminates the old
            // 16× gap between wallet ($0.63/credit) and add-on ($9–$10/credit) pricing.
            new BillingAddOn { Id = "addon-credits-3", Code = "credits-3", Name = "3 Review Credits", Description = "Pack of 3 tutor review credits.", Price = 9.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 0, GrantCredits = 3, GrantEntitlementsJson = JsonSupport.Serialize(new { ai_credits = 3 }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "basic-monthly", "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = true, IsStackable = true, QuantityStep = 1, MaxQuantity = 5, DisplayOrder = 10, CreatedAt = now.AddMonths(-8), UpdatedAt = now },
            new BillingAddOn { Id = "addon-credits-5", Code = "credits-5", Name = "5 Review Credits", Description = "Pack of 5 tutor review credits.", Price = 14.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 0, GrantCredits = 5, GrantEntitlementsJson = JsonSupport.Serialize(new { ai_credits = 5 }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "basic-monthly", "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = true, IsStackable = true, QuantityStep = 1, MaxQuantity = 5, DisplayOrder = 20, CreatedAt = now.AddMonths(-8), UpdatedAt = now },
            new BillingAddOn { Id = "addon-priority-review", Code = "priority-review", Name = "Priority Review Pack", Description = "Temporary priority review handling for one request.", Price = 4.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 30, GrantCredits = 0, GrantEntitlementsJson = JsonSupport.Serialize(new { priorityReview = true }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = false, IsStackable = true, QuantityStep = 1, MaxQuantity = 1, DisplayOrder = 30, CreatedAt = now.AddMonths(-6), UpdatedAt = now }
        );

        var billingCouponIds = new[]
        {
            "coupon-welcome10",
            "coupon-review5"
        };

        var existingBillingCoupons = db.BillingCoupons
            .Where(coupon => billingCouponIds.Contains(coupon.Id))
            .ToList();
        if (existingBillingCoupons.Count > 0)
        {
            db.BillingCoupons.RemoveRange(existingBillingCoupons);
        }

        db.BillingCoupons.AddRange(
            new BillingCoupon { Id = "coupon-welcome10", Code = "WELCOME10", Name = "Welcome 10", Description = "10% off your first premium plan or add-on purchase.", DiscountType = BillingDiscountType.Percentage, DiscountValue = 10m, Currency = "AUD", Status = BillingCouponStatus.Active, StartsAt = now.AddMonths(-2), EndsAt = now.AddMonths(6), UsageLimitTotal = 1000, UsageLimitPerUser = 1, MinimumSubtotal = 19.99m, ApplicablePlanCodesJson = JsonSupport.Serialize(new[] { "premium-monthly", "premium-yearly", "intensive-monthly" }), ApplicableAddOnCodesJson = JsonSupport.Serialize(new[] { "credits-3", "credits-5", "priority-review" }), IsStackable = false, Notes = "Seeded welcome coupon.", RedemptionCount = 0, CreatedAt = now.AddMonths(-2), UpdatedAt = now.AddDays(-1) },
            new BillingCoupon { Id = "coupon-review5", Code = "REVIEW5", Name = "Review Pack 5", Description = "5 AUD off review credit packs.", DiscountType = BillingDiscountType.FixedAmount, DiscountValue = 5m, Currency = "AUD", Status = BillingCouponStatus.Active, StartsAt = now.AddMonths(-1), EndsAt = now.AddMonths(2), UsageLimitTotal = 500, UsageLimitPerUser = 2, MinimumSubtotal = 29.99m, ApplicablePlanCodesJson = JsonSupport.Serialize(new string[0]), ApplicableAddOnCodesJson = JsonSupport.Serialize(new[] { "credits-3", "credits-5" }), IsStackable = true, Notes = "Seeded add-on coupon.", RedemptionCount = 0, CreatedAt = now.AddMonths(-1), UpdatedAt = now.AddDays(-1) }
        );

        db.ContentRevisions.AddRange(
            new ContentRevision { Id = "rev-w01-1", ContentItemId = "writing-referral-01", RevisionNumber = 1, State = "draft", ChangeNote = "Initial creation", SnapshotJson = "{}", CreatedBy = "Admin", CreatedAt = now.AddDays(-30) },
            new ContentRevision { Id = "rev-w01-2", ContentItemId = "writing-referral-01", RevisionNumber = 2, State = "published", ChangeNote = "Published after QA review", SnapshotJson = "{}", CreatedBy = "Admin", CreatedAt = now.AddDays(-28) }
        );

    }
}
