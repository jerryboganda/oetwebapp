using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Tests for the grounded writing-draft pipeline.
///
/// Coverage:
///   1. DraftService refuses unusable AI replies without persisting template
///      content.
/// </summary>
public class WritingDraftServiceTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task DraftService_FailsClosed_WhenAiReplyUnusable()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var loader = new RulebookLoader();
        // Mock provider returns scoring JSON, not an authoring draft.
        var gateway = new AiGatewayService(loader, new IAiModelProvider[] { new MockAiProvider() });
        var service = new WritingDraftService(db, loader, gateway, NullLogger<WritingDraftService>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.GenerateAsync(
            new WritingDraftRequest(
                Profession: "medicine",
                LetterType: "routine_referral",
                RecipientSpecialty: "cardiology",
                Prompt: "65-year-old presents with chest pain on exertion; suspected stable angina.",
                Difficulty: "medium",
                TargetCaseNoteCount: 12),
            adminId: "admin-001",
            adminName: "Admin",
            authAccountId: "auth-001",
            default));

        Assert.Equal("WRITING_AI_DRAFT_UNUSABLE", ex.ErrorCode);
        Assert.Empty(await db.ContentItems.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }
}
