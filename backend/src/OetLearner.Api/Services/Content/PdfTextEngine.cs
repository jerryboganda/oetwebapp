using System.Reflection;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Identity of the PDF text engine (OET-RWP/1 section 6.1.4).
///
/// <para>
/// This file is deliberately dependency-free (System + UglyToad.PdfPig only) so the
/// fleet agent can link-compile it next to <c>PdfPigPdfTextExtractor.cs</c>. Both
/// processes then derive the same <see cref="EngineVersion"/> string, which is part of
/// every remote job's idempotency key and result echo.
/// </para>
///
/// <para>
/// <b>Bump <see cref="LayoutRevision"/> by hand whenever the logic of
/// <c>PdfPigPdfTextExtractor</c> changes</b> (line grouping, watermark filter, page
/// joining ...). A golden-hash test fails when the extractor source changes without a
/// bump, so output can never drift silently between the API (oracle) and a helper. A
/// bump changes <see cref="EngineVersion"/>, which re-keys every remote job.
/// </para>
/// </summary>
public static class PdfTextEngine
{
    /// <summary>Human-maintained revision of the extractor's behaviour.</summary>
    public const int LayoutRevision = 1;

    /// <summary>PdfPig assembly informational version without any <c>+</c> build metadata.</summary>
    public static string PdfPigVersion { get; } = ResolvePdfPigVersion();

    /// <summary><c>pdfpig:&lt;version&gt;/oet-text:&lt;LayoutRevision&gt;</c>.</summary>
    public static string EngineVersion { get; } = "pdfpig:" + PdfPigVersion + "/oet-text:" + LayoutRevision;

    private static string ResolvePdfPigVersion()
    {
        var assembly = typeof(UglyToad.PdfPig.PdfDocument).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return (plus >= 0 ? informational[..plus] : informational).Trim();
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
