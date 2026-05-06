using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class GroundedListeningExtractionAiTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsStub_WhenPaperHasNoUsableExtractedSourceText()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = "paper-1",
            SubtestCode = "listening",
            Title = "Source-less listening paper",
            Slug = "source-less-listening-paper",
            Status = ContentStatus.Draft,
            ExtractedTextJson = JsonSerializer.Serialize(new
            {
                listeningQuestions = Array.Empty<object>(),
                listeningExtracts = Array.Empty<object>(),
            }),
            CreatedAt = now,
            UpdatedAt = now,
        };
        paper.Assets.Add(new ContentPaperAsset
        {
            Id = "asset-question-paper",
            PaperId = paper.Id,
            Role = PaperAssetRole.QuestionPaper,
            MediaAssetId = "media-question-paper",
            IsPrimary = true,
            CreatedAt = now,
        });
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();

        var ai = new GroundedListeningExtractionAi(db, new ThrowingAiGateway(), NullLogger<GroundedListeningExtractionAi>.Instance);

        var result = await ai.ExtractAsync(paper.Id, mediaAssetId: null, default);

        Assert.True(result.IsStub);
        Assert.Contains("No extracted text", result.StubReason);
    }

    private sealed class ThrowingAiGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Gateway should not be called without usable source text.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) =>
            throw new InvalidOperationException("Prompt should not be built without usable source text.");
    }
}
