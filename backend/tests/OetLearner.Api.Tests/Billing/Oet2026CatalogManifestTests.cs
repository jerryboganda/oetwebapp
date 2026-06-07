using System.Text.Json;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

public sealed class Oet2026CatalogManifestTests
{
    [Fact]
    public async Task ZeroPricePlans_AreNotPubliclyVisible()
    {
        var manifest = await LoadManifestAsync();
        var visibleZeroPricePlans = manifest.RootElement
            .GetProperty("plans")
            .EnumerateArray()
            .Where(plan =>
                plan.GetProperty("price").GetDecimal() <= 0 &&
                !plan.GetProperty("isDraft").GetBoolean() &&
                plan.GetProperty("isVisible").GetBoolean())
            .Select(plan => plan.GetProperty("code").GetString())
            .ToArray();

        Assert.Empty(visibleZeroPricePlans);
    }

    private static async Task<JsonDocument> LoadManifestAsync()
    {
        var repoRoot = FindRepoRoot();
        var seedPath = Path.Combine(repoRoot, "backend", "src", "OetLearner.Api", "Data", "Seeds", "oet-2026-catalog.json");
        await using var stream = File.OpenRead(seedPath);
        return await JsonDocument.ParseAsync(stream);
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

        throw new DirectoryNotFoundException("Could not find repository root for OET 2026 catalog manifest test.");
    }
}
