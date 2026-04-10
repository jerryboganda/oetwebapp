using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static class SeedData
{
    private static readonly SemaphoreSlim DemoMediaSeedLock = new(1, 1);

    public const string LocalSeedPassword = "Password123!";
    public const string LearnerAuthAccountId = "auth_learner_local_001";
    public const string ExpertAuthAccountId = "auth_expert_local_001";
    public const string ExpertSecondaryAuthAccountId = "auth_expert_local_002";
    public const string AdminAuthAccountId = "auth_admin_local_001";
    public const string LearnerEmail = "learner@oet-prep.dev";
    public const string ExpertEmail = "expert@oet-prep.dev";
    public const string ExpertSecondaryEmail = "expert-unauthorised@oet-prep.dev";
    public const string AdminEmail = "admin@oet-prep.dev";

    public static async Task EnsureReferenceDataAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        var hasChanges = false;

        if (!await db.Professions.AnyAsync(cancellationToken))
        {
            SeedReferenceData(db);
            hasChanges = true;
        }

        if (!await db.SignupExamTypeCatalog.AnyAsync(cancellationToken))
        {
            SeedSignupCatalog(db);
            hasChanges = true;
        }

        if (!await db.ExamFamilies.AnyAsync(cancellationToken))
        {
            SeedExamFamilies(db);
            hasChanges = true;
        }

        if (!await db.BillingPlans.AnyAsync(cancellationToken))
        {
            SeedBillingPlans(db);
            hasChanges = true;
        }

        if (!await db.ExamTypes.AnyAsync(cancellationToken))
        {
            SeedExamTypes(db);
            hasChanges = true;
        }

        if (!await db.Achievements.AnyAsync(cancellationToken))
        {
            SeedAchievements(db);
            hasChanges = true;
        }

        if (!await db.VocabularyTerms.AnyAsync(cancellationToken))
        {
            SeedVocabularyTerms(db);
            hasChanges = true;
        }

        if (!await db.ForumCategories.AnyAsync(cancellationToken))
        {
            SeedForumCategories(db);
            hasChanges = true;
        }

        if (!await db.PronunciationDrills.AnyAsync(cancellationToken))
        {
            SeedPronunciationDrills(db);
            hasChanges = true;
        }

        if (!await db.ContentPackages.AnyAsync(cancellationToken))
        {
            SeedContentPackages(db);
            hasChanges = true;
        }

        if (!await db.ContentPrograms.AnyAsync(cancellationToken))
        {
            SeedContentPrograms(db);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static async Task EnsureDemoDataAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(x => x.Id == "mock-user-001", cancellationToken))
        {
            SeedDemoUser(db);
            await db.SaveChangesAsync(cancellationToken);
        }

        EnsureLocalAuthAccounts(db);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureDemoOperationalStateAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var demoReviewIds = new[] { "review-001", "review-queue-001", "review-queue-002" };

        var reviewRequests = await db.ReviewRequests
            .Where(reviewRequest => demoReviewIds.Contains(reviewRequest.Id))
            .ToDictionaryAsync(reviewRequest => reviewRequest.Id, cancellationToken);

        UpsertReviewRequest(
            db,
            reviewRequests,
            id: "review-001",
            attemptId: "wa-001",
            subtestCode: "writing",
            state: ReviewRequestState.Completed,
            turnaroundOption: "standard",
            focusAreas: ["conciseness", "genre"],
            learnerNotes: "Please focus on conciseness and layout.",
            paymentSource: "credits",
            priceSnapshot: 1m,
            createdAt: now.AddDays(-3),
            completedAt: now.AddDays(-1),
            eligibilitySnapshot: new { canRequestReview = true, reasonCodes = Array.Empty<string>() });

        UpsertReviewRequest(
            db,
            reviewRequests,
            id: "review-queue-001",
            attemptId: "sa-001",
            subtestCode: "speaking",
            state: ReviewRequestState.InReview,
            turnaroundOption: "express",
            focusAreas: ["fluency"],
            learnerNotes: "Please focus on flow and clarity.",
            paymentSource: "credits",
            priceSnapshot: 2m,
            createdAt: now.AddHours(-8),
            completedAt: null,
            eligibilitySnapshot: new { canRequestReview = true, reasonCodes = Array.Empty<string>() });

        UpsertReviewRequest(
            db,
            reviewRequests,
            id: "review-queue-002",
            attemptId: "wa-001",
            subtestCode: "writing",
            state: ReviewRequestState.InReview,
            turnaroundOption: "standard",
            focusAreas: ["content", "language"],
            learnerNotes: "Please focus on clinical relevance and language control.",
            paymentSource: "credits",
            priceSnapshot: 1m,
            createdAt: now.AddHours(-6),
            completedAt: null,
            eligibilitySnapshot: new { canRequestReview = true, reasonCodes = Array.Empty<string>() });

        var existingAssignments = await db.ExpertReviewAssignments
            .Where(assignment => demoReviewIds.Contains(assignment.ReviewRequestId))
            .ToListAsync(cancellationToken);
        if (existingAssignments.Count > 0)
        {
            db.ExpertReviewAssignments.RemoveRange(existingAssignments);
        }

        db.ExpertReviewAssignments.AddRange(
            new ExpertReviewAssignment
            {
                Id = "era-001",
                ReviewRequestId = "review-001",
                AssignedReviewerId = "expert-001",
                AssignedAt = now.AddDays(-2),
                ClaimState = ExpertAssignmentState.Released,
                ReleasedAt = now.AddDays(-1),
                ReasonCode = "submitted"
            },
            new ExpertReviewAssignment
            {
                Id = "era-queue-001",
                ReviewRequestId = "review-queue-001",
                AssignedReviewerId = "expert-001",
                AssignedAt = now.AddHours(-7),
                ClaimState = ExpertAssignmentState.Claimed
            },
            new ExpertReviewAssignment
            {
                Id = "era-queue-002",
                ReviewRequestId = "review-queue-002",
                AssignedReviewerId = "expert-001",
                AssignedAt = now.AddHours(-5),
                ClaimState = ExpertAssignmentState.Claimed
            });

        var existingDrafts = await db.ExpertReviewDrafts
            .Where(draft => demoReviewIds.Contains(draft.ReviewRequestId))
            .ToListAsync(cancellationToken);
        if (existingDrafts.Count > 0)
        {
            db.ExpertReviewDrafts.RemoveRange(existingDrafts);
        }

        db.ExpertReviewDrafts.AddRange(
            new ExpertReviewDraft
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
                ScratchpadJson = JsonSupport.Serialize("Double-check whether the safety-net advice is explicit enough for community follow-up."),
                ChecklistItemsJson = JsonSupport.Serialize(new[]
                {
                    new { id = "purpose", label = "Purpose is explicit in the opening lines.", @checked = true },
                    new { id = "content", label = "Only clinically relevant post-operative facts remain.", @checked = true },
                    new { id = "safety-net", label = "Follow-up and escalation advice are obvious to the receiving clinician.", @checked = false }
                }),
                DraftSavedAt = now.AddDays(-1)
            },
            new ExpertReviewDraft
            {
                Id = "erd-queue-002",
                ReviewRequestId = "review-queue-002",
                ReviewerId = "expert-001",
                Version = 2,
                State = "saved",
                RubricEntriesJson = JsonSupport.Serialize(new Dictionary<string, int>
                {
                    ["purpose"] = 4, ["content"] = 4, ["conciseness"] = 3, ["genre"] = 4, ["organization"] = 4, ["language"] = 3
                }),
                CriterionCommentsJson = JsonSupport.Serialize(new Dictionary<string, string>
                {
                    ["content"] = "The main referral reason is clear, but a few details still compete with the urgent follow-up request.",
                    ["language"] = "Expression is mostly controlled, with a couple of phrasing choices worth smoothing before submission."
                }),
                FinalCommentDraft = "Promising draft. Tighten the referral request and reduce lower-value history so the receiving clinician sees the action sooner.",
                ScratchpadJson = JsonSupport.Serialize("Re-check whether the urgent follow-up request appears early enough for the reader."),
                ChecklistItemsJson = JsonSupport.Serialize(new[]
                {
                    new { id = "purpose", label = "Referral purpose is immediately obvious.", @checked = true },
                    new { id = "content", label = "Only details that change clinical follow-up are retained.", @checked = false },
                    new { id = "closing", label = "Closing request clearly tells the receiving clinician what action is needed next.", @checked = false }
                }),
                DraftSavedAt = now.AddMinutes(-90)
            });

        var existingAuditEvents = await db.AuditEvents
            .Where(auditEvent => auditEvent.ResourceType == "ExpertReview" && auditEvent.ResourceId != null && demoReviewIds.Contains(auditEvent.ResourceId))
            .ToListAsync(cancellationToken);
        if (existingAuditEvents.Count > 0)
        {
            db.AuditEvents.RemoveRange(existingAuditEvents);
        }

        db.AuditEvents.AddRange(
            new AuditEvent
            {
                Id = "aud-exp-demo-001",
                OccurredAt = now.AddDays(-1),
                ActorId = "expert-001",
                ActorName = "Expert Reviewer",
                Action = "Submitted Writing Review",
                ResourceType = "ExpertReview",
                ResourceId = "review-001",
                Details = "Clear improvement in structure and clinical filtering."
            },
            new AuditEvent
            {
                Id = "aud-exp-demo-002",
                OccurredAt = now.AddHours(-7),
                ActorId = "expert-001",
                ActorName = "Expert Reviewer",
                Action = "Claimed Review",
                ResourceType = "ExpertReview",
                ResourceId = "review-queue-001",
                Details = "Speaking review claimed from the expert queue."
            },
            new AuditEvent
            {
                Id = "aud-exp-demo-003",
                OccurredAt = now.AddHours(-5),
                ActorId = "expert-001",
                ActorName = "Expert Reviewer",
                Action = "Claimed Review",
                ResourceType = "ExpertReview",
                ResourceId = "review-queue-002",
                Details = "Writing review claimed from the expert queue."
            },
            new AuditEvent
            {
                Id = "aud-exp-demo-004",
                OccurredAt = now.AddMinutes(-90),
                ActorId = "expert-001",
                ActorName = "Expert Reviewer",
                Action = "Saved Review Draft",
                ResourceType = "ExpertReview",
                ResourceId = "review-queue-002",
                Details = "Expert review draft saved."
            });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureDemoMediaAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        StorageOptions storageOptions,
        CancellationToken cancellationToken = default)
    {
        const string speakingAttemptId = "sa-001";
        const string demoAudioStorageKey = "audio/sa-001.wav";
        const string demoAudioContentType = "audio/wav";

        var attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == speakingAttemptId, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        var hasChanges = false;
        if (!string.Equals(attempt.AudioObjectKey, demoAudioStorageKey, StringComparison.Ordinal))
        {
            attempt.AudioObjectKey = demoAudioStorageKey;
            hasChanges = true;
        }

        var metadata = JsonSupport.Deserialize(attempt.AudioMetadataJson, new Dictionary<string, object?>());
        if (!metadata.TryGetValue("contentType", out var contentType) || !string.Equals(contentType?.ToString(), demoAudioContentType, StringComparison.OrdinalIgnoreCase))
        {
            metadata["contentType"] = demoAudioContentType;
            attempt.AudioMetadataJson = JsonSupport.Serialize(metadata);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var fullPath = ResolveStoragePath(environment, storageOptions, demoAudioStorageKey);
        await DemoMediaSeedLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(fullPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, BuildDemoWaveFile(), cancellationToken);
        }
        finally
        {
            DemoMediaSeedLock.Release();
        }
    }

    private static void SeedDemoUser(LearnerDbContext db)
    {
        SeedDemoUserCore(db);
        SeedDemoUserData(db);
    }

    private static void UpsertReviewRequest(
        LearnerDbContext db,
        IDictionary<string, ReviewRequest> reviewRequests,
        string id,
        string attemptId,
        string subtestCode,
        ReviewRequestState state,
        string turnaroundOption,
        IReadOnlyList<string> focusAreas,
        string learnerNotes,
        string paymentSource,
        decimal priceSnapshot,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        object eligibilitySnapshot)
    {
        if (!reviewRequests.TryGetValue(id, out var reviewRequest))
        {
            reviewRequest = new ReviewRequest
            {
                Id = id
            };
            db.ReviewRequests.Add(reviewRequest);
            reviewRequests[id] = reviewRequest;
        }

        reviewRequest.AttemptId = attemptId;
        reviewRequest.SubtestCode = subtestCode;
        reviewRequest.State = state;
        reviewRequest.TurnaroundOption = turnaroundOption;
        reviewRequest.FocusAreasJson = JsonSupport.Serialize(focusAreas);
        reviewRequest.LearnerNotes = learnerNotes;
        reviewRequest.PaymentSource = paymentSource;
        reviewRequest.PriceSnapshot = priceSnapshot;
        reviewRequest.CreatedAt = createdAt;
        reviewRequest.CompletedAt = completedAt;
        reviewRequest.EligibilitySnapshotJson = JsonSupport.Serialize(eligibilitySnapshot);
    }

    private static void EnsureLocalAuthAccounts(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var passwordHasher = new PasswordHasher<ApplicationUserAccount>();

        var learner = db.Users.Single(x => x.Id == "mock-user-001");
        var expert = db.ExpertUsers.Single(x => x.Id == "expert-001");
        var secondaryExpert = db.ExpertUsers.SingleOrDefault(x => x.Id == "expert-unauthorised");
        if (secondaryExpert is null)
        {
            secondaryExpert = new ExpertUser
            {
                Id = "expert-unauthorised",
                Role = ApplicationUserRoles.Expert,
                DisplayName = "Expert Reviewer Two",
                Email = ExpertSecondaryEmail,
                SpecialtiesJson = JsonSupport.Serialize(new[] { "nursing" }),
                Timezone = "Australia/Sydney",
                IsActive = true,
                CreatedAt = now.AddMonths(-4)
            };
            db.ExpertUsers.Add(secondaryExpert);
        }

        var learnerAccount = UpsertLocalAuthAccount(
            db,
            passwordHasher,
            LearnerAuthAccountId,
            LearnerEmail,
            ApplicationUserRoles.Learner,
            now.AddMonths(-3));
        learner.AuthAccountId = learnerAccount.Id;
        learner.Email = learnerAccount.Email;

        var expertAccount = UpsertLocalAuthAccount(
            db,
            passwordHasher,
            ExpertAuthAccountId,
            ExpertEmail,
            ApplicationUserRoles.Expert,
            now.AddMonths(-6));
        expert.AuthAccountId = expertAccount.Id;
        expert.Email = expertAccount.Email;

        var secondaryExpertAccount = UpsertLocalAuthAccount(
            db,
            passwordHasher,
            ExpertSecondaryAuthAccountId,
            ExpertSecondaryEmail,
            ApplicationUserRoles.Expert,
            now.AddMonths(-4));
        secondaryExpert.AuthAccountId = secondaryExpertAccount.Id;
        secondaryExpert.Email = secondaryExpertAccount.Email;

        var adminAccount = UpsertLocalAuthAccount(
            db,
            passwordHasher,
            AdminAuthAccountId,
            AdminEmail,
            ApplicationUserRoles.Admin,
            now.AddMonths(-6));

        var localAuthAccountIds = new[]
        {
            learnerAccount.Id,
            expertAccount.Id,
            secondaryExpertAccount.Id,
            adminAccount.Id
        };

        var existingRecoveryCodes = db.MfaRecoveryCodes
            .Where(x => localAuthAccountIds.Contains(x.ApplicationUserAccountId))
            .ToList();
        if (existingRecoveryCodes.Count > 0)
        {
            db.MfaRecoveryCodes.RemoveRange(existingRecoveryCodes);
        }

        foreach (var auditEvent in db.AuditEvents.Where(x => x.ActorId == "admin-user-001" && x.ActorAuthAccountId != adminAccount.Id))
        {
            auditEvent.ActorAuthAccountId = adminAccount.Id;
        }
    }

    private static ApplicationUserAccount UpsertLocalAuthAccount(
        LearnerDbContext db,
        PasswordHasher<ApplicationUserAccount> passwordHasher,
        string accountId,
        string email,
        string role,
        DateTimeOffset createdAt)
    {
        var normalizedEmail = email.ToUpperInvariant();
        var account = db.ApplicationUserAccounts
            .SingleOrDefault(x => x.Id == accountId || x.NormalizedEmail == normalizedEmail);

        if (account is null)
        {
            account = new ApplicationUserAccount
            {
                Id = accountId,
                CreatedAt = createdAt
            };
            db.ApplicationUserAccounts.Add(account);
        }

        account.Email = email;
        account.NormalizedEmail = normalizedEmail;
        account.Role = role;
        account.EmailVerifiedAt ??= createdAt;
        account.ProtectedAuthenticatorSecret = null;
        account.AuthenticatorEnabledAt = null;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        account.PasswordHash = passwordHasher.HashPassword(account, LocalSeedPassword);
        return account;
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
            LastActiveAt = now.AddMinutes(-10),
            // Engagement tracking
            CurrentStreak = 7,
            LongestStreak = 14,
            LastPracticeDate = now.AddHours(-2),
            TotalPracticeMinutes = 1860,
            TotalPracticeSessions = 42,
            WeeklyActivityJson = JsonSupport.Serialize(new[] { true, true, true, false, true, true, true })
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
            new ProfessionReference { Id = "physiotherapy", Code = "physiotherapy", Label = "Physiotherapy", Status = "active", SortOrder = 5 },
            new ProfessionReference { Id = "academic-english", Code = "academic-english", Label = "Academic / General English", Status = "active", SortOrder = 6 }
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
            new CriterionReference { Id = "cri-conciseness", SubtestCode = "writing", Code = "conciseness", Label = "Conciseness & Clarity", Description = "Clarity of writing without unnecessary detail.", Status = "active", SortOrder = 3 },
            new CriterionReference { Id = "cri-genre", SubtestCode = "writing", Code = "genre", Label = "Genre & Style", Description = "Appropriate register and professional tone.", Status = "active", SortOrder = 4 },
            new CriterionReference { Id = "cri-organization", SubtestCode = "writing", Code = "organization", Label = "Organisation & Layout", Description = "Logical structure and formatting.", Status = "active", SortOrder = 5 },
            new CriterionReference { Id = "cri-language", SubtestCode = "writing", Code = "language", Label = "Language", Description = "Accuracy and range of grammar and vocabulary.", Status = "active", SortOrder = 6 },
            new CriterionReference { Id = "cri-intelligibility", SubtestCode = "speaking", Code = "intelligibility", Label = "Intelligibility", Description = "Pronunciation, stress, and clarity.", Status = "active", SortOrder = 1 },
            new CriterionReference { Id = "cri-fluency", SubtestCode = "speaking", Code = "fluency", Label = "Fluency", Description = "Smoothness, pacing, and hesitation control.", Status = "active", SortOrder = 2 },
            new CriterionReference { Id = "cri-appropriateness", SubtestCode = "speaking", Code = "appropriateness", Label = "Appropriateness of Language", Description = "Suitability of professional vocabulary and tone.", Status = "active", SortOrder = 3 },
            new CriterionReference { Id = "cri-grammar-expression", SubtestCode = "speaking", Code = "grammar_expression", Label = "Resources of Grammar and Expression", Description = "Range and accuracy of spoken language.", Status = "active", SortOrder = 4 }
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
                    new { criterion = "purpose", benchmarkScore = 4, rationale = "Referral purpose is established immediately." },
                    new { criterion = "content", benchmarkScore = 5, rationale = "The strongest benchmark keeps only information that changes follow-up urgency." },
                    new { criterion = "conciseness", benchmarkScore = 3, rationale = "A few extra procedural details reduce efficiency." },
                    new { criterion = "genre", benchmarkScore = 4, rationale = "Professional referral register is sustained." },
                    new { criterion = "organization", benchmarkScore = 4, rationale = "Information flows from reason for referral to current concerns and request." },
                    new { criterion = "language", benchmarkScore = 4, rationale = "Minor slips remain, but overall control is strong." }
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
                    new { criterion = "grammar", benchmarkScore = 5, rationale = "Grammar stays controlled throughout the handover." },
                    new { criterion = "clinicalCommunication", benchmarkScore = 5, rationale = "Prioritisation, escalation, and safety-netting are explicit." }
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
            // ── Existing operational flags ──
            new FeatureFlag { Id = "flg-001", Name = "AI Scoring V2", Key = "ai_scoring_v2", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable AI scoring V2 pipeline for all subtests.", Owner = "Platform Team", CreatedAt = now.AddDays(-60), UpdatedAt = now.AddDays(-5) },
            new FeatureFlag { Id = "flg-002", Name = "Mock Exam Timer", Key = "mock_exam_timer", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Show countdown timers in full mock exams.", Owner = "Product", CreatedAt = now.AddDays(-45), UpdatedAt = now.AddDays(-10) },
            new FeatureFlag { Id = "flg-003", Name = "Expert Double Review", Key = "expert_double_review", FlagType = FeatureFlagType.Experiment, Enabled = false, RolloutPercentage = 25, Description = "A/B test: assign two expert reviewers to each writing submission.", Owner = "QA Team", CreatedAt = now.AddDays(-14), UpdatedAt = now.AddDays(-1) },
            new FeatureFlag { Id = "flg-004", Name = "Maintenance Banner", Key = "maintenance_banner", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Show maintenance notice banner across the platform.", Owner = "DevOps", CreatedAt = now.AddDays(-90), UpdatedAt = now.AddDays(-30) },
            // ── Phase 1 new feature flags ──
            new FeatureFlag { Id = "flg-005", Name = "Multi-Exam Foundation", Key = "multi_exam_foundation", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable multi-exam support (IELTS, PTE, Cambridge, TOEFL) alongside OET.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-006", Name = "Adaptive Difficulty", Key = "adaptive_difficulty", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable Elo-based adaptive difficulty engine for content recommendations.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-007", Name = "Spaced Repetition", Key = "spaced_repetition", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable SM-2 spaced repetition review system for mistakes and weak areas.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-008", Name = "Gamification", Key = "gamification", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable XP, streaks, achievements, and leaderboard gamification mechanics.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-009", Name = "Vocabulary Builder", Key = "vocabulary_builder", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable medical and academic vocabulary builder with flashcards and quizzes.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-010", Name = "AI Content Generation", Key = "ai_content_generation", FlagType = FeatureFlagType.Operational, Enabled = false, RolloutPercentage = 0, Description = "Enable admin AI content generation tool.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            // ── Phase 2 new feature flags ──
            new FeatureFlag { Id = "flg-011", Name = "AI Conversation Practice", Key = "ai_conversation", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable AI roleplay conversation partner for speaking practice.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-012", Name = "AI Writing Coach", Key = "ai_writing_coach", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable real-time AI writing suggestions in the writing editor.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-013", Name = "Pronunciation Analysis", Key = "pronunciation_analysis", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable phoneme-level pronunciation analysis and drills.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-014", Name = "Performance Prediction", Key = "performance_prediction", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable predicted score forecasting based on practice history.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            // ── Phase 3 new feature flags ──
            new FeatureFlag { Id = "flg-015", Name = "Grammar Lessons", Key = "grammar_lessons", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable structured grammar lesson modules.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-016", Name = "Video Lessons", Key = "video_lessons", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable video lesson catalogue.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-017", Name = "Strategy Guides", Key = "strategy_guides", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable written exam strategy guides.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-018", Name = "Community Forums", Key = "community_forums", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable community discussion forums and study groups.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-019", Name = "Live Tutoring", Key = "live_tutoring", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable one-on-one live tutoring sessions with experts.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-020", Name = "Certificates", Key = "certificates", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable achievement certificate generation and download.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-021", Name = "Referral Program", Key = "referral_program", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable referral program with credit rewards.", Owner = "Growth", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-022", Name = "Sponsor Dashboard", Key = "sponsor_cohorts", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable sponsor dashboard and cohort management.", Owner = "Enterprise", CreatedAt = now, UpdatedAt = now },
            // ── Phase 4 new feature flags ──
            new FeatureFlag { Id = "flg-023", Name = "Exam Booking", Key = "exam_booking", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable exam booking integration with official booking portals.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-024", Name = "Content Marketplace", Key = "content_marketplace", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable content contributor marketplace.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-025", Name = "Offline Mode", Key = "offline_mode", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable offline practice mode for mobile.", Owner = "Mobile Team", CreatedAt = now, UpdatedAt = now }
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
            new BillingPlan { Id = "plan-premium-monthly", Code = "premium-monthly", Name = "Premium Monthly", Description = "Adds productive-skill review capacity and richer mock support for active preparation.", Price = 49.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 20, IncludedCredits = 3, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 1250, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-premium-yearly", Code = "premium-yearly", Name = "Premium Yearly", Description = "Annual premium access with the same learner benefits and stronger retention value.", Price = 399.99m, Currency = "AUD", Interval = "yearly", DurationMonths = 12, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 30, IncludedCredits = 6, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 820, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-basic-monthly", Code = "basic-monthly", Name = "Basic Monthly", Description = "Core OET practice with AI evaluation and learner analytics.", Price = 19.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 10, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 3400, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-18), UpdatedAt = now.AddDays(-10) },
            new BillingPlan { Id = "plan-intensive-monthly", Code = "intensive-monthly", Name = "Intensive Monthly", Description = "Higher review capacity for repeated writing and speaking feedback before the exam window.", Price = 79.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 40, IncludedCredits = 8, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 540, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-10), UpdatedAt = now.AddDays(-3) },
            new BillingPlan { Id = "plan-legacy-trial", Code = "legacy-trial", Name = "Legacy Trial", Description = "Legacy trial plan retained for compatibility.", Price = 0m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = false, IsRenewable = false, TrialDays = 14, DisplayOrder = 0, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 0, Status = BillingPlanStatus.Legacy, CreatedAt = now.AddMonths(-24), UpdatedAt = now.AddMonths(-6) }
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
            new BillingAddOn { Id = "addon-credits-3", Code = "credits-3", Name = "3 Review Credits", Description = "Pack of 3 expert review credits.", Price = 29.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 0, GrantCredits = 3, GrantEntitlementsJson = JsonSupport.Serialize(new { reviewCredits = 3 }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "basic-monthly", "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = true, IsStackable = true, QuantityStep = 1, MaxQuantity = 5, DisplayOrder = 10, CreatedAt = now.AddMonths(-8), UpdatedAt = now.AddDays(-4) },
            new BillingAddOn { Id = "addon-credits-5", Code = "credits-5", Name = "5 Review Credits", Description = "Pack of 5 expert review credits.", Price = 44.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 0, GrantCredits = 5, GrantEntitlementsJson = JsonSupport.Serialize(new { reviewCredits = 5 }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "basic-monthly", "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = true, IsStackable = true, QuantityStep = 1, MaxQuantity = 5, DisplayOrder = 20, CreatedAt = now.AddMonths(-8), UpdatedAt = now.AddDays(-4) },
            new BillingAddOn { Id = "addon-priority-review", Code = "priority-review", Name = "Priority Review Pack", Description = "Temporary priority review handling for one request.", Price = 14.99m, Currency = "AUD", Interval = "one_time", Status = BillingAddOnStatus.Active, IsRecurring = false, DurationDays = 30, GrantCredits = 0, GrantEntitlementsJson = JsonSupport.Serialize(new { priorityReview = true }), CompatiblePlanCodesJson = JsonSupport.Serialize(new[] { "premium-monthly", "premium-yearly", "intensive-monthly" }), AppliesToAllPlans = false, IsStackable = true, QuantityStep = 1, MaxQuantity = 1, DisplayOrder = 30, CreatedAt = now.AddMonths(-6), UpdatedAt = now.AddDays(-2) }
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

    private static void SeedSignupCatalog(LearnerDbContext db)
    {
        db.SignupExamTypeCatalog.AddRange(
            new SignupExamTypeCatalog
            {
                Id = "oet",
                Label = "OET",
                Code = "OET",
                Description = "Occupational English Test preparation and enrollment.",
                SortOrder = 1,
                IsActive = true
            },
            new SignupExamTypeCatalog
            {
                Id = "ielts",
                Label = "IELTS",
                Code = "IELTS",
                Description = "IELTS preparation and session enrollment.",
                SortOrder = 2,
                IsActive = true
            });

        db.SignupProfessionCatalog.AddRange(
            new SignupProfessionCatalog
            {
                Id = "nursing",
                Label = "Nursing",
                CountryTargetsJson = JsonSupport.Serialize(new[] { "Australia", "New Zealand" }),
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Registered nurse and clinical nursing candidates.",
                SortOrder = 1,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "medicine",
                Label = "Medicine",
                CountryTargetsJson = JsonSupport.Serialize(new[] { "United Kingdom", "Australia" }),
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Doctors and physicians preparing for healthcare pathways.",
                SortOrder = 2,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "pharmacy",
                Label = "Pharmacy",
                CountryTargetsJson = JsonSupport.Serialize(new[] { "Ireland", "Australia" }),
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Pharmacists and pharmacy practice candidates.",
                SortOrder = 3,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "dentistry",
                Label = "Dentistry",
                CountryTargetsJson = JsonSupport.Serialize(new[] { "United Kingdom", "New Zealand" }),
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Dental professionals and dentistry applicants.",
                SortOrder = 4,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "academic-english",
                Label = "Academic / General English",
                CountryTargetsJson = JsonSupport.Serialize(new[] { "Canada", "United Kingdom", "Australia" }),
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "ielts" }),
                Description = "General academic and migration IELTS candidates.",
                SortOrder = 5,
                IsActive = true
            });

        db.SignupSessionCatalog.AddRange(
            new SignupSessionCatalog
            {
                Id = "session-oet-nursing-apr",
                Name = "OET Nursing April Cohort",
                ExamTypeId = "oet",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "nursing" }),
                PriceLabel = "$299",
                StartDate = "2026-04-06",
                EndDate = "2026-06-28",
                DeliveryMode = "online",
                Capacity = 40,
                SeatsRemaining = 11,
                SortOrder = 1,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-oet-medicine-may",
                Name = "OET Medicine Intensive",
                ExamTypeId = "oet",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "medicine", "dentistry", "pharmacy" }),
                PriceLabel = "$349",
                StartDate = "2026-05-11",
                EndDate = "2026-07-05",
                DeliveryMode = "hybrid",
                Capacity = 32,
                SeatsRemaining = 9,
                SortOrder = 2,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-ielts-foundation-apr",
                Name = "IELTS Foundation Sprint",
                ExamTypeId = "ielts",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "academic-english" }),
                PriceLabel = "$199",
                StartDate = "2026-04-20",
                EndDate = "2026-06-01",
                DeliveryMode = "online",
                Capacity = 60,
                SeatsRemaining = 21,
                SortOrder = 3,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-ielts-weekend-may",
                Name = "IELTS Weekend Cohort",
                ExamTypeId = "ielts",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "academic-english" }),
                PriceLabel = "$239",
                StartDate = "2026-05-23",
                EndDate = "2026-07-19",
                DeliveryMode = "online",
                Capacity = 45,
                SeatsRemaining = 0,
                SortOrder = 4,
                IsActive = true
            });
    }

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
                    new { code = "listening", label = "Listening", duration = 40, isProfessionSpecific = false }
                }),
                CriteriaConfigJson = JsonSupport.Serialize(new object[]
                {
                    new { subtest = "writing", criteria = new[] { "purpose", "content", "conciseness", "genre", "organization", "language" } },
                    new { subtest = "speaking", criteria = new[] { "intelligibility", "fluency", "appropriateness", "grammar_expression" } }
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

    private static void SeedBillingPlans(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        db.BillingPlans.AddRange(
            new BillingPlan { Id = "plan-basic-monthly", Code = "basic-monthly", Name = "Basic Monthly", Description = "Core OET practice with AI evaluation and learner analytics.", Price = 19.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 10, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 3400, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-18), UpdatedAt = now.AddDays(-10) },
            new BillingPlan { Id = "plan-premium-monthly", Code = "premium-monthly", Name = "Premium Monthly", Description = "Adds productive-skill review capacity and richer mock support for active preparation.", Price = 49.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 20, IncludedCredits = 3, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 1250, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-premium-yearly", Code = "premium-yearly", Name = "Premium Yearly", Description = "Annual premium access with the same learner benefits and stronger retention value.", Price = 399.99m, Currency = "AUD", Interval = "yearly", DurationMonths = 12, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 30, IncludedCredits = 6, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 820, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-12), UpdatedAt = now.AddDays(-5) },
            new BillingPlan { Id = "plan-intensive-monthly", Code = "intensive-monthly", Name = "Intensive Monthly", Description = "Higher review capacity for repeated writing and speaking feedback before the exam window.", Price = 79.99m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = true, IsRenewable = true, TrialDays = 0, DisplayOrder = 40, IncludedCredits = 8, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 540, Status = BillingPlanStatus.Active, CreatedAt = now.AddMonths(-10), UpdatedAt = now.AddDays(-3) },
            new BillingPlan { Id = "plan-legacy-trial", Code = "legacy-trial", Name = "Legacy Trial", Description = "Legacy trial plan retained for compatibility.", Price = 0m, Currency = "AUD", Interval = "monthly", DurationMonths = 1, IsVisible = false, IsRenewable = false, TrialDays = 14, DisplayOrder = 0, IncludedCredits = 0, IncludedSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }), EntitlementsJson = JsonSupport.Serialize(new { productiveSkillReviewsEnabled = true, invoiceDownloadsAvailable = true }), ActiveSubscribers = 0, Status = BillingPlanStatus.Legacy, CreatedAt = now.AddMonths(-24), UpdatedAt = now.AddMonths(-6) }
        );
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
            new VocabularyTerm { Id = "vt-001", Term = "analgesia", Definition = "The absence of pain or the relief of pain without loss of consciousness.", ExampleSentence = "The patient was given analgesia to manage post-operative pain.", ContextNotes = "Commonly used when referring to pain management in clinical settings.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["pain relief","pain management"]""", CollocationsJson = """["provide analgesia","adequate analgesia","post-operative analgesia"]""", RelatedTermsJson = """["analgesic","nociception","pain score"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-002", Term = "dyspnoea", Definition = "Difficulty or laboured breathing; shortness of breath.", ExampleSentence = "The patient presented with acute dyspnoea on exertion.", ContextNotes = "Preferred clinical spelling in Australian/UK English (vs. 'dyspnea' in US English).", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["shortness of breath","breathlessness","SOB"]""", CollocationsJson = """["acute dyspnoea","dyspnoea on exertion","nocturnal dyspnoea"]""", RelatedTermsJson = """["tachypnoea","orthopnoea","respiratory distress"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-003", Term = "haemoptysis", Definition = "The coughing up of blood or blood-stained mucus from the bronchi, larynx, trachea, or lungs.", ExampleSentence = "The referral letter noted intermittent haemoptysis over the past three weeks.", ContextNotes = "Important OET writing term; always flag as a red flag symptom.", ExamTypeCode = "oet", Category = "medical", Difficulty = "hard", SynonymsJson = """["coughing up blood"]""", CollocationsJson = """["frank haemoptysis","intermittent haemoptysis","massive haemoptysis"]""", RelatedTermsJson = """["haematemesis","epistaxis","pulmonary embolism"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-004", Term = "oedema", Definition = "Swelling caused by excess fluid trapped in the body's tissues.", ExampleSentence = "Peripheral oedema was noted bilaterally up to the knees.", ContextNotes = "Australian/UK spelling. US spelling is 'edema'.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["edema","swelling","fluid retention"]""", CollocationsJson = """["peripheral oedema","pitting oedema","pulmonary oedema","bilateral oedema"]""", RelatedTermsJson = """["ascites","pleural effusion","lymphoedema"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-005", Term = "tachycardia", Definition = "Abnormally rapid heart rate, typically defined as over 100 beats per minute in adults.", ExampleSentence = "The patient was in sinus tachycardia at 118 bpm on admission.", ContextNotes = "Can be physiological (exercise) or pathological; specify subtype in clinical notes.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["rapid heart rate","fast pulse"]""", CollocationsJson = """["sinus tachycardia","ventricular tachycardia","resting tachycardia"]""", RelatedTermsJson = """["bradycardia","arrhythmia","palpitations"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-006", Term = "hypotension", Definition = "Abnormally low blood pressure, generally defined as systolic BP below 90 mmHg.", ExampleSentence = "Postural hypotension was identified as the likely cause of the patient's recurrent falls.", ContextNotes = "Distinguish orthostatic/postural hypotension from general hypotension in referrals.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["low blood pressure"]""", CollocationsJson = """["postural hypotension","orthostatic hypotension","severe hypotension"]""", RelatedTermsJson = """["hypertension","syncope","vasodilation"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-007", Term = "contraindicated", Definition = "Not recommended or inadvisable due to the potential for harm in a specific situation.", ExampleSentence = "NSAIDs are contraindicated in patients with active peptic ulcer disease.", ContextNotes = "Passive form 'is contraindicated' is standard in clinical writing.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "medium", SynonymsJson = """["not recommended","inadvisable","unsafe"]""", CollocationsJson = """["contraindicated in","absolutely contraindicated","relatively contraindicated"]""", RelatedTermsJson = """["precaution","adverse reaction","side effect"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-008", Term = "prognosis", Definition = "A forecast of the likely course and outcome of a disease or condition.", ExampleSentence = "The prognosis for stage II breast cancer with current treatment protocols is generally favourable.", ContextNotes = "Distinguish from 'diagnosis' (identification of condition) in OET speaking scenarios.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "easy", SynonymsJson = """["outlook","forecast","expected outcome"]""", CollocationsJson = """["poor prognosis","favourable prognosis","guarded prognosis","overall prognosis"]""", RelatedTermsJson = """["diagnosis","diagnosis and management","treatment outcome"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-009", Term = "exacerbation", Definition = "A worsening or increase in severity of a disease or its symptoms.", ExampleSentence = "The patient was admitted with an acute exacerbation of COPD.", ContextNotes = "Frequently appears in OET writing tasks for chronic disease referrals.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["flare-up","deterioration","worsening"]""", CollocationsJson = """["acute exacerbation","exacerbation of COPD","prevent exacerbation"]""", RelatedTermsJson = """["remission","relapse","deterioration"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-010", Term = "palliative", Definition = "Providing relief from the symptoms of a serious illness without curing it; relating to end-of-life care.", ExampleSentence = "The patient and family elected to pursue palliative care rather than further curative treatment.", ContextNotes = "Sensitive term; use with care in OET speaking scenarios involving end-of-life discussions.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "medium", SynonymsJson = """["comfort care","supportive care","end-of-life care"]""", CollocationsJson = """["palliative care","palliative approach","palliative management","palliative intent"]""", RelatedTermsJson = """["hospice","curative","terminal illness"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-011", Term = "aetiology", Definition = "The cause, set of causes, or manner of causation of a disease or condition.", ExampleSentence = "The aetiology of the patient's hypertension was considered to be multifactorial.", ContextNotes = "Australian/UK spelling; US English uses 'etiology'.", ExamTypeCode = "oet", Category = "medical", Difficulty = "hard", SynonymsJson = """["cause","etiology","origin"]""", CollocationsJson = """["unknown aetiology","multifactorial aetiology","aetiology of"]""", RelatedTermsJson = """["pathophysiology","risk factor","precipitating factor"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-012", Term = "pyrexia", Definition = "Fever; an abnormally high body temperature, typically above 38°C.", ExampleSentence = "The child presented with pyrexia of unknown origin for five days.", ContextNotes = "Formal clinical term; 'fever' is acceptable in patient-facing communication.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["fever","febrile","elevated temperature"]""", CollocationsJson = """["pyrexia of unknown origin","low-grade pyrexia","high pyrexia"]""", RelatedTermsJson = """["hyperpyrexia","hypothermia","sepsis"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-013", Term = "diuresis", Definition = "Increased or excessive production of urine.", ExampleSentence = "Forced diuresis was initiated to manage the patient's fluid overload.", ContextNotes = "Often used in renal and cardiac nursing contexts.", ExamTypeCode = "oet", Category = "medical", Difficulty = "hard", SynonymsJson = """["urine output","polyuria"]""", CollocationsJson = """["forced diuresis","osmotic diuresis","inadequate diuresis"]""", RelatedTermsJson = """["oliguria","anuria","fluid balance"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-014", Term = "ambulate", Definition = "To walk or be able to walk; to move around under one's own power.", ExampleSentence = "The physiotherapist documented that the patient could ambulate 20 metres with a walking frame.", ContextNotes = "Common in rehabilitation and nursing notes.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "easy", SynonymsJson = """["walk","mobilise","move independently"]""", CollocationsJson = """["ambulate independently","ambulate with assistance","unable to ambulate"]""", RelatedTermsJson = """["mobilisation","gait","transfers"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-015", Term = "haemorrhage", Definition = "Escape of blood from a ruptured blood vessel; heavy bleeding.", ExampleSentence = "Emergency surgery was required due to post-partum haemorrhage.", ContextNotes = "Australian/UK spelling; US spelling is 'hemorrhage'.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["bleeding","hemorrhage","blood loss"]""", CollocationsJson = """["postpartum haemorrhage","intracranial haemorrhage","subarachnoid haemorrhage","major haemorrhage"]""", RelatedTermsJson = """["coagulopathy","transfusion","haemostasis"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-016", Term = "nausea", Definition = "A feeling of sickness with an inclination to vomit.", ExampleSentence = "The patient reported persistent nausea and one episode of vomiting following chemotherapy.", ContextNotes = "Distinguish from vomiting (emesis); both may need to be reported in OET writing tasks.", ExamTypeCode = "oet", Category = "medical", Difficulty = "easy", SynonymsJson = """["queasiness","sickness","feeling sick"]""", CollocationsJson = """["nausea and vomiting","persistent nausea","intractable nausea","post-operative nausea"]""", RelatedTermsJson = """["emesis","antiemetic","dyspepsia"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-017", Term = "aspiration", Definition = "The inhalation of food, liquid, or foreign material into the airway; or the act of drawing fluid out of a body cavity.", ExampleSentence = "The patient was at high risk of aspiration due to impaired swallowing function.", ContextNotes = "Dual meaning — context determines which sense is intended.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["inhalation","silent aspiration"]""", CollocationsJson = """["aspiration risk","aspiration pneumonia","silent aspiration","aspiration of fluid"]""", RelatedTermsJson = """["dysphagia","choking","pneumonia"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-018", Term = "arrhythmia", Definition = "An irregular or abnormal heart rhythm.", ExampleSentence = "The ECG confirmed a new arrhythmia that required urgent cardiology review.", ContextNotes = "Umbrella term encompassing tachycardia, bradycardia, fibrillation, etc.", ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", SynonymsJson = """["dysrhythmia","irregular heartbeat","cardiac dysrhythmia"]""", CollocationsJson = """["cardiac arrhythmia","arrhythmia management","ventricular arrhythmia","atrial arrhythmia"]""", RelatedTermsJson = """["atrial fibrillation","tachycardia","bradycardia","ECG"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-019", Term = "benign", Definition = "Not malignant; (of a tumour) not cancerous and not tending to spread.", ExampleSentence = "The biopsy results confirmed the lesion was benign.", ContextNotes = "Contrast with 'malignant' — critical distinction in OET writing referral letters.", ExamTypeCode = "oet", Category = "medical", Difficulty = "easy", SynonymsJson = """["non-cancerous","non-malignant","harmless"]""", CollocationsJson = """["benign tumour","benign lesion","benign condition","benign prostatic hyperplasia"]""", RelatedTermsJson = """["malignant","carcinoma","neoplasm"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-020", Term = "prophylaxis", Definition = "Treatment given to prevent disease rather than to treat an existing disease.", ExampleSentence = "The patient was commenced on antibiotic prophylaxis prior to the dental procedure.", ContextNotes = "Common in OET writing for surgical and high-risk patient referrals.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "hard", SynonymsJson = """["prevention","preventive treatment","precaution"]""", CollocationsJson = """["antibiotic prophylaxis","DVT prophylaxis","primary prophylaxis","post-exposure prophylaxis"]""", RelatedTermsJson = """["indication","contraindication","treatment"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-021", Term = "iatrogenic", Definition = "Caused by medical examination or treatment.", ExampleSentence = "The iatrogenic pneumothorax occurred following central line insertion.", ContextNotes = "Important term for documenting adverse events in clinical notes.", ExamTypeCode = "oet", Category = "medical", Difficulty = "hard", SynonymsJson = """["treatment-induced","medically caused"]""", CollocationsJson = """["iatrogenic injury","iatrogenic complication","iatrogenic disease"]""", RelatedTermsJson = """["adverse event","complication","harm"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-022", Term = "titrate", Definition = "To adjust the dose of a drug to the required level in a measured way.", ExampleSentence = "The opioid dose was titrated upwards according to the patient's pain response.", ContextNotes = "Frequently used in pain management and ICU nursing documentation.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "medium", SynonymsJson = """["adjust","dose-adjust","calibrate"]""", CollocationsJson = """["titrate to effect","titrate dose","up-titrate","down-titrate"]""", RelatedTermsJson = """["analgesic","dosing","therapeutic range"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-023", Term = "triage", Definition = "The process of determining the priority of patients' treatment based on urgency of need.", ExampleSentence = "The emergency department nurse triaged the patient as category 2 (emergency).", ContextNotes = "Common in emergency nursing OET speaking and writing tasks.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "easy", SynonymsJson = """["priority assessment","sorting"]""", CollocationsJson = """["emergency triage","triage category","triage nurse","triage assessment"]""", RelatedTermsJson = """["primary survey","ABCDE assessment","acuity"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-024", Term = "consent", Definition = "Permission granted for medical treatment or procedure, especially informed consent.", ExampleSentence = "Written informed consent was obtained before the procedure.", ContextNotes = "Critical ethical and legal concept in OET healthcare scenarios.", ExamTypeCode = "oet", Category = "clinical_communication", Difficulty = "easy", SynonymsJson = """["agreement","permission","authorisation"]""", CollocationsJson = """["informed consent","written consent","consent form","capacity to consent","consent obtained"]""", RelatedTermsJson = """["capacity","autonomy","refusal"]""", Status = "active" },
            new VocabularyTerm { Id = "vt-025", Term = "idiopathic", Definition = "Relating to a disease or condition that arises spontaneously and for which no cause is known.", ExampleSentence = "The diagnosis was idiopathic pulmonary fibrosis.", ContextNotes = "Use when no identifiable cause has been determined despite investigation.", ExamTypeCode = "oet", Category = "medical", Difficulty = "hard", SynonymsJson = """["unknown cause","of unknown origin"]""", CollocationsJson = """["idiopathic disease","idiopathic pulmonary fibrosis","idiopathic hypertension"]""", RelatedTermsJson = """["aetiology","primary","secondary"]""", Status = "active" }
        );
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
            new ForumCategory { Id = "fcat-009", ExamTypeCode = null, Name = "Ask an Expert", Description = "Post your OET preparation questions and get verified answers from certified expert reviewers.", SortOrder = 5, Status = "active" }
        );
    }

    private static void SeedPronunciationDrills(LearnerDbContext db)
    {
        db.PronunciationDrills.AddRange(
            new PronunciationDrill
            {
                Id = "pd-001", TargetPhoneme = "θ", Label = "th (voiceless) — as in 'think'",
                ExampleWordsJson = """["think","therapy","three","breath","tooth","both","method","author"]""",
                MinimalPairsJson = """[{"a":"think","b":"sink"},{"a":"three","b":"free"},{"a":"bath","b":"bass"},{"a":"thin","b":"tin"}]""",
                SentencesJson = """["The therapist recommended three therapeutic exercises.","Breathe through your mouth during the assessment.","The pathologist confirmed the diagnosis on Thursday."]""",
                TipsHtml = "<p>Place the tip of your tongue lightly between your upper and lower front teeth. Blow air through gently — do <strong>not</strong> voice this sound.</p>",
                Difficulty = "medium", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-002", TargetPhoneme = "ð", Label = "th (voiced) — as in 'this'",
                ExampleWordsJson = """["this","the","that","breathe","soothe","other","mother","whether"]""",
                MinimalPairsJson = """[{"a":"this","b":"miss"},{"a":"then","b":"den"},{"a":"breathe","b":"breed"},{"a":"they","b":"day"}]""",
                SentencesJson = """["Breathe in deeply through the nose.","The other doctor confirmed the diagnosis.","Although the patient denied symptoms, further investigation was warranted."]""",
                TipsHtml = "<p>Same tongue position as voiceless th, but <strong>add voice</strong> by vibrating your vocal cords. You should feel vibration in your throat.</p>",
                Difficulty = "medium", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-003", TargetPhoneme = "v", Label = "v — as in 'vital'",
                ExampleWordsJson = """["vital","valve","intravenous","invasive","vomit","vast","fever","verbal"]""",
                MinimalPairsJson = """[{"a":"very","b":"berry"},{"a":"van","b":"ban"},{"a":"vest","b":"best"},{"a":"vein","b":"bane"}]""",
                SentencesJson = """["Vital signs were stable on arrival.","The vascular surgeon reviewed the patient.","Intravenous fluids were administered at a very slow rate."]""",
                TipsHtml = "<p>Lightly bite your lower lip with your upper teeth, then push air through while vibrating your vocal cords. Avoid closing the lips completely.</p>",
                Difficulty = "easy", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-004", TargetPhoneme = "w", Label = "w — as in 'wound'",
                ExampleWordsJson = """["wound","ward","weight","swallow","withdraw","weakness","well-being","away"]""",
                MinimalPairsJson = """[{"a":"wet","b":"vet"},{"a":"wine","b":"vine"},{"a":"west","b":"vest"},{"a":"while","b":"vile"}]""",
                SentencesJson = """["The wound was well-healed at the two-week review.","The patient's weight had reduced significantly.","Withdrawal of treatment was discussed with the family."]""",
                TipsHtml = "<p>Round your lips tightly into a small circle, then open them quickly while pushing air out and voicing. Do not use your teeth.</p>",
                Difficulty = "easy", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-005", TargetPhoneme = "ɪ", Label = "Short i — as in 'symptom'",
                ExampleWordsJson = """["symptom","physical","clinic","insulin","infusion","intravenous","inhibitor","risk"]""",
                MinimalPairsJson = """[{"a":"bit","b":"beat"},{"a":"sit","b":"seat"},{"a":"ship","b":"sheep"},{"a":"fill","b":"feel"}]""",
                SentencesJson = """["The clinical symptoms included intermittent nausea.","Physical examination findings were significant.","The patient was given insulin via infusion."]""",
                TipsHtml = "<p>This is a short, relaxed vowel. The tongue is high and forward, but <strong>more relaxed</strong> than the long 'ee' (iː) sound. Keep it brief.</p>",
                Difficulty = "easy", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-006", TargetPhoneme = "æ", Label = "Short a (trap vowel) — as in 'catheter'",
                ExampleWordsJson = """["catheter","analgesia","abdominal","fracture","clamp","traction","anaphylaxis","anaemia"]""",
                MinimalPairsJson = """[{"a":"bad","b":"bed"},{"a":"band","b":"bend"},{"a":"can","b":"ken"},{"a":"mass","b":"mess"}]""",
                SentencesJson = """["A nasogastric catheter was inserted.","The abdominal examination was unremarkable.","Anaphylaxis protocol was activated immediately."]""",
                TipsHtml = "<p>Open your jaw wide, spread your lips, and position your tongue low and forward. This is the 'flat' a sound — do not let it sound like 'eh'.</p>",
                Difficulty = "medium", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-007", TargetPhoneme = "ɜː", Label = "er vowel — as in 'nurse'",
                ExampleWordsJson = """["nurse","word","alert","observed","referred","further","concerns","determinant"]""",
                MinimalPairsJson = """[{"a":"word","b":"ward"},{"a":"nurse","b":"Norse"},{"a":"bird","b":"bad"},{"a":"hurt","b":"heart"}]""",
                SentencesJson = """["The nurse observed the patient throughout the turn.","Further assessment was required.","The patient was referred for urgent evaluation."]""",
                TipsHtml = "<p>This is a mid-central vowel. Your tongue should be in the middle of your mouth, relaxed. In Australian and British English, this vowel is <strong>non-rhotic</strong> — do not pronounce the 'r'.</p>",
                Difficulty = "medium", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-008", TargetPhoneme = "stress", Label = "Word Stress in Medical Terms",
                ExampleWordsJson = """["hyPERtension","DIAgnosis","preSCRIPtion","MEDication","aNAEsthesia","surGEry","theraPEUtic","physiOLogy"]""",
                MinimalPairsJson = "[]",
                SentencesJson = """["The patient's hypertension was managed with medication.","The diagnosis was confirmed following further investigation.","A therapeutic approach was outlined for the family."]""",
                TipsHtml = "<p>In English, the stressed syllable is louder, longer, and higher pitched. Medical terms often stress the second-to-last syllable (penultimate stress). Practise by clapping the rhythm of each word.</p>",
                Difficulty = "hard", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-009", TargetPhoneme = "r", Label = "Non-rhotic r — post-vocalic r in Australian/British English",
                ExampleWordsJson = """["care","refer","disorder","further","monitor","procedure","doctor","fever"]""",
                MinimalPairsJson = """[{"a":"car (AU)","b":"car (US)"},{"a":"nurse (AU)","b":"nurse (US)"}]""",
                SentencesJson = """["The doctor ordered further investigations.","Post-operative monitoring continued throughout the day.","The procedure was performed under general anaesthesia."]""",
                TipsHtml = "<p>In Australian and British English, the 'r' after a vowel is <strong>not pronounced</strong> unless the next word begins with a vowel. This is called non-rhotic pronunciation. Do not curl your tongue for a word-final 'r'.</p>",
                Difficulty = "hard", Status = "active"
            },
            new PronunciationDrill
            {
                Id = "pd-010", TargetPhoneme = "intonation", Label = "Rising vs. Falling Intonation in Clinical Questioning",
                ExampleWordsJson = "[]",
                MinimalPairsJson = "[]",
                SentencesJson = """["Are you experiencing any chest pain? (rising)","Tell me where the pain is located. (falling)","Have you taken your medication today? (rising)","I'd like to check your blood pressure now. (falling)"]""",
                TipsHtml = "<p>In OET Speaking, use <strong>rising intonation</strong> for yes/no questions and <strong>falling intonation</strong> for statements and wh-questions. Appropriate intonation signals empathy and clinical authority.</p>",
                Difficulty = "medium", Status = "active"
            }
        );
    }

    private static string ResolveStoragePath(IWebHostEnvironment environment, StorageOptions options, string storageKey)
    {
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(options.LocalRootPath)
                ? options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, options.LocalRootPath));

        var normalizedKey = storageKey
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return Path.Combine(rootPath, normalizedKey);
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

    // ── Content Packages (reference data) ──

    private static void SeedContentPackages(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPackages.AddRange(
            new ContentPackage
            {
                Id = "pkg-full-en-2026", Code = "full-en-2026", Title = "Full OET Course 2026 (English)",
                Description = "Comprehensive OET preparation covering all four subtests with structured modules, practice tasks, and model answers.",
                PackageType = "full_course", InstructionLanguage = "en", BillingPlanId = "premium-monthly",
                Status = ContentStatus.Published, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-full-ar-nursing-2026", Code = "full-ar-nursing-2026", Title = "Full OET Nursing Course 2026 (Arabic)",
                Description = "Complete OET Nursing course with Arabic instruction. All subtests covered with profession-specific materials.",
                PackageType = "full_course", ProfessionId = "nursing", InstructionLanguage = "ar",
                BillingPlanId = "premium-monthly", Status = ContentStatus.Published, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-full-ar-medicine-2026", Code = "full-ar-medicine-2026", Title = "Full OET Medicine Course 2026 (Arabic)",
                Description = "Complete OET Medicine course with Arabic instruction for doctors.",
                PackageType = "full_course", ProfessionId = "medicine", InstructionLanguage = "ar",
                BillingPlanId = "premium-monthly", Status = ContentStatus.Published, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-en-general", Code = "crash-en-general", Title = "OET Crash Course (English)",
                Description = "Intensive short-format OET preparation covering key strategies and high-impact practice across all subtests.",
                PackageType = "crash_course", InstructionLanguage = "en", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-ar-general", Code = "crash-ar-general", Title = "OET Crash Course (Arabic)",
                Description = "Condensed OET preparation with Arabic instruction.",
                PackageType = "crash_course", InstructionLanguage = "ar", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 5, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-en-pharmacy", Code = "crash-en-pharmacy", Title = "OET Pharmacy Crash Course (English)",
                Description = "Pharmacy-focused OET crash course with profession-specific tasks and strategies.",
                PackageType = "crash_course", ProfessionId = "pharmacy", InstructionLanguage = "en",
                BillingPlanId = "basic-monthly", Status = ContentStatus.Published, DisplayOrder = 6, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-combo-lr-recalls", Code = "combo-lr-recalls", Title = "Listening, Reading & Recalls Combo",
                Description = "Combined package for Listening and Reading practice including recent recall materials.",
                PackageType = "combo", InstructionLanguage = "en", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 7, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-foundation-basic-en", Code = "foundation-basic-en", Title = "Basic English for OET",
                Description = "Foundation English course to build core language skills before starting OET-specific preparation.",
                PackageType = "foundation", InstructionLanguage = "en",
                Status = ContentStatus.Published, DisplayOrder = 8, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            }
        );

        // Package content rules linking packages to programs
        db.PackageContentRules.AddRange(
            new PackageContentRule { Id = "pcr-001", PackageId = "pkg-full-en-2026", RuleType = "include_program", TargetId = "prg-full-en-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-002", PackageId = "pkg-full-ar-nursing-2026", RuleType = "include_program", TargetId = "prg-full-ar-nursing-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-003", PackageId = "pkg-full-ar-medicine-2026", RuleType = "include_program", TargetId = "prg-full-ar-medicine-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-004", PackageId = "pkg-crash-en-general", RuleType = "include_program", TargetId = "prg-crash-en-general", TargetType = "program" },
            new PackageContentRule { Id = "pcr-005", PackageId = "pkg-crash-ar-general", RuleType = "include_program", TargetId = "prg-crash-ar-general", TargetType = "program" },
            new PackageContentRule { Id = "pcr-006", PackageId = "pkg-crash-en-pharmacy", RuleType = "include_program", TargetId = "prg-crash-en-pharmacy", TargetType = "program" },
            new PackageContentRule { Id = "pcr-007", PackageId = "pkg-foundation-basic-en", RuleType = "include_program", TargetId = "prg-foundation-basic-en", TargetType = "program" }
        );
    }

    // ── Content Programs with Tracks and Modules (reference data) ──

    private static void SeedContentPrograms(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        // ── Full English OET Course 2026 ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-en-2026", Code = "full-en-2026", Title = "Full OET Online Course 2026",
            Description = "Comprehensive OET preparation covering Writing, Speaking, Reading, and Listening with structured progression.",
            InstructionLanguage = "en", ProgramType = "full_course", Status = ContentStatus.Published,
            DisplayOrder = 1, EstimatedDurationMinutes = 4800, CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.AddRange(
            new ContentTrack { Id = "trk-full-en-writing", ProgramId = "prg-full-en-2026", SubtestCode = "writing", Title = "Writing Track", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-speaking", ProgramId = "prg-full-en-2026", SubtestCode = "speaking", Title = "Speaking Track", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-reading", ProgramId = "prg-full-en-2026", SubtestCode = "reading", Title = "Reading Track", DisplayOrder = 3, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-listening", ProgramId = "prg-full-en-2026", SubtestCode = "listening", Title = "Listening Track", DisplayOrder = 4, Status = ContentStatus.Published }
        );

        db.ContentModules.AddRange(
            new ContentModule { Id = "mod-full-en-wr-01", TrackId = "trk-full-en-writing", Title = "Writing Fundamentals", Description = "Core OET writing skills: purpose, structure, and register.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-wr-02", TrackId = "trk-full-en-writing", Title = "Case Notes & Task Analysis", Description = "Extracting relevant information from case notes.", DisplayOrder = 2, EstimatedDurationMinutes = 90, PrerequisiteModuleId = "mod-full-en-wr-01", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-wr-03", TrackId = "trk-full-en-writing", Title = "Model Answers & Criteria Deep-Dive", Description = "Understanding scoring criteria through model answer analysis.", DisplayOrder = 3, EstimatedDurationMinutes = 120, PrerequisiteModuleId = "mod-full-en-wr-02", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-sp-01", TrackId = "trk-full-en-speaking", Title = "Speaking Fundamentals", Description = "OET speaking format, role card analysis, and clinical communication.", DisplayOrder = 1, EstimatedDurationMinutes = 90, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-sp-02", TrackId = "trk-full-en-speaking", Title = "Role Play Practice", Description = "Structured practice with common OET speaking scenarios.", DisplayOrder = 2, EstimatedDurationMinutes = 120, PrerequisiteModuleId = "mod-full-en-sp-01", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-rd-01", TrackId = "trk-full-en-reading", Title = "Reading Parts A, B & C Strategies", Description = "Strategies and practice for all three reading parts.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-lt-01", TrackId = "trk-full-en-listening", Title = "Listening Parts A, B & C Strategies", Description = "Strategies and practice for all three listening parts.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published }
        );

        // Wire existing demo content items into lessons
        db.ContentLessons.AddRange(
            new ContentLesson { Id = "lsn-wr-01-task", ModuleId = "mod-full-en-wr-01", ContentItemId = "wt-001", Title = "Practice: Discharge Summary", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-wr-02-task", ModuleId = "mod-full-en-wr-02", ContentItemId = "wt-002", Title = "Practice: Referral Letter", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-sp-01-task", ModuleId = "mod-full-en-sp-02", ContentItemId = "st-001", Title = "Practice: Patient Handover", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-sp-02-task", ModuleId = "mod-full-en-sp-02", ContentItemId = "st-002", Title = "Practice: Breaking Bad News", LessonType = "practice_task", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-rd-01-task", ModuleId = "mod-full-en-rd-01", ContentItemId = "rt-001", Title = "Practice: HAI Prevention (Part C)", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-lt-01-task", ModuleId = "mod-full-en-lt-01", ContentItemId = "lt-001", Title = "Practice: Asthma Management", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published }
        );

        // ── Arabic Nursing Course ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-ar-nursing-2026", Code = "full-ar-nursing-2026", Title = "Full OET Nursing Course 2026 (Arabic)",
            Description = "Comprehensive nursing-focused OET course with Arabic instruction.",
            ProfessionId = "nursing", InstructionLanguage = "ar", ProgramType = "full_course",
            Status = ContentStatus.Published, DisplayOrder = 2, EstimatedDurationMinutes = 4800,
            CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.AddRange(
            new ContentTrack { Id = "trk-ar-nursing-writing", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "writing", Title = "Writing Track (Arabic)", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-speaking", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "speaking", Title = "Speaking Track (Arabic)", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-reading", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "reading", Title = "Reading Track (Arabic)", DisplayOrder = 3, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-listening", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "listening", Title = "Listening Track (Arabic)", DisplayOrder = 4, Status = ContentStatus.Published }
        );

        // ── Arabic Medicine Course ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-ar-medicine-2026", Code = "full-ar-medicine-2026", Title = "Full OET Medicine Course 2026 (Arabic Doctors)",
            Description = "Medicine-focused OET course with Arabic instruction for doctors.",
            ProfessionId = "medicine", InstructionLanguage = "ar", ProgramType = "full_course",
            Status = ContentStatus.Published, DisplayOrder = 3, EstimatedDurationMinutes = 4800,
            CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        // ── Crash Courses ──
        db.ContentPrograms.AddRange(
            new ContentProgram
            {
                Id = "prg-crash-en-general", Code = "crash-en-general", Title = "OET Crash Course (English)",
                Description = "Intensive short-format OET preparation.",
                InstructionLanguage = "en", ProgramType = "crash_course", Status = ContentStatus.Published,
                DisplayOrder = 4, EstimatedDurationMinutes = 1200, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentProgram
            {
                Id = "prg-crash-ar-general", Code = "crash-ar-general", Title = "OET Crash Course (Arabic)",
                Description = "Condensed OET preparation with Arabic instruction.",
                InstructionLanguage = "ar", ProgramType = "crash_course", Status = ContentStatus.Published,
                DisplayOrder = 5, EstimatedDurationMinutes = 1200, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentProgram
            {
                Id = "prg-crash-en-pharmacy", Code = "crash-en-pharmacy", Title = "OET Pharmacy Crash Course",
                Description = "Pharmacy-focused crash course with profession-specific tasks.",
                ProfessionId = "pharmacy", InstructionLanguage = "en", ProgramType = "crash_course",
                Status = ContentStatus.Published, DisplayOrder = 6, EstimatedDurationMinutes = 900,
                CreatedAt = now, UpdatedAt = now, PublishedAt = now
            }
        );

        // ── Foundation English ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-foundation-basic-en", Code = "foundation-basic-en", Title = "Basic English for OET Preparation",
            Description = "Foundation level English course to build language skills before starting OET-specific preparation.",
            InstructionLanguage = "en", ProgramType = "foundation", Status = ContentStatus.Published,
            DisplayOrder = 7, EstimatedDurationMinutes = 2400, CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.Add(
            new ContentTrack { Id = "trk-foundation-core", ProgramId = "prg-foundation-basic-en", SubtestCode = null, Title = "Core English Skills", DisplayOrder = 1, Status = ContentStatus.Published }
        );

        db.ContentModules.AddRange(
            new ContentModule { Id = "mod-foundation-grammar", TrackId = "trk-foundation-core", Title = "Grammar Essentials", Description = "Tenses, articles, prepositions, and formal register.", DisplayOrder = 1, EstimatedDurationMinutes = 180, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-foundation-vocab", TrackId = "trk-foundation-core", Title = "Medical Vocabulary", Description = "Common medical terms, abbreviations, and clinical language.", DisplayOrder = 2, EstimatedDurationMinutes = 150, PrerequisiteModuleId = "mod-foundation-grammar", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-foundation-reading", TrackId = "trk-foundation-core", Title = "Reading Comprehension Basics", Description = "Building reading speed and comprehension for healthcare texts.", DisplayOrder = 3, EstimatedDurationMinutes = 120, Status = ContentStatus.Published }
        );

        // ── Sample free preview assets ──
        db.FreePreviewAssets.AddRange(
            new FreePreviewAsset { Id = "fp-grammar-sample", Title = "OET Grammar Sample", PreviewType = "sample_lesson", ConversionCtaText = "Unlock the full course", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 1, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-writing-sample", Title = "OET Writing Sample", PreviewType = "sample_lesson", ConversionCtaText = "Start your OET Writing journey", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 2, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-speaking-sample", Title = "OET Speaking Sample", PreviewType = "sample_lesson", ConversionCtaText = "Master OET Speaking", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 3, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-reading-sample", Title = "OET Reading Sample", PreviewType = "sample_lesson", ConversionCtaText = "Improve your OET Reading", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 4, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-listening-sample", Title = "OET Listening Sample", PreviewType = "sample_lesson", ConversionCtaText = "Sharpen your Listening skills", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 5, CreatedAt = now }
        );
    }
}
