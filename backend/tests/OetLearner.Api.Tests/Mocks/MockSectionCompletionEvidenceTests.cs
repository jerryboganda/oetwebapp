using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Mocks;

public class MockSectionCompletionEvidenceTests
{
    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task ReadingSectionCompletion_AcceptsSubmittedReadingAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-reading", "section-reading", "bundle-section-reading", "learner-1", "reading", "paper-reading", now);
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-attempt-1",
            UserId = "learner-1",
            PaperId = "paper-reading",
            Status = ReadingAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-60),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.BindSectionContentAttemptIfRequestedAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            "reading-attempt-1",
            "reading",
            "paper-reading",
            CancellationToken.None);

        await service.CompleteMockSectionAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            CompletionRequest("reading-attempt-1"),
            CancellationToken.None);

        var section = await db.MockSectionAttempts.SingleAsync(x => x.Id == "section-reading");
        Assert.Equal("reading-attempt-1", section.ContentAttemptId);
        Assert.Equal(AttemptState.Completed, section.State);
        Assert.Equal(30, section.RawScore);
        Assert.Equal(42, section.RawScoreMax);
        Assert.Equal(OetScoring.OetRawToScaled(30), section.ScaledScore);
        Assert.Equal("B", section.Grade);
    }

    [Fact]
    public async Task ReadingSectionCompletion_RejectsUnboundSubmittedReadingAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-reading", "section-reading", "bundle-section-reading", "learner-1", "reading", "paper-reading", now);
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-attempt-1",
            UserId = "learner-1",
            PaperId = "paper-reading",
            Status = ReadingAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-60),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var service = new MockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            CompletionRequest("reading-attempt-1"),
            CancellationToken.None));

        Assert.Equal("content_attempt_not_bound", ex.ErrorCode);
    }

    [Fact]
    public async Task ReadingSectionCompletion_RejectsPriorSamePaperAttemptReplay()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-reading", "section-reading", "bundle-section-reading", "learner-1", "reading", "paper-reading", now);
        db.ReadingAttempts.AddRange(
            new ReadingAttempt
            {
                Id = "reading-attempt-prior",
                UserId = "learner-1",
                PaperId = "paper-reading",
                Status = ReadingAttemptStatus.Submitted,
                StartedAt = now.AddDays(-7),
                SubmittedAt = now.AddDays(-7).AddMinutes(45),
                LastActivityAt = now.AddDays(-7).AddMinutes(45),
                RawScore = 42,
                MaxRawScore = 42,
            },
            new ReadingAttempt
            {
                Id = "reading-attempt-bound",
                UserId = "learner-1",
                PaperId = "paper-reading",
                Status = ReadingAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-45),
                SubmittedAt = now,
                LastActivityAt = now,
                RawScore = 30,
                MaxRawScore = 42,
            });
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.BindSectionContentAttemptIfRequestedAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            "reading-attempt-bound",
            "reading",
            "paper-reading",
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            CompletionRequest("reading-attempt-prior"),
            CancellationToken.None));

        Assert.Equal("content_attempt_mismatch", ex.ErrorCode);
    }

    [Fact]
    public async Task ReadingSectionCompletion_RequiresCanonicalEvidence()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-reading", "section-reading", "bundle-section-reading", "learner-1", "reading", "paper-reading", now);
        await db.SaveChangesAsync();

        var service = new MockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            CompletionRequest(null),
            CancellationToken.None));

        Assert.Equal("content_attempt_required", ex.ErrorCode);
    }

    [Fact]
    public async Task ReadingSectionCompletion_RejectsLegacyAttemptEvidence()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-reading", "section-reading", "bundle-section-reading", "learner-1", "reading", "paper-reading", now);
        db.Attempts.Add(new Attempt
        {
            Id = "legacy-reading-attempt",
            UserId = "learner-1",
            ContentId = "paper-reading",
            SubtestCode = "reading",
            Context = "mock",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-60),
            SubmittedAt = now,
            CompletedAt = now,
            AnswersJson = "{}",
        });
        await db.SaveChangesAsync();

        var legacySection = await db.MockSectionAttempts.SingleAsync(x => x.Id == "section-reading");
        legacySection.ContentAttemptId = "legacy-reading-attempt";
        await db.SaveChangesAsync();

        var service = new MockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-1",
            "mock-reading",
            "section-reading",
            CompletionRequest("legacy-reading-attempt"),
            CancellationToken.None));

        Assert.Equal("content_attempt_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task ListeningSectionCompletion_AcceptsSubmittedListeningAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-listening", "section-listening", "bundle-section-listening", "learner-2", "listening", "paper-listening", now);
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-attempt-1",
            UserId = "learner-2",
            PaperId = "paper-listening",
            Status = ListeningAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-45),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.BindSectionContentAttemptIfRequestedAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            "listening-attempt-1",
            "listening",
            "paper-listening",
            CancellationToken.None);

        await service.CompleteMockSectionAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            CompletionRequest("listening-attempt-1"),
            CancellationToken.None);

        var section = await db.MockSectionAttempts.SingleAsync(x => x.Id == "section-listening");
        Assert.Equal("listening-attempt-1", section.ContentAttemptId);
        Assert.Equal(AttemptState.Completed, section.State);
        Assert.Equal(30, section.RawScore);
        Assert.Equal(42, section.RawScoreMax);
        Assert.Equal(OetScoring.OetRawToScaled(30), section.ScaledScore);
        Assert.Equal("B", section.Grade);
    }

    [Fact]
    public async Task ListeningSectionCompletion_RejectsPriorSamePaperAttemptReplay()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-listening", "section-listening", "bundle-section-listening", "learner-2", "listening", "paper-listening", now);
        db.ListeningAttempts.AddRange(
            new ListeningAttempt
            {
                Id = "listening-attempt-prior",
                UserId = "learner-2",
                PaperId = "paper-listening",
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddDays(-7),
                SubmittedAt = now.AddDays(-7).AddMinutes(45),
                LastActivityAt = now.AddDays(-7).AddMinutes(45),
                RawScore = 42,
                MaxRawScore = 42,
            },
            new ListeningAttempt
            {
                Id = "listening-attempt-bound",
                UserId = "learner-2",
                PaperId = "paper-listening",
                Status = ListeningAttemptStatus.Submitted,
                StartedAt = now.AddMinutes(-45),
                SubmittedAt = now,
                LastActivityAt = now,
                RawScore = 30,
                MaxRawScore = 42,
            });
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.BindSectionContentAttemptIfRequestedAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            "listening-attempt-bound",
            "listening",
            "paper-listening",
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            CompletionRequest("listening-attempt-prior"),
            CancellationToken.None));

        Assert.Equal("content_attempt_mismatch", ex.ErrorCode);
    }

    [Fact]
    public async Task ListeningSectionBinding_RejectsJsonFallbackMockPaper()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(
            db,
            "mock-listening",
            "section-listening",
            "bundle-section-listening",
            "learner-2",
            "listening",
            "paper-listening",
            now,
            seedListeningStructure: false);
        await db.SaveChangesAsync();

        var service = new MockService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.ValidateSectionContentAttemptBindingTargetIfRequestedAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            "listening",
            "paper-listening",
            CancellationToken.None));

        Assert.Equal("mock_listening_structure_required", ex.ErrorCode);
    }

    [Fact]
    public async Task ListeningSectionCompletion_RejectsInProgressListeningAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        SeedMockSection(db, "mock-listening", "section-listening", "bundle-section-listening", "learner-2", "listening", "paper-listening", now);
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-attempt-1",
            UserId = "learner-2",
            PaperId = "paper-listening",
            Status = ListeningAttemptStatus.InProgress,
            StartedAt = now.AddMinutes(-45),
            LastActivityAt = now,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var service = new MockService(db);
        await service.BindSectionContentAttemptIfRequestedAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            "listening-attempt-1",
            "listening",
            "paper-listening",
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompleteMockSectionAsync(
            "learner-2",
            "mock-listening",
            "section-listening",
            CompletionRequest("listening-attempt-1"),
            CancellationToken.None));

        Assert.Equal("content_attempt_not_found", ex.ErrorCode);
    }

    private static MockSectionCompleteRequest CompletionRequest(string? contentAttemptId) => new(
        contentAttemptId,
        RawScore: 42,
        RawScoreMax: 42,
        ScaledScore: 500,
        Grade: "A",
        Evidence: new Dictionary<string, object?> { ["source"] = "test" });

    private static void SeedMockSection(
        LearnerDbContext db,
        string mockAttemptId,
        string sectionAttemptId,
        string bundleSectionId,
        string userId,
        string subtestCode,
        string paperId,
        DateTimeOffset now,
        bool seedListeningStructure = true)
    {
        db.MockBundles.Add(new MockBundle
        {
            Id = "bundle-test",
            Title = "Evidence validation bundle",
            Slug = $"evidence-validation-{mockAttemptId}",
            MockType = MockTypes.Diagnostic,
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 60,
            ReleasePolicy = MockReleasePolicies.Instant,
            SourceStatus = MockSourceStatuses.Original,
            QualityStatus = MockQualityStatuses.Approved,
            SourceProvenance = "Mock section evidence validation test seed.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = subtestCode,
            Title = $"{subtestCode} evidence paper",
            Slug = $"{paperId}-slug",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = "Mock section evidence validation test paper.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.MockAttempts.Add(new MockAttempt
        {
            Id = mockAttemptId,
            UserId = userId,
            MockBundleId = "bundle-test",
            MockType = MockTypes.Diagnostic,
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-10),
            ConfigJson = "{}",
            ReviewSelection = "none",
            StrictTimer = false,
        });
        db.MockBundleSections.Add(new MockBundleSection
        {
            Id = bundleSectionId,
            MockBundleId = "bundle-test",
            SectionOrder = 1,
            SubtestCode = subtestCode,
            ContentPaperId = paperId,
            TimeLimitMinutes = 60,
            ReviewEligible = false,
            IsRequired = true,
            CreatedAt = now,
        });
        db.MockSectionAttempts.Add(new MockSectionAttempt
        {
            Id = sectionAttemptId,
            MockAttemptId = mockAttemptId,
            MockBundleSectionId = bundleSectionId,
            SubtestCode = subtestCode,
            ContentPaperId = paperId,
            LaunchRoute = "/mocks",
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-10),
        });
        if (seedListeningStructure && string.Equals(subtestCode, "listening", StringComparison.OrdinalIgnoreCase))
        {
            db.ListeningQuestions.Add(new ListeningQuestion
            {
                Id = $"{paperId}-q1",
                PaperId = paperId,
                ListeningPartId = $"{paperId}-part-a1",
                QuestionNumber = 1,
                DisplayOrder = 1,
                Points = 1,
                QuestionType = ListeningQuestionType.ShortAnswer,
                Stem = "Dose: ____ milligrams",
                CorrectAnswerJson = "\"five\"",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }
}