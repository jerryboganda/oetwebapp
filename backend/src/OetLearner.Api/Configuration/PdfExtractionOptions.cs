namespace OetLearner.Api.Configuration;

/// <summary>
/// Configuration for the <see cref="OetLearner.Api.Services.Content.IPdfTextExtractor"/>
/// pipeline. <see cref="Provider"/> selects the engine; "auto" runs PdfPig first
/// and falls back to Azure Document Intelligence (OCR) when the embedded text
/// is below <see cref="MinTextLengthForSuccess"/> AND Azure is configured.
/// </summary>
public sealed class PdfExtractionOptions
{
    public const string SectionName = "PdfExtraction";

    /// <summary>noop | pdfpig | azure | auto (default: auto = pdfpig with azure fallback when configured)</summary>
    public string Provider { get; set; } = "auto";

    /// <summary>Azure DocIntel endpoint, e.g. https://{name}.cognitiveservices.azure.com/. Empty = disabled.</summary>
    public string AzureEndpoint { get; set; } = string.Empty;

    /// <summary>Azure DocIntel key. Empty = disabled. NEVER log this.</summary>
    public string AzureApiKey { get; set; } = string.Empty;

    /// <summary>Min chars from PdfPig before declaring success. Below this we retry with Azure if configured.</summary>
    public int MinTextLengthForSuccess { get; set; } = 50;
}
