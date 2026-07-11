using System.Globalization;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetLearner.Api.Services;

/// <summary>
/// Immutable data needed to render a billing invoice as a PDF. Carries only what
/// the document shows — no DB entities — so the renderer stays pure and testable.
/// </summary>
public sealed record InvoicePdfModel(
    string InvoiceId,
    int? Number,
    DateTimeOffset IssuedAt,
    decimal Amount,
    string Currency,
    string Status,
    string Description,
    string BillToName,
    string? BillToEmail);

/// <summary>Generated PDF artefact for a billing invoice — never persisted; streamed to the caller.</summary>
public sealed record InvoicePdfArtifact(byte[] Bytes, string Filename);

public interface IInvoicePdfService
{
    InvoicePdfArtifact Generate(InvoicePdfModel model);
}

/// <summary>
/// Renders the branded invoice/receipt PDF for OET with Dr Ahmed Hesham (The Tutor
/// Academy). Purple brand identity: logo header, PAID pill, Qty/Price/Total line-item
/// table, and a full-bleed footer wave. Pure managed output (QuestPDF, Community
/// licence) — no native dependencies, no persistence.
/// </summary>
public sealed partial class InvoicePdfService : IInvoicePdfService
{
    // Brand palette (matches the web app's violet identity).
    private const string BrandPurple = "#7C3AED";
    private const string BrandPurpleDeep = "#5B21B6";
    private const string LavenderBg = "#F3EFFD";
    private const string LavenderBorder = "#E2DAF8";
    private const string InkDark = "#1F2937";
    private const string InkGrey = "#6B7280";
    private const string PaidGreen = "#15803D";

    private const string PlatformDomain = "oetwithdrhesham.co.uk";

