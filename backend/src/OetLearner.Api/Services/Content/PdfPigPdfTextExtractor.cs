using System.Text;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// PdfPig-backed implementation of <see cref="IPdfTextExtractor"/>. Concatenates
/// per-page text separated by blank lines. Returns empty string on any failure
/// (e.g. corrupted PDF, scanned/image-only PDF) so callers can gracefully fall
/// back to OCR or skip the asset — matches the no-op semantics.
/// </summary>
public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigPdfTextExtractor> _logger;

    public PdfPigPdfTextExtractor(ILogger<PdfPigPdfTextExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
    {
        try
        {
            // PdfPig requires a seekable stream; always copy to MemoryStream for safety.
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var doc = UglyToad.PdfPig.PdfDocument.Open(ms);
            var sb = new StringBuilder();
            bool first = true;
            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                if (!first) sb.Append("\n\n");
                sb.Append(page.Text);
                first = false;
            }
            return sb.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PdfPig extraction failed; returning empty string.");
            return string.Empty;
        }
    }
}
