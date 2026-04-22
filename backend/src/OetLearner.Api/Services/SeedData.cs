using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
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
        else if (await EnsureMissingOetVocabularyBankAsync(db, cancellationToken))
        {
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

        if (!await db.GrammarLessons.AnyAsync(cancellationToken))
        {
            SeedGrammarLessons(db);
            hasChanges = true;
        }

        await ConversationSeedData.EnsureAsync(db, cancellationToken);

        if (!await db.StrategyGuides.AnyAsync(guide => guide.ExamTypeCode == "oet", cancellationToken))
        {
            SeedStrategyGuides(db);
            hasChanges = true;
        }

        var strategyGuidesFlag = await db.FeatureFlags.FirstOrDefaultAsync(flag => flag.Key == "strategy_guides", cancellationToken);
        if (strategyGuidesFlag is not null && (!strategyGuidesFlag.Enabled || strategyGuidesFlag.RolloutPercentage < 100))
        {
            strategyGuidesFlag.Enabled = true;
            strategyGuidesFlag.RolloutPercentage = 100;
            strategyGuidesFlag.Description = "Enable written OET exam strategy guides.";
            strategyGuidesFlag.UpdatedAt = DateTimeOffset.UtcNow;
            hasChanges = true;
        }

        if (!await db.AiQuotaPlans.AnyAsync(cancellationToken))
        {
            SeedAiQuotaPlans(db);
            hasChanges = true;
        }

        if (!await db.AiGlobalPolicies.AnyAsync(cancellationToken))
        {
            SeedAiGlobalPolicy(db);
            hasChanges = true;
        }

        if (!await db.AiProviders.AnyAsync(cancellationToken))
        {
            // Note: platform API key is NOT seeded here — admins must register
            // it via /admin/ai-usage/providers so the encrypted ciphertext
            // lives under the production Data Protection key ring.
            SeedAiProviderStub(db);
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
                    ["purpose"] = 2, ["content"] = 4, ["conciseness_clarity"] = 4, ["genre_style"] = 5, ["organisation_layout"] = 5, ["language"] = 4
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

        // Ensure seeded admin has system_admin permission (satisfies all granular policies)
        var hasSystemAdmin = db.AdminPermissionGrants.Any(
            g => g.AdminUserId == adminAccount.Id && g.Permission == AdminPermissions.SystemAdmin);
        if (!hasSystemAdmin)
        {
            db.AdminPermissionGrants.Add(new AdminPermissionGrant
            {
                Id = $"grant_seed_{Guid.NewGuid():N}",
                AdminUserId = adminAccount.Id,
                Permission = AdminPermissions.SystemAdmin,
                GrantedBy = "seed",
                GrantedAt = now
            });
        }

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
            new FeatureFlag { Id = "flg-011", Name = "AI Conversation Practice", Key = "ai_conversation", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable AI roleplay conversation partner for speaking practice.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-012", Name = "AI Writing Coach", Key = "ai_writing_coach", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable real-time AI writing suggestions in the writing editor.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-013", Name = "Pronunciation Analysis", Key = "pronunciation_analysis", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Phoneme-level pronunciation analysis, drills, recording UX, and ASR scoring.", Owner = "Platform Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-014", Name = "Performance Prediction", Key = "performance_prediction", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable predicted score forecasting based on practice history.", Owner = "Product", CreatedAt = now, UpdatedAt = now },
            // ── Phase 3 new feature flags ──
            new FeatureFlag { Id = "flg-015", Name = "Grammar Lessons", Key = "grammar_lessons", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable structured grammar lesson modules.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-016", Name = "Video Lessons", Key = "video_lessons", FlagType = FeatureFlagType.Release, Enabled = false, RolloutPercentage = 0, Description = "Enable video lesson catalogue.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
            new FeatureFlag { Id = "flg-017", Name = "Strategy Guides", Key = "strategy_guides", FlagType = FeatureFlagType.Release, Enabled = true, RolloutPercentage = 100, Description = "Enable written OET exam strategy guides.", Owner = "Content Team", CreatedAt = now, UpdatedAt = now },
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
            new AIConfigVersion { Id = "aic-001", Model = "anthropic-claude-opus-4.7", Provider = "DigitalOcean Serverless (Anthropic)", TaskType = "writing", Status = AIConfigStatus.Active, Accuracy = 94.2, ConfidenceThreshold = 0.85, RoutingRule = "default", PromptLabel = "Writing Eval v3.2", CreatedBy = "Admin", CreatedAt = now.AddDays(-30) },
            new AIConfigVersion { Id = "aic-002", Model = "anthropic-claude-opus-4.7", Provider = "DigitalOcean Serverless (Anthropic)", TaskType = "speaking", Status = AIConfigStatus.Active, Accuracy = 91.8, ConfidenceThreshold = 0.80, RoutingRule = "default", PromptLabel = "Speaking Eval v2.1", CreatedBy = "Admin", CreatedAt = now.AddDays(-25) },
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
            new ForumCategory { Id = "fcat-009", ExamTypeCode = null, Name = "Ask an Expert", Description = "Post your OET preparation questions and get verified answers from certified expert reviewers.", SortOrder = 5, Status = "active" }
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
        drills.Add(D("pd-001", "θ", "th (voiceless) — as in 'think'", "all", "phoneme", "P01.1", "medium",
            """["think","therapy","three","breath","tooth","both","method","author","thyroid","pathology"]""",
            """[{"a":"think","b":"sink"},{"a":"three","b":"free"},{"a":"bath","b":"bass"},{"a":"thin","b":"tin"}]""",
            """["The therapist recommended three therapeutic exercises.","Breathe through your mouth during the assessment.","The pathologist confirmed the diagnosis on Thursday."]""",
            "<p>Place the tip of your tongue lightly between your upper and lower front teeth. Blow air through gently — do <strong>not</strong> voice this sound.</p>", 1));

        drills.Add(D("pd-002", "ð", "th (voiced) — as in 'this'", "all", "phoneme", "P01.2", "medium",
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

    private static void SeedStrategyGuides(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        StrategyGuide Guide(
            string id,
            string slug,
            string? subtestCode,
            string title,
            string summary,
            string category,
            int minutes,
            int sortOrder,
            string overview,
            string[] actions,
            string[] takeaways,
            string[] mistakes)
            => new()
            {
                Id = id,
                Slug = slug,
                ExamTypeCode = "oet",
                SubtestCode = subtestCode,
                Title = title,
                Summary = summary,
                Category = category,
                ReadingTimeMinutes = minutes,
                SortOrder = sortOrder,
                Status = "active",
                IsPreviewEligible = true,
                ContentHtml = StrategyHtml(overview, actions, takeaways),
                ContentJson = StrategyContentJson(overview, actions, takeaways, mistakes),
                SourceProvenance = "Original OET Prep editorial content reviewed for learner guidance.",
                RightsStatus = "owned",
                FreshnessConfidence = "current",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now
            };

        db.StrategyGuides.AddRange(
            Guide(
                "strategy-oet-overview",
                "oet-strategy-overview",
                null,
                "OET Strategy Overview",
                "A simple plan for turning four subtests into a weekly study system.",
                "exam_overview",
                5,
                10,
                "Treat OET preparation as four linked skills: language control, task awareness, time management, and healthcare communication.",
                [
                    "Start with a diagnostic or recent mock score.",
                    "Choose one weak subtest and one maintenance subtest for each week.",
                    "Review mistakes within 24 hours so patterns are still fresh."
                ],
                [
                    "Do not study every resource at once.",
                    "Use your weakest subtest to choose the next strategy guide."
                ],
                [
                    "Collecting materials without a weekly plan.",
                    "Ignoring review because the next mock feels more urgent."
                ]),
            Guide(
                "strategy-writing-case-notes",
                "writing-case-notes-selection",
                "writing",
                "Writing: Case Notes Selection",
                "Select relevant case notes by reader need, not by the order shown on the paper.",
                "writing",
                6,
                20,
                "High-scoring writing starts before the first sentence: decide the purpose, reader, and clinically relevant notes first.",
                [
                    "Underline the task line and identify the reader.",
                    "Mark notes as must include, useful if space allows, or omit.",
                    "Group notes by purpose: reason for writing, background, current status, and requested action."
                ],
                [
                    "Relevance is more important than quantity.",
                    "The task line decides what belongs in the letter."
                ],
                [
                    "Copying every date and medication detail.",
                    "Writing for the patient instead of the named reader."
                ]),
            Guide(
                "strategy-writing-letter-structure",
                "writing-letter-structure",
                "writing",
                "Writing: Letter Structure",
                "Build a clear referral, discharge, or transfer letter with a purposeful paragraph plan.",
                "writing",
                6,
                30,
                "A strong OET letter uses clear paragraph roles so the reader can act quickly.",
                [
                    "Open with the purpose and patient identity.",
                    "Use one paragraph for relevant background only.",
                    "Put current clinical status and requested action near the end."
                ],
                [
                    "Paragraph order should help the reader make a decision.",
                    "Avoid long chronological retelling unless the task requires it."
                ],
                [
                    "Starting with irrelevant social history.",
                    "Ending without a clear request or follow-up action."
                ]),
            Guide(
                "strategy-speaking-roleplay",
                "speaking-roleplay-flow",
                "speaking",
                "Speaking: Roleplay Flow",
                "Keep the interaction patient-centred while covering the task card naturally.",
                "speaking",
                5,
                40,
                "OET Speaking rewards natural healthcare communication, not memorised speeches.",
                [
                    "Open with a warm greeting and confirm the concern.",
                    "Ask before explaining, then chunk information into short turns.",
                    "Check understanding and invite questions before closing."
                ],
                [
                    "The interlocutor is a patient, carer, or colleague, not an examiner.",
                    "Empathy and structure must work together."
                ],
                [
                    "Reading the card aloud.",
                    "Explaining for too long without checking understanding."
                ]),
            Guide(
                "strategy-reading-part-a",
                "reading-part-a-speed",
                "reading",
                "Reading Part A: Speed And Accuracy",
                "Use the 15 minutes to locate facts quickly without sacrificing exact wording.",
                "reading",
                5,
                50,
                "Reading Part A is a controlled information hunt: headings, keywords, and exact answer format matter.",
                [
                    "Scan all headings before answering.",
                    "Match synonyms, then copy the exact needed word or phrase.",
                    "Leave hard items and return after easier facts are secured."
                ],
                [
                    "Your first job is location, not deep reading.",
                    "Answer format errors lose easy marks."
                ],
                [
                    "Reading every text from top to bottom.",
                    "Changing spelling or grammar when copying short answers."
                ]),
            Guide(
                "strategy-reading-bc",
                "reading-parts-b-c",
                "reading",
                "Reading Parts B/C: Reasoning",
                "Part B handles short extracts from different healthcare contexts; Part C handles 2 long articles with evidence-based elimination.",
                "reading",
                6,
                60,
                "Parts B and C test meaning, purpose, opinion, and inference. The correct option must be supported by the text.",
                [
                    "Read the question stem before the options.",
                    "Find the line that proves or rejects each option.",
                    "Eliminate options that are true but do not answer the question."
                ],
                [
                    "Evidence beats intuition.",
                    "A partially true option is still wrong if it misses the question."
                ],
                [
                    "Choosing familiar medical vocabulary instead of textual proof.",
                    "Letting one difficult paragraph consume the clock."
                ]),
            Guide(
                "strategy-listening-part-a",
                "listening-part-a-notes",
                "listening",
                "Listening Part A: Notes",
                "Capture patient consultation details with abbreviations and prediction.",
                "listening",
                5,
                70,
                "Listening Part A rewards prediction and fast note completion. You must use the pauses actively.",
                [
                    "Read headings and predict the type of answer before audio starts.",
                    "Write short medical abbreviations during the first pass.",
                    "Use grammar around the blank to decide singular, plural, or adjective form."
                ],
                [
                    "The pause is part of the task.",
                    "Prediction reduces panic when the audio begins."
                ],
                [
                    "Waiting until the speaker finishes before writing.",
                    "Missing plural endings and units."
                ]),
            Guide(
                "strategy-listening-bc",
                "listening-parts-b-c",
                "listening",
                "Listening Parts B/C: Decision Making",
                "Identify speaker purpose, attitude, and the reason behind each answer.",
                "listening",
                6,
                80,
                "Parts B and C often test why a speaker says something, not just what words you hear.",
                [
                    "Preview the stem and options for the decision you need to make.",
                    "Listen for contrast markers such as however, but, and actually.",
                    "Choose after the full exchange, not after one matching word."
                ],
                [
                    "Same-word matches are often traps.",
                    "Tone and purpose can decide the answer."
                ],
                [
                    "Selecting an option as soon as you hear a keyword.",
                    "Ignoring the final correction or qualification."
                ]),
            Guide(
                "strategy-time-management",
                "oet-time-management",
                null,
                "Time Management Across The OET",
                "Use repeatable timing rules for mocks, practice blocks, and exam day.",
                "time_management",
                4,
                90,
                "Good timing is trained before exam day through consistent cut-off rules.",
                [
                    "Practise each subtest under real section timing at least weekly.",
                    "Set a maximum time for hard items, then move on.",
                    "Review where time was lost after every mock."
                ],
                [
                    "A timing rule prevents emotional decisions under pressure.",
                    "Review time loss as carefully as wrong answers."
                ],
                [
                    "Doing untimed practice for too long.",
                    "Changing timing strategy on exam day."
                ]),
            Guide(
                "strategy-common-mistakes",
                "common-oet-mistakes",
                null,
                "Common OET Mistakes",
                "Avoid the recurring errors that cost marks across writing, speaking, reading, and listening.",
                "common_mistakes",
                5,
                100,
                "Most score plateaus come from repeated small errors, not from one missing secret technique.",
                [
                    "Keep an error log grouped by subtest.",
                    "Fix one recurring error at a time.",
                    "Check whether each mistake is language, strategy, timing, or attention."
                ],
                [
                    "Patterns matter more than isolated mistakes.",
                    "The error log should drive your next study session."
                ],
                [
                    "Only checking the final score.",
                    "Repeating full mocks without targeted repair."
                ]),
            Guide(
                "strategy-exam-day-checklist",
                "oet-exam-day-checklist",
                null,
                "Exam-Day Checklist",
                "Prepare documents, timing, equipment, and mindset before the test begins.",
                "exam_day",
                4,
                110,
                "Exam day should feel familiar. Reduce avoidable stress before you enter the test room or online check-in.",
                [
                    "Confirm identification, test time, venue or online setup, and travel plan.",
                    "Prepare permitted items the evening before.",
                    "Use a short warm-up, not a last-minute cram session."
                ],
                [
                    "Preparation reduces cognitive load.",
                    "Your goal is calm execution of familiar routines."
                ],
                [
                    "Studying new material on the morning of the exam.",
                    "Arriving without checking ID or technical requirements."
                ])
        );
    }

    private static string StrategyContentJson(string overview, string[] actions, string[] takeaways, string[] mistakes)
        => JsonSupport.Serialize(new
        {
            version = 1,
            overview,
            sections = new[]
            {
                new { heading = "What to do", body = overview, bullets = actions },
                new { heading = "Common traps", body = "Avoid these recurring mistakes while practising.", bullets = mistakes }
            },
            keyTakeaways = takeaways
        });

    private static string StrategyHtml(string overview, string[] actions, string[] takeaways)
        => $"<p>{overview}</p><h2>Action steps</h2><ul>{string.Join("", actions.Select(action => $"<li>{action}</li>"))}</ul><h2>Key takeaways</h2><ul>{string.Join("", takeaways.Select(takeaway => $"<li>{takeaway}</li>"))}</ul>";

    private static void SeedGrammarLessons(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        SeedGrammarStarterCatalog(db, now);
    }

    private static void SeedGrammarLessonsLegacy(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        db.GrammarLessons.AddRange(
            CreateGrammarLesson(
                now,
                "grm-oet-passive-1",
                "oet",
                "Passive voice in handovers",
                "Use passive forms to emphasise actions and patient care in clinical handovers.",
                "passive_voice",
                "intermediate",
                12,
                1,
                "Seeded grammar starter pack v1",
                [
                    GrammarContentBlock("intro", 1, "callout", "In handovers, passive voice keeps the focus on the action and the patient."),
                    GrammarContentBlock("example", 2, "example", "Example: The wound **was cleaned** before the dressing was changed."),
                    GrammarContentBlock("note", 3, "note", "Use the passive when the agent is obvious, unknown, or less important.")
                ],
                [
                    GrammarExercise(
                        "exercise-1",
                        1,
                        "mcq",
                        "Which sentence uses the clearest passive form?",
                        new[]
                        {
                            new { id = "a", label = "The nurse checked the blood pressure." },
                            new { id = "b", label = "The blood pressure was checked by the nurse." },
                            new { id = "c", label = "The nurse was checking the blood pressure." }
                        },
                        "b",
                        Array.Empty<string>(),
                        "Passive voice moves the focus to the blood pressure rather than the nurse.",
                        "intermediate",
                        1),
                    GrammarExercise(
                        "exercise-2",
                        2,
                        "fill_blank",
                        "The medication was ___ at 8:00 am.",
                        Array.Empty<object>(),
                        "administered",
                        new[] { "administered" },
                        "Administered is the past participle that completes the passive structure.",
                        "beginner",
                        1),
                    GrammarExercise(
                        "exercise-3",
                        3,
                        "matching",
                        "Match each active sentence to its passive version.",
                        new[]
                        {
                            new { left = "The clinician reviewed the chart", right = "The chart was reviewed by the clinician" },
                            new { left = "The team arranged follow-up", right = "Follow-up was arranged by the team" },
                            new { left = "The nurse documents the findings", right = "The findings are documented by the nurse" }
                        },
                        new[]
                        {
                            new { left = "The clinician reviewed the chart", right = "The chart was reviewed by the clinician" },
                            new { left = "The team arranged follow-up", right = "Follow-up was arranged by the team" },
                            new { left = "The nurse documents the findings", right = "The findings are documented by the nurse" }
                        },
                        Array.Empty<string>(),
                        "These passive rewrites keep the patient record style clear and formal.",
                        "intermediate",
                        2)
                ]),

            CreateGrammarLesson(
                now,
                "grm-oet-articles-1",
                "oet",
                "Articles in clinical writing",
                "Choose a, an, or the correctly when writing clear medical notes and referrals.",
                "articles",
                "beginner",
                10,
                2,
                "Seeded grammar starter pack v1",
                [
                    GrammarContentBlock("intro", 1, "callout", "Articles help your message sound precise and professional."),
                    GrammarContentBlock("example", 2, "example", "Example: **The** patient was transferred to **an** emergency department."),
                    GrammarContentBlock("note", 3, "note", "Use **the** for specific items and **a/an** for first mention or general reference.")
                ],
                [
                    GrammarExercise(
                        "exercise-1",
                        1,
                        "error_correction",
                        "Correct the sentence: \"The nurse noted patient was comfortable.\"",
                        Array.Empty<object>(),
                        "The nurse noted the patient was comfortable.",
                        new[] { "The nurse noted the patient was comfortable" },
                        "The article 'the' is needed before the specific patient.",
                        "beginner",
                        1),
                    GrammarExercise(
                        "exercise-2",
                        2,
                        "fill_blank",
                        "She was admitted to ___ emergency department.",
                        Array.Empty<object>(),
                        "the",
                        new[] { "the" },
                        "A specific department needs the definite article.",
                        "beginner",
                        1),
                    GrammarExercise(
                        "exercise-3",
                        3,
                        "sentence_transformation",
                        "Rewrite the sentence with the correct article: 'Please contact doctor on call.'",
                        Array.Empty<object>(),
                        "Please contact the doctor on call.",
                        new[] { "Please contact the doctor on call" },
                        "The doctor is a specific person, so the definite article fits.",
                        "beginner",
                        2)
                ],
                "grm-oet-passive-1"),

            CreateGrammarLesson(
                now,
                "grm-ielts-conditionals-1",
                "ielts",
                "Conditional clauses for advice",
                "Practise if-clauses and advice structures for IELTS-style writing and speaking.",
                "conditionals",
                "intermediate",
                11,
                3,
                "Seeded grammar starter pack v1",
                [
                    GrammarContentBlock("intro", 1, "callout", "Conditionals let you express possibility, advice, and hypothetical situations."),
                    GrammarContentBlock("example", 2, "example", "Example: If the symptoms worsen, you **should** contact a doctor."),
                    GrammarContentBlock("note", 3, "note", "Keep the tense pattern consistent in the if-clause and main clause.")
                ],
                [
                    GrammarExercise(
                        "exercise-1",
                        1,
                        "mcq",
                        "If the symptoms worsen, you ___ contact a doctor.",
                        new[]
                        {
                            new { id = "a", label = "should" },
                            new { id = "b", label = "might" },
                            new { id = "c", label = "could to" }
                        },
                        "a",
                        Array.Empty<string>(),
                        "Should is the clearest choice for advice.",
                        "intermediate",
                        1),
                    GrammarExercise(
                        "exercise-2",
                        2,
                        "fill_blank",
                        "If I ___ more time, I would check the notes again.",
                        Array.Empty<object>(),
                        "had",
                        new[] { "had" },
                        "The second conditional uses the past simple in the if-clause.",
                        "intermediate",
                        1),
                    GrammarExercise(
                        "exercise-3",
                        3,
                        "sentence_transformation",
                        "Rewrite using unless: 'If the medication is not available, call the pharmacy.'",
                        Array.Empty<object>(),
                        "Unless the medication is available, call the pharmacy.",
                        new[] { "Unless the medication is available, call pharmacy", "Unless the medication is available, call the pharmacy" },
                        "Unless expresses the negative condition more naturally.",
                        "advanced",
                        2)
                ]),

            CreateGrammarLesson(
                now,
                "grm-pte-formal-1",
                "pte",
                "Formal register in referrals",
                "Use a professional tone when writing referral notes or patient communications.",
                "formal_register",
                "intermediate",
                11,
                4,
                "Seeded grammar starter pack v1",
                [
                    GrammarContentBlock("intro", 1, "callout", "Formal register sounds polite, precise, and appropriate for professional contexts."),
                    GrammarContentBlock("example", 2, "example", "Example: **Please be advised** that the appointment has been rescheduled."),
                    GrammarContentBlock("note", 3, "note", "Prefer neutral verbs and avoid contractions in formal communication.")
                ],
                [
                    GrammarExercise(
                        "exercise-1",
                        1,
                        "fill_blank",
                        "Please ___ be advised that the appointment has been rescheduled.",
                        Array.Empty<object>(),
                        "kindly",
                        new[] { "kindly" },
                        "Kindly is a conventional, polite choice in formal notices.",
                        "intermediate",
                        1),
                    GrammarExercise(
                        "exercise-2",
                        2,
                        "error_correction",
                        "Correct the sentence: \"Thanks for your quick reply and help.\"",
                        Array.Empty<object>(),
                        "Thank you for your prompt reply and assistance.",
                        new[] { "Thank you for your prompt reply and assistance" },
                        "Formal register prefers 'thank you', 'prompt', and 'assistance'.",
                        "intermediate",
                        1),
                    GrammarExercise(
                        "exercise-3",
                        3,
                        "matching",
                        "Match each informal phrase to a more formal version.",
                        new[]
                        {
                            new { left = "Can you send it soon?", right = "Could you please send it as soon as possible?" },
                            new { left = "I'm writing to tell you", right = "I am writing to inform you" },
                            new { left = "Thanks for your help", right = "Thank you for your assistance" }
                        },
                        new[]
                        {
                            new { left = "Can you send it soon?", right = "Could you please send it as soon as possible?" },
                            new { left = "I'm writing to tell you", right = "I am writing to inform you" },
                            new { left = "Thanks for your help", right = "Thank you for your assistance" }
                        },
                        Array.Empty<string>(),
                        "Formal register softens the tone without losing clarity.",
                        "advanced",
                        2)
                ]),

            CreateGrammarLesson(
                now,
                "grm-oet-sva-1",
                "oet",
                "Subject-verb agreement in notes",
                "Keep subjects and verbs aligned in concise, accurate clinical notes.",
                "subject_verb_agreement",
                "beginner",
                9,
                5,
                "Seeded grammar starter pack v1",
                [
                    GrammarContentBlock("intro", 1, "callout", "Subject-verb agreement is essential for accuracy in every note and report."),
                    GrammarContentBlock("example", 2, "example", "Example: **The results indicate** infection, not 'indicates'."),
                    GrammarContentBlock("note", 3, "note", "Plural subjects take plural verbs; singular subjects take singular verbs.")
                ],
                [
                    GrammarExercise(
                        "exercise-1",
                        1,
                        "mcq",
                        "Choose the correct sentence.",
                        new[]
                        {
                            new { id = "a", label = "The results indicates infection." },
                            new { id = "b", label = "The results indicate infection." },
                            new { id = "c", label = "The result indicate infection." }
                        },
                        "b",
                        Array.Empty<string>(),
                        "Plural subject 'results' requires the plural verb 'indicate'.",
                        "beginner",
                        1),
                    GrammarExercise(
                        "exercise-2",
                        2,
                        "error_correction",
                        "Correct the sentence: \"Each of the patients require follow-up.\"",
                        Array.Empty<object>(),
                        "Each of the patients requires follow-up.",
                        new[] { "Each of the patients requires follow-up" },
                        "'Each' is singular, so the verb should be 'requires'.",
                        "beginner",
                        1),
                    GrammarExercise(
                        "exercise-3",
                        3,
                        "fill_blank",
                        "A series of tests ___ ordered yesterday.",
                        Array.Empty<object>(),
                        "was",
                        new[] { "was" },
                        "The head noun 'series' is singular, so use 'was'.",
                        "intermediate",
                        2)
                ])
        );
    }

    private static GrammarLesson CreateGrammarLesson(
        DateTimeOffset now,
        string id,
        string examTypeCode,
        string title,
        string description,
        string category,
        string level,
        int estimatedMinutes,
        int sortOrder,
        string sourceProvenance,
        object[] contentBlocks,
        object[] exercises,
        string? prerequisiteLessonId = null)
    {
        var document = new
        {
            topicId = category,
            category,
            sourceProvenance,
            prerequisiteLessonIds = prerequisiteLessonId is null ? Array.Empty<string>() : new[] { prerequisiteLessonId },
            contentBlocks,
            exercises,
            version = 1,
            updatedAt = now.ToString("O")
        };

        return new GrammarLesson
        {
            Id = id,
            ExamTypeCode = examTypeCode,
            Title = title,
            Description = description,
            Category = category,
            Level = level,
            ContentHtml = JsonSupport.Serialize(document),
            ExercisesJson = JsonSupport.Serialize(exercises),
            EstimatedMinutes = estimatedMinutes,
            SortOrder = sortOrder,
            PrerequisiteLessonId = prerequisiteLessonId,
            Status = "active"
        };
    }

    private static object GrammarContentBlock(string id, int sortOrder, string type, string contentMarkdown)
        => new { id, sortOrder, type, contentMarkdown };

    private static object GrammarExercise(
        string id,
        int sortOrder,
        string type,
        string promptMarkdown,
        object options,
        object correctAnswer,
        string[] acceptedAnswers,
        string explanationMarkdown,
        string difficulty,
        int points)
        => new
        {
            id,
            sortOrder,
            type,
            promptMarkdown,
            options,
            correctAnswer,
            acceptedAnswers,
            explanationMarkdown,
            difficulty,
            points
        };

    // ────────────────────────────────────────────────────────────────────
    // AI Usage Management seed data. See docs/AI-USAGE-POLICY.md.
    // ────────────────────────────────────────────────────────────────────

    private static void SeedAiQuotaPlans(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AiQuotaPlans.AddRange(
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "free", Name = "Free tier",
                Description = "Entry tier — tight caps, conversational features only.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 20_000, DailyTokenCap = 5_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Join(",",
                    AiFeatureCodes.ConversationReply,
                    AiFeatureCodes.ConversationOpening,
                    AiFeatureCodes.ConversationEvaluation,
                    AiFeatureCodes.VocabularyGloss,
                    AiFeatureCodes.SummarisePassage),
                IsActive = true, DisplayOrder = 10,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "starter", Name = "Starter",
                Description = "Entry paid tier — all practice features, capped grading.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 200_000, DailyTokenCap = 50_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Empty, // all features
                IsActive = true, DisplayOrder = 20,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "pro", Name = "Pro",
                Description = "Unlimited practice + generous grading allowance.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 1_000_000, DailyTokenCap = 200_000,
                RolloverPolicy = AiQuotaRolloverPolicy.RolloverCapped, RolloverCapPct = 20,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Empty,
                IsActive = true, DisplayOrder = 30,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "enterprise", Name = "Enterprise",
                Description = "Sponsored / institutional tier, very high limits.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 10_000_000, DailyTokenCap = 2_000_000,
                RolloverPolicy = AiQuotaRolloverPolicy.RolloverCapped, RolloverCapPct = 50,
                OveragePolicy = AiOveragePolicy.AllowWithCharge,
                OverageRatePer1kTokens = 0.004m,
                AllowedFeaturesCsv = string.Empty,
                IsActive = true, DisplayOrder = 40,
                CreatedAt = now, UpdatedAt = now,
            }
        );
    }

    private static void SeedAiGlobalPolicy(LearnerDbContext db)
    {
        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            KillSwitchEnabled = false,
            KillSwitchScope = AiKillSwitchScope.PlatformKeysOnly,
            MonthlyBudgetUsd = 0m,                  // admin sets this on /admin/ai-usage → Budget
            SoftWarnPct = 80,
            HardKillPct = 100,
            AllowByokOnScoringFeatures = false,     // safe default; see §1
            AllowByokOnNonScoringFeatures = true,
            DefaultPlatformProviderId = "digitalocean-serverless",
            ByokErrorCooldownHours = 24,
            ByokTransientRetryCount = 2,
            AnomalyDetectionEnabled = true,
            AnomalyMultiplierX = 10m,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static void SeedAiProviderStub(LearnerDbContext db)
    {
        // Production default: DigitalOcean Serverless Inference with
        // Anthropic Claude Opus 4.7 (high reasoning effort). The API key is
        // supplied via AI__ApiKey env var and synchronised into this row at
        // boot (DatabaseBootstrapper) — never committed to source.
        var now = DateTimeOffset.UtcNow;
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "digitalocean-serverless",
            Name = "DigitalOcean Serverless Inference (Claude Opus 4.7)",
            Dialect = AiProviderDialect.OpenAiCompatible,
            BaseUrl = "https://inference.do-ai.run/v1",
            EncryptedApiKey = string.Empty,  // synchronised from AI__ApiKey at boot
            ApiKeyHint = "(synchronised from AI__ApiKey env var at boot)",
            DefaultModel = "anthropic-claude-opus-4.7",
            PricePer1kPromptTokens = 0.0150m,     // Claude Opus 4.x input rate
            PricePer1kCompletionTokens = 0.0750m, // Claude Opus 4.x output rate
            RetryCount = 2,
            CircuitBreakerThreshold = 5,
            CircuitBreakerWindowSeconds = 30,
            FailoverPriority = 100,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
