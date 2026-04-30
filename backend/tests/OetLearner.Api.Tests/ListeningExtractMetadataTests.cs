using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 5 tail tests for <see cref="ListeningAuthoringService"/> — paper-level
/// extract metadata (accent + speakers + audio window).
/// </summary>
public class ListeningExtractMetadataTests
{
    private static (LearnerDbContext db, ListeningAuthoringService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningAuthoringService(db));
    }

    private static async Task<string> AddPaperAsync(LearnerDbContext db, string? json = null)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Extract Test",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = json ?? "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper.Id;
    }

    [Fact]
    public async Task ReplaceExtracts_RoundTrips_AccentAndSpeakers()
    {
        var (db, svc) = Build();
        var paperId = await AddPaperAsync(db);

        var input = new[]
        {
            new ListeningAuthoredExtract(
                PartCode: "A1",
                DisplayOrder: 0,
                Kind: "consultation",
                Title: "Consultation 1",
                AccentCode: "en-GB",
                Speakers: new[]
                {
                    new ListeningAuthoredSpeaker("doc", "GP", "f", "en-GB"),
                    new ListeningAuthoredSpeaker("pat", "patient", "m", null),
                },
                AudioStartMs: 0,
                AudioEndMs: 300_000),
            new ListeningAuthoredExtract(
                PartCode: "B",
                DisplayOrder: 1,
                Kind: "workplace",
                Title: "Workplace block",
                AccentCode: "en-AU",
                Speakers: Array.Empty<ListeningAuthoredSpeaker>(),
                AudioStartMs: null,
                AudioEndMs: null),
        };

        var saved = await svc.ReplaceExtractsAsync(paperId, input, "admin-1", default);

        Assert.Equal(2, saved.Count);
        Assert.Equal("A1", saved[0].PartCode);
        Assert.Equal("en-GB", saved[0].AccentCode);
        Assert.Equal(2, saved[0].Speakers.Count);
        Assert.Equal("GP", saved[0].Speakers[0].Role);
        Assert.Equal("f", saved[0].Speakers[0].Gender);

        // Re-read.
        var fresh = await svc.GetExtractsAsync(paperId, default);
        Assert.Equal(2, fresh.Count);
        Assert.Equal("A1", fresh[0].PartCode);
        Assert.Equal("Consultation 1", fresh[0].Title);

        // listeningQuestions sibling key is preserved when set later.
        var paper = await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paperId);
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)!;
        Assert.True(root.ContainsKey("listeningExtracts"));
    }

    [Fact]
    public async Task ReplaceExtracts_RejectsInvalidPartCode_FallsBackToA1()
    {
        var (db, svc) = Build();
        var paperId = await AddPaperAsync(db);

        var input = new[]
        {
            new ListeningAuthoredExtract(
                PartCode: "Z9",
                DisplayOrder: 0,
                Kind: "garbage",
                Title: "",
                AccentCode: "  ",
                Speakers: Array.Empty<ListeningAuthoredSpeaker>(),
                AudioStartMs: -1,
                AudioEndMs: 5),
        };

        var saved = await svc.ReplaceExtractsAsync(paperId, input, "admin-1", default);

        Assert.Single(saved);
        Assert.Equal("A1", saved[0].PartCode);
        // Default kind for A1 = consultation.
        Assert.Equal("consultation", saved[0].Kind);
        // Empty title gets defaulted.
        Assert.Equal("A1 extract", saved[0].Title);
        // Accent normalised away because whitespace-only.
        Assert.Null(saved[0].AccentCode);
        // Negative ms snapped to null.
        Assert.Null(saved[0].AudioStartMs);
    }

    [Fact]
    public async Task ReplaceExtracts_PreservesListeningQuestionsKey()
    {
        var (db, svc) = Build();
        var seedJson = JsonSerializer.Serialize(new
        {
            listeningQuestions = new[] { new { id = "q1", number = 1, partCode = "A1", text = "x" } },
        });
        var paperId = await AddPaperAsync(db, seedJson);

        await svc.ReplaceExtractsAsync(paperId,
            new[]
            {
                new ListeningAuthoredExtract("A1", 0, "consultation", "Consultation 1", "en-GB",
                    Array.Empty<ListeningAuthoredSpeaker>(), null, null),
            },
            "admin-1", default);

        var paper = await db.ContentPapers.AsNoTracking().SingleAsync(p => p.Id == paperId);
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)!;
        Assert.True(root.ContainsKey("listeningQuestions"));
        Assert.True(root.ContainsKey("listeningExtracts"));
    }

    [Fact]
    public async Task ReplaceExtracts_RejectsDuplicatePartMetadata()
    {
        var (db, svc) = Build();
        var paperId = await AddPaperAsync(db);

        var input = new[]
        {
            new ListeningAuthoredExtract("B", 0, "workplace", "Workplace 1", null,
                Array.Empty<ListeningAuthoredSpeaker>(), null, null),
            new ListeningAuthoredExtract("B", 1, "workplace", "Workplace 2", null,
                Array.Empty<ListeningAuthoredSpeaker>(), null, null),
        };

        var error = await Assert.ThrowsAsync<ApiException>(() => svc.ReplaceExtractsAsync(paperId, input, "admin-1", default));

        Assert.Equal("listening_extract_duplicate_part", error.ErrorCode);
    }
}
