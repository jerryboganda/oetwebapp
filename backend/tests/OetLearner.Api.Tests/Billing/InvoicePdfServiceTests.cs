using System.Text;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Pure-unit coverage for the branded invoice PDF renderer. No DI / no DB — the
/// service only transforms an <see cref="InvoicePdfModel"/> into PDF bytes.
/// </summary>
public sealed class InvoicePdfServiceTests
{
    private static InvoicePdfModel SampleModel() => new(
        InvoiceId: "inv-sub-abc123",
        Number: 42,
        IssuedAt: new DateTimeOffset(2026, 6, 20, 9, 30, 0, TimeSpan.Zero),
        Amount: 100m,
        Currency: "GBP",
        Status: "Paid",
        Description: "Full Condensed Recorded OET Course — Medicine (one_time)",
        BillToName: "Dr Faisal Maqsood",
        BillToEmail: "learner@example.test");

    [Fact]
    public void Generate_ReturnsNonEmptyPdfBytes()
    {
        var artifact = new InvoicePdfService().Generate(SampleModel());

        Assert.NotNull(artifact.Bytes);
        Assert.True(artifact.Bytes.Length > 0, "PDF byte array should not be empty.");

        // Valid PDFs begin with the "%PDF" magic header.
        var header = Encoding.ASCII.GetString(artifact.Bytes, 0, 4);
        Assert.Equal("%PDF", header);
    }

    [Fact]
    public void Generate_FilenameUsesInvoiceNumberAndPdfExtension()
    {
        var artifact = new InvoicePdfService().Generate(SampleModel());
        Assert.Equal("INV-00042.pdf", artifact.Filename);
    }

    [Fact]
    public void BrandLogo_IsEmbeddedInApiAssembly()
    {
        // The invoice header renders the brand logo from an embedded resource; a
        // missing resource silently degrades to the text wordmark, so guard the
        // csproj wiring here.
        var names = typeof(InvoicePdfService).Assembly.GetManifestResourceNames();
        Assert.Contains(names, n => n.EndsWith("oet-with-dr-hesham-logo.png", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_FallsBackToInvoiceIdWhenNumberMissing()
    {
        var model = SampleModel() with { Number = null };
        var artifact = new InvoicePdfService().Generate(model);
        Assert.Equal("inv-sub-abc123.pdf", artifact.Filename);
    }

    [Fact]
    public void Generate_ToleratesBlankDescriptionAndUnknownCurrency()
    {
        var model = SampleModel() with { Description = "", Currency = "XYZ" };
        var artifact = new InvoicePdfService().Generate(model);
        Assert.True(artifact.Bytes.Length > 0);
    }
}
