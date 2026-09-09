using System.Text;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// PdfPig-backed implementation of <see cref="IPdfTextExtractor"/>.
///
/// Text is reconstructed from PdfPig's WORD boxes rather than <c>page.Text</c>.
/// <c>page.Text</c> concatenates glyphs in content-stream order with no word
/// spacing and no line breaks, which produces a long unusable run: a Listening
/// question paper came out at ~25 000 characters from which not one printed
/// question could be read back. Grouping words into lines by their vertical
/// position and ordering them left-to-right restores the printed layout, which
/// is what every downstream consumer (Part B/C stem recovery, AI grounding)
/// actually needs.
///
/// Returns an empty string on any failure — a corrupted PDF, or a scanned /
/// image-only one with no text layer — so callers can fall back to OCR or skip
/// the asset, matching the no-op implementation's semantics.
/// </summary>
public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigPdfTextExtractor> _logger;

    public PdfPigPdfTextExtractor(ILogger<PdfPigPdfTextExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Two words belong to the same printed line when their baselines sit within
    /// this fraction of the line's own height. Generous enough for the slight
    /// baseline drift in a scanned-then-OCR'd page, tight enough not to merge
    /// adjacent lines.
    /// </summary>
    private const double SameLineTolerance = 0.6;

    /// <summary>Kept as two newlines, the separator callers already parse against.</summary>
    private const string PageSeparator = "\n\n";

    public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
    {
        var pages = await ExtractPagesAsync(pdfStream, ct);
        return string.Join(PageSeparator, pages).Trim();
    }

    public async Task<IReadOnlyList<string>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        try
        {
            // PdfPig requires a seekable stream; always copy to MemoryStream for safety.
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var doc = UglyToad.PdfPig.PdfDocument.Open(ms);
            var pages = new List<string>();

            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                // Empty pages are kept, not dropped: the index of a page in this
                // list IS its page number, so skipping a blank cover page would
                // shift every citation in the document by one.
                pages.Add(ExtractPage(page));
            }

            // A PDF with no text layer yields only stray marks; let the caller
            // fall through to OCR rather than caching noise.
            return pages.All(string.IsNullOrWhiteSpace) ? [] : pages;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PdfPig extraction failed; returning no pages.");
            return [];
        }
    }

    private static string ExtractPage(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToList();

        // Some producers emit no word boxes at all; fall back to the raw run so
        // the asset is not treated as textless.
        if (words.Count == 0) return page.Text ?? string.Empty;

        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var height = Math.Max(word.BoundingBox.Height, 1);
            var current = lines.Count > 0 ? lines[^1] : null;
            if (current is not null
                && Math.Abs(current[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= height * SameLineTolerance)
            {
                current.Add(word);
                continue;
            }
            lines.Add([word]);
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(string.Join(' ', line
                .OrderBy(word => word.BoundingBox.Left)
                .Select(word => word.Text)));
        }
        return builder.ToString();
    }
}
