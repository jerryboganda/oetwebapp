using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Tests.Writing;

/// <summary>
/// Covers <see cref="WritingTaskAuthoringService.BulkAsync"/>: bulk archive,
/// the delete-vs-force-delete learner-data safety distinction, the force-delete
/// cascade across submissions/grades/appeals/annotations/moderation/attempt
/// events and scenario-owned children, and unknown-action rejection. Parity with
/// the Reading/Listening papers bulk path.
/// </summary>
public class WritingTaskBulkActionTests
{
    private static (LearnerDbContext db, WritingTaskAuthoringService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var svc = new WritingTaskAuthoringService(
            db, NullLogger<WritingTaskAuthoringService>.Instance);
        return (db, svc);
    }

    private static async Task<WritingScenario> SeedScenarioAsync(
        LearnerDbContext db, string status = "draft")
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = "Referral letter",
            LetterType = "routine_referral",
            Profession = "medicine",
            Status = status,
            AuthorId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.WritingScenarios.Add(scenario);
        db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            Ordinal = 0,
            SentenceText = "Patient presents with hypertension.",
            CreatedAt = now,
        });
        db.WritingScenarioEmbeddings.Add(new WritingScenarioEmbedding
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenario.Id,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return scenario;
    }

    /// <summary>Adds a learner submission with a full graded/appealed/moderated/annotated trail.</summary>
    private static async Task<Guid> SeedLearnerTrailAsync(LearnerDbContext db, Guid scenarioId)
    {
        var now = DateTimeOffset.UtcNow;
        var submission = new WritingSubmission
        {
            Id = Guid.NewGuid(),
            UserId = "learner-1",
            ScenarioId = scenarioId,
            LetterContent = "Dear Dr Smith, ...",
            LetterContentHash = "hash",
            Status = "graded",
            StartedAt = now,
            SubmittedAt = now,
            CreatedAt = now,
        };
        db.WritingSubmissions.Add(submission);
        db.WritingGrades.Add(new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            BandLabel = "B",
            ModelUsed = "test",
            CanonVersion = "v1",
            GradedAt = now,
            CreatedAt = now,
        });
        db.WritingScoreAppeals.Add(new WritingScoreAppeal
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            OriginalGradeId = Guid.NewGuid(),
            UserId = "learner-1",
            Reason = "Please re-mark",
            RequestedAt = now,
        });
        db.WritingFeedbackAnnotations.Add(new WritingFeedbackAnnotation
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            TutorId = "tutor-1",
            CreatedAt = now,
        });
        db.WritingModerations.Add(new WritingModeration
        {
            Id = Guid.NewGuid(),
            SubmissionId = submission.Id,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.WritingAttemptEvents.Add(new WritingAttemptEvent
        {
            Id = Guid.NewGuid(),
            UserId = "learner-1",
            ScenarioId = scenarioId,
            SubmissionId = submission.Id,
            EventType = "submit_clicked",
            Timestamp = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return submission.Id;
    }

    [Fact]
    public async Task BulkArchive_SetsStatusAndCountsSucceeded()
    {
        var (db, svc) = Build();
        var a = await SeedScenarioAsync(db, "draft");
        var b = await SeedScenarioAsync(db, "published");

        var result = await svc.BulkAsync("archive", new[] { a.Id.ToString(), b.Id.ToString() });

        Assert.Equal(2, result.TotalRequested);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.All(await db.WritingScenarios.ToListAsync(), s => Assert.Equal("archived", s.Status));
    }

    [Fact]
    public async Task BulkDelete_NoLearnerData_RemovesScenarioAndChildren()
    {
        var (db, svc) = Build();
        var scenario = await SeedScenarioAsync(db);

        var result = await svc.BulkAsync("delete", new[] { scenario.Id.ToString() });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Skipped);
        Assert.False(await db.WritingScenarios.AnyAsync(s => s.Id == scenario.Id));
        Assert.False(await db.WritingScenarioStructuredSentences.AnyAsync(x => x.ScenarioId == scenario.Id));
        Assert.False(await db.WritingScenarioEmbeddings.AnyAsync(x => x.ScenarioId == scenario.Id));
    }

    [Fact]
    public async Task BulkDelete_WithLearnerData_SkipsAndKeepsEverything()
    {
        var (db, svc) = Build();
        var scenario = await SeedScenarioAsync(db);
        await SeedLearnerTrailAsync(db, scenario.Id);

        var result = await svc.BulkAsync("delete", new[] { scenario.Id.ToString() });

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        Assert.True(await db.WritingScenarios.AnyAsync(s => s.Id == scenario.Id));
        Assert.True(await db.WritingSubmissions.AnyAsync(s => s.ScenarioId == scenario.Id));
    }

    [Fact]
    public async Task BulkForceDelete_WithLearnerData_PurgesEverything()
    {
        var (db, svc) = Build();
        var scenario = await SeedScenarioAsync(db);
        var submissionId = await SeedLearnerTrailAsync(db, scenario.Id);

        var result = await svc.BulkAsync("force-delete", new[] { scenario.Id.ToString() });

        Assert.Equal(1, result.Succeeded);
        Assert.False(await db.WritingScenarios.AnyAsync(s => s.Id == scenario.Id));
        Assert.False(await db.WritingSubmissions.AnyAsync(s => s.Id == submissionId));
        Assert.False(await db.WritingGrades.AnyAsync(g => g.SubmissionId == submissionId));
        Assert.False(await db.WritingScoreAppeals.AnyAsync(a => a.SubmissionId == submissionId));
        Assert.False(await db.WritingFeedbackAnnotations.AnyAsync(a => a.SubmissionId == submissionId));
        Assert.False(await db.WritingModerations.AnyAsync(m => m.SubmissionId == submissionId));
        Assert.False(await db.WritingAttemptEvents.AnyAsync(e => e.ScenarioId == scenario.Id));
        Assert.False(await db.WritingScenarioStructuredSentences.AnyAsync(x => x.ScenarioId == scenario.Id));
        Assert.False(await db.WritingScenarioEmbeddings.AnyAsync(x => x.ScenarioId == scenario.Id));
    }

    [Fact]
    public async Task Bulk_UnknownAction_Throws()
    {
        var (db, svc) = Build();
        var scenario = await SeedScenarioAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.BulkAsync("nuke", new[] { scenario.Id.ToString() }));
    }
}
