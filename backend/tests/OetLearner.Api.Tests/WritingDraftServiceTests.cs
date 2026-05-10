using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Tests for the grounded writing-draft pipeline.
///
/// Coverage:
///   1. DraftService creates a ContentItem with Status="draft" and metadata
///      populated (case notes, model letter, applied rule IDs) even when
///      the AI reply is unusable (deterministic fallback path).
///   2. AuditEvent is recorded for every draft.
/// </summary>
public class WritingDraftServiceTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task DraftService_CreatesDraftContentItem_WithMetadataAndAudit()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var loader = new RulebookLoader();
        // Mock provider returns a non-writing JSON; service falls back to the
        // deterministic starter template, but still persists a draft row.
        var gateway = new AiGatewayService(loader, new IAiModelProvider[] { new MockAiProvider() });
        var service = new WritingDraftService(db, loader, gateway, NullLogger<WritingDraftService>.Instance);

        var result = await service.GenerateAsync(
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
            default);

        Assert.False(string.IsNullOrWhiteSpace(result.ContentId));
        Assert.NotEmpty(result.AppliedRuleIds);
        Assert.False(string.IsNullOrWhiteSpace(result.RulebookVersion));

        // Persisted as draft with metadata
        var saved = await db.ContentItems.FirstAsync(x => x.Id == result.ContentId);
        Assert.Equal(ContentStatus.Draft, saved.Status);
        Assert.Equal("writing", saved.SubtestCode);
        Assert.Equal("routine_referral", saved.ScenarioType);
        Assert.False(string.IsNullOrWhiteSpace(saved.CaseNotes));
        Assert.False(string.IsNullOrWhiteSpace(saved.DetailJson));
        Assert.Contains("modelLetterMarkdown", saved.DetailJson);
        Assert.Contains("appliedRuleIds", saved.DetailJson);

        // Audit event recorded
        var audits = await db.AuditEvents.Where(a => a.ResourceId == result.ContentId).ToListAsync();
        Assert.Contains(audits, a => a.Action == "writing.ai_draft_generated");
    }
}
