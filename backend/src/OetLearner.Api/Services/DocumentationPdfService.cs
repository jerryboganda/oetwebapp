using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetLearner.Api.Services;

public sealed record DocumentationPdfSectionModel(string Heading, string BodyMarkdown);

public sealed record DocumentationPdfModuleModel(
    string Code,
    string Title,
    int VersionNumber,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DocumentationPdfSectionModel> Sections);

public sealed record DocumentationPdfEvidenceEntry(
    string EvidenceId,
    string EvidenceType,
    string Description,
    string SourceReference);

public sealed record DocumentationPdfRevisionRow(string Version, string Date, string Author, string Reviewer, string Approval, string KeyChange);

public sealed record DocumentationPdfSignOffRow(string Role, string Name, string Date);

/// <summary>
/// Immutable data needed to render one PDF pack (Master, single-module, or Evidence
/// Annex). Already filtered for <see cref="Mode"/> by the caller — this renderer has
/// no redaction logic of its own, it only draws what it is given.
/// </summary>
public sealed record DocumentationPdfModel(
    string ReportName,
    string Owner,
    string Version,
    DateTimeOffset ReportDate,
    string DocumentStatus,
    string DocumentId,
    string? SourceRepoCommitSha,
    string ModeLabel,
    IReadOnlyList<DocumentationPdfModuleModel> Modules,
    IReadOnlyList<DocumentationPdfEvidenceEntry> EvidenceRegister,
    IReadOnlyList<DocumentationPdfRevisionRow> RevisionHistory,
    IReadOnlyList<DocumentationPdfSignOffRow> SignOffs);

/// <summary>Generated PDF artefact — never persisted by the renderer itself; the caller decides whether to store it via <c>IFileStorage</c>.</summary>
public sealed record DocumentationPdfArtifact(byte[] Bytes, string Filename);

public interface IDocumentationPdfService
{
    DocumentationPdfArtifact Generate(DocumentationPdfModel model);
}

/// <summary>
/// Renders the Admin Documentation Center's evidence packs — cover page, table of
/// contents with jump links, numbered sections (each becomes a real PDF bookmark via
/// <c>.Section()</c>), an evidence register table, a revision-history table and a
/// sign-off page. Pure managed output (QuestPDF, Community licence); text is native
/// (searchable/selectable), not rasterized. Brand palette matches every other
/// company-issued PDF (<see cref="InvoicePdfService"/>).
/// </summary>
public sealed class DocumentationPdfService : IDocumentationPdfService
{
    private const string BrandPurple = "#7C3AED";
    private const string BrandPurpleDeep = "#5B21B6";
    private const string LavenderBg = "#F3EFFD";
    private const string LavenderBorder = "#E2DAF8";
    private const string InkDark = "#1F2937";
    private const string InkGrey = "#6B7280";
    private const string PlatformDomain = "oetwithdrhesham.co.uk";

    static DocumentationPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public DocumentationPdfArtifact Generate(DocumentationPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var bytes = Document.Create(container =>
        {
            ComposeCover(container, model);
            ComposeBody(container, model);
        }).GeneratePdf();

        var safeName = model.ReportName.Replace('/', '-').Replace(' ', '-');
        return new DocumentationPdfArtifact(bytes, $"{safeName}-{model.Version}.pdf");
    }

    private static void ComposeCover(IDocumentContainer container, DocumentationPdfModel model)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(56);
            page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Lato").FontColor(InkDark));

