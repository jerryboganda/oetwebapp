namespace OetLearner.Api.Tests;

public sealed class S3CompatibleFileStorageStructureTests
{
    [Fact]
    public void CombinedRead_UsesOneGetObjectAndNoMetadataRequest()
    {
        var sourcePath = Path.Combine(
            FindRepoRoot(),
            "backend",
            "src",
            "OetLearner.Api",
            "Services",
            "Content",
            "S3CompatibleFileStorage.cs");
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf(
            "public async Task<FileStorageReadResult> OpenReadWithMetadataAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf("public Uri? ResolveReadUrl", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not locate the S3 combined-read implementation.");
        var combinedReadSection = source[start..end];
        Assert.Equal(
            1,
            combinedReadSection.Split(".GetObjectAsync(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("GetObjectMetadata", combinedReadSection, StringComparison.Ordinal);
        Assert.Contains("response.ContentLength", combinedReadSection, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not find the OET repository root.");
    }
}
