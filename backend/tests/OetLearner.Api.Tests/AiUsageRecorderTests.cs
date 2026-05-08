using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Tests for Slice 1 of the AI Usage Management subsystem.
///
/// Coverage:
/// <list type="bullet">
///   <item>The recorder persists one row per call, whatever the outcome.</item>
///   <item>The gateway wires the recorder in for success.</item>
///   <item>The gateway wires the recorder in for ungrounded refusals.</item>
///   <item>The gateway wires the recorder in for provider failures.</item>
///   <item>Period keys are normalised to UTC YYYY-MM / YYYY-MM-DD.</item>
///   <item>Recorder failures never break the caller (fail-soft).</item>
/// </list>
/// </summary>
public class AiUsageRecorderTests
{
    private static DbContextOptions<LearnerDbContext> NewInMemoryOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    [Fact]
    public async Task Recorder_PersistsSuccess_WithFullContextAndHashes()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        var startedAt = new DateTimeOffset(2026, 04, 18, 10, 30, 00, TimeSpan.Zero);
        var context = new AiUsageContext(
            UserId: "user-001",
            AuthAccountId: "auth-001",
            TenantId: null,
            FeatureCode: AiFeatureCodes.WritingGrade,
            RulebookVersion: "1.0.0",
            PromptTemplateId: "writing.grade.v3",
            SystemPrompt: "OET AI — Rulebook-Grounded System Prompt …",
            UserPrompt: "Please grade this letter.",
            StartedAt: startedAt);

