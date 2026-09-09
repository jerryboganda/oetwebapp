using Microsoft.Extensions.Configuration;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The owner settled the final user-facing name: <b>Sami</b>. The earlier working
/// name "Jana" must not reach a candidate through any surface.
///
/// <para>
/// This is not cosmetic. Testing Pack 1 scenario 1 checks the persona explicitly
/// ("Introduces itself as Sami, not Jana"), so a stale literal is a scored
/// failure. Two independent sites resolve the name — this composer and the
/// <c>/v1/companion/session</c> endpoint — and both read the same constant, so
/// these tests pin the constant rather than each call site.
/// </para>
/// </summary>
public sealed class CompanionPersonaTests
{
    private static CompanionPromptComposer Composer(string? configuredPersona = null)
    {
        var settings = new List<KeyValuePair<string, string?>>();
        if (configuredPersona is not null)
        {
            settings.Add(new(CompanionPromptComposer.PersonaSettingKey, configuredPersona));
        }

        return new CompanionPromptComposer(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    /// <summary>Only UserId is required; every other field defaults.</summary>
    private static CompanionTurnContext Context() => new() { UserId = "learner-1" };

    private static CompanionRetrievalResult NoEvidence() =>
        new([], false, false, false, []);

    private static Task<string> ComposeAsync(string? configuredPersona = null) =>
        Composer(configuredPersona).ComposeAsync(Context(), NoEvidence(), CancellationToken.None);

    [Fact]
    public void DefaultPersona_is_Sami()
    {
        Assert.Equal("Sami", CompanionPromptComposer.DefaultPersona);
    }

    [Fact]
    public async Task Composed_prompt_names_Sami_when_unconfigured()
    {
        // The production case: no Companion__PersonaName is set in any appsettings
        // or environment file, so the constant is what a learner actually sees.
        var prompt = await ComposeAsync();

        Assert.Contains("You are Sami,", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Composed_prompt_never_contains_the_retired_name()
    {
        var prompt = await ComposeAsync();

        Assert.DoesNotContain("Jana", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configuration_still_overrides_the_default()
    {
        // The name is settled, but keeping the key working means a future rename
        // stays a config change rather than a redeploy of this assembly.
        var prompt = await ComposeAsync("Layla");

        Assert.Contains("You are Layla,", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_configuration_falls_back_to_Sami(string configured)
    {
        // A blank environment variable is a realistic deployment slip. Falling
        // through to an empty persona would render "You are , the AI Learning
        // Companion" to a candidate.
        var prompt = await ComposeAsync(configured);

        Assert.Contains("You are Sami,", prompt, StringComparison.Ordinal);
    }
}
