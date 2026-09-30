using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// The Listening integrity-event write path on REAL PostgreSQL. The SQLite twin in
/// <see cref="ListeningAttemptEventLoggingTests"/> proves the behaviour; this proves
/// Npgsql's translation of the same targeted UPDATEs — the jsonb timeline parameter,
/// the COALESCE keep-first-value rules and the timestamptz parameters — because a
/// provider-specific failure here would fail every Listening audio event in
/// production. Skips (never fails) when OET_TEST_POSTGRES_CONNECTION is unset.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class ListeningIntegrityEventPostgreSqlTests
{
    private const string UserId = "learner-pg";
    private const string AttemptId = "lat-pg";
    private const string Hold = "audio_playback_error";

    [PostgreSqlFact]
    public async Task TargetedWrites_SurviveAConcurrentRowVersionBump_AndKeepTheTimelineJsonb()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = NewContext(database);
        await CreateAttemptTableAsync(database, db);
        await SeedAttemptAsync(db);

        // This request's context already tracks the attempt at RowVersion 1 ...
        var seen = await db.ListeningAttempts.SingleAsync();
        var activityBefore = seen.LastActivityAt;
        // ... when an autosave from another request commits RowVersion 2.
        await using (var autosave = NewContext(database))
        {
            var row = await autosave.ListeningAttempts.SingleAsync();
            row.RowVersion++;
            await autosave.SaveChangesAsync();
        }

        await ListeningLearnerService.TouchAttemptForIntegrityEventAsync(
            db, UserId, AttemptId, DateTimeOffset.UtcNow,
            cueTimelineJson: "[{\"cue\":\"audio_started\",\"atMs\":1000}]",
            raiseHold: false, releaseHold: false, Hold, default);

        await using var check = NewContext(database);
        var after = await check.ListeningAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(2, after.RowVersion); // the autosave's bump is left alone
        Assert.True(after.LastActivityAt > activityBefore);
        Assert.Contains("audio_started", after.AudioCueTimelineJson);
        await using var typeOf = database.Command("""SELECT pg_typeof("AudioCueTimelineJson")::text FROM "ListeningAttempts";""");
        Assert.Equal("jsonb", await typeOf.ExecuteScalarAsync());
    }

    [PostgreSqlFact]
    public async Task Hold_IsRaisedOnce_KeepsItsFlagTime_AndIsReleasedOnlyWhileItIsOurs()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = NewContext(database);
        await CreateAttemptTableAsync(database, db);
        await SeedAttemptAsync(db);

        await ListeningLearnerService.TouchAttemptForIntegrityEventAsync(
            db, UserId, AttemptId, DateTimeOffset.UtcNow, null, raiseHold: true, releaseHold: false, Hold, default);
        var held = await ReadAsync(database);
        Assert.True(held.RequiresAdminReview);
        Assert.Equal(Hold, held.AdminReviewReason);
        Assert.NotNull(held.AdminReviewFlaggedAt);

        // A second error keeps the original flag time instead of moving it.
        await ListeningLearnerService.TouchAttemptForIntegrityEventAsync(
            db, UserId, AttemptId, DateTimeOffset.UtcNow.AddMinutes(1), null, raiseHold: true, releaseHold: false, Hold, default);
        Assert.Equal(held.AdminReviewFlaggedAt, (await ReadAsync(database)).AdminReviewFlaggedAt);

        // A playback start releases it ...
        await ListeningLearnerService.TouchAttemptForIntegrityEventAsync(
            db, UserId, AttemptId, DateTimeOffset.UtcNow, null, raiseHold: false, releaseHold: true, Hold, default);
        var released = await ReadAsync(database);
        Assert.False(released.RequiresAdminReview);
        Assert.Null(released.AdminReviewReason);
        Assert.Null(released.AdminReviewFlaggedAt);

        // ... but never a hold that was set for some other reason.
        await database.ExecuteAsync("""
            UPDATE "ListeningAttempts"
            SET "RequiresAdminReview" = TRUE, "AdminReviewReason" = 'scored_media_failure', "AdminReviewFlaggedAt" = NOW();
            """);
        await ListeningLearnerService.TouchAttemptForIntegrityEventAsync(
            db, UserId, AttemptId, DateTimeOffset.UtcNow, null, raiseHold: true, releaseHold: true, Hold, default);
        var other = await ReadAsync(database);
        Assert.True(other.RequiresAdminReview);
        Assert.Equal("scored_media_failure", other.AdminReviewReason);
    }

    [PostgreSqlFact]
    public async Task RecordIntegrityEvent_SurvivesAConcurrentAutosave_EndToEnd()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = NewContext(database);
        await CreateTablesAsync(database, db, typeof(LearnerUser), typeof(ListeningAttempt), typeof(AuditEvent));
        await SeedAttemptAsync(db, withUser: true);

        // The request's context already tracks the attempt at RowVersion 1 when an
        // autosave commits RowVersion 2 — the old tracked save threw a retryable 409 here.
        var seen = await db.ListeningAttempts.SingleAsync();
        await using (var autosave = NewContext(database))
        {
            var row = await autosave.ListeningAttempts.SingleAsync();
            row.RowVersion++;
            await autosave.SaveChangesAsync();
        }

        var service = new ListeningLearnerService(db, new AllowAllContentEntitlementService());
        await service.RecordIntegrityEventAsync(
            UserId, AttemptId,
            new ListeningIntegrityEventRequest("audio_started", "{\"cuePointMs\":1000}", DateTimeOffset.UtcNow),
            default);
        await service.RecordIntegrityEventAsync(
            UserId, AttemptId,
            new ListeningIntegrityEventRequest("audio_progress", "{\"cuePointMs\":2500}", DateTimeOffset.UtcNow),
            default);

        await using var check = NewContext(database);
        var after = await check.ListeningAttempts.AsNoTracking().SingleAsync();
        Assert.Equal(2, after.RowVersion);
        Assert.True(after.LastActivityAt > seen.LastActivityAt);
        Assert.Contains("audio_started", after.AudioCueTimelineJson);
        Assert.Contains("audio_progress", after.AudioCueTimelineJson);
        Assert.Equal(2, await check.AuditEvents.CountAsync(e => e.Action == "ListeningIntegrityEvent"));
    }

    private static LearnerDbContext NewContext(PostgreSqlTestDatabase database)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(database.SchemaConnectionString, npgsql => npgsql.UseVector())
            .Options);

    private static async Task<ListeningAttempt> ReadAsync(PostgreSqlTestDatabase database)
    {
        await using var db = NewContext(database);
        return await db.ListeningAttempts.AsNoTracking().SingleAsync();
    }

    private static Task CreateAttemptTableAsync(PostgreSqlTestDatabase database, LearnerDbContext db)
        => CreateTablesAsync(database, db, typeof(ListeningAttempt));

    /// <summary>Creates just these tables, from the model's own column names and store
    /// types (the full model needs the pgvector search path).</summary>
    private static async Task CreateTablesAsync(PostgreSqlTestDatabase database, LearnerDbContext db, params Type[] entityTypes)
    {
        foreach (var entityType in entityTypes)
        {
            var entity = db.Model.FindEntityType(entityType)!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var columns = entity.GetProperties().Select(p =>
                $"\"{p.GetColumnName(table)}\" {p.GetColumnType()}{(p.IsNullable ? string.Empty : " NOT NULL")}");
            await database.ExecuteAsync(
                $"CREATE TABLE \"{table.Name}\" ({string.Join(", ", columns)}, PRIMARY KEY (\"Id\"));");
        }
    }

    private static async Task SeedAttemptAsync(LearnerDbContext db, bool withUser = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (withUser)
        {
            db.Users.Add(new LearnerUser
            {
                Id = UserId,
                DisplayName = "Learner pg",
                Email = "learner-pg@example.test",
                AccountStatus = "active",
                CreatedAt = now,
                LastActiveAt = now,
            });
        }
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = AttemptId,
            UserId = UserId,
            PaperId = "paper-pg",
            StartedAt = now.AddMinutes(-10),
            LastActivityAt = now.AddMinutes(-5),
            Mode = ListeningAttemptMode.Home,
            RowVersion = 1,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