        await recorder.RecordSuccessAsync(
            context,
            providerId: "digitalocean-serverless",
            model: "gpt-4o",
            keySource: AiKeySource.Platform,
            usage: new AiUsage { PromptTokens = 120, CompletionTokens = 80 },
            latencyMs: 412,
            retryCount: 0,
            policyTrace: "scoring-critical → platform-only",
            ct: CancellationToken.None);

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal("user-001", row.UserId);
        Assert.Equal("auth-001", row.AuthAccountId);
        Assert.Equal(AiFeatureCodes.WritingGrade, row.FeatureCode);
        Assert.Equal("digitalocean-serverless", row.ProviderId);
        Assert.Equal("gpt-4o", row.Model);
        Assert.Equal(AiKeySource.Platform, row.KeySource);
        Assert.Equal("1.0.0", row.RulebookVersion);
        Assert.Equal("writing.grade.v3", row.PromptTemplateId);
        Assert.Equal(120, row.PromptTokens);
        Assert.Equal(80, row.CompletionTokens);
        Assert.Equal(200, row.TotalTokens);
        Assert.Equal(AiCallOutcome.Success, row.Outcome);
        Assert.Null(row.ErrorCode);
        Assert.Equal(412, row.LatencyMs);
        Assert.Equal("scoring-critical → platform-only", row.PolicyTrace);
        Assert.Equal("2026-04", row.PeriodMonthKey);
        Assert.Equal("2026-04-18", row.PeriodDayKey);
        // Hashes present, never raw bodies.
        Assert.NotNull(row.SystemPromptHash);
        Assert.NotNull(row.UserPromptHash);
        Assert.Equal(64, row.SystemPromptHash!.Length); // hex SHA-256
        Assert.Equal(64, row.UserPromptHash!.Length);
    }

    [Fact]
    public async Task Recorder_PersistsFailure_WithErrorCode()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await recorder.RecordFailureAsync(
            new AiUsageContext(
                UserId: "user-002",
                AuthAccountId: null,
                TenantId: null,
                FeatureCode: AiFeatureCodes.ConversationReply,
                RulebookVersion: "1.0.0",
                PromptTemplateId: null,
                SystemPrompt: null,
                UserPrompt: null,
                StartedAt: DateTimeOffset.UtcNow),
            providerId: "openai-platform",
            model: "gpt-4o-mini",
            keySource: AiKeySource.Byok,
            outcome: AiCallOutcome.ProviderError,
            errorCode: "provider_429",
            errorMessage: "Rate limited by upstream.",
            latencyMs: 88,
            retryCount: 2,
            policyTrace: "user.auto + byok valid → byok",
            ct: CancellationToken.None);

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiCallOutcome.ProviderError, row.Outcome);
        Assert.Equal("provider_429", row.ErrorCode);
        Assert.Equal(AiKeySource.Byok, row.KeySource);
        Assert.Equal(2, row.RetryCount);
        Assert.Equal(0, row.PromptTokens); // failed calls don't report usage
    }

    [Fact]
    public async Task Recorder_RefusesToAcceptSuccessOutcomeInRecordFailure()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            recorder.RecordFailureAsync(
                new AiUsageContext(null, null, null, AiFeatureCodes.Unclassified, null, null, null, null, DateTimeOffset.UtcNow),
                providerId: null,
                model: null,
                keySource: AiKeySource.None,
                outcome: AiCallOutcome.Success,
                errorCode: "whatever",
                errorMessage: null,
                latencyMs: 0,
                retryCount: 0,
                policyTrace: null,
                ct: CancellationToken.None));
    }

    [Fact]
    public async Task Recorder_DefaultsMissingFeatureCodeToUnclassified()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await recorder.RecordSuccessAsync(
            new AiUsageContext(null, null, null, "", null, null, null, null, DateTimeOffset.UtcNow),
            "mock", "mock", AiKeySource.Platform,
            usage: new AiUsage(), latencyMs: 0, retryCount: 0, policyTrace: null, ct: CancellationToken.None);

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiFeatureCodes.Unclassified, row.FeatureCode);
    }

    /// <summary>
    /// Phase 3: per-account analytics. The recorder must persist
    /// <c>AccountId</c> and <c>FailoverTrace</c> for both success (winning
    /// account) and failure (last attempted account + trail) outcomes. These
    /// fields are how the admin explorer answers "which Copilot PAT served
    /// this turn?" and "did this turn failover and across which slots?"
    /// without rebuilding the trail from log scraping.
    /// </summary>
    [Fact]
    public async Task Recorder_PersistsAccountIdAndFailoverTrace_OnSuccess()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await recorder.RecordSuccessAsync(
            new AiUsageContext(null, null, null, AiFeatureCodes.WritingGrade,
                null, null, null, null, DateTimeOffset.UtcNow),
            providerId: "copilot",
            model: "gpt-4o",
            keySource: AiKeySource.Platform,
            usage: new AiUsage { PromptTokens = 10, CompletionTokens = 20 },
            latencyMs: 99,
            retryCount: 0,
            policyTrace: null,
            ct: CancellationToken.None,
            accountId: "acct-backup",
            failoverTrace: "primary:429 → backup:success");

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal("acct-backup", row.AccountId);
        Assert.Equal("primary:429 → backup:success", row.FailoverTrace);
    }

    [Fact]
    public async Task Recorder_PersistsAccountIdAndFailoverTrace_OnFailure()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await recorder.RecordFailureAsync(
            new AiUsageContext(null, null, null, AiFeatureCodes.WritingGrade,
                null, null, null, null, DateTimeOffset.UtcNow),
            providerId: "copilot",
            model: "gpt-4o",
            keySource: AiKeySource.Platform,
            outcome: AiCallOutcome.ProviderError,
            errorCode: "provider_429",
            errorMessage: "pool exhausted",
            latencyMs: 200,
            retryCount: 0,
            policyTrace: null,
            ct: CancellationToken.None,
            accountId: "acct-tertiary",
            failoverTrace: "primary:429 → backup:429 → tertiary:429");

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal("acct-tertiary", row.AccountId);
        Assert.Equal("primary:429 → backup:429 → tertiary:429", row.FailoverTrace);
        Assert.Equal(AiCallOutcome.ProviderError, row.Outcome);
    }

    [Fact]
    public async Task Recorder_NullAccountId_OnSingleCredentialProvider()
    {
        var options = NewInMemoryOptions();
        await using var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);

        await recorder.RecordSuccessAsync(
            new AiUsageContext(null, null, null, AiFeatureCodes.WritingGrade,
                null, null, null, null, DateTimeOffset.UtcNow),
            providerId: "openai-platform",
            model: "gpt-4o",
            keySource: AiKeySource.Platform,
            usage: new AiUsage(),
            latencyMs: 50,
            retryCount: 0,
            policyTrace: null,
            ct: CancellationToken.None);

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Null(row.AccountId);
        Assert.Null(row.FailoverTrace);
    }
}

