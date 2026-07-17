using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Reading;

namespace OetWithDrHesham.Api.Tests.Reading;

/// <summary>
/// WORK-STREAM 8 — Reading tutor feedback <c>Scope</c> is constrained to the
/// <see cref="ReadingFeedbackScope"/> vocabulary {Test, Section, Question,
/// Skill} while staying string-backed in the database. These tests assert:
///
/// <list type="bullet">
/// <item><description>every valid scope (any casing) is accepted and persisted
/// as its normalized lowercase canonical name;</description></item>
/// <item><description>an unknown scope is rejected with the
/// <c>reading_feedback_scope_invalid</c> validation error (400);</description></item>
/// <item><description>create + update round-trip the stored name without
/// drift.</description></item>
/// </list>
///
/// Everything runs on the in-memory EF provider; feedback CRUD only requires
/// the attempt row to exist (no grading), so the attempt is seeded directly.
/// </summary>
public class ReadingTutorScopeTests
{
    private static (LearnerDbContext db, ReadingTutorService tutor) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new ReadingPolicyService(db, cache);
        var grader = new ReadingGradingService(db, policy, NullLogger<ReadingGradingService>.Instance);
        var tutor = new ReadingTutorService(db, grader, NullLogger<ReadingTutorService>.Instance);
        return (db, tutor);
    }

    private static async Task<string> SeedAttemptAsync(LearnerDbContext db, string userId = "learner-1")
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new ReadingAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PaperId = "p1",
            StartedAt = now,
            LastActivityAt = now,
            SubmittedAt = now,
            Status = ReadingAttemptStatus.Submitted,
            RawScore = 30,
            ScaledScore = 350,
            MaxRawScore = 42,
        };
        db.ReadingAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    [Theory]
    [InlineData("test", "test")]
    [InlineData("section", "section")]
    [InlineData("question", "question")]
    [InlineData("skill", "skill")]
    // Mixed / upper casing normalises to the lowercase canonical name.
    [InlineData("Test", "test")]
    [InlineData("SECTION", "section")]
    [InlineData("Question", "question")]
    [InlineData("  Skill  ", "skill")]
    public async Task CreateFeedback_accepts_and_normalises_every_valid_scope(string input, string expected)
    {
        var (db, tutor) = Build();
        var attemptId = await SeedAttemptAsync(db);

        var created = await tutor.CreateFeedbackAsync(
            attemptId, new ReadingFeedbackRequest(input, null, "Solid work"), "admin-1", default);

        Assert.NotNull(created);
        Assert.Equal(expected, created!.Scope);

        // Round-trip: the stored column holds the normalized name verbatim.
        var stored = await db.ReadingAttemptFeedbacks.AsNoTracking().SingleAsync(f => f.Id == created.Id);
        Assert.Equal(expected, stored.Scope);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("everything")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tests")]      // close-but-wrong: trailing plural
    [InlineData("qeustion")]   // transposed typo
    [InlineData("0")]          // numeric — must not bind to the underlying value
    public async Task CreateFeedback_rejects_unknown_scope_with_validation_code(string input)
    {
        var (db, tutor) = Build();
        var attemptId = await SeedAttemptAsync(db);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            tutor.CreateFeedbackAsync(
                attemptId, new ReadingFeedbackRequest(input, null, "Solid work"), "admin-1", default));

        Assert.Equal("reading_feedback_scope_invalid", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(await db.ReadingAttemptFeedbacks.AsNoTracking().ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateFeedback_accepts_valid_scope_and_persists_normalised_name()
    {
        var (db, tutor) = Build();
        var attemptId = await SeedAttemptAsync(db);

        var created = await tutor.CreateFeedbackAsync(
            attemptId, new ReadingFeedbackRequest("test", null, "Initial"), "admin-1", default);
        Assert.NotNull(created);

        var updated = await tutor.UpdateFeedbackAsync(
            attemptId, created!.Id, new ReadingFeedbackRequest("SECTION", "A", "Revise Part A"), "admin-1", default);

        Assert.NotNull(updated);
        Assert.Equal("section", updated!.Scope);
        Assert.Equal("A", updated.TargetRef);

        var stored = await db.ReadingAttemptFeedbacks.AsNoTracking().SingleAsync(f => f.Id == created.Id);
        Assert.Equal("section", stored.Scope);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateFeedback_rejects_unknown_scope_and_leaves_stored_value_unchanged()
    {
        var (db, tutor) = Build();
        var attemptId = await SeedAttemptAsync(db);

        var created = await tutor.CreateFeedbackAsync(
            attemptId, new ReadingFeedbackRequest("question", "q-1", "Initial"), "admin-1", default);
        Assert.NotNull(created);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            tutor.UpdateFeedbackAsync(
                attemptId, created!.Id, new ReadingFeedbackRequest("bogus", "q-1", "changed"), "admin-1", default));

        Assert.Equal("reading_feedback_scope_invalid", ex.ErrorCode);

        // The reject must not have mutated the persisted row.
        var stored = await db.ReadingAttemptFeedbacks.AsNoTracking().SingleAsync(f => f.Id == created!.Id);
        Assert.Equal("question", stored.Scope);
        Assert.Equal("Initial", stored.FeedbackText);
        await db.DisposeAsync();
    }

    [Fact]
    public void IsValidScope_boundary_helper_matches_the_enum_case_insensitively()
    {
        Assert.True(ReadingFeedbackScopeExtensions.IsValidScope("test"));
        Assert.True(ReadingFeedbackScopeExtensions.IsValidScope("Section"));
        Assert.True(ReadingFeedbackScopeExtensions.IsValidScope("QUESTION"));
        Assert.True(ReadingFeedbackScopeExtensions.IsValidScope("  skill "));

        Assert.False(ReadingFeedbackScopeExtensions.IsValidScope(null));
        Assert.False(ReadingFeedbackScopeExtensions.IsValidScope(""));
        Assert.False(ReadingFeedbackScopeExtensions.IsValidScope("paper"));
        // Reject numeric strings that Enum.TryParse would otherwise bind to the
        // underlying value (e.g. "0" → Test); only the four names are accepted.
        Assert.False(ReadingFeedbackScopeExtensions.IsValidScope("0"));
        Assert.False(ReadingFeedbackScopeExtensions.IsValidScope("99"));
    }
}
