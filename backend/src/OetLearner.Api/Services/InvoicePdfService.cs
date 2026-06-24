using System.Globalization;
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
/// Renders a branded, printable invoice PDF for OET (with Dr Ahmed Hesham).
/// Pure managed output (QuestPDF, Community licence) — no native dependencies,
/// no persistence. Mirrors the pattern established by <see cref="WritingPdfService"/>.
/// </summary>
public sealed class InvoicePdfService : IInvoicePdfService
{
    private static readonly string BrandNavy = Colors.Blue.Darken4;

    static InvoicePdfService()
    {
        // QuestPDF Community licence is free for organisations under USD 1M ARR.
        // Accepted explicitly so the library does not throw on first use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public InvoicePdfArtifact Generate(InvoicePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issuedIso = model.IssuedAt.UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var reference = model.Number is { } n ? $"INV-{n:0000}" : model.InvoiceId;
        var amountText = FormatMoney(model.Amount, model.Currency);
        var description = string.IsNullOrWhiteSpace(model.Description) ? "OET subscription" : model.Description;

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(Fonts.Calibri).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(brand =>
                        {
                            brand.Item().Text("OET").FontSize(26).Bold().FontColor(BrandNavy);
                            brand.Item().Text("with Dr Ahmed Hesham").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(160).AlignRight().Column(meta =>
                        {
                            meta.Item().AlignRight().Text("INVOICE").FontSize(18).Bold().FontColor(BrandNavy);
                            meta.Item().AlignRight().Text(reference).FontSize(11).SemiBold();
                            meta.Item().AlignRight().Text($"Issued {issuedIso}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(BrandNavy);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Column(billTo =>
                    {
                        billTo.Item().Text("BILLED TO").FontSize(9).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                        billTo.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(model.BillToName) ? "Learner" : model.BillToName)
                            .FontSize(12).SemiBold().FontColor(BrandNavy);
                        if (!string.IsNullOrWhiteSpace(model.BillToEmail))
                        {
                            billTo.Item().Text(model.BillToEmail!).FontSize(10).FontColor(Colors.Grey.Darken1);
                        }
                    });

                    // Line-item table: description + amount.
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.ConstantColumn(120);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(8).Text("Description").Bold().FontSize(10);
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(8).AlignRight().Text("Amount").Bold().FontSize(10);
                        });

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).Text(description);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(8).AlignRight().Text(amountText);
                    });

                    col.Item().AlignRight().Row(row =>
                    {
                        row.ConstantItem(220).Column(totals =>
                        {
                            totals.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Total").Bold();
                                r.ConstantItem(110).AlignRight().Text(amountText).Bold().FontSize(13).FontColor(BrandNavy);
                            });
                            totals.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem().Text("Status").FontColor(Colors.Grey.Darken1);
                                r.ConstantItem(110).AlignRight().Text(model.Status).SemiBold().FontColor(Colors.Green.Darken2);
                            });
                        });
                    });
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(4).Text("Thank you for studying with OET — Dr Ahmed Hesham. This invoice is generated electronically and is valid without signature.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().Text($"Reference: {model.InvoiceId}").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();

        var safeRef = reference.Replace('/', '-').Replace(' ', '-');
        return new InvoicePdfArtifact(bytes, $"{safeRef}.pdf");
    }

    private static string FormatMoney(decimal amount, string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "GBP" : currency.Trim().ToUpperInvariant();
        var symbol = code switch
        {
            "GBP" => "£",
            "USD" or "AUD" or "CAD" or "NZD" => "$",
            "EUR" => "€",
            _ => string.Empty
        };
        var value = amount.ToString("0.00", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(symbol) ? $"{value} {code}" : $"{symbol}{value} {code}";
    }
}