    static InvoicePdfService()
    {
        // QuestPDF Community licence is free for organisations under USD 1M ARR.
        // Accepted explicitly so the library does not throw on first use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // Header logo, embedded in the assembly so the API container never depends on
    // the web app's public/ folder. Null-tolerant: a missing resource falls back
    // to a text wordmark instead of failing invoice downloads.
    private static readonly Lazy<byte[]?> LogoPng = new(() =>
    {
        try
        {
            var assembly = typeof(InvoicePdfService).Assembly;
            // LogicalName separators vary by build host (see RulebookLoader) —
            // resolve by filename suffix instead of an exact manifest name.
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("oet-with-dr-hesham-logo.png", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return null;
            }
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return null;
            }
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    });

    public InvoicePdfArtifact Generate(InvoicePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issuedText = model.IssuedAt.UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var reference = model.Number is { } n ? $"INV-{n:00000}" : model.InvoiceId;
        var isPaid = string.Equals(model.Status?.Trim(), "Paid", StringComparison.OrdinalIgnoreCase);
        var description = string.IsNullOrWhiteSpace(model.Description) ? "OET subscription" : model.Description;
        var (itemName, quantity) = ParseLineItem(description);
        var unitPrice = quantity > 1 ? model.Amount / quantity : model.Amount;
        var currencyCode = NormalizeCurrency(model.Currency);

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(46);
                page.MarginTop(42);
                // Bottom margin clears the full-bleed wave painted on the page background.
                page.MarginBottom(116);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Lato").FontColor(InkDark));
                page.Background().Svg(FooterWaveSvg);

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(brand =>
                        {
                            if (LogoPng.Value is { } logo)
                            {
                                brand.Item().Height(68).AlignLeft().AlignMiddle().Image(logo).FitHeight();
                            }
                            else
                            {
                                brand.Item().Text("OET").FontSize(26).Black().FontColor(BrandPurple);
                                brand.Item().Text("with DR AHMED HESHAM").FontSize(10).SemiBold().FontColor(BrandPurpleDeep);
                            }
                            brand.Item().PaddingTop(2).Text(PlatformDomain).FontSize(10.5f).SemiBold().FontColor(BrandPurple);
                        });

                        row.ConstantItem(200).Column(meta =>
                        {
                            meta.Item().AlignRight().Text("INVOICE").FontSize(25).Black().FontColor(BrandPurple);
                            meta.Item().PaddingTop(4).PaddingBottom(8).LineHorizontal(1.4f).LineColor(BrandPurple);
                            meta.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Invoice No:").FontSize(10).FontColor(InkGrey);
                                r.AutoItem().Text(reference).FontSize(10).Bold();
                            });
                            meta.Item().PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem().Text("Issue Date:").FontSize(10).FontColor(InkGrey);
                                r.AutoItem().Text(issuedText).FontSize(10).Bold();
                            });
                            meta.Item().PaddingTop(9).AlignRight().Element(e => StatusPill(e, model.Status, isPaid));
                        });
                    });

                    col.Item().PaddingTop(14).LineHorizontal(0.8f).LineColor(LavenderBorder);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Spacing(24);

                    // BILLED TO — avatar badge + learner identity.
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(58).Element(e => e.Width(46).Height(46).Svg(AvatarSvg));
                        row.RelativeItem().AlignMiddle().Column(billTo =>
                        {
                            billTo.Item().Text("BILLED TO").FontSize(9.5f).Bold().FontColor(BrandPurple).LetterSpacing(0.08f);
                            billTo.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(model.BillToName) ? "Learner" : model.BillToName)
                                .FontSize(17).Bold().FontColor(InkDark);
                            if (!string.IsNullOrWhiteSpace(model.BillToEmail))
                            {
                                billTo.Item().PaddingTop(1).Text(model.BillToEmail!).FontSize(10.5f).FontColor(InkGrey);
                            }
                        });
                    });

                    // Line-item table: Description | Qty | Price | Total.
                    col.Item().Border(1).BorderColor(LavenderBorder).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.ConstantColumn(58);
                            columns.ConstantColumn(84);
                            columns.ConstantColumn(84);
                        });

                        table.Header(header =>
                        {
                            IContainer HeadCell(IContainer c) => c.Background(LavenderBg).PaddingVertical(9).PaddingHorizontal(12);
                            header.Cell().Element(HeadCell).Text("Description").FontSize(10.5f).Bold().FontColor(BrandPurple);
                            header.Cell().Element(HeadCell).AlignCenter().Text("Qty").FontSize(10.5f).Bold().FontColor(BrandPurple);
                            header.Cell().Element(HeadCell).AlignRight().Text("Price").FontSize(10.5f).Bold().FontColor(BrandPurple);
                            header.Cell().Element(HeadCell).AlignRight().Text("Total").FontSize(10.5f).Bold().FontColor(BrandPurple);
                        });

                        IContainer BodyCell(IContainer c) => c.BorderTop(1).BorderColor(LavenderBorder).PaddingVertical(11).PaddingHorizontal(12);
                        table.Cell().Element(BodyCell).Text(itemName).FontSize(11);
                        table.Cell().Element(BodyCell).AlignCenter().Text(quantity.ToString(CultureInfo.InvariantCulture)).FontSize(11);
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(unitPrice, currencyCode, includeCode: false)).FontSize(11);
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(model.Amount, currencyCode, includeCode: false)).FontSize(11);
                    });

                    // Totals block — mirrors the brand card: rule, label, big purple amount.
                    col.Item().AlignRight().Width(230).Column(totals =>
                    {
                        totals.Item().LineHorizontal(1.2f).LineColor(BrandPurple);
                        totals.Item().PaddingTop(12).Row(r =>
                        {
                            r.RelativeItem().AlignMiddle().Text(isPaid ? "Total Paid:" : "Total Due:").FontSize(11.5f).FontColor(InkDark);
                            r.AutoItem().Text(t =>
                            {
                                t.Span(FormatMoney(model.Amount, currencyCode, includeCode: false)).FontSize(21).Black().FontColor(BrandPurpleDeep);
                                t.Span(" " + currencyCode).FontSize(11).Bold().FontColor(BrandPurpleDeep);
                            });
                        });
                    });
                });

                page.Footer().Column(col =>
                {
                    col.Item().AlignCenter().Width(34).Height(34).Svg(HeartSvg);
                    col.Item().PaddingTop(8).AlignCenter().Text(t =>
                    {
                        t.Span("Thank you for purchasing at ").FontSize(10.5f).Bold().FontColor(InkDark);
                        t.Span(PlatformDomain).FontSize(10.5f).Bold().FontColor(BrandPurple);
                        t.Span(".").FontSize(10.5f).Bold().FontColor(InkDark);
                    });
                    col.Item().PaddingTop(3).AlignCenter().Text(t =>
                    {
                        t.Span("OET with Dr Ahmed Hesham").FontSize(9).SemiBold().FontColor(BrandPurpleDeep);
                        t.Span("  •  ").FontSize(9).FontColor(InkGrey);
                        t.Span("Tutor Commerce Academy").FontSize(9).SemiBold().FontColor(BrandPurpleDeep);
                    });
                    col.Item().PaddingTop(3).AlignCenter()
                        .Text("This receipt was generated electronically and is valid without a signature.")
                        .FontSize(8).FontColor(InkGrey);
                    col.Item().PaddingTop(2).AlignCenter()
                        .Text($"Reference: {model.InvoiceId}")
                        .FontSize(7).FontColor(InkGrey);
                });
            });
        }).GeneratePdf();

        var safeRef = reference.Replace('/', '-').Replace(' ', '-');
        return new InvoicePdfArtifact(bytes, $"{safeRef}.pdf");
    }

    /// <summary>
    /// Rounded status pill: green "PAID" with a check badge, or an amber pill carrying
    /// the raw status for anything else. Rounded shape comes from a fixed-aspect SVG
    /// layer (QuestPDF 2024.x has no native corner radius).
    /// </summary>
    private static void StatusPill(IContainer container, string? status, bool isPaid)
    {
        var label = isPaid ? "PAID" : (string.IsNullOrWhiteSpace(status) ? "PENDING" : status!.Trim().ToUpperInvariant());
        var pillWidth = isPaid ? 76 : Math.Min(160, 34 + label.Length * 7);

        container.Width(pillWidth).Height(25).Layers(layers =>
        {
            layers.Layer().Svg(isPaid ? PaidPillBackgroundSvg : PendingPillBackgroundSvg);
            layers.PrimaryLayer().AlignCenter().AlignMiddle().Row(row =>
            {
                if (isPaid)
                {
                    row.AutoItem().AlignMiddle().Width(12).Height(12).Svg(CheckBadgeSvg);
                    row.AutoItem().Width(5);
                }
                row.AutoItem().AlignMiddle().Text(label).FontSize(10).Bold()
                    .FontColor(isPaid ? PaidGreen : "#B45309");
            });
        });
    }

    /// <summary>
    /// Splits an invoice description like "1 x Listening Starter." into a clean item
    /// name and quantity for the line-item table. Descriptions without the quantity
    /// prefix ("Premium Monthly subscription", "Wallet top-up: …") pass through as a
    /// single-quantity item.
    /// </summary>
    private static (string Name, int Quantity) ParseLineItem(string description)
    {
        var text = description.Trim().TrimEnd('.');
        var match = QuantityPrefixRegex().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var qty) && qty > 0)
        {
            return (match.Groups[2].Value.Trim(), qty);
        }
        return (text, 1);
    }

    [GeneratedRegex(@"^(\d+)\s*[x×]\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityPrefixRegex();

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "GBP" : currency.Trim().ToUpperInvariant();

    private static string FormatMoney(decimal amount, string currencyCode, bool includeCode)
    {
        var symbol = currencyCode switch
        {
            "GBP" => "£",
            "USD" or "AUD" or "CAD" or "NZD" => "$",
            "EUR" => "€",
            "EGP" => "E£",
            _ => string.Empty
        };
        var value = amount.ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(symbol))
        {
            return $"{value} {currencyCode}";
        }
        return includeCode ? $"{symbol}{value} {currencyCode}" : $"{symbol}{value}";
    }

    // ── Vector artwork (pure shapes — no SVG text, so rendering never depends on fonts) ──

    private const string AvatarSvg =
        """
        <svg viewBox="0 0 46 46" xmlns="http://www.w3.org/2000/svg">
          <circle cx="23" cy="23" r="23" fill="#EDE9FE"/>
          <circle cx="23" cy="18" r="6.2" fill="none" stroke="#7C3AED" stroke-width="2.6"/>
          <path d="M11.5 35.5c2-6.3 8.3-7.9 11.5-7.9s9.5 1.6 11.5 7.9" fill="none" stroke="#7C3AED" stroke-width="2.6" stroke-linecap="round"/>
        </svg>
        """;

    private const string CheckBadgeSvg =
        """
        <svg viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg">
          <circle cx="8" cy="8" r="8" fill="#16A34A"/>
          <path d="M4.6 8.3l2.2 2.2 4.6-4.8" fill="none" stroke="#FFFFFF" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """;

    private const string PaidPillBackgroundSvg =
        """
        <svg viewBox="0 0 76 25" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <rect x="0.5" y="0.5" width="75" height="24" rx="12" fill="#DCFCE7" stroke="#86EFAC"/>
        </svg>
        """;

    private const string PendingPillBackgroundSvg =
        """
        <svg viewBox="0 0 76 25" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <rect x="0.5" y="0.5" width="75" height="24" rx="12" fill="#FEF3C7" stroke="#FCD34D"/>
        </svg>
        """;

    private const string HeartSvg =
        """
        <svg viewBox="0 0 40 40" xmlns="http://www.w3.org/2000/svg">
          <circle cx="20" cy="20" r="20" fill="#F3E8FF"/>
          <path d="M20 27.5c-5.4-3.7-8.3-6.6-8.3-10 0-2.4 1.9-4.2 4.2-4.2 1.7 0 3.1 1 4.1 2.4 1-1.4 2.4-2.4 4.1-2.4 2.3 0 4.2 1.8 4.2 4.2 0 3.4-2.9 6.3-8.3 10z" fill="none" stroke="#7C3AED" stroke-width="2.1" stroke-linejoin="round"/>
        </svg>
        """;

    /// <summary>
    /// Full-page background: a layered brand wave pinned to the bottom edge. Painted
    /// via page.Background() so it bleeds past the content margins like the web brand.
    /// </summary>
    private const string FooterWaveSvg =
        """
        <svg viewBox="0 0 595 842" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <defs>
            <linearGradient id="oetWave" x1="0" y1="0" x2="1" y2="0">
              <stop offset="0" stop-color="#6D28D9"/>
              <stop offset="0.55" stop-color="#7C3AED"/>
              <stop offset="1" stop-color="#8B5CF6"/>
            </linearGradient>
          </defs>
          <path d="M0 766 C 120 744, 250 744, 370 766 C 460 782, 540 782, 595 770 L 595 842 L 0 842 Z" fill="#EDE9FE"/>
          <path d="M0 808 C 110 828, 250 820, 380 796 C 470 780, 545 774, 595 780 L 595 842 L 0 842 Z" fill="url(#oetWave)"/>
        </svg>
        """;
}
