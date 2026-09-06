using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// F-047 — companion memory controls.
///
/// <para>
/// <c>save_user_note</c> and <c>bookmark_recall_term</c> write rows on the
/// learner's behalf, so the learner must be able to see and delete them. These
/// tests pin the two properties that make that safe: the delete predicate is
/// scoped by user id, and it only ever touches rows the <i>companion</i>
/// authored — a note the learner wrote themselves, or one written by a different
/// AI feature, is not the companion's to remove.
/// </para>
///
/// <para>
/// They exercise the same predicate shape as
/// <c>CompanionLearnerEndpoints</c>; the endpoint itself is covered for
/// registration and authorisation by <c>EndpointRegistrationTests</c>.
/// </para>
/// </summary>
public sealed class CompanionMemoryIsolationTests : IAsyncDisposable
{
    private static readonly string[] CompanionFeatureCodes =
    [
        AiFeatureCodes.AiAssistantLearner,
        AiFeatureCodes.CompanionChat,
        AiFeatureCodes.CompanionAction,
    ];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionMemoryIsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Listing_ReturnsOnlyCompanionAuthoredNotesForThisLearner()
    {
        await SeedNoteAsync("n1", "learner-1", AiFeatureCodes.AiAssistantLearner);
        await SeedNoteAsync("n2", "learner-1", AiFeatureCodes.CompanionAction);
        await SeedNoteAsync("n3", "learner-1", featureCode: null);            // hand-written
        await SeedNoteAsync("n4", "learner-1", "writing.coach.suggest");      // another feature
        await SeedNoteAsync("n5", "learner-2", AiFeatureCodes.AiAssistantLearner); // someone else

        await using var db = new LearnerDbContext(_options);
        var ids = await CompanionNotes(db, "learner-1").Select(n => n.Id).ToListAsync();

        Assert.Equal(["n1", "n2"], ids.Order().ToArray());
    }

    [Fact]
    public async Task Delete_DoesNotTouchAnotherLearnersNote()
    {
        await SeedNoteAsync("victim", "learner-2", AiFeatureCodes.AiAssistantLearner);

        await using var db = new LearnerDbContext(_options);
        var deleted = await CompanionNotes(db, "learner-1")
            .Where(n => n.Id == "victim")
            .ExecuteDeleteAsync();

        Assert.Equal(0, deleted);
        Assert.True(await db.UserNotes.AnyAsync(n => n.Id == "victim"));
    }

    [Fact]
    public async Task Delete_DoesNotTouchALearnersOwnHandwrittenNote()
    {
        await SeedNoteAsync("mine", "learner-1", featureCode: null);

        await using var db = new LearnerDbContext(_options);
        var deleted = await CompanionNotes(db, "learner-1")
            .Where(n => n.Id == "mine")
            .ExecuteDeleteAsync();

        Assert.Equal(0, deleted);
        Assert.True(await db.UserNotes.AnyAsync(n => n.Id == "mine"));
    }

    [Fact]
    public async Task Reset_RemovesEveryCompanionRowAndNothingElse()
    {
        await SeedNoteAsync("c1", "learner-1", AiFeatureCodes.AiAssistantLearner);
        await SeedNoteAsync("c2", "learner-1", AiFeatureCodes.CompanionChat);
        await SeedNoteAsync("own", "learner-1", featureCode: null);
        await SeedNoteAsync("other", "learner-2", AiFeatureCodes.AiAssistantLearner);
        await SeedBookmarkAsync("b1", "learner-1", AiFeatureCodes.AiAssistantLearner);
        await SeedBookmarkAsync("b2", "learner-2", AiFeatureCodes.AiAssistantLearner);

        await using var db = new LearnerDbContext(_options);
        var notes = await CompanionNotes(db, "learner-1").ExecuteDeleteAsync();
        var bookmarks = await db.RecallBookmarks
            .Where(b => b.UserId == "learner-1"
                        && b.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode))
            .ExecuteDeleteAsync();

        Assert.Equal(2, notes);
        Assert.Equal(1, bookmarks);

        var survivors = await db.UserNotes.Select(n => n.Id).ToListAsync();
        Assert.Equal(["other", "own"], survivors.Order().ToArray());
        Assert.True(await db.RecallBookmarks.AnyAsync(b => b.Id == "b2"));
    }

    // ---------------------------------------------------------------- helpers

    private static IQueryable<UserNote> CompanionNotes(LearnerDbContext db, string userId) =>
        db.UserNotes.Where(n => n.UserId == userId
                                && n.CreatedByFeatureCode != null
                                && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode));

    private async Task SeedNoteAsync(string id, string userId, string? featureCode)
    {
        await using var db = new LearnerDbContext(_options);
        db.UserNotes.Add(new UserNote
        {
            Id = id,
            UserId = userId,
            Title = $"note {id}",
            BodyMarkdown = "body",
            Source = featureCode is null ? "user" : "ai_tool",
            CreatedByFeatureCode = featureCode,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedBookmarkAsync(string id, string userId, string featureCode)
    {
        await using var db = new LearnerDbContext(_options);
        db.RecallBookmarks.Add(new RecallBookmark
        {
            Id = id,
            UserId = userId,
            VocabularyTermId = $"term-{id}",
            Source = "ai_tool",
            CreatedByFeatureCode = featureCode,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
