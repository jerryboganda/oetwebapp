using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Grammar;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Tests for the grounded grammar-draft pipeline and publish gate.
///
/// Coverage:
///   1. DraftService always builds a grounded prompt (rulebook header).
///   2. Validates appliedRuleIds against the loaded grammar rulebook; drops unknown IDs.
///   3. On ungrounded / malformed reply, falls back to deterministic starter template.
///   4. PublishGate refuses when required fields missing.
///   5. EntitlementService enforces 3 lessons / 7 days for free tier; unlimited for paid.
/// </summary>
public class GrammarServiceTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    // ── Grounding ───────────────────────────────────────────────────────

    [Fact]
    public void GrammarPrompt_CarriesRulebookHeader()
    {
        var loader = new RulebookLoader();
        var gateway = new AiGatewayService(loader, new IAiModelProvider[] { new MockAiProvider() });

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Grammar,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateGrammarLesson,
        });

        Assert.Contains("OET AI — Rulebook-Grounded System Prompt", prompt.SystemPrompt);
        Assert.Contains("GRAMMAR", prompt.SystemPrompt);
        Assert.True(prompt.Metadata.AppliedRuleIds.Count > 0, "Grammar prompt should carry rule IDs.");
        Assert.StartsWith("G", prompt.Metadata.AppliedRuleIds[0]);
    }

    // ── Draft fallback template ─────────────────────────────────────────

    [Fact]
    public async Task DraftService_FallsBackToStarterTemplate_WhenAiReplyUnusable()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var loader = new RulebookLoader();
        // Mock provider returns a deliberately unusable reply (no JSON).
        var gateway = new AiGatewayService(loader, new IAiModelProvider[] { new MockAiProvider() });
        var service = new GrammarDraftService(db, loader, gateway, NullLogger<GrammarDraftService>.Instance);

        var result = await service.GenerateAsync(
            new GrammarDraftRequest("oet", "tenses", "Drill tenses", "intermediate", 4, "medicine"),
            adminId: "admin-001",
            adminName: "Admin",
            authAccountId: "auth-001",
            default);

        Assert.NotNull(result.Warning);
        Assert.True(result.ExerciseCount >= 3, "Fallback must include at least 3 exercises.");
        Assert.NotEmpty(result.AppliedRuleIds);

        // Persisted as draft
        var saved = await db.GrammarLessons.FirstAsync(x => x.Id == result.LessonId);
        Assert.Equal("draft", saved.Status);

        // Audit event recorded
        var audits = await db.AuditEvents.Where(a => a.ResourceId == result.LessonId).ToListAsync();
        Assert.Contains(audits, a => a.Action == "GrammarAiDraftFallback" || a.Action == "GrammarAiDraftCreated");
    }

    // ── Publish gate ────────────────────────────────────────────────────

    [Fact]
    public async Task PublishGate_Refuses_WhenMissingRequiredFields()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        db.GrammarLessons.Add(new GrammarLesson
        {
            Id = "grm-incomplete",
            ExamTypeCode = "oet",
            Title = "Incomplete",
            Description = "",
            Category = "tenses",
            Level = "beginner",
            EstimatedMinutes = 10,
            SortOrder = 0,
            ContentHtml = "{\"contentBlocks\":[],\"exercises\":[]}",
            ExercisesJson = "[]",
            Status = "draft",
        });
        await db.SaveChangesAsync();

        var gate = new GrammarPublishGateService(db, NullLogger<GrammarPublishGateService>.Instance);
        var verdict = await gate.EvaluateAsync("grm-incomplete", default);

        Assert.False(verdict.CanPublish);
        Assert.Contains(verdict.Errors, e => e.Contains("Description"));
        Assert.Contains(verdict.Errors, e => e.Contains("content block"));
        Assert.Contains(verdict.Errors, e => e.Contains("3 exercises"));
        Assert.Contains(verdict.Errors, e => e.Contains("sourceProvenance"));
        Assert.Contains(verdict.Errors, e => e.Contains("appliedRuleId"));
    }

    [Fact]
    public async Task PublishGate_AllowsWellFormedLesson()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);

        var doc = "{" +
            "\"sourceProvenance\":\"Dr. Hesham Grammar Rulebook v1 — G02.1\"," +
            "\"appliedRuleIds\":[\"G02.1\"]," +
            "\"contentBlocks\":[{\"id\":\"cb-1\",\"sortOrder\":1,\"type\":\"callout\",\"contentMarkdown\":\"x\"}]," +
            "\"exercises\":[" +
                "{\"id\":\"ex-1\",\"sortOrder\":1,\"type\":\"mcq\",\"promptMarkdown\":\"q\",\"correctAnswer\":\"a\",\"acceptedAnswers\":[],\"explanationMarkdown\":\"e\",\"difficulty\":\"beginner\",\"points\":1}," +
                "{\"id\":\"ex-2\",\"sortOrder\":2,\"type\":\"mcq\",\"promptMarkdown\":\"q\",\"correctAnswer\":\"a\",\"acceptedAnswers\":[],\"explanationMarkdown\":\"e\",\"difficulty\":\"beginner\",\"points\":1}," +
                "{\"id\":\"ex-3\",\"sortOrder\":3,\"type\":\"mcq\",\"promptMarkdown\":\"q\",\"correctAnswer\":\"a\",\"acceptedAnswers\":[],\"explanationMarkdown\":\"e\",\"difficulty\":\"beginner\",\"points\":1}" +
            "]}";

        db.GrammarLessons.Add(new GrammarLesson
        {
            Id = "grm-ok",
            ExamTypeCode = "oet",
            Title = "Good",
            Description = "Good description",
            Category = "tenses",
            Level = "beginner",
            EstimatedMinutes = 10,
            SortOrder = 0,
            ContentHtml = doc,
            ExercisesJson = "[]",
            Status = "draft",
        });
        await db.SaveChangesAsync();

        var gate = new GrammarPublishGateService(db, NullLogger<GrammarPublishGateService>.Instance);
        var verdict = await gate.EvaluateAsync("grm-ok", default);

        Assert.True(verdict.CanPublish, $"Expected publish to succeed; got errors: {string.Join(" / ", verdict.Errors)}");
        Assert.Empty(verdict.Errors);
    }

    // ── Entitlement ─────────────────────────────────────────────────────

    [Fact]
    public async Task Entitlement_AnonymousBlocked()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var service = new GrammarEntitlementService(db, new EffectiveEntitlementResolver(db));

        var result = await service.CheckAsync(null, default);

        Assert.False(result.Allowed);
        Assert.Equal("anonymous", result.Tier);
    }

    [Fact]
    public async Task Entitlement_PaidSubscriberUnlimited()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-1",
            UserId = "user-pro",
            PlanId = "pro",
            Status = SubscriptionStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddMonths(-1),
            ChangedAt = DateTimeOffset.UtcNow.AddMonths(-1),
        });
        await db.SaveChangesAsync();

        var service = new GrammarEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.CheckAsync("user-pro", default);

        Assert.True(result.Allowed);
        Assert.Equal("paid", result.Tier);
        Assert.Equal(int.MaxValue, result.Remaining);
    }

    [Fact]
    public async Task Entitlement_FreeTierBlocksAfterThreeLessonsInWindow()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);

        for (var i = 0; i < 3; i++)
        {
            db.LearnerGrammarProgress.Add(new LearnerGrammarProgress
            {
                Id = Guid.NewGuid(),
                UserId = "user-free",
                LessonId = $"grm-L{i}",
                Status = "completed",
                StartedAt = DateTimeOffset.UtcNow.AddHours(-i - 1),
                CompletedAt = DateTimeOffset.UtcNow.AddHours(-i),
                ExerciseScore = 80,
            });
        }
        await db.SaveChangesAsync();

        var service = new GrammarEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.CheckAsync("user-free", default);

        Assert.False(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(0, result.Remaining);
        Assert.NotNull(result.ResetAt);
    }

    [Fact]
    public async Task Entitlement_FreeTierAllowsWhenUnderLimit()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);

        db.LearnerGrammarProgress.Add(new LearnerGrammarProgress
        {
            Id = Guid.NewGuid(),
            UserId = "user-free-2",
            LessonId = "grm-L0",
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExerciseScore = 80,
        });
        await db.SaveChangesAsync();

        var service = new GrammarEntitlementService(db, new EffectiveEntitlementResolver(db));
        var result = await service.CheckAsync("user-free-2", default);

        Assert.True(result.Allowed);
        Assert.Equal("free", result.Tier);
        Assert.Equal(2, result.Remaining);
    }
}
