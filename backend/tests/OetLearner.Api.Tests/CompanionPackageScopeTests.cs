using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Manifest 1.B — "content from one package must not be silently mixed into
/// another", and the profession boundary that sits beside it.
///
/// <para>
/// These are two axes, and the tests below exist to keep them two. Collapsing
/// them looks tidy and breaks in one of two directions: scope everything by
/// package and a Crash learner loses the method they paid for; scope everything
/// by profession and Full Course material leaks into Crash accounts. Golden case
/// GC-007 pins the profession axis; the package axis is pinned here.
/// </para>
/// </summary>
public sealed class CompanionPackageScopeTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionPackageScopeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Unscoped_material_reaches_everyone()
    {
        // The default, and the reason the column is nullable: the rulebooks are
        // the method, and the method is not package-isolated.
        await SeedAsync("shared handout", packageScope: null);

        Assert.NotEmpty((await RetrieveAsync("handout", [])).Evidence);
        Assert.NotEmpty((await RetrieveAsync("handout", [VideoVisibilityScopes.Crash])).Evidence);
    }

    [Fact]
    public async Task Explicitly_shared_material_reaches_everyone()
    {
        await SeedAsync("shared handout", VideoVisibilityScopes.Shared);

        Assert.NotEmpty((await RetrieveAsync("handout", [])).Evidence);
    }

    [Fact]
    public async Task Full_course_material_does_not_reach_a_crash_learner()
    {
        await SeedAsync("full course handout", VideoVisibilityScopes.FullMedicine);

        var result = await RetrieveAsync("handout", [VideoVisibilityScopes.Crash]);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task Crash_material_does_not_reach_a_full_course_learner()
    {
        // The isolation runs both ways: a Crash-specific condensed sheet is not
        // part of the Full Course product either.
        await SeedAsync("crash handout", VideoVisibilityScopes.Crash);

        var result = await RetrieveAsync("handout", [VideoVisibilityScopes.FullMedicine]);

        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task A_learner_holding_both_packages_reaches_both()
    {
        // Package scopes are a set, not a single value — someone can own a Full
        // Course and a Crash Course, and both are theirs.
        await SeedAsync("full course handout", VideoVisibilityScopes.FullMedicine);
        await SeedAsync("crash handout", VideoVisibilityScopes.Crash, sourceKey: "material:2");

        var result = await RetrieveAsync(
            "handout", [VideoVisibilityScopes.FullMedicine, VideoVisibilityScopes.Crash]);

        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public async Task A_learner_with_no_package_reaches_no_isolated_material()
    {
        await SeedAsync("full course handout", VideoVisibilityScopes.FullMedicine);

        Assert.Empty((await RetrieveAsync("handout", [])).Evidence);
    }

    [Fact]
    public async Task One_profession_full_course_does_not_open_another()
    {
        // FULL_MEDICINE and FULL_NURSING are separate products, not two labels
        // for "the full course".
        await SeedAsync("nursing handout", VideoVisibilityScopes.FullNursing);

        Assert.Empty((await RetrieveAsync("handout", [VideoVisibilityScopes.FullMedicine])).Evidence);
    }

    [Fact]
    public async Task Package_scope_and_profession_both_apply()
    {
        // Holding the right package does not open another profession's material,
        // which is what keeps the two axes from collapsing into each other.
        await SeedAsync("pharmacy handout", VideoVisibilityScopes.FullMedicine, professionId: "pharmacy");

        var result = await RetrieveAsync(
            "handout", [VideoVisibilityScopes.FullMedicine], profession: "medicine");

        Assert.Empty(result.Evidence);
    }

    private async Task SeedAsync(
        string text,
        string? packageScope,
        string sourceKey = "material:1",
        string? professionId = null)
    {
        await using var db = new LearnerDbContext(_options);
        var now = DateTimeOffset.UtcNow;

        var source = new CompanionSource
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey,
            Version = "v1",
            SourceType = "material_pdf",
            Title = text,
            AuthorityClass = CompanionAuthorityClass.CourseMaterial,
            State = CompanionSourceState.Approved,
            ExamTypeCode = "OET",
            ProfessionId = professionId,
            PackageScope = packageScope,
            IsProprietary = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CompanionSources.Add(source);

        db.CompanionChunks.Add(new CompanionChunk
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Ordinal = 0,
            Heading = "Page 1",
            Text = $"This is the {text} covering letter structure.",
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
    }

    private async Task<CompanionRetrievalResult> RetrieveAsync(
        string query,
        string[] packageScopes,
        string profession = "medicine")
    {
        await using var db = new LearnerDbContext(_options);
        var retriever = new CompanionRetriever(
            db, new UnusedEmbeddings(),
            new CompanionExtractionBudget(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<CompanionRetriever>.Instance);

        var context = new CompanionTurnContext
        {
            UserId = "learner-1",
            ProfessionId = profession,
            ExamTypeCode = "OET",
            RetrievalEnabled = true,
            PackageScopes = new HashSet<string>(packageScopes, StringComparer.OrdinalIgnoreCase),
        };

        return await retriever.RetrieveAsync(query, context, maxResults: 8, CancellationToken.None);
    }

    private sealed class UnusedEmbeddings : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");

        public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new InvalidOperationException("vector path must not run on SQLite");
    }
}
