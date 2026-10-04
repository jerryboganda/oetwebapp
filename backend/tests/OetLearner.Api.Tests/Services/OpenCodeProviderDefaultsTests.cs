using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// The OpenCode constants and the shared "may this row be picked implicitly" predicate. Nothing
/// here touches the network or the database.
/// </summary>
public sealed class OpenCodeProviderDefaultsTests
{
    [Theory]
    [InlineData("opencode.ai", true)]
    [InlineData("OpenCode.AI", true)]
    [InlineData("api.opencode.ai", true)]
    [InlineData("zen.gateway.opencode.ai", true)]
    // Look-alikes must never match: the host check decides who receives the gateway key.
    [InlineData("evilopencode.ai", false)]
    [InlineData("opencode.ai.evil.example", false)]
    [InlineData("opencode.com", false)]
    [InlineData("notopencode.ai.", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsOpenCodeHost_MatchesOnlyTheDomainAndItsSubdomains(string? host, bool expected)
    {
        Assert.Equal(expected, OpenCodeProviderDefaults.IsOpenCodeHost(host));
    }

    [Theory]
    [InlineData("https://opencode.ai/zen/v1", true)]
    [InlineData("https://opencode.ai/zen/go/v1", true)]
    [InlineData("https://opencode.ai/zen/v1/", true)]
    [InlineData("https://API.OpenCode.ai/v1", true)]
    [InlineData("https://opencode.ai.evil.example/zen/v1", false)]
    [InlineData("https://evil.example/opencode.ai/zen/v1", false)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("http://oet-writing-codex:8080", false)]
    [InlineData("opencode.ai/zen/v1", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOpenCodeBaseUrl_NeedsAnAbsoluteUrlOnAnOpenCodeHost(string? baseUrl, bool expected)
    {
        Assert.Equal(expected, OpenCodeProviderDefaults.IsOpenCodeBaseUrl(baseUrl));
    }

    [Fact]
    public void BothGatewayBaseUrls_AreOpenCodeHostsAndPassTheSsrfGuard()
    {
        foreach (var url in new[] { OpenCodeProviderDefaults.ZenBaseUrl, OpenCodeProviderDefaults.GoBaseUrl })
        {
            Assert.True(OpenCodeProviderDefaults.IsOpenCodeBaseUrl(url), url);
            Assert.Null(AiProviderConnectionTester.GetUnsafeBaseUrlReason(url));
        }

        Assert.NotEqual(OpenCodeProviderDefaults.ZenBaseUrl, OpenCodeProviderDefaults.GoBaseUrl);
    }

    [Fact]
    public void CuratedModels_AreDisjointFromClaudeAndUbag_AndSafeAsPickerValues()
    {
        var curated = OpenCodeProviderDefaults.CuratedChatModels;

        Assert.NotEmpty(curated);
        Assert.Contains(OpenCodeProviderDefaults.DefaultModel, curated);
        Assert.Equal(curated.Length, curated.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(curated.Intersect(AssistantModelCatalog.ClaudeApiModels, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(curated.Intersect(AssistantModelCatalog.UbagModels, StringComparer.OrdinalIgnoreCase));
        // '|' is the UBAG composite separator ("provider|label"): a curated id carrying one would be
        // mis-routed as a UBAG model, and a comma would corrupt the allowed-models CSV.
        Assert.All(curated, model =>
        {
            Assert.False(string.IsNullOrWhiteSpace(model));
            Assert.Equal(model.Trim(), model);
            Assert.DoesNotContain('|', model);
            Assert.DoesNotContain(',', model);
        });
    }

    [Fact]
    public void AllowedModelsCsv_RoundTripsTheCuratedList()
    {
        Assert.Equal(
            OpenCodeProviderDefaults.CuratedChatModels,
            OpenCodeProviderDefaults.AllowedModelsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void DefaultReasoningEffort_IsAValueBothCuratedModelsAccept()
    {
        Assert.Contains(OpenCodeProviderDefaults.DefaultReasoningEffort, new[] { "low", "high", "max" });
    }

    [Fact]
    public void LearnerBusyMessage_SaysBusyAndLeaksNothingAboutTheFailure()
    {
        var message = OpenCodeProviderDefaults.LearnerBusyMessage;

        Assert.Contains("busy at the moment", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("few minutes", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opencode", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP", message, StringComparison.Ordinal);
        Assert.DoesNotContain("quota", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credit", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitOnlyCodes_ListOpenCode_AndFailoverPriorityIsLast()
    {
        Assert.Contains(OpenCodeProviderDefaults.ProviderCode, OpenCodeProviderDefaults.ExplicitOnlyCodes);
        Assert.Equal(OpenCodeProviderDefaults.ProviderCode, OpenCodeProviderDefaults.ProviderCode.ToLowerInvariant());
        // The seeded keyless sidecars sit at 1 and 2 and the core rows at 20-95: OpenCode is last.
        Assert.True(OpenCodeProviderDefaults.FailoverPriority > 95);
    }

    // ── AiProviderDefaultEligibility ─────────────────────────────────────

    private static AiProvider Row(string code, string encryptedApiKey = "encrypted-test-key") => new()
    {
        Id = code,
        Code = code,
        Name = code,
        Dialect = AiProviderDialect.OpenAiCompatible,
        Category = AiProviderCategory.TextChat,
        BaseUrl = "https://provider.example.test",
        EncryptedApiKey = encryptedApiKey,
        IsActive = true,
    };

    [Fact]
    public void IsDefaultEligible_AcceptsAnOrdinaryKeyedRow()
    {
        Assert.True(AiProviderDefaultEligibility.IsDefaultEligible(Row("openai-platform")));
        Assert.True(AiProviderDefaultEligibility.IsDefaultEligible(Row("anthropic", string.Empty)));
    }

    [Fact]
    public void IsDefaultEligible_RejectsKeylessSidecarMarkerRows()
    {
        Assert.False(AiProviderDefaultEligibility.IsDefaultEligible(
            Row(WritingSubscriptionProviderDefaults.CodexCode, WritingSubscriptionProviderDefaults.MarkerKey)));
        Assert.False(AiProviderDefaultEligibility.IsDefaultEligible(
            Row(WritingSubscriptionProviderDefaults.ClaudeCode, WritingSubscriptionProviderDefaults.MarkerKey)));
    }

    [Theory]
    [InlineData("opencode")]
    [InlineData("OpenCode")]
    [InlineData("  opencode  ")]
    public void IsDefaultEligible_RejectsAnExplicitOnlyRow_EvenWithARealKey(string code)
    {
        // A real-key row has no marker, so only the code list keeps it out of "first credentialed row" picks.
        Assert.False(AiProviderDefaultEligibility.IsDefaultEligible(Row(code, "ciphertext-of-a-real-key")));
        Assert.True(AiProviderDefaultEligibility.IsExplicitOnlyCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("opencode-lookalike")]
    [InlineData("anthropic")]
    public void IsExplicitOnlyCode_IsFalseForEverythingElse(string? code)
    {
        Assert.False(AiProviderDefaultEligibility.IsExplicitOnlyCode(code));
    }
}
