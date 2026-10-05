using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fleet.Agent;

internal sealed record ChunkRow(string Heading, int PageNumber, string Text);

/// <summary>
/// The only place the agent touches the API's CompanionChunker, which is link-compiled (never copied) once the API track
/// has extracted it into its own pure file. Without that source, companion.index-prep is simply not offered.
/// </summary>
internal static class CompanionChunkAdapter
{
    public static bool Available =>
#if FLEET_HAS_COMPANION_CHUNKER
        true;
#else
        false;
#endif

    public static IReadOnlyList<ChunkRow> Build(IReadOnlyList<string> pages)
    {
#if FLEET_HAS_COMPANION_CHUNKER
        var rows = new List<ChunkRow>();
        foreach (var draft in OetLearner.Api.Services.Companion.CompanionChunker.Build(pages))
        {
            rows.Add(new ChunkRow(draft.Heading, Convert.ToInt32(draft.PageNumber), draft.Text));
        }

        return rows;
#else
        throw new NotSupportedException("the CompanionChunker source is not linked into this build");
#endif
    }
}

/// <summary>companion.index-prep v1 (protocol 6.2): extraction by the PdfPig tier plus chunking. Embeddings stay on the API.</summary>
internal static class CompanionPrepCore
{
    public const int MaxChunks = 5000;
    public const int MaxChunkChars = 1100;

    private static readonly Regex HeadingPattern = new(@"^Page \d+( \(\d+\))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Strict = new(false, true);

    /// <summary>First 16 lowercase hex characters of SHA-256 over the UTF-8 pages joined by LF (identical to ChecksumVersion).</summary>
    public static string VersionOf(IReadOnlyList<string> pages) => Hashing.Sha256Hex(Strict.GetBytes(string.Join("\n", pages)))[..16];

    public static string ChunksSha256(IReadOnlyList<ChunkRow> chunks)
    {
        var lines = chunks.Select(c => c.Heading + "|" + c.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + Hashing.Sha256Hex(Strict.GetBytes(c.Text)));
        return Hashing.Sha256Hex(Strict.GetBytes(string.Join("\n", lines)));
    }

    public static byte[] Build(byte[] pdf, int minTextLength, string engineVersion, string inputSha256, CancellationToken ct)
    {
        var (magic, pages) = PdfExtractCore.ExtractPages(pdf, ct);
        return BuildFromPages(magic, pages, minTextLength, engineVersion, inputSha256, chunker: CompanionChunkAdapter.Build);
    }

    /// <summary>Separated from <see cref="Build"/> so tests can supply a chunker without the linked source.</summary>
    public static byte[] BuildFromPages(bool magic, IReadOnlyList<string> pages, int minTextLength, string engineVersion, string inputSha256,
        Func<IReadOnlyList<string>, IReadOnlyList<ChunkRow>> chunker)
    {
        var embedded = pages.Sum(p => p.Length);
        var needsOcr = !magic || embedded < minTextLength;
        string? reason = !magic ? "not_pdf" : pages.Count == 0 ? "no_text_layer" : needsOcr ? "below_min_text" : null;

        IReadOnlyList<ChunkRow> chunks = [];
        string? version = null;
        string? chunksSha = null;
        if (!needsOcr)
        {
            try
            {
                version = VersionOf(pages);
                chunks = chunker(pages);
                Validate(chunks, pages.Count);
                chunksSha = ChunksSha256(chunks);
            }
            catch (EncoderFallbackException)
            {
                throw new UnrepresentableResultException("text contains an unpaired surrogate");
            }
        }

        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "companion.index-prep.result/1");
            writer.WriteString("engineVersion", engineVersion);
            writer.WriteString("inputSha256", inputSha256);
            writer.WriteBoolean("needsOcr", needsOcr);
            PdfExtractCore.WriteNullable(writer, "needsOcrReason", reason);
            writer.WriteNumber("pageCount", pages.Count);
            writer.WriteNumber("embeddedChars", embedded);
            PdfExtractCore.WriteNullable(writer, "version", version);
            writer.WriteNumber("chunkCount", chunks.Count);
            writer.WriteStartArray("chunks");
            foreach (var chunk in chunks)
            {
                writer.WriteStartObject();
                writer.WriteString("heading", chunk.Heading);
                writer.WriteNumber("pageNumber", chunk.PageNumber);
                writer.WriteString("text", chunk.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            PdfExtractCore.WriteNullable(writer, "chunksSha256", chunksSha);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The API's validation rules of section 6.2, applied before sending so an honest node is never struck.</summary>
    public static void Validate(IReadOnlyList<ChunkRow> chunks, int pageCount)
    {
        if (chunks.Count > MaxChunks) throw new UnrepresentableResultException("chunk count exceeds the protocol bound");
        foreach (var chunk in chunks)
        {
            if (chunk.Text.Length is < 1 or > MaxChunkChars) throw new UnrepresentableResultException("chunk text length is out of bounds");
            if (!HeadingPattern.IsMatch(chunk.Heading)) throw new UnrepresentableResultException("chunk heading has an unexpected shape");
            if (chunk.PageNumber < 1 || chunk.PageNumber > pageCount) throw new UnrepresentableResultException("chunk page number is out of range");
        }
    }
}
