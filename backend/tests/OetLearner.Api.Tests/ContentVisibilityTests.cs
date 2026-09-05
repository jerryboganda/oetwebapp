using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

/// <summary>
/// Hide/show vectors for the ContentVisibility seam (V1, #196).
/// Fail-closed: anything not Published+Visible is indistinguishable
/// from missing on every learner surface.
/// </summary>
public sealed class ContentVisibilityTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static ContentPaper Paper(string id, ContentStatus status, bool visible) => new()
    {
        Id = id,
        SubtestCode = "reading",
        Title = id,
        Slug = id,
        AppliesToAllProfessions = true,
        Status = status,
        CandidateVisible = visible,
    };

    private static async Task<LearnerDbContext> SeededAsync()
    {
        var db = NewContext();
        db.ContentPapers.AddRange(
            Paper("visible", ContentStatus.Published, true),
            Paper("hidden", ContentStatus.Published, false),
            Paper("draft", ContentStatus.Draft, true),
            Paper("archived", ContentStatus.Archived, true));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task WhereCandidateVisible_ReturnsOnlyPublishedVisible()
    {
        await using var db = await SeededAsync();

        var ids = await db.ContentPapers
            .WhereCandidateVisible()
            .Select(p => p.Id)
            .ToListAsync();

        Assert.Equal(["visible"], ids);
    }

    [Fact]
    public async Task WhereAttemptable_WithoutArchived_ExcludesArchived()
    {
        await using var db = await SeededAsync();

        var ids = await db.ContentPapers
            .WhereAttemptable(allowArchived: false)
            .Select(p => p.Id)
            .ToListAsync();

        Assert.Equal(["visible"], ids);
    }

    [Fact]
    public async Task WhereAttemptable_WithArchived_AdmitsArchivedVisible()
    {
        await using var db = await SeededAsync();

        var ids = await db.ContentPapers
            .WhereAttemptable(allowArchived: true)
            .Select(p => p.Id)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(["archived", "visible"], ids);
    }

    [Theory]
    [InlineData(ContentStatus.Published, true, true)]
    [InlineData(ContentStatus.Published, false, false)]
    [InlineData(ContentStatus.Draft, true, false)]
    [InlineData(ContentStatus.Archived, true, false)]
    public void IsCandidateVisible_Matrix(ContentStatus status, bool visible, bool expected)
    {
        Assert.Equal(expected, ContentVisibility.IsCandidateVisible(Paper("x", status, visible)));
        Assert.False(ContentVisibility.IsCandidateVisible(null));
    }
}
