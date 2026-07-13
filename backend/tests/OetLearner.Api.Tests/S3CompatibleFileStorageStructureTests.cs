using System.Text.RegularExpressions;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public sealed class S3CompatibleFileStorageStructureTests
{
    [Fact]
    public void CombinedRead_UsesOneGetObjectAndNoMetadataRequest()
    {
        var sourcePath = GetS3SourcePath();
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

    [Fact]
    public void S3Provider_HasNoSyncWaitsOrWholePayloadMemoryBuffer()
    {
        var source = File.ReadAllText(GetS3SourcePath());

        Assert.DoesNotContain(".GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\.Result\b", RegexOptions.CultureInvariant), source);
        Assert.DoesNotMatch(new Regex(@"\.Wait\s*\(", RegexOptions.CultureInvariant), source);
        Assert.DoesNotContain("MemoryStream", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToArray(", source, StringComparison.Ordinal);
        Assert.Contains("TransferUtilityUploadRequest", source, StringComparison.Ordinal);
        Assert.Contains("PartSize = MultipartPartSizeBytes", source, StringComparison.Ordinal);
        Assert.Contains("FileOptions.DeleteOnClose", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageContract_ContainsOnlyAsyncRemoteIoOperations()
    {
        var methodNames = typeof(IFileStorage)
            .GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Exists", methodNames);
        Assert.DoesNotContain("ListKeys", methodNames);
        Assert.DoesNotContain("Delete", methodNames);
        Assert.DoesNotContain("Length", methodNames);
        Assert.DoesNotContain("Move", methodNames);
        Assert.DoesNotContain("DeletePrefix", methodNames);

        Assert.Contains("ExistsAsync", methodNames);
        Assert.Contains("ListKeysAsync", methodNames);
        Assert.Contains("DeleteAsync", methodNames);
        Assert.Contains("LengthAsync", methodNames);
        Assert.Contains("MoveAsync", methodNames);
        Assert.Contains("DeletePrefixAsync", methodNames);
    }

    [Fact]
    public void ProductionStorageCallers_HaveNoLegacyCalls_AndAwaitDeferredWrites()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api");
        var legacyCall = new Regex(
            @"\b(?:storage|fileStorage|_storage|_fileStorage)\s*\.\s*" +
            @"(?:Exists|ListKeys|Delete|Length|Move|DeletePrefix)\s*\(",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var violations = new List<string>();
        var openWriteViolations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(apiRoot, path);
            var source = File.ReadAllText(path);
            foreach (Match match in legacyCall.Matches(source))
                violations.Add($"{relative}: {match.Value}");

            if (path.EndsWith("IFileStorage.cs", StringComparison.Ordinal) ||
                path.EndsWith("S3CompatibleFileStorage.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(".OpenWriteAsync(", StringComparison.Ordinal) &&
                    !lines[index].Contains("await using", StringComparison.Ordinal))
                {
                    openWriteViolations.Add($"{relative}:{index + 1}");
                }
            }
        }

        Assert.Empty(violations);
        Assert.Empty(openWriteViolations);
    }

    private static string GetS3SourcePath()
        => Path.Combine(
            FindRepoRoot(),
            "backend",
            "src",
            "OetLearner.Api",
            "Services",
            "Content",
            "S3CompatibleFileStorage.cs");

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
