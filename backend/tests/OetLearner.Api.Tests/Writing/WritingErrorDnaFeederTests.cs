using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests.Writing;

/// <summary>Error-DNA feeder: published writing findings (high confidence only)
/// become the learner's evidenced recurring errors. Low-confidence detections are
/// skipped — feeding uncertainty would fabricate weaknesses.</summary>
public sealed class WritingErrorDnaFeederTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public WritingErrorDnaFeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private async Task<Guid> SeedReportAsync(WritingAssessmentV11Status status)
    {
        await using var db = new LearnerDbContext(_options);
        var report = new WritingAssessmentReportV11
        {
            Id = Guid.NewGuid(),
            SubmissionId = Guid.NewGuid(),
            Status = status,
            Profession = "medicine",
            LetterType = "referral",
            RulePackVersion = "test",
            ModelVersion = "test",
            CalibrationSetVersion = "test",
        };
        db.WritingAssessmentReportsV11.Add(report);
        db.WritingAssessmentErrors.AddRange(
            new WritingAssessmentError
            {
                Id = Guid.NewGuid(), ReportId = report.Id, Category = "Grammar",
                CandidateWording = "advised to reducing salt", Correction = "advised to reduce salt",
                RuleSource = "verb pattern after 'advise to'", Severity = "major", Confidence = "high",
                PrimaryCriterionCode = "LANG",
            },
            new WritingAssessmentError
            {
                Id = Guid.NewGuid(), ReportId = report.Id, Category = "Punctuation",
                CandidateWording = "however ,", Correction = "however,",
                Severity = "minor", Confidence = "low",
                PrimaryCriterionCode = "LANG",
            });
        await db.SaveChangesAsync(ct);
        return report.Id;
    }

    [Fact]
    public async Task Feeder_SkipsLowConfidence_AndUsesRuleSourceAsPattern()
    {
        var reportId = await SeedReportAsync(WritingAssessmentV11Status.CandidateReady);
        var svc = new WritingErrorDnaFeeder(new LearnerDbContext(_options),
            new ErrorDnaService(new LearnerDbContext(_options), new FixedClock()), NullLogger.Instance);

        var fed = await svc.FeedFromReportAsync(reportId, "usr-1", CancellationToken.None);

        Assert.Equal(1, fed); // only the high-confidence finding
        var errors = await new ErrorDnaService(new LearnerDbContext(_options), new FixedClock())
            .TopWeaknessesAsync("usr-1", 10, CancellationToken.None);
        var entry = Assert.Single(errors);
        Assert.Equal("grammar", entry.Category);
        Assert.Equal("verb pattern after 'advise to'", entry.Pattern);
    }

    [Fact]
    public async Task Feeder_IgnoresReportsThatAreNotCandidateReady()
    {
        var reportId = await SeedReportAsync(WritingAssessmentV11Status.BlockedReleaseGate);
        var svc = new WritingErrorDnaFeeder(new LearnerDbContext(_options),
            new ErrorDnaService(new LearnerDbContext(_options), new FixedClock()), NullLogger.Instance);

        var fed = await svc.FeedFromReportAsync(reportId, "usr-1", CancellationToken.None);

        Assert.Equal(0, fed);
        Assert.Empty(await new ErrorDnaService(new LearnerDbContext(_options), new FixedClock())
            .TopWeaknessesAsync("usr-1", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Feeder_RepeatsAreUpserted_NotDuplicated()
    {
        var reportId = await SeedReportAsync(WritingAssessmentV11Status.CandidateReady);
        var svc = new WritingErrorDnaFeeder(new LearnerDbContext(_options),
            new ErrorDnaService(new LearnerDbContext(_options), new FixedClock()), NullLogger.Instance);

        await svc.FeedFromReportAsync(reportId, "usr-1", CancellationToken.None);
        await svc.FeedFromReportAsync(reportId, "usr-1", CancellationToken.None);

        var errors = await new ErrorDnaService(new LearnerDbContext(_options), new FixedClock())
            .TopWeaknessesAsync("usr-1", 10, CancellationToken.None);
        var entry = Assert.Single(errors);
        Assert.Equal(2, entry.EvidenceCount);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 13, 9, 0, 0, TimeSpan.Zero);
    }

    private static ILogger<WritingErrorDnaFeeder> NullLogger =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<WritingErrorDnaFeeder>.Instance;
}