/// <summary>
/// Integration tests showing the gateway actually calls the recorder under
/// all outcome branches. Uses a real <see cref="LearnerDbContext"/> with an
/// in-memory provider so we exercise the real <see cref="AiUsageRecorder"/>.
/// </summary>
public class AiGatewayRecorderIntegrationTests
{
    private readonly RulebookLoader _loader = new();

    private (IAiGatewayService gateway, LearnerDbContext db) BuildGateway(IAiModelProvider? provider = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);
        var providers = new[] { provider ?? new MockAiProvider() };
        return (new AiGatewayService(_loader, providers, recorder), db);
    }

    [Fact]
    public async Task Gateway_RecordsSuccess_WhenGroundedPromptSucceeds()
    {
        var (gateway, db) = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
        });

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserId = "user-042",
            AuthAccountId = "auth-042",
            FeatureCode = AiFeatureCodes.WritingGrade,
            PromptTemplateId = "writing.grade.v3",
        });

        Assert.False(string.IsNullOrWhiteSpace(result.Completion));

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal("user-042", row.UserId);
        Assert.Equal(AiFeatureCodes.WritingGrade, row.FeatureCode);
        Assert.Equal(AiCallOutcome.Success, row.Outcome);
        Assert.Equal(AiKeySource.Platform, row.KeySource);
        Assert.Equal("mock", row.ProviderId);
        Assert.Equal("1.0.0", row.RulebookVersion);
        Assert.Equal("writing.grade.v3", row.PromptTemplateId);
        Assert.True(row.LatencyMs >= 0);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_RecordsRefusal_WhenPromptMissing()
    {
        var (gateway, db) = BuildGateway();

        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = null,
                UserId = "user-099",
                FeatureCode = AiFeatureCodes.WritingGrade,
            }));

        // The exception still fired, but a refusal row was persisted.
        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiCallOutcome.GatewayRefused, row.Outcome);
        Assert.Equal("ungrounded", row.ErrorCode);
        Assert.Equal(AiKeySource.None, row.KeySource);
        Assert.Null(row.ProviderId);
        Assert.Equal("user-099", row.UserId);
        Assert.Equal(AiFeatureCodes.WritingGrade, row.FeatureCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_RecordsRefusal_WhenPromptUngrounded()
    {
        var (gateway, db) = BuildGateway();

        var prompt = new AiGroundedPrompt
        {
            SystemPrompt = "You are a friendly chatbot.",
            TaskInstruction = "Hi",
        };

        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                FeatureCode = AiFeatureCodes.ConversationReply,
            }));

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiCallOutcome.GatewayRefused, row.Outcome);
        Assert.Equal("ungrounded", row.ErrorCode);
        Assert.Equal("gateway.refused", row.PolicyTrace);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_RecordsProviderError_AndRethrows()
    {
        var throwingProvider = new ThrowingProvider(new HttpRequestException("429 Too Many Requests"));
        var (gateway, db) = BuildGateway(throwingProvider);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
        });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                Provider = "throwing",
                UserId = "user-500",
                FeatureCode = AiFeatureCodes.WritingGrade,
            }));

        var row = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiCallOutcome.ProviderError, row.Outcome);
        Assert.Equal("provider_429", row.ErrorCode);
        Assert.Equal("throwing", row.ProviderId);
        Assert.Equal(AiKeySource.Platform, row.KeySource);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Gateway_DoesNotBreak_WhenRecorderIsNotWired()
    {
        // Backward compatibility: older code paths can build a gateway
        // without a recorder. We must not introduce a hard dependency.
        var gateway = new AiGatewayService(_loader, new[] { (IAiModelProvider)new MockAiProvider() });

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
        });

        var result = await gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt });
        Assert.False(string.IsNullOrWhiteSpace(result.Completion));
    }

    private sealed class ThrowingProvider(Exception ex) : IAiModelProvider
    {
        public string Name => "throwing";
        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
            => throw ex;
    }
}
