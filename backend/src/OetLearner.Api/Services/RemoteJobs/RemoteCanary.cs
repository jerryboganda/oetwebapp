using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>What a correct node must report for the canary document.</summary>
public sealed record CanaryExpected(int PageCount, string TextSha256, string PagesSha256, int EmbeddedChars);

/// <summary>
/// The known-answer canary (OET-RWP/1 section 6.5): a small PDF embedded in the API assembly that a node extracts
/// and whose result the API checks against the in-process oracle. It carries no learner or content data, never touches
/// <c>IFileStorage</c>, and is how a node earns Active (and re-proves itself periodically).
///
/// <para>
/// The expected hashes are produced by running <see cref="PdfPigPdfTextExtractor"/> (the oracle the agent is
/// link-compiled from) over the embedded bytes, once per process, so they can never drift from the extractor. A manual
/// test source (<c>RemoteEngineMigrationAndWiringTests</c>) pins the result as non-empty text; nothing runs it automatically.
/// </para>
/// </summary>
public static class RemoteCanary
{
    public const string ResourceType = "Canary";
    public const string ResourceId = "pdf-canary-v1";

    /// <summary>Storage key prefix of a manifest entry that is served from the embedded resource.</summary>
    public const string EmbeddedKeyPrefix = "embedded:";

    public const string EmbeddedKey = EmbeddedKeyPrefix + ResourceId;

    // LogicalName of the EmbeddedResource in OetLearner.Api.csproj (no path separators, so it is identical on every OS).
    private const string ManifestResourceName = "OetRemoteCanary.pdf-canary-v1.pdf";

    /// <summary>Minimum text length the canary run is judged by (the document is far longer).</summary>
    public const int MinTextLength = 50;

    private static readonly Lazy<byte[]> Bytes = new(LoadBytes);
    private static readonly Lazy<Task<CanaryExpected>> ExpectedValue = new(ComputeExpectedAsync);

    public static byte[] PdfBytes => Bytes.Value;

    public static string PdfSha256 => RemoteIds.Sha256Hex(PdfBytes);

    public static long PdfSize => PdfBytes.LongLength;

    /// <summary>Opens a fresh read stream over the embedded document.</summary>
    public static Stream OpenRead() => new MemoryStream(PdfBytes, writable: false);

    public static Task<CanaryExpected> GetExpectedAsync() => ExpectedValue.Value;

    /// <summary>
    /// Derives the canonical hashes from a page list exactly as the protocol defines them: <c>flat</c> is the pages joined
    /// by a blank line and trimmed; <c>pagesSha256</c> hashes the per-page hashes joined by a newline.
    /// </summary>
    public static CanaryExpected Derive(IReadOnlyList<string> pages)
    {
        var flat = string.Join("\n\n", pages).Trim();
        var pageHashes = pages.Select(page => RemoteIds.Sha256Hex(page)).ToArray();
        return new CanaryExpected(
            pages.Count,
            RemoteIds.Sha256Hex(flat),
            RemoteIds.Sha256Hex(string.Join("\n", pageHashes)),
            flat.Length);
    }

    private static async Task<CanaryExpected> ComputeExpectedAsync()
    {
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        await using var stream = OpenRead();
        var pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);
        return Derive(pages);
    }

    private static byte[] LoadBytes()
    {
        using var stream = typeof(RemoteCanary).Assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidOperationException("The canary PDF is not embedded in the API assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
