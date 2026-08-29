using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningCoordinatorMigrationTests
{
    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("auto", "auto")]
    [InlineData("none", "none")]
    [InlineData("any", "any")]
    [InlineData("emit_part_a_verdicts", "tool")]
    public void AnthropicProvider_honours_tool_choice(string? toolChoice, string expectedType)
    {
        var resolved = AnthropicProvider.ResolveAnthropicToolChoice(toolChoice);
        Assert.Equal(expectedType, resolved["type"]);
        if (expectedType == "tool")
            Assert.Equal("emit_part_a_verdicts", resolved["name"]);
    }

    [Fact]
    public void Listening_services_have_no_direct_anthropic_http()
    {
        var listeningDir = FindListeningServicesDirectory();
        Assert.True(Directory.Exists(listeningDir), listeningDir);

        foreach (var file in Directory.GetFiles(listeningDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("api.anthropic.com", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("x-api-key", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ListeningPartAScoringAnthropic", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ListeningExtractionAnthropic", text, StringComparison.Ordinal);
        }
    }

    private static string FindListeningServicesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OetLearner.Api", "Services", "Listening");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "backend", "src", "OetLearner.Api", "Services", "Listening");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Services/Listening from the test base directory.");
    }
}
