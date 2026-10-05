using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Pure page-to-chunk algorithm for the companion document index (OET-RWP/1 section 6.2).
///
/// <para>
/// Extracted from <c>CompanionDocumentIndexer</c> so the same code runs in the API process and,
/// link-compiled, in the fleet agent (<c>companion.index-prep</c>). It therefore depends on
/// nothing but <c>System</c> and <see cref="CompanionChunkDraft"/> (declared in
/// <c>CompanionIndexWriter.cs</c>; a link-compiling project supplies a shim with the same shape:
/// <c>Heading</c>, <c>Text</c>, <c>PageNumber</c>). No EF, no logging, no configuration.
/// </para>
///
/// <para>
/// <b>Do not change behaviour casually.</b> Chunk text feeds the content hash that decides
/// whether a chunk needs re-embedding, and <see cref="Version"/> is part of the remote job's
/// engine version, so a behavioural change must bump it.
/// </para>
/// </summary>
public static class CompanionChunker
{
    /// <summary>Identity of this algorithm; part of the remote engine version string.</summary>
    public const string Version = "companion-chunker:1";

    /// <summary>
    /// Longest page kept whole. Beyond this the page is split, because a single chunk larger
    /// than the retriever's per-source verbatim cap can only ever be returned truncated.
    /// </summary>
    public const int MaxChunkChars = 1100;

    /// <summary>
    /// Pages shorter than this are merged forward. A PDF of mostly slide titles otherwise
    /// produces hundreds of two-word chunks that match everything weakly.
    /// </summary>
    public const int MinChunkChars = 200;

    /// <summary>
    /// One chunk per page, merging pages too short to stand alone and splitting pages too long
    /// to be returned whole. The index of a page in <paramref name="pages"/> is its page number
    /// minus one, so blank pages are skipped but still counted.
    /// </summary>
    public static IReadOnlyList<CompanionChunkDraft> Build(IReadOnlyList<string> pages)
    {
        var chunks = new List<CompanionChunkDraft>();
        var carried = new StringBuilder();
        var carriedFrom = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index].Trim();
            if (page.Length == 0) continue;

            var pageNumber = index + 1;

            if (carried.Length == 0) carriedFrom = pageNumber;
            if (carried.Length > 0) carried.AppendLine();
            carried.Append(page);

            if (carried.Length < MinChunkChars && index < pages.Count - 1) continue;

            Flush(chunks, carried.ToString(), carriedFrom);
            carried.Clear();
        }

        if (carried.Length > 0) Flush(chunks, carried.ToString(), carriedFrom);

        return chunks;
    }

    /// <summary>
    /// Version stamp derived from the extracted content (first 16 lowercase hex characters of
    /// the SHA-256 of the pages joined by a newline), so re-uploading the same file is a no-op
    /// while a genuinely revised handout supersedes its previous version.
    /// </summary>
    public static string ChecksumVersion(IReadOnlyList<string> pages)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", pages)));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static void Flush(List<CompanionChunkDraft> chunks, string text, int pageNumber)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        if (text.Length <= MaxChunkChars)
        {
            chunks.Add(new CompanionChunkDraft($"Page {pageNumber}", text, PageNumber: pageNumber));
            return;
        }

        // Split on line boundaries so a rule or worked example is not cut in
        // half, and fall back to a hard cut only for a single line with no
        // boundary to use.
        var part = 1;
        var builder = new StringBuilder();

        void Emit()
        {
            var body = builder.ToString().Trim();
            builder.Clear();
            if (body.Length == 0) return;

            chunks.Add(new CompanionChunkDraft($"Page {pageNumber} ({part})", body, PageNumber: pageNumber));
            part++;
        }

        foreach (var line in text.Split('\n'))
        {
            // A line longer than the whole cap has no boundary to split on, so it
            // is cut into pieces. Whatever is already buffered goes out first,
            // otherwise the pieces would appear before the text that preceded them.
            var remaining = line;
            while (remaining.Length > MaxChunkChars)
            {
                Emit();
                chunks.Add(new CompanionChunkDraft(
                    $"Page {pageNumber} ({part})", remaining[..MaxChunkChars], PageNumber: pageNumber));
                part++;
                remaining = remaining[MaxChunkChars..];
            }

            if (builder.Length > 0 && builder.Length + remaining.Length + 1 > MaxChunkChars) Emit();
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(remaining);
        }

        Emit();
    }
}