            page.Content().Column(col =>
            {
                col.Item().PaddingTop(120);
                col.Item().Text("OET").FontSize(34).Black().FontColor(BrandPurple);
                col.Item().Text("with DR AHMED HESHAM").FontSize(13).SemiBold().FontColor(BrandPurpleDeep);
                col.Item().PaddingTop(2).Text(PlatformDomain).FontSize(11).FontColor(InkGrey);

                col.Item().PaddingTop(60).LineHorizontal(1.4f).LineColor(BrandPurple);
                col.Item().PaddingTop(24).Text(model.ReportName).FontSize(26).Bold().FontColor(InkDark);
                col.Item().PaddingTop(6).Text(model.ModeLabel).FontSize(12).SemiBold().FontColor(BrandPurpleDeep);

                col.Item().PaddingTop(40).Element(e => CoverMetaRow(e, "Document owner", model.Owner));
                col.Item().PaddingTop(8).Element(e => CoverMetaRow(e, "Version", model.Version));
                col.Item().PaddingTop(8).Element(e => CoverMetaRow(e, "Report date", model.ReportDate.UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
                col.Item().PaddingTop(8).Element(e => CoverMetaRow(e, "Document status", model.DocumentStatus));
                col.Item().PaddingTop(8).Element(e => CoverMetaRow(e, "Document ID", model.DocumentId));
                if (!string.IsNullOrWhiteSpace(model.SourceRepoCommitSha))
                {
                    col.Item().PaddingTop(8).Element(e => CoverMetaRow(e, "Technical baseline", model.SourceRepoCommitSha!));
                }

                col.Item().PaddingTop(48).Text(
                        "This report is a technical and innovation record. Every material claim is traceable to an " +
                        "EV-[MODULE]-[NUMBER] evidence reference resolved in the Evidence Annex. Planned or in-development " +
                        "work is labelled as such and is never presented as live.")
                    .FontSize(9.5f).Italic().FontColor(InkGrey);
            });

            page.Footer().AlignCenter().Text(PlatformDomain).FontSize(8).FontColor(InkGrey);
        });
    }

    private static void CoverMetaRow(IContainer container, string label, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(150).Text(label).FontSize(10.5f).FontColor(InkGrey);
            row.RelativeItem().Text(value).FontSize(11).SemiBold().FontColor(InkDark);
        });
    }

    private static void ComposeBody(IDocumentContainer container, DocumentationPdfModel model)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(46);
            page.MarginVertical(50);
            page.DefaultTextStyle(t => t.FontSize(10.5f).FontFamily("Lato").FontColor(InkDark).LineHeight(1.35f));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(model.ReportName).FontSize(9).SemiBold().FontColor(BrandPurpleDeep);
                    row.AutoItem().Text(model.ModeLabel).FontSize(9).FontColor(InkGrey);
                });
                col.Item().PaddingTop(4).LineHorizontal(0.8f).LineColor(LavenderBorder);
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(model.DocumentId).FontSize(7.5f).FontColor(InkGrey);
                row.AutoItem().Text(t =>
                {
                    t.Span("Page ").FontSize(8).FontColor(InkGrey);
                    t.CurrentPageNumber().FontSize(8).FontColor(InkGrey);
                    t.Span(" of ").FontSize(8).FontColor(InkGrey);
                    t.TotalPages().FontSize(8).FontColor(InkGrey);
                });
            });

            page.Content().PaddingTop(16).Column(col =>
            {
                col.Spacing(4);

                ComposeTableOfContents(col, model);
                var sectionNumber = 0;
                foreach (var module in model.Modules)
                {
                    ComposeModule(col, module, ref sectionNumber);
                }

                if (model.EvidenceRegister.Count > 0)
                {
                    ComposeEvidenceRegister(col, model.EvidenceRegister);
                }

                if (model.RevisionHistory.Count > 0)
                {
                    ComposeRevisionHistory(col, model.RevisionHistory);
                }

                if (model.SignOffs.Count > 0)
                {
                    ComposeSignOffPage(col, model.SignOffs);
                }
            });
        });
    }

    private static void ComposeTableOfContents(ColumnDescriptor col, DocumentationPdfModel model)
    {
        col.Item().Text("Table of Contents").FontSize(16).Bold().FontColor(BrandPurpleDeep);
        col.Item().PaddingTop(8).PaddingBottom(16).Column(toc =>
        {
            var n = 0;
            foreach (var module in model.Modules)
            {
                n++;
                toc.Item().PaddingVertical(1.5f).Row(row =>
                {
                    row.ConstantItem(24).Text($"{n}.").FontColor(InkGrey);
                    row.RelativeItem().SectionLink(module.Code).Text($"{module.Title} (v{module.VersionNumber})").FontColor(InkDark);
                });
            }
            if (model.EvidenceRegister.Count > 0)
            {
                toc.Item().PaddingVertical(1.5f).Row(row =>
                {
                    row.ConstantItem(24).Text($"{n + 1}.").FontColor(InkGrey);
                    row.RelativeItem().SectionLink("EVIDENCE-REGISTER").Text("Evidence Register").FontColor(InkDark);
                });
            }
        });
        col.Item().PageBreak();
    }

    private static void ComposeModule(ColumnDescriptor col, DocumentationPdfModuleModel module, ref int sectionNumber)
    {
        sectionNumber++;
        var localNumber = sectionNumber;
        col.Item().Section(module.Code).Column(mod =>
        {
            mod.Item().Text($"{localNumber}. {module.Title}").FontSize(15).Bold().FontColor(BrandPurpleDeep);
            mod.Item().PaddingBottom(10).Text(
                    $"Module {module.Code} · Version {module.VersionNumber} · Generated {module.GeneratedAt.UtcDateTime:dd MMM yyyy}")
                .FontSize(8.5f).FontColor(InkGrey);

            var sub = 0;
            foreach (var section in module.Sections)
            {
                sub++;
                mod.Item().PaddingTop(10).Text($"{localNumber}.{sub} {section.Heading}").FontSize(12).SemiBold().FontColor(InkDark);
                foreach (var paragraph in SplitParagraphs(section.BodyMarkdown))
                {
                    mod.Item().PaddingTop(5).Text(paragraph).FontSize(10.5f).FontColor(InkDark);
                }
            }
        });
        col.Item().PageBreak();
    }

    private static void ComposeEvidenceRegister(ColumnDescriptor col, IReadOnlyList<DocumentationPdfEvidenceEntry> evidence)
    {
        col.Item().Section("EVIDENCE-REGISTER").Column(sec =>
        {
            sec.Item().Text("Evidence Register").FontSize(15).Bold().FontColor(BrandPurpleDeep);
            sec.Item().PaddingTop(4).PaddingBottom(10).Text(
                    "Every EV-[MODULE]-[NUMBER] reference cited in this document resolves to a real, checkable source below.")
                .FontSize(9).FontColor(InkGrey);

            sec.Item().Border(1).BorderColor(LavenderBorder).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(90);
                    c.ConstantColumn(90);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                IContainer Head(IContainer c) => c.Background(LavenderBg).PaddingVertical(6).PaddingHorizontal(8);
                table.Header(h =>
                {
                    h.Cell().Element(Head).Text("Evidence ID").FontSize(9).Bold().FontColor(BrandPurple);
                    h.Cell().Element(Head).Text("Type").FontSize(9).Bold().FontColor(BrandPurple);
                    h.Cell().Element(Head).Text("Description").FontSize(9).Bold().FontColor(BrandPurple);
                    h.Cell().Element(Head).Text("Source").FontSize(9).Bold().FontColor(BrandPurple);
                });

                IContainer Body(IContainer c) => c.BorderTop(1).BorderColor(LavenderBorder).PaddingVertical(6).PaddingHorizontal(8);
                foreach (var e in evidence)
                {
                    table.Cell().Element(Body).Text(e.EvidenceId).FontSize(9).SemiBold();
                    table.Cell().Element(Body).Text(e.EvidenceType).FontSize(9);
                    table.Cell().Element(Body).Text(e.Description).FontSize(9);
                    table.Cell().Element(Body).Text(e.SourceReference).FontSize(8.5f).FontColor(InkGrey);
                }
            });
        });
        col.Item().PageBreak();
    }

    private static void ComposeRevisionHistory(ColumnDescriptor col, IReadOnlyList<DocumentationPdfRevisionRow> rows)
    {
        col.Item().Text("Revision History").FontSize(15).Bold().FontColor(BrandPurpleDeep);
        col.Item().PaddingTop(8).PaddingBottom(16).Border(1).BorderColor(LavenderBorder).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(50);
                c.ConstantColumn(70);
                c.RelativeColumn();
                c.RelativeColumn();
                c.ConstantColumn(70);
                c.RelativeColumn(2);
            });

            IContainer Head(IContainer c) => c.Background(LavenderBg).PaddingVertical(6).PaddingHorizontal(6);
            table.Header(h =>
            {
                h.Cell().Element(Head).Text("Version").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Date").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Author").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Reviewer").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Approval").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Key change").FontSize(9).Bold().FontColor(BrandPurple);
            });

            IContainer Body(IContainer c) => c.BorderTop(1).BorderColor(LavenderBorder).PaddingVertical(6).PaddingHorizontal(6);
            foreach (var r in rows)
            {
                table.Cell().Element(Body).Text(r.Version).FontSize(8.5f);
                table.Cell().Element(Body).Text(r.Date).FontSize(8.5f);
                table.Cell().Element(Body).Text(r.Author).FontSize(8.5f);
                table.Cell().Element(Body).Text(r.Reviewer).FontSize(8.5f);
                table.Cell().Element(Body).Text(r.Approval).FontSize(8.5f);
                table.Cell().Element(Body).Text(r.KeyChange).FontSize(8.5f);
            }
        });
    }

    private static void ComposeSignOffPage(ColumnDescriptor col, IReadOnlyList<DocumentationPdfSignOffRow> rows)
    {
        col.Item().PageBreak();
        col.Item().Text("Sign-off").FontSize(15).Bold().FontColor(BrandPurpleDeep);
        col.Item().PaddingTop(4).PaddingBottom(10).Text(
                "Named technical sign-offs for the current official release of this pack.")
            .FontSize(9).FontColor(InkGrey);

        col.Item().Border(1).BorderColor(LavenderBorder).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn();
            });

            IContainer Head(IContainer c) => c.Background(LavenderBg).PaddingVertical(6).PaddingHorizontal(8);
            table.Header(h =>
            {
                h.Cell().Element(Head).Text("Role").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Name").FontSize(9).Bold().FontColor(BrandPurple);
                h.Cell().Element(Head).Text("Date").FontSize(9).Bold().FontColor(BrandPurple);
            });

            IContainer Body(IContainer c) => c.BorderTop(1).BorderColor(LavenderBorder).PaddingVertical(8).PaddingHorizontal(8);
            foreach (var r in rows)
            {
                table.Cell().Element(Body).Text(r.Role).FontSize(9.5f).SemiBold();
                table.Cell().Element(Body).Text(r.Name).FontSize(9.5f);
                table.Cell().Element(Body).Text(r.Date).FontSize(9.5f);
            }
        });
    }

    /// <summary>Splits markdown-ish body text into paragraphs on blank lines. Deliberately not a full markdown renderer — see ponytail note in PR description.</summary>
    private static IEnumerable<string> SplitParagraphs(string bodyMarkdown)
    {
        if (string.IsNullOrWhiteSpace(bodyMarkdown)) yield break;
        foreach (var raw in bodyMarkdown.Replace("\r\n", "\n").Split("\n\n"))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0) yield return trimmed;
        }
    }
}
