using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;

namespace Fleet.Agent;

/// <summary>Engine version strings offered at claim (protocol 6.0, 6.1.4 item 3).</summary>
internal static class EngineVersions
{
    /// <summary>
    /// pdfpig:&lt;PdfPig assembly informational version without build metadata&gt;/oet-text:&lt;LayoutRevision&gt;. Taken from the
    /// link-compiled <see cref="PdfTextEngine"/> itself, so the agent and the API cannot derive different strings
    /// (RemoteJobKinds.EngineVersion uses the same property).
    /// </summary>
    public static string Pdf { get; } = PdfTextEngine.EngineVersion;

    /// <summary>
    /// Chunker revision of companion.index-prep; part of its engineVersion and of the job's chunkerVersion param. The link-compiled
    /// CompanionChunker's own constant, so a chunker revision bump in the API follows into the next agent build.
    /// </summary>
    public const string CompanionChunkerVersion = OetLearner.Api.Services.Companion.CompanionChunker.Version;

    public static string CompanionIndexPrep => Pdf + "/" + CompanionChunkerVersion;

    /// <summary>engineVersion for the ffmpeg-backed kinds: ffmpeg:&lt;parsed version&gt;/&lt;procedure&gt;. Null when the version is unknown.</summary>
    public static string? Media(string? ffmpegVersion, string procedure) =>
        string.IsNullOrEmpty(ffmpegVersion) ? null : "ffmpeg:" + ffmpegVersion + "/" + procedure;
}

internal sealed record PdfExtractRequest(string Mode, int MinTextLength, bool IncludePages, string EngineVersion, string InputSha256);

/// <summary>The extracted text cannot be carried in a valid UTF-8 JSON result (an unpaired surrogate) or exceeds protocol bounds.</summary>
internal sealed class UnrepresentableResultException : Exception
{
    public UnrepresentableResultException(string detail)
        : base(detail)
    {
    }
}

internal sealed record PdfExtraction(
    bool HasMagic,
    IReadOnlyList<string> Pages,
    string Flat,
    int EmbeddedChars,
    bool NeedsOcr,
    string? NeedsOcrReason,
    string? TextSha256,
    string? PagesSha256,
    IReadOnlyList<string> PageSha256s);

/// <summary>
/// The pure core of pdf.extract (protocol 6.1): PdfPig tier ONLY, output byte-identical to the in-process
/// PdfPigPdfTextExtractor because that very source file is link-compiled into this assembly. OCR never runs here: below the
/// threshold the job answers needsOcr and the API's local path takes over (D7).
/// </summary>
internal static class PdfExtractCore
{
    public const int MaxPages = 5000;
    public const int MaxPageChars = 1_000_000;
    public const int MaxTotalChars = 6_000_000;

    private static readonly UTF8Encoding Strict = new(false, true);

    public static bool HasPdfMagic(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-';

    /// <summary>Runs the linked extractor synchronously on the calling thread (no thread-pool hop: the stream is in memory).</summary>
    public static (bool Magic, IReadOnlyList<string> Pages) ExtractPages(byte[] pdf, CancellationToken ct)
    {
        var magic = HasPdfMagic(pdf);
        IReadOnlyList<string> pages = [];
        if (magic)
        {
            var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
            using var stream = new MemoryStream(pdf, writable: false);
            pages = extractor.ExtractPagesAsync(stream, ct).GetAwaiter().GetResult();
        }

        return (magic, pages);
    }

    public static PdfExtraction Extract(byte[] pdf, string mode, int minTextLength, CancellationToken ct)
    {
        var (magic, pages) = ExtractPages(pdf, ct);
        return Evaluate(magic, pages, mode, minTextLength);
    }

    /// <summary>Applies the definitions of section 6.1.3 to an extractor output.</summary>
    public static PdfExtraction Evaluate(bool magic, IReadOnlyList<string> pages, string mode, int minTextLength)
    {
        var flat = string.Join("\n\n", pages).Trim();
        var embedded = mode == "pages" ? pages.Sum(p => p.Length) : flat.Length;
        var needsOcr = !magic || embedded < minTextLength;
        string? reason = !magic ? "not_pdf" : pages.Count == 0 ? "no_text_layer" : needsOcr ? "below_min_text" : null;
        if (needsOcr)
        {
            return new PdfExtraction(magic, pages, flat, embedded, true, reason, null, null, []);
        }

        if (pages.Count > MaxPages) throw new UnrepresentableResultException("page count exceeds the protocol bound");
        long total = 0;
        foreach (var page in pages)
        {
            if (page.Length > MaxPageChars) throw new UnrepresentableResultException("page text exceeds the protocol bound");
            total += page.Length;
        }

        if (total > MaxTotalChars) throw new UnrepresentableResultException("total text exceeds the protocol bound");

        string textSha;
        var pageShas = new List<string>(pages.Count);
        try
        {
            textSha = Hashing.Sha256Hex(Strict.GetBytes(flat));
            foreach (var page in pages) pageShas.Add(Hashing.Sha256Hex(Strict.GetBytes(page)));
        }
        catch (EncoderFallbackException)
        {
            throw new UnrepresentableResultException("text contains an unpaired surrogate");
        }

        var pagesSha = Hashing.Sha256Hex(string.Join("\n", pageShas));
        return new PdfExtraction(magic, pages, flat, embedded, false, null, textSha, pagesSha, pageShas);
    }

    /// <summary>Serialises the result object (schema pdf.extract.result/1) with the property order of the spec.</summary>
    public static byte[] Serialize(PdfExtraction extraction, PdfExtractRequest request, int pdfBytes, long durationMs, long peakRssMiB)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "pdf.extract.result/1");
            writer.WriteString("engineVersion", request.EngineVersion);
            writer.WriteString("inputSha256", request.InputSha256);
            writer.WriteString("mode", request.Mode);
            writer.WriteBoolean("needsOcr", extraction.NeedsOcr);
            WriteNullable(writer, "needsOcrReason", extraction.NeedsOcrReason);
            writer.WriteNumber("pageCount", extraction.Pages.Count);
            writer.WriteNumber("embeddedChars", extraction.EmbeddedChars);
            WriteNullable(writer, "textSha256", extraction.TextSha256);
            WriteNullable(writer, "pagesSha256", extraction.PagesSha256);
            writer.WriteStartArray("pageSha256s");
            foreach (var hash in extraction.PageSha256s) writer.WriteStringValue(hash);
            writer.WriteEndArray();
            if (!extraction.NeedsOcr && request.IncludePages)
            {
                writer.WriteStartArray("pages");
                foreach (var page in extraction.Pages) writer.WriteStringValue(page);
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteNull("pages");
            }

            writer.WriteStartObject("stats");
            writer.WriteNumber("pdfBytes", pdfBytes);
            writer.WriteNumber("durationMs", durationMs);
            writer.WriteNumber("peakRssMiB", peakRssMiB);
            writer.WriteBoolean("sha256Verified", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    internal static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }
}
