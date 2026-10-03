using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// RULE MAX-ALWAYS-ON (AGENTS.md, owner directive 2026-10-02): the Speaking grading pin to the
/// Claude Max route can never be switched off by configuration. These tests fail the build if
/// the default changes or a blank value turns the pin off again.
/// </summary>
public sealed class SpeakingMaxAlwaysOnTests
{
    [Fact]
    public void Default_PinsTheMaxRoute()
        => Assert.Equal(WritingSubscriptionProviders.Claude, new SpeakingGradingOptions().PinnedProviderCode);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankValue_ResolvesToTheMaxRoute(string? value)
        => Assert.Equal(WritingSubscriptionProviders.Claude, new SpeakingGradingOptions { PinnedProviderCode = value! }.PinnedProviderCode);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankConfiguration_BoundLikeProduction_StillPinsMax(string value)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Speaking:Grading:PinnedProviderCode"] = value })
            .Build();

        var options = new SpeakingGradingOptions();
        config.GetSection(SpeakingGradingOptions.SectionName).Bind(options);

        Assert.Equal(WritingSubscriptionProviders.Claude, options.PinnedProviderCode);
    }

    [Fact]
    public void Source_OptionsClassCannotLetThePinBeEmpty()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api");
        var options = File.ReadAllText(Path.Combine(apiRoot, "Configuration", "SpeakingGradingOptions.cs"));

        // The backing field starts on Max and the setter maps blank to Max.
        Assert.Matches(new Regex(@"_pinnedProviderCode\s*=\s*WritingSubscriptionProviders\.Claude\s*;"), options);
        Assert.Matches(new Regex(@"IsNullOrWhiteSpace\(value\)\s*\?\s*WritingSubscriptionProviders\.Claude"), options);
        // No auto-property / empty initialiser that would make blank mean "off" again.
        Assert.DoesNotMatch(new Regex(@"PinnedProviderCode\s*\{\s*get;\s*set;\s*\}"), options);
        Assert.DoesNotMatch(new Regex(@"_pinnedProviderCode\s*=\s*(string\.Empty|"""")"), options);

        // The shipped appsettings never configures a different (or empty) pin.
        var appsettings = File.ReadAllText(Path.Combine(apiRoot, "appsettings.json"));
        Assert.Matches(new Regex(@"""PinnedProviderCode""\s*:\s*""writing-claude-sub"""), appsettings);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (AGENTS.md + backend/) not found.");
    }
}
