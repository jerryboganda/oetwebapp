// Shim for the link-compiled PdfPigPdfTextExtractor (protocol 6.1.4 item 1). The real interface lives beside the
// EF-bound ContentTextExtractionService in the API; the agent must not reference that file, so the two members the
// extractor implements are declared here with the SAME signatures and the SAME default ExtractPagesAsync.
namespace OetLearner.Api.Services.Content;

public interface IPdfTextExtractor
{
    Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct);

    async Task<IReadOnlyList<string>> ExtractPagesAsync(Stream pdfStream, CancellationToken ct)
    {
        var text = await ExtractAsync(pdfStream, ct);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }
}
