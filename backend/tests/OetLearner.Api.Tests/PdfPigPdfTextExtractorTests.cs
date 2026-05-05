using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;
using UglyToad.PdfPig.Writer;

namespace OetLearner.Api.Tests;

public class PdfPigPdfTextExtractorTests
{
    [Fact]
    public async Task Extracts_text_from_simple_pdf()
    {
        // Build a 1-page PDF in-memory using PdfPig's writer with a Standard 14 font.
        // Use explicit A4 dimensions (points) — the PageSize enum lives in different
        // namespaces across PdfPig major versions, so passing width/height directly
        // is the version-stable form.
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.TimesRoman);
        page.AddText("Hello PdfPig Extractor", 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        var bytes = builder.Build();

        using var stream = new MemoryStream(bytes);
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        var text = await extractor.ExtractAsync(stream, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Hello PdfPig Extractor", text);
    }

    [Fact]
    public async Task Returns_empty_on_corrupted_pdf()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        using var stream = new MemoryStream(bytes);
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);

        var text = await extractor.ExtractAsync(stream, CancellationToken.None);

        Assert.Equal(string.Empty, text);
    }
}
