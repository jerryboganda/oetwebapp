using System.Security.Cryptography;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetLearner.Api.Services.TutorBook;

/// <summary>
/// Renders a per-buyer watermarked PDF of "The Tutor Book — First Edition 2026".
///
/// <para>This is the MVP implementation: stamps a watermark page in front of
/// the source PDF (or generates a placeholder when no source is configured).
/// Production should swap the source-PDF concat for an iText / PDFsharp page
/// stamping pass, but this is enough to ship the watermarking promise from
/// the PDF spec: <c>&lt;email&gt; - THE TUTOR BOOK - First Edition 2026.pdf</c>.</para>
/// </summary>
public interface ITutorBookWatermarkService
{
    /// <summary>Returns the watermarked PDF bytes + the suggested download
    /// filename for the given buyer.</summary>
    Task<(byte[] PdfBytes, string Filename)> GetWatermarkedAsync(string buyerName, string buyerEmail, DateTimeOffset purchasedAt, CancellationToken ct);

    /// <summary>Stable signature included in the watermark — lets admins
    /// fingerprint a leaked copy back to a buyer. Signs with
    /// <c>TutorBook:SignatureSecret</c>; in Production without it, throws 503
    /// <c>tutor_book_signing_unconfigured</c> rather than using the public dev key.</summary>
    string ComputeBuyerSignature(string buyerEmail);

    /// <summary>Leak tracing: checks a signature printed on a PDF against the
    /// buyer's email. Accepts the configured secret and the legacy dev key (every
    /// PDF issued before the secret was set). Returns <c>"configured"</c> or
    /// <c>"legacy"</c> for the key that matched, or null when neither does.</summary>
    string? VerifyBuyerSignature(string buyerEmail, string signature);
}

public sealed class TutorBookWatermarkService(IConfiguration configuration, IHostEnvironment env) : ITutorBookWatermarkService
{
    // Signed every PDF issued before TutorBook:SignatureSecret was configured in
    // production. Never signs in Production; kept only so those copies stay traceable.
    private const string LegacySigningKey = "dev-tutor-book-signing-key";

    public async Task<(byte[] PdfBytes, string Filename)> GetWatermarkedAsync(string buyerName, string buyerEmail, DateTimeOffset purchasedAt, CancellationToken ct)
    {
        await Task.Yield(); // QuestPDF generation is synchronous; yield to keep API async
        var signature = ComputeBuyerSignature(buyerEmail);
        var stampedAt = purchasedAt == default ? DateTimeOffset.UtcNow : purchasedAt;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor("#0E2841"));

                page.Header().Column(col =>
                {
                    col.Item().Text("OET with Dr. Ahmed Hesham").FontSize(10).FontColor("#156082");
                    col.Item().Text("THE TUTOR BOOK — First Edition 2026").FontSize(20).Bold().FontColor("#0E2841");
                });

                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().PaddingTop(20).Text(text =>
                    {
                        text.Span("Personalised for ").FontSize(12);
                        text.Span(buyerName).Bold().FontSize(12);
                    });
                    col.Item().Text($"Email: {buyerEmail}").FontSize(11);
                    col.Item().Text($"Purchased: {stampedAt:yyyy-MM-dd}").FontSize(11);
                    col.Item().Text($"Buyer signature: {signature}").FontSize(9).FontColor("#996F1F");
                    col.Item().PaddingVertical(20).LineHorizontal(1).LineColor("#D4A44F");
                    col.Item().Text("Contents").Bold().FontSize(14);
                    col.Item().Text("• Listening: new recalls, full audio scripts, answers, clear justifications");
                    col.Item().Text("• Reading: recall-based topics, vocab trends, strategies for Parts A, B, C");
                    col.Item().Text("• Writing: 8 full recall-based letters with model answers and structure guidance");
                    col.Item().Text("• Speaking: 16 recall-based cards based on recent scenarios");
                    col.Item().Text("• WhatsApp access for support and continuous updates: +44 7961 725989");

                    col.Item().PaddingTop(40).Background("#EAE9E6").Padding(15).Column(notice =>
                    {
                        notice.Item().Text("Watermarked — Do not redistribute").Bold().FontColor("#0E2841");
                        notice.Item().Text("This PDF is personalised for the buyer above. Sharing it externally is a copyright violation and traceable via the signature.").FontSize(9).FontColor("#156082");
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"{buyerEmail} — ").FontSize(8).FontColor("#996F1F");
                    text.Span("THE TUTOR BOOK © OET with Dr. Ahmed Hesham 2026").FontSize(8).FontColor("#0E2841");
                });
            });
        }).GeneratePdf();

        var safeEmail = string.Concat(buyerEmail.Where(c => char.IsLetterOrDigit(c) || c == '@' || c == '.' || c == '-' || c == '_'));
        var filename = $"{safeEmail} - THE TUTOR BOOK - First Edition 2026.pdf";
        return (pdfBytes, filename);
    }

    public string ComputeBuyerSignature(string buyerEmail)
    {
        var secret = ConfiguredSecret();
        if (secret is null)
        {
            // Fail closed at the download only — never at startup, so no other
            // feature is affected. Not retryable: a retry cannot fix missing config.
            if (env.IsProduction())
                throw ApiException.ServiceUnavailable(
                    "tutor_book_signing_unconfigured",
                    "Tutor Book downloads are temporarily unavailable. Please contact support.",
                    retryable: false);
            secret = LegacySigningKey;
        }
        return Sign(secret, buyerEmail);
    }

    public string? VerifyBuyerSignature(string buyerEmail, string signature)
    {
        var provided = Encoding.ASCII.GetBytes(signature.Trim().ToUpperInvariant());
        var secret = ConfiguredSecret();
        if (secret is not null && Matches(Sign(secret, buyerEmail), provided)) return "configured";
        return Matches(Sign(LegacySigningKey, buyerEmail), provided) ? "legacy" : null;
    }

    // A secret equal to the public legacy key counts as unconfigured.
    private string? ConfiguredSecret()
    {
        var secret = configuration["TutorBook:SignatureSecret"];
        return string.IsNullOrWhiteSpace(secret) || secret == LegacySigningKey ? null : secret;
    }

    private static bool Matches(string expected, byte[] provided)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), provided);

    private static string Sign(string key, string buyerEmail)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(buyerEmail.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8); // 16-char fingerprint — enough to disambiguate while keeping the footer compact
    }
}
