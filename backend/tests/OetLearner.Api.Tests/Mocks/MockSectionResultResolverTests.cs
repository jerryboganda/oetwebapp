using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Mocks.Results;

namespace OetLearner.Api.Tests.Mocks;

public class MockSectionResultResolverTests
{
    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task ReadingAdapter_OverwritesTamperedSectionScore_FromAuthoritativeReadingAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var mockAttempt = SeedMockAttempt("mock-reading", "learner-1", "bundle-reading", now);
        var bundleSection = SeedBundleSection("section-reading", "bundle-reading", "reading", "paper-reading", now);
        var sectionAttempt = SeedSectionAttempt("section-attempt-reading", mockAttempt.Id, bundleSection, "reading-attempt-1");
        sectionAttempt.RawScore = 42;
        sectionAttempt.RawScoreMax = 42;
        sectionAttempt.ScaledScore = 500;
        db.MockAttempts.Add(mockAttempt);
        db.MockBundleSections.Add(bundleSection);
        db.MockSectionAttempts.Add(sectionAttempt);
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "reading-attempt-1",
            UserId = "learner-1",
            PaperId = "paper-reading",
            Status = ReadingAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-50),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var resolver = NewResolver();
        var resolved = await resolver.ResolveAsync(new MockSectionResultContext(db, mockAttempt, sectionAttempt, bundleSection), CancellationToken.None);

        Assert.Equal(30, resolved.RawScore);
        Assert.Equal(42, resolved.RawScoreMax);
        Assert.Equal(OetScoring.OetRawToScaled(30), resolved.ScaledScore);
        Assert.Equal("reading_attempt", resolved.EvidenceSource);
        Assert.Equal(30, sectionAttempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(30), sectionAttempt.ScaledScore);
        Assert.Equal("reading_attempt", ReadFeedbackSource(sectionAttempt.FeedbackJson));
    }

    [Fact]
    public async Task ListeningAdapter_OverwritesTamperedSectionScore_FromAuthoritativeListeningAttempt()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var mockAttempt = SeedMockAttempt("mock-listening", "learner-2", "bundle-listening", now);
        var bundleSection = SeedBundleSection("section-listening", "bundle-listening", "listening", "paper-listening", now);
        var sectionAttempt = SeedSectionAttempt("section-attempt-listening", mockAttempt.Id, bundleSection, "listening-attempt-1");
        sectionAttempt.RawScore = 1;
        sectionAttempt.RawScoreMax = 42;
        sectionAttempt.ScaledScore = 10;
        db.MockAttempts.Add(mockAttempt);
        db.MockBundleSections.Add(bundleSection);
        db.MockSectionAttempts.Add(sectionAttempt);
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "listening-attempt-1",
            UserId = "learner-2",
            PaperId = "paper-listening",
            Status = ListeningAttemptStatus.Submitted,
            StartedAt = now.AddMinutes(-42),
            SubmittedAt = now,
            LastActivityAt = now,
            RawScore = 30,
            MaxRawScore = 42,
        });
        await db.SaveChangesAsync();

        var resolver = NewResolver();
        var resolved = await resolver.ResolveAsync(new MockSectionResultContext(db, mockAttempt, sectionAttempt, bundleSection), CancellationToken.None);

        Assert.Equal(30, resolved.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(30), resolved.ScaledScore);
        Assert.Equal("listening_attempt", resolved.EvidenceSource);
        Assert.Equal(30, sectionAttempt.RawScore);
        Assert.Equal(OetScoring.OetRawToScaled(30), sectionAttempt.ScaledScore);
        Assert.Equal("listening_attempt", ReadFeedbackSource(sectionAttempt.FeedbackJson));
    }

    private static MockSectionResultResolver NewResolver() => new([
        new ReadingMockSectionResultAdapter(),
        new ListeningMockSectionResultAdapter(),
        new LegacyMockSectionResultAdapter(),
    ]);

    private static MockAttempt SeedMockAttempt(string id, string learnerId, string bundleId, DateTimeOffset now) => new()
    {
        Id = id,
        UserId = learnerId,
        MockBundleId = bundleId,
        MockType = "full",
        State = AttemptState.InProgress,
        StartedAt = now.AddHours(-1),
        ConfigJson = "{}",
    };

    private static MockBundleSection SeedBundleSection(
        string id,
        string bundleId,
        string subtestCode,
        string paperId,
        DateTimeOffset now) => new()
    {
        Id = id,
        MockBundleId = bundleId,
        SectionOrder = 1,
        SubtestCode = subtestCode,
        ContentPaperId = paperId,
        TimeLimitMinutes = 60,
        CreatedAt = now,
    };

    private static MockSectionAttempt SeedSectionAttempt(
        string id,
        string mockAttemptId,
        MockBundleSection bundleSection,
        string contentAttemptId) => new()
    {
        Id = id,
        MockAttemptId = mockAttemptId,
        MockBundleSectionId = bundleSection.Id,
        SubtestCode = bundleSection.SubtestCode,
        ContentPaperId = bundleSection.ContentPaperId,
        LaunchRoute = "/mocks",
        ContentAttemptId = contentAttemptId,
        State = AttemptState.Completed,
    };

    private static string? ReadFeedbackSource(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("evidenceSource").GetString();
    }
}