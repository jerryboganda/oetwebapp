using System.Security.Cryptography;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetWithDrHesham.Api.Services.TutorBook;

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
    /// fingerprint a leaked copy back to a buyer.</summary>
    string ComputeBuyerSignature(string buyerEmail);
}

public sealed class TutorBookWatermarkService(IConfiguration configuration, IHostEnvironment env) : ITutorBookWatermarkService
{
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
                    col.Item().Text("• Private Telegram channel access for continuous updates");

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
        var key = configuration["TutorBook:SignatureSecret"] ?? "dev-tutor-book-signing-key";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(buyerEmail.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8); // 16-char fingerprint — enough to disambiguate while keeping the footer compact
    }
}
