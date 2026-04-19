using System.Text;

namespace OetLearner.Api.Services.Progress;

/// <summary>
/// Minimal PDF 1.4 renderer for the Progress summary. Avoids pulling in a PDF
/// library; produces a single-page, plain-text report that opens in every
/// standard viewer (Chrome, Preview, Acrobat).
///
/// <para>
/// Kept intentionally primitive — this is a first-pass export. The admin
/// <c>ProgressPolicy.ExportPdfEnabled</c> gate means the endpoint is off by
/// default until the marketing/legal copy is finalised. When the policy team
/// approves, we can swap this for a Blink-rendered PDF that shares the
/// <c>OetStatementOfResultsCard</c> visual.
/// </para>
/// </summary>
internal static class ProgressPdfRenderer
{
    public static byte[] Render(ProgressPayload payload)
    {
        var lines = new List<string>
        {
            "OET Prep — Progress Summary",
            "",
            $"Range: {payload.Meta.Range}   Country: {payload.Meta.TargetCountry ?? "(not set)"}",
            $"Generated: {payload.Freshness.GeneratedAt:yyyy-MM-dd HH:mm} UTC",
            "",
            "Subtest summary",
            "  Subtest     Latest     Grade   Target   Gap",
        };
        foreach (var s in payload.Subtests)
        {
            lines.Add($"  {s.SubtestCode,-10}  {(s.LatestScaled?.ToString() ?? "—"),6}    {s.LatestGrade ?? "—",-5}   {(s.TargetScaled?.ToString() ?? "—"),6}   {(s.GapToTarget?.ToString() ?? "—"),6}");
        }
        lines.Add("");
        lines.Add($"Completed attempts: {payload.Totals.CompletedAttempts}");
        lines.Add($"Completed evaluations: {payload.Totals.CompletedEvaluations}");
        lines.Add($"Mock attempts: {payload.Totals.MockAttempts}");
        lines.Add($"Reviews: {payload.ReviewUsage.CompletedRequests} / {payload.ReviewUsage.TotalRequests} — avg {payload.ReviewUsage.AverageTurnaroundHours?.ToString("F1") ?? "—"} h");
        lines.Add("");
        lines.Add("This document is a practice-platform summary, not an official OET result.");
        return BuildSinglePagePdf(lines);
    }

    private static byte[] BuildSinglePagePdf(IReadOnlyList<string> textLines)
    {
        // Escape parens + backslashes per PDF literal-string rules.
        string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = new StringBuilder();
        content.Append("BT\n/F1 11 Tf\n12 TL\n50 770 Td\n");
        for (var i = 0; i < textLines.Count; i++)
        {
            content.Append('(').Append(Esc(textLines[i])).Append(") Tj T*\n");
        }
        content.Append("ET");
        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        using var ms = new MemoryStream();
        void Write(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        Write("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xrefPos = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
        return ms.ToArray();
    }
}
