using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// <c>GET /v1/admin/ai/typesafe/status</c>: read-only TypeSafe / Jev status for the admin
/// page. Null-safe on an empty database, 7-day usage grouped by feature code for the
/// <c>typesafe-jev</c> provider only, and the API key is never part of the response
/// (key presence booleans and the provider row's last-4 hint only).
/// </summary>
[Collection("AuthFlows")]
public sealed class JevTypeSafeAdminStatusTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static async Task<JsonElement> StatusAsync(LearnerDbContext db, TypeSafeOptions opts, DateTimeOffset now)
    {
        var status = await TypeSafeAdminEndpoints.BuildStatusAsync(db, opts, now, CancellationToken.None);
        return JsonSerializer.SerializeToElement(status, Web);
    }

    private static AiUsageRecord Usage(string featureCode, string providerId, AiCallOutcome outcome, int latencyMs, decimal costUsd, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = "learner-1",
        FeatureCode = featureCode,
        ProviderId = providerId,
        PromptTokens = 1000,
        CompletionTokens = 10,
        CostEstimateUsd = costUsd,
        Outcome = outcome,
        LatencyMs = latencyMs,
        CreatedAt = at,
        PeriodMonthKey = at.ToString("yyyy-MM"),
        PeriodDayKey = at.ToString("yyyy-MM-dd"),
    };

    [Fact]
    public async Task EmptyDatabase_IsNullSafe_EveryFlagOffAndNoUsage()
    {
        await using var db = NewContext(nameof(EmptyDatabase_IsNullSafe_EveryFlagOffAndNoUsage));

        var status = await StatusAsync(db, new TypeSafeOptions(), DateTimeOffset.UtcNow);

        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal("jev-1.13.0", status.GetProperty("model").GetString());
        Assert.Equal("typesafe-jev", status.GetProperty("providerCode").GetString());

        var key = status.GetProperty("key");
        Assert.False(key.GetProperty("envKeyConfigured").GetBoolean());
        Assert.False(key.GetProperty("providerRowExists").GetBoolean());
        Assert.False(key.GetProperty("providerRowKeyConfigured").GetBoolean());
        Assert.False(key.GetProperty("providerRowActive").GetBoolean());
        Assert.False(key.GetProperty("effectiveKeyAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, key.GetProperty("apiKeyHint").ValueKind);

        var surfaces = status.GetProperty("surfaces").EnumerateArray().ToList();
        // 12 original surfaces + 8 added 2026-10-03 (coach need, conversation cross-check, pronunciation words,
        // Model Answer review, listening gaps, mock weakness, answer-key triage, extraction verify).
        Assert.Equal(20, surfaces.Count);
        // The frontend keys rows by feature code and the 7-day usage join is by feature code: both must be unique.
        Assert.Equal(20, surfaces.Select(s => s.GetProperty("featureCode").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, surfaces.Select(s => s.GetProperty("envVar").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(surfaces, s =>
        {
            Assert.False(s.GetProperty("enabled").GetBoolean());
            Assert.Equal(0, s.GetProperty("calls").GetInt32());
            Assert.Equal(0, s.GetProperty("failures").GetInt32());
            Assert.Equal(0, s.GetProperty("avgLatencyMs").GetInt32());
            Assert.Equal(0m, s.GetProperty("costUsd").GetDecimal());
            Assert.StartsWith("jev.", s.GetProperty("featureCode").GetString(), StringComparison.Ordinal);
        });
        Assert.Empty(status.GetProperty("otherUsage").EnumerateArray());
    }

    [Fact]
    public async Task Flags_And_Thresholds_MirrorTheOptions_WithTheirEnvironmentNames()
    {
        await using var db = NewContext(nameof(Flags_And_Thresholds_MirrorTheOptions_WithTheirEnvironmentNames));
        var opts = new TypeSafeOptions
        {
            Enabled = true,
            WritingVerifyEnabled = true,
            DevelopmentTriageEnabled = true,
            WritingGuardEnforced = true,
            GuardBlockThreshold = 0.9,
        };

        var status = await StatusAsync(db, opts, DateTimeOffset.UtcNow);

        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.True(status.GetProperty("guardEnforced").GetBoolean());
        var enabled = status.GetProperty("surfaces").EnumerateArray()
            .Where(s => s.GetProperty("enabled").GetBoolean())
            .Select(s => s.GetProperty("envVar").GetString())
            .Order()
            .ToList();
        Assert.Equal(new[] { "TYPESAFE__DEVELOPMENTTRIAGEENABLED", "TYPESAFE__WRITINGVERIFYENABLED" }, enabled);

        var guardBlock = status.GetProperty("thresholds").EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == nameof(TypeSafeOptions.GuardBlockThreshold));
        Assert.Equal("TYPESAFE__GUARDBLOCKTHRESHOLD", guardBlock.GetProperty("envVar").GetString());
        Assert.Equal(0.9, guardBlock.GetProperty("value").GetDouble());
        Assert.Contains(status.GetProperty("limits").EnumerateArray(),
            l => l.GetProperty("name").GetString() == nameof(TypeSafeOptions.BreakerCooldownSeconds));
    }

    [Fact]
    public async Task Usage_IsTheLastSevenDays_ForTheJevProviderOnly_GroupedByFeatureCode()
    {
        await using var db = NewContext(nameof(Usage_IsTheLastSevenDays_ForTheJevProviderOnly_GroupedByFeatureCode));
        var now = DateTimeOffset.UtcNow;
        db.AiUsageRecords.AddRange(
            Usage("jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, 100, 0.0004m, now.AddHours(-1)),
            Usage("jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, 200, 0.0004m, now.AddDays(-3)),
            Usage("jev.writing.verify", "typesafe-jev", AiCallOutcome.ProviderError, 300, 0.0004m, now.AddDays(-6)),
            // Outside the window, and the same feature code on a different provider: both ignored.
            Usage("jev.writing.verify", "typesafe-jev", AiCallOutcome.Success, 999, 5m, now.AddDays(-8)),
            Usage("jev.writing.verify", "anthropic", AiCallOutcome.Success, 999, 5m, now.AddHours(-2)),
            Usage("writing.grade", "writing-claude-sub", AiCallOutcome.Success, 999, 5m, now.AddHours(-2)),
            // A Jev code no surface owns shows up under otherUsage.
            Usage("jev.reading.future", "typesafe-jev", AiCallOutcome.Success, 50, 0.0001m, now.AddHours(-1)));
        await db.SaveChangesAsync();

        var status = await StatusAsync(db, new TypeSafeOptions(), now);

        var verify = status.GetProperty("surfaces").EnumerateArray()
            .Single(s => s.GetProperty("featureCode").GetString() == "jev.writing.verify");
        Assert.Equal(3, verify.GetProperty("calls").GetInt32());
        Assert.Equal(1, verify.GetProperty("failures").GetInt32());
        Assert.Equal(200, verify.GetProperty("avgLatencyMs").GetInt32());
        Assert.Equal(0.0012m, verify.GetProperty("costUsd").GetDecimal());

        var other = Assert.Single(status.GetProperty("otherUsage").EnumerateArray());
        Assert.Equal("jev.reading.future", other.GetProperty("featureCode").GetString());
        Assert.Equal(1, other.GetProperty("calls").GetInt32());
        Assert.Equal(7, status.GetProperty("usageWindowDays").GetInt32());
    }

    [Fact]
    public async Task KeyStatus_ReportsPresenceAndTheLastFourHint_NeverTheKeyOrItsCiphertext()
    {
        await using var db = NewContext(nameof(KeyStatus_ReportsPresenceAndTheLastFourHint_NeverTheKeyOrItsCiphertext));
        const string envKey = "env-placeholder-not-a-real-key-0123456789";
        const string ciphertext = "ciphertext-placeholder-not-a-real-blob";
        db.AiProviders.Add(new AiProvider
        {
            Id = "typesafe-jev-row",
            Code = TypeSafeOptions.ProviderCode,
            Name = "TypeSafe Jev (typed judgments)",
            Dialect = AiProviderDialect.TypeSafeJev,
            Category = AiProviderCategory.Judgment,
            BaseUrl = "https://api.typesafe.ai",
            EncryptedApiKey = ciphertext,
            ApiKeyHint = "…wxyz",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var inactiveRow = await StatusAsync(db, new TypeSafeOptions { Enabled = true, ApiKey = envKey }, DateTimeOffset.UtcNow);
        var key = inactiveRow.GetProperty("key");
        Assert.True(key.GetProperty("envKeyConfigured").GetBoolean());
        Assert.True(key.GetProperty("providerRowExists").GetBoolean());
        Assert.True(key.GetProperty("providerRowKeyConfigured").GetBoolean());
        Assert.False(key.GetProperty("providerRowActive").GetBoolean());
        Assert.Equal("…wxyz", key.GetProperty("apiKeyHint").GetString());
        Assert.True(key.GetProperty("effectiveKeyAvailable").GetBoolean());

        // An inactive provider row is skipped at call time, so without the env key nothing is usable.
        var rowOnly = await StatusAsync(db, new TypeSafeOptions { Enabled = true }, DateTimeOffset.UtcNow);
        Assert.False(rowOnly.GetProperty("key").GetProperty("effectiveKeyAvailable").GetBoolean());

        var json = inactiveRow.GetRawText();
        Assert.DoesNotContain(envKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ciphertext, json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Endpoint_RequiresAiConfig_AndReturnsStatusWithoutTheKey()
    {
        using var env = DevAuthEnv.Enable();
        using var baseFactory = new TestWebApplicationFactory();
        const string envKey = "env-placeholder-not-a-real-key-0123456789";
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["TypeSafe:ApiKey"] = envKey })));

        using var learner = factory.CreateClient();
        learner.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        learner.DefaultRequestHeaders.Add("X-Debug-UserId", $"learner-{Guid.NewGuid():N}");
        var denied = await learner.GetAsync("/v1/admin/ai/typesafe/status");
        Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);

        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        admin.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.AiConfig);
        admin.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        admin.DefaultRequestHeaders.Add("X-Debug-Email", "typesafe-status@example.test");
        admin.DefaultRequestHeaders.Add("X-Debug-Name", "TypeSafe Status Probe");
        var response = await admin.GetAsync("/v1/admin/ai/typesafe/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(envKey, body, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("key").GetProperty("envKeyConfigured").GetBoolean());
    }
}
