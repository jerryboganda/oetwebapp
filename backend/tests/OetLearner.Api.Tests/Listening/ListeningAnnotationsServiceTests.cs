using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening Module — Wave 2 of the OET Listening gap-fill plan. Exercises
/// <c>ListeningSessionService.SaveAnnotationsAsync</c> /
/// <c>GetAnnotationsAsync</c>. Verifies the 64 KB cap, JSON shape guard,
/// owner-only access, and in-progress requirement.
/// </summary>
public class ListeningAnnotationsServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ListeningSessionService NewService(LearnerDbContext db)
        => new(
            db,
            new ListeningModePolicyResolver(),
            new ListeningConfirmTokenService(Options.Create(new AuthTokenOptions
            {
                AccessTokenSigningKey = "test-signing-key-1234567890123456789012",
            })),
            TimeProvider.System);

    private static ListeningAttempt SeedInProgressAttempt(LearnerDbContext db, string userId = "learner-1")
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new ListeningAttempt
        {
            Id = "att-anno",
            UserId = userId,
            PaperId = "paper-anno",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 0,
            Mode = ListeningAttemptMode.Exam,
            Status = ListeningAttemptStatus.InProgress,
        };
        db.ListeningAttempts.Add(attempt);
        db.SaveChanges();
        return attempt;
    }

    [Fact]
    public async Task SaveAnnotationsAsync_persists_payload_and_reads_back()
    {
        await using var db = NewDb();
        SeedInProgressAttempt(db);
        var svc = NewService(db);
        var payload = "{\"highlights\":[{\"qid\":\"q-1\",\"text\":\"chronic pain\"}]}";

        await svc.SaveAnnotationsAsync("att-anno", "learner-1", payload, CancellationToken.None);
        var got = await svc.GetAnnotationsAsync("att-anno", "learner-1", CancellationToken.None);

        Assert.Equal(payload, got);
        var reloaded = await db.ListeningAttempts.SingleAsync(a => a.Id == "att-anno");
        Assert.Equal(payload, reloaded.AnnotationsJson);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_treats_blank_payload_as_clear()
    {
        await using var db = NewDb();
        var attempt = SeedInProgressAttempt(db);
        attempt.AnnotationsJson = "{\"highlights\":[]}";
        await db.SaveChangesAsync();
        var svc = NewService(db);

        await svc.SaveAnnotationsAsync("att-anno", "learner-1", "  ", CancellationToken.None);

        var reloaded = await db.ListeningAttempts.SingleAsync(a => a.Id == "att-anno");
        Assert.Null(reloaded.AnnotationsJson);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_invalid_json()
    {
        await using var db = NewDb();
        SeedInProgressAttempt(db);
        var svc = NewService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SaveAnnotationsAsync("att-anno", "learner-1", "not json", CancellationToken.None));
        Assert.Equal("listening_annotations_invalid_json", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_oversized_payload()
    {
        await using var db = NewDb();
        SeedInProgressAttempt(db);
        var svc = NewService(db);

        // Build a JSON document just over the 64 KB byte cap.
        var pad = new string('a', ListeningSessionService.MaxAnnotationsBytes);
        var oversized = $"{{\"pad\":\"{pad}\"}}";

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SaveAnnotationsAsync("att-anno", "learner-1", oversized, CancellationToken.None));
        Assert.Equal("listening_annotations_too_large", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_foreign_user()
    {
        await using var db = NewDb();
        SeedInProgressAttempt(db, userId: "owner-1");
        var svc = NewService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SaveAnnotationsAsync(
                "att-anno", "different-learner", "{\"x\":1}", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAnnotationsAsync_refuses_when_attempt_already_submitted()
    {
        await using var db = NewDb();
        var attempt = SeedInProgressAttempt(db);
        attempt.Status = ListeningAttemptStatus.Submitted;
        await db.SaveChangesAsync();
        var svc = NewService(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SaveAnnotationsAsync("att-anno", "learner-1", "{}", CancellationToken.None));
        Assert.Equal("listening_annotations_attempt_not_in_progress", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }
}
