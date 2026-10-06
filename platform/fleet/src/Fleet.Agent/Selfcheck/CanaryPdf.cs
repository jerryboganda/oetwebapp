using System.Text;

namespace Fleet.Agent;

/// <summary>
/// A tiny deterministic PDF built in memory (one Helvetica line). The agent's local self-check runs it through the REAL
/// pdf.extract path, in the child process, before the agent ever claims work. Content is fixed and public; no learner data.
/// </summary>
internal static class CanaryPdf
{
    public const string Text = "OET Fleet Canary Check";

    public static byte[] Build()
    {
        var content = "BT /F1 12 Tf 72 720 Td (" + Text + ") Tj ET";
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < bodies.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(bodies.Length + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(bodies.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>Collapses runs of whitespace so the comparison tolerates a different word spacing from the extractor.</summary>
    public static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
