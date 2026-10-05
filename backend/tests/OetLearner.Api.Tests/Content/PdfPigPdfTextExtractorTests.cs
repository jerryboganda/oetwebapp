using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;
using UglyToad.PdfPig.Writer;

namespace OetLearner.Api.Tests.Content;

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
    public async Task Drops_giant_watermark_glyphs_that_share_a_question_line()
    {
        // Reproduces the production defect: a 118pt watermark letter sitting on the
        // same baseline band as a 10pt Part C stem used to be merged into it as a
        // lone "S" ("...role of cholesterol S largely arise...").
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText("31. Dr Everall thinks misunderstandings about cholesterol largely arise", 10, new UglyToad.PdfPig.Core.PdfPoint(40, 700), font);
        page.AddText("A a lack of focus on its positive influences", 10, new UglyToad.PdfPig.Core.PdfPoint(55, 685), font);
        page.AddText("Part C", 20, new UglyToad.PdfPig.Core.PdfPoint(40, 760), font);
        page.AddText("S", 118, new UglyToad.PdfPig.Core.PdfPoint(300, 690), font);
        page.AddText("E", 118, new UglyToad.PdfPig.Core.PdfPoint(420, 640), font);

        // A "BLANK" page whose only text is the watermark itself.
        var blank = builder.AddPage(595, 842);
        blank.AddText("B", 118, new UglyToad.PdfPig.Core.PdfPoint(150, 400), font);
        blank.AddText("L", 118, new UglyToad.PdfPig.Core.PdfPoint(250, 470), font);

        using var stream = new MemoryStream(builder.Build());
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        var pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);

        Assert.Equal(2, pages.Count);
        Assert.Contains("31. Dr Everall thinks misunderstandings about cholesterol largely arise", pages[0]);
        Assert.Contains("A a lack of focus on its positive influences", pages[0]);
        Assert.Contains("Part C", pages[0]);
        Assert.DoesNotMatch(@"(^|\s)[SE](\s|$)", pages[0]);
        Assert.Equal(string.Empty, pages[1]);
    }

    [Fact]
    public async Task Reports_page_and_character_counts_for_the_extraction_log_without_changing_the_text()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        builder.AddPage(595, 842).AddText("First page text", 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        builder.AddPage(595, 842).AddText("Second page text", 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        var bytes = builder.Build();
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);

        string withFacts;
        var facts = PdfExtractionFacts.Begin();
        try
        {
            using var stream = new MemoryStream(bytes);
            withFacts = await extractor.ExtractAsync(stream, CancellationToken.None);
        }
        finally
        {
            PdfExtractionFacts.End();
        }

        using var plain = new MemoryStream(bytes);
        var withoutFacts = await extractor.ExtractAsync(plain, CancellationToken.None);

        Assert.Equal(withoutFacts, withFacts);
        Assert.Equal(2, facts.Pages);
        Assert.Equal(withFacts.Length, facts.EmbeddedChars);
        Assert.Equal("embedded", PdfExtractionFacts.DescribeTier(withFacts.Length, facts.EmbeddedChars));
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
