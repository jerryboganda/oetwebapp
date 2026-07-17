using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Entitlements;
using OetWithDrHesham.Api.Services.Reading;

namespace OetWithDrHesham.Api.Tests.Reading;

/// <summary>
/// R08 — exercises <c>ReadingAttemptService.SaveAnnotationsAsync</c> /
/// <c>GetAnnotationsAsync</c> (the persistent MCQ rule-out / highlight payload).
/// Mirrors <c>ListeningAnnotationsServiceTests</c>: verifies round-trip, the
/// 64 KB cap, the JSON shape guard, blank-clears, owner-only access, and the
/// in-progress requirement. Runs on the in-memory EF provider — the service's
/// relational <c>ExecuteUpdateAsync</c> path falls back to a tracked save there.
/// </summary>
public class ReadingAnnotationsServiceTests
{
    private static (LearnerDbContext db, ReadingAttemptService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new ReadingPolicyService(db, cache);
        var grader = new ReadingGradingService(db, policy, NullLogger<ReadingGradingService>.Instance);
        var entitlements = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var svc = new ReadingAttemptService(db, policy, grader, entitlements, NullLogger<ReadingAttemptService>.Instance);
        return (db, svc);
    }

    private static ReadingAttempt SeedInProgressAttempt(LearnerDbContext db, string userId = "learner-1")
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new ReadingAttempt
        {
            Id = "att-anno",
            UserId = userId,
            PaperId = "paper-anno",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 0,
            Mode = ReadingAttemptMode.Exam,
            Status = ReadingAttemptStatus.InProgress,
        };
        db.ReadingAttempts.Add(attempt);
        db.SaveChanges();
        return attempt;
    }

    [Fact]
    public async Task SaveAnnotationsAsync_persists_payload_and_reads_back()
    {
        var (db, svc) = Build();
        await using var _ = db;
        SeedInProgressAttempt(db);
        var payload = "{\"byQuestion\":{\"q-1\":{\"struckOptions\":[\"A\",\"C\"],\"stemHighlighted\":true}}}";

        await svc.SaveAnnotationsAsync("learner-1", "att-anno", payload, CancellationToken.None);
        var got = await svc.GetAnnotationsAsync("learner-1", "att-anno", CancellationToken.None);

        Assert.Equal(payload, got);
        var reloaded = await db.ReadingAttempts.AsNoTracking().SingleAsync(a => a.Id == "att-anno");
        Assert.Equal(payload, reloaded.AnnotationsJson);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_treats_blank_payload_as_clear()
    {
        var (db, svc) = Build();
        await using var _ = db;
        var attempt = SeedInProgressAttempt(db);
        attempt.AnnotationsJson = "{\"byQuestion\":{\"q-1\":{\"struckOptions\":[\"A\"]}}}";
        await db.SaveChangesAsync();

        await svc.SaveAnnotationsAsync("learner-1", "att-anno", "  ", CancellationToken.None);

        var reloaded = await db.ReadingAttempts.AsNoTracking().SingleAsync(a => a.Id == "att-anno");
        Assert.Null(reloaded.AnnotationsJson);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_invalid_json()
    {
        var (db, svc) = Build();
        await using var _ = db;
        SeedInProgressAttempt(db);

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            svc.SaveAnnotationsAsync("learner-1", "att-anno", "not json", CancellationToken.None));
        Assert.Equal("reading_annotations_invalid_json", ex.Code);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_oversized_payload()
    {
        var (db, svc) = Build();
        await using var _ = db;
        SeedInProgressAttempt(db);

        // Build a JSON document just over the 64 KB byte cap.
        var pad = new string('a', ReadingAttemptService.MaxAnnotationsBytes);
        var oversized = $"{{\"pad\":\"{pad}\"}}";

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            svc.SaveAnnotationsAsync("learner-1", "att-anno", oversized, CancellationToken.None));
        Assert.Equal("reading_annotations_too_large", ex.Code);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_rejects_foreign_user()
    {
        var (db, svc) = Build();
        await using var _ = db;
        SeedInProgressAttempt(db, userId: "owner-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveAnnotationsAsync("different-learner", "att-anno", "{\"x\":1}", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAnnotationsAsync_refuses_when_attempt_already_submitted()
    {
        var (db, svc) = Build();
        await using var _ = db;
        var attempt = SeedInProgressAttempt(db);
        attempt.Status = ReadingAttemptStatus.Submitted;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            svc.SaveAnnotationsAsync("learner-1", "att-anno", "{}", CancellationToken.None));
        Assert.Equal("attempt_not_in_progress", ex.Code);
    }

    [Fact]
    public async Task GetAnnotationsAsync_returns_null_when_unset()
    {
        var (db, svc) = Build();
        await using var _ = db;
        SeedInProgressAttempt(db);

        var got = await svc.GetAnnotationsAsync("learner-1", "att-anno", CancellationToken.None);

        Assert.Null(got);
    }
}
