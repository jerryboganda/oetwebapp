namespace OetLearner.Api.Tests.Services;

public sealed class AiTransportArchitectureTests
{
    private static readonly string[] Banned =
        ["api.anthropic.com", "x-api-key", "anthropic-version"];

    [Fact]
    public void No_direct_anthropic_transport_outside_rulebook_adapters()
    {
        var srcRoot = FindSrcRoot();
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsAllowed(srcRoot, file))
                continue;
            var text = File.ReadAllText(file);
            foreach (var token in Banned)
            {
                var comparison = token == "api.anthropic.com"
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (text.Contains(token, comparison))
                    violations.Add($"{Path.GetRelativePath(srcRoot, file)} :: {token}");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool IsAllowed(string srcRoot, string file)
    {
        var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
        if (!rel.Contains("/Services/Rulebook/", StringComparison.OrdinalIgnoreCase))
            return false;
        var name = Path.GetFileName(file);
        return name.Contains("Provider", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "AiProviderConnectionTester.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("backend/src not found from " + AppContext.BaseDirectory);
    }
}
