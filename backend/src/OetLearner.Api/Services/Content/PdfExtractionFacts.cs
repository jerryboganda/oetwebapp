namespace OetLearner.Api.Services.Content;

/// <summary>
/// Per-call facts about one PDF extraction, carried for the structured
/// <c>pdf.extract.done</c> log line.
///
/// <para>
/// <see cref="IPdfTextExtractor"/> returns only text, and its output is a
/// contract that downstream parsers are tuned to, so the facts travel out of
/// band: <see cref="ContentTextExtractionService"/> opens a scope with
/// <see cref="Begin"/> around each asset and the PdfPig tier reports into it.
/// The holder is an object reachable through an <see cref="AsyncLocal{T}"/>, so
/// it flows down into the (awaited) extractor and its mutations are visible to
/// the caller afterwards. With no scope open <see cref="RecordEmbedded"/> is a
/// no-op, so extractors used outside the service pay nothing.
/// </para>
/// </summary>
public sealed class PdfExtractionFacts
{
    private static readonly AsyncLocal<PdfExtractionFacts?> Ambient = new();

    /// <summary>Pages of the PDF text layer. 0 when unknown, which includes a
    /// PDF with no text layer (PdfPig reports no pages for one).</summary>
    public int Pages { get; private set; }

    /// <summary>Characters the PDF's own text layer produced (the string the
    /// PdfPig tier returned). 0 when there was none or PdfPig did not run.</summary>
    public int EmbeddedChars { get; private set; }

    internal static PdfExtractionFacts Begin()
    {
        var facts = new PdfExtractionFacts();
        Ambient.Value = facts;
        return facts;
    }

    internal static void End() => Ambient.Value = null;

    internal static void RecordEmbedded(int pages, int chars)
    {
        if (Ambient.Value is { } facts)
        {
            facts.Pages = pages;
            facts.EmbeddedChars = chars;
        }
    }

    /// <summary>
    /// Coarse tier label for logs, inferred from what the call produced:
    /// <c>none</c> (empty), <c>embedded</c> (the result is exactly the PDF's
    /// text layer) or <c>ocr</c> (anything else, i.e. an OCR tier answered).
    /// </summary>
    internal static string DescribeTier(int resultChars, int embeddedChars)
        => resultChars == 0
            ? "none"
            : embeddedChars > 0 && resultChars == embeddedChars ? "embedded" : "ocr";
}
