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

        if (await EnsureSignupCatalogAsync(db, cancellationToken))
        {
            hasChanges = true;
        }

        if (!await db.ExamFamilies.AnyAsync(cancellationToken))
        {
            SeedExamFamilies(db);
            hasChanges = true;
        }

        // Demo billing catalog (Basic/Premium/Intensive plans, demo add-ons & coupons) is
        // intentionally NOT auto-seeded: it kept re-appearing in production after admins
        // deleted it. The real catalog is managed in the admin UI / Oet2026CatalogSeeder.
        // The demo catalog seeder has been removed.

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

        // Vocabulary seed disabled — admin manages recalls catalog manually.
        // if (!await db.VocabularyTerms.AnyAsync(cancellationToken))
        // {
        //     SeedVocabularyTerms(db);
        //     hasChanges = true;
        // }
        // else if (await EnsureMissingOetVocabularyBankAsync(db, cancellationToken))
        // {
        //     hasChanges = true;
        // }

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
        else if (await EnsureSeededPublishedContentCompatibilityAsync(db, cancellationToken))
        {
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

        // Companion tiers (F-136/137/138, priced by owner delegation 2026-09-07:
        // Plus £9/mo, Pro £19/mo, Ultimate £39/mo). Seeded per-code so existing
        // databases gain them without touching the base plans above. Quota
        // POLICY only for now: sellable products + subscription mapping are a
        // separate billing project (see traceability notes).
        var companionTiers = CompanionQuotaTiers();
        var existingTierCodes = (await db.AiQuotaPlans
            .AsNoTracking()
            .Select(plan => plan.Code)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in companionTiers.Where(tier => !existingTierCodes.Contains(tier.Code)))
        {
            db.AiQuotaPlans.Add(tier);
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

    private static async Task<bool> EnsureSeededPublishedContentCompatibilityAsync(
        LearnerDbContext db,
        CancellationToken cancellationToken)
    {
        // Some early production databases stored the seeded learner catalogue
        // with integer status value 1 when the enum meant "Published". The
        // workflow enum later gained review states before Published, so those
        // same rows now hydrate as InReview and learners cannot start attempts
        // from the public diagnostic/reading, writing, speaking, listening, or
        // course-entry routes. Repair only the known platform-seeded IDs; do
        // not change administrator-authored draft/review content.
        var hasChanges = false;
        var now = DateTimeOffset.UtcNow;

        static bool Publish(ContentStatus status) => status != ContentStatus.Published;

        var seededContentItemIds = new[]
        {
            "wt-001", "wt-002",
            "st-001", "st-002",
            "rt-001", "lt-001",
            "sd-phrasing-001", "sd-intonation-001", "sd-pronunciation-001",
            "sd-vocabulary-001", "sd-chunking-001", "sd-empathy-001"
        };
        var contentItems = await db.ContentItems
            .Where(item => seededContentItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        foreach (var item in contentItems.Where(item => Publish(item.Status)))
        {
            item.Status = ContentStatus.Published;
            item.PublishedAt ??= now;
            item.UpdatedAt = now;
            hasChanges = true;
        }

        var seededPackageIds = new[]
        {
            "pkg-full-en-2026", "pkg-full-ar-nursing-2026", "pkg-full-ar-medicine-2026",
            "pkg-crash-en-general", "pkg-crash-ar-general", "pkg-crash-en-pharmacy",
            "pkg-foundation-basic-en", "pkg-combo-lr-recalls"
        };
        var packages = await db.ContentPackages
            .Where(package => seededPackageIds.Contains(package.Id))
            .ToListAsync(cancellationToken);
        foreach (var package in packages.Where(package => Publish(package.Status)))
        {
            package.Status = ContentStatus.Published;
            package.PublishedAt ??= now;
            package.UpdatedAt = now;
            hasChanges = true;
        }

        var seededProgramIds = new[]
        {
            "prg-full-en-2026", "prg-full-ar-nursing-2026", "prg-full-ar-medicine-2026",
            "prg-crash-en-general", "prg-crash-ar-general", "prg-crash-en-pharmacy",
            "prg-foundation-basic-en"
        };
        var programs = await db.ContentPrograms
            .Where(program => seededProgramIds.Contains(program.Id))
            .ToListAsync(cancellationToken);
        foreach (var program in programs.Where(program => Publish(program.Status)))
        {
            program.Status = ContentStatus.Published;
            program.PublishedAt ??= now;
            program.UpdatedAt = now;
            hasChanges = true;
        }

        var seededTrackIds = new[]
        {
            "trk-full-en-writing", "trk-full-en-speaking", "trk-full-en-reading", "trk-full-en-listening",
            "trk-ar-nursing-writing", "trk-ar-nursing-speaking", "trk-ar-nursing-reading", "trk-ar-nursing-listening",
            "trk-foundation-core"
        };
        var tracks = await db.ContentTracks
            .Where(track => seededTrackIds.Contains(track.Id))
            .ToListAsync(cancellationToken);
        foreach (var track in tracks.Where(track => Publish(track.Status)))
        {
            track.Status = ContentStatus.Published;
            hasChanges = true;
        }

        var seededModuleIds = new[]
        {
            "mod-full-en-wr-01", "mod-full-en-wr-02", "mod-full-en-wr-03",
            "mod-full-en-sp-01", "mod-full-en-sp-02", "mod-full-en-rd-01", "mod-full-en-lt-01",
            "mod-foundation-grammar", "mod-foundation-vocab", "mod-foundation-reading"
        };
        var modules = await db.ContentModules
            .Where(module => seededModuleIds.Contains(module.Id))
            .ToListAsync(cancellationToken);
        foreach (var module in modules.Where(module => Publish(module.Status)))
        {
            module.Status = ContentStatus.Published;
            hasChanges = true;
        }

        var seededLessonIds = new[]
        {
            "lsn-wr-01-task", "lsn-wr-02-task", "lsn-sp-01-task",
            "lsn-sp-02-task", "lsn-rd-01-task", "lsn-lt-01-task"
        };
        var lessons = await db.ContentLessons
            .Where(lesson => seededLessonIds.Contains(lesson.Id))
            .ToListAsync(cancellationToken);
        foreach (var lesson in lessons.Where(lesson => Publish(lesson.Status)))
        {
            lesson.Status = ContentStatus.Published;
            hasChanges = true;
        }

        var seededPreviewIds = new[]
        {
            "fp-grammar-sample", "fp-writing-sample", "fp-speaking-sample",
            "fp-reading-sample", "fp-listening-sample"
        };
        var previewAssets = await db.FreePreviewAssets
            .Where(asset => seededPreviewIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        foreach (var asset in previewAssets.Where(asset => Publish(asset.Status)))
        {
            asset.Status = ContentStatus.Published;
            hasChanges = true;
        }

        return hasChanges;
    }

    public static async Task EnsureDemoDataAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(x => x.Id == "mock-user-001", cancellationToken))
        {
            SeedDemoUser(db);
            await db.SaveChangesAsync(cancellationToken);
        }

        // The demo goal already contains a real target date. Keep the new
        // confirmation marker aligned on existing development databases so the
        // authenticated dashboard does not get redirected into first-run goal
        // setup during browser and native performance checks.
        var demoGoal = await db.Goals.SingleOrDefaultAsync(
            goal => goal.UserId == "mock-user-001",
            cancellationToken);
        if (demoGoal?.TargetExamDate is not null && !demoGoal.TargetExamDateSetByUser)
        {
            demoGoal.TargetExamDateSetByUser = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        EnsureLocalAuthAccounts(db);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureSpeakingMockSetsAsync(LearnerDbContext db, CancellationToken cancellationToken = default)
    {
        // Wave 3 of docs/SPEAKING-MODULE-PLAN.md - seed a single canonical
        // speaking mock set so the orchestrator UI has something to bind
        // to in dev/staging without authoring content. Only inserted when
        // both underlying role-play content papers exist and no mock set
        // is already present.
        if (await db.SpeakingMockSets.AnyAsync(cancellationToken))
        {
            return;
        }

        var rolePlay1 = await db.ContentItems.FirstOrDefaultAsync(
            x => x.Id == "st-001" && x.SubtestCode == "speaking", cancellationToken);
        var rolePlay2 = await db.ContentItems.FirstOrDefaultAsync(
            x => x.Id == "st-002" && x.SubtestCode == "speaking", cancellationToken);
        if (rolePlay1 is null || rolePlay2 is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.SpeakingMockSets.Add(new SpeakingMockSet
        {
            Id = "sms-nursing-core-1",
            ProfessionId = "nursing",
            Title = "Nursing Mock Set 1 - Core",
            Description = "Two paired role-plays (handover + breaking bad news) attempted as a single OET-style mock.",
            RolePlay1ContentId = rolePlay1.Id,
            RolePlay2ContentId = rolePlay2.Id,
            Status = SpeakingMockSetStatus.Published,
            Difficulty = "core",
            CriteriaFocus = "informationGiving,relationshipBuilding,providingStructure",
            Tags = "nursing,core,handover",
            SortOrder = 1,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
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
                ActorName = "Dr. Ahmed Hesham",
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
                ActorName = "Dr. Ahmed Hesham",
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
                ActorName = "Dr. Ahmed Hesham",
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
                ActorName = "Dr. Ahmed Hesham",
                Action = "Saved Review Draft",
                ResourceType = "ExpertReview",
                ResourceId = "review-queue-002",
                Details = "Tutor review draft saved."
            });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureDemoMediaAsync(
        LearnerDbContext db,
        OetLearner.Api.Services.Content.IFileStorage storage,
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

        // MISSION CRITICAL (AGENTS.md §Content uploads): never write raw files via File.* — route
        // all blob writes through IFileStorage so storage swaps (S3/R2) remain a DI-only change.
        await DemoMediaSeedLock.WaitAsync(cancellationToken);
        try
        {
            if (await storage.ExistsAsync(demoAudioStorageKey, cancellationToken))
            {
                return;
            }

            var payload = BuildDemoWaveFile();
            using var source = new MemoryStream(payload, writable: false);
            await storage.WriteAsync(demoAudioStorageKey, source, cancellationToken);
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
        // IAM-01: seed accounts are hashed with the same PBKDF2-HMAC-SHA512/>=220k
        // profile the DI-configured hasher uses (PasswordHasherPolicy).
        var passwordHasher = OetLearner.Api.Security.PasswordHasherPolicy.CreateHasher<ApplicationUserAccount>();

        var learner = db.Users.Single(x => x.Id == "mock-user-001");
        var expert = db.ExpertUsers.Single(x => x.Id == "expert-001");
        var secondaryExpert = db.ExpertUsers.SingleOrDefault(x => x.Id == "expert-unauthorised");
        if (secondaryExpert is null)
        {
            secondaryExpert = new ExpertUser
            {
                Id = "expert-unauthorised",
                Role = ApplicationUserRoles.Expert,
                DisplayName = "Tutor Reviewer Two",
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
        IPasswordHasher<ApplicationUserAccount> passwordHasher,
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
            TargetExamDateSetByUser = true,
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

    private static void SeedGrammarLessons(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        SeedGrammarStarterCatalog(db, now);
    }

    private static object GrammarContentBlock(string id, int sortOrder, string type, string contentMarkdown)
        => new { id, sortOrder, type, contentMarkdown };
}
