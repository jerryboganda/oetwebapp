namespace OetLearner.Api.Services.Content;

/// <summary>
/// Shim of the API's <c>IPdfTextExtractor</c> (backend/src/OetLearner.Api/Services/Content/
/// ContentTextExtractionService.cs). The real declaration sits in a file that also holds EF-backed
/// services, so it cannot be link-compiled; the two members and the default
/// <see cref="ExtractPagesAsync"/> are copied exactly so <c>PdfPigPdfTextExtractor</c> compiles
/// unchanged. If the API's interface changes, this shim must change with it (the benchmark would stop
/// compiling or drift, and the parity step would flag it).
/// </summary>
public interface IPdfTextExtractor
{
    Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct);

    async Task<IReadOnlyList<string>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        var text = await ExtractAsync(pdfStream, ct);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }
}
