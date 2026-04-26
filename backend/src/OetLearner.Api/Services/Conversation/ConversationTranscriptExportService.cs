using System.Security.Cryptography;
using System.Text;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Conversation;

public interface IConversationTranscriptExportService
{
    Task<ConversationTranscriptExportResult> ExportAsync(
        ConversationSession session,
        IReadOnlyList<ConversationTurn> turns,
        ConversationEvaluation? evaluation,
        string format,
        CancellationToken ct);
}

public sealed record ConversationTranscriptExportResult(
    string Key,
    string FileName,
    string ContentType,
    long Bytes,
    string Sha256,
    byte[] Content);

public sealed class ConversationTranscriptExportService(IFileStorage storage) : IConversationTranscriptExportService
{
    private const string Root = "conversation/transcripts";

    public async Task<ConversationTranscriptExportResult> ExportAsync(
        ConversationSession session,
        IReadOnlyList<ConversationTurn> turns,
        ConversationEvaluation? evaluation,
        string format,
        CancellationToken ct)
    {
        var normalized = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "txt";
        var text = BuildPlainText(session, turns, evaluation);
        var bytes = normalized == "pdf"
            ? BuildPdf(text)
            : Encoding.UTF8.GetBytes(text);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var key = ContentAddressed.PublishedKey(Root, sha, normalized);
        if (!storage.Exists(key))
        {
            await using var ms = new MemoryStream(bytes, writable: false);
            await storage.WriteAsync(key, ms, ct);
        }

        return new ConversationTranscriptExportResult(
            key,
            $"conversation-{session.Id}-transcript.{normalized}",
            normalized == "pdf" ? "application/pdf" : "text/plain; charset=utf-8",
            bytes.LongLength,
            sha,
            bytes);
    }

    private static string BuildPlainText(
        ConversationSession session,
        IReadOnlyList<ConversationTurn> turns,
        ConversationEvaluation? evaluation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OET AI Conversation Transcript");
        sb.AppendLine("================================");
        sb.AppendLine($"Session: {session.Id}");
        sb.AppendLine($"Task: {session.TaskTypeCode}");
        sb.AppendLine($"Profession: {session.Profession}");
        sb.AppendLine($"State: {session.State}");
        sb.AppendLine($"Created: {session.CreatedAt:u}");
        if (session.CompletedAt.HasValue) sb.AppendLine($"Completed: {session.CompletedAt:u}");
        if (evaluation is not null)
        {
            sb.AppendLine($"Score: {evaluation.OverallScaled}/500 ({evaluation.OverallGrade})");
            sb.AppendLine($"Passed: {(evaluation.Passed ? "yes" : "no")}");
        }
        sb.AppendLine();
        sb.AppendLine("Transcript");
        sb.AppendLine("----------");
        foreach (var turn in turns.OrderBy(t => t.TurnNumber))
        {
            var speaker = turn.Role.Equals("ai", StringComparison.OrdinalIgnoreCase) ? "AI partner" : "Learner";
            sb.AppendLine($"[{turn.TurnNumber:00}] {speaker}: {turn.Content}");
        }
        return sb.ToString();
    }

    private static byte[] BuildPdf(string text)
    {
        var lines = WrapLines(ToPdfSafeText(text), 92).ToArray();
        var pages = lines.Chunk(50).ToArray();
        if (pages.Length == 0) pages = [Array.Empty<string>()];

        var objects = new List<string>();
        var pageObjectNumbers = new List<int>();
        var contentObjectNumbers = new List<int>();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add("__PAGES__");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (var i = 0; i < pages.Length; i++)
        {
            var pageObj = objects.Count + 1;
            var contentObj = pageObj + 1;
            pageObjectNumbers.Add(pageObj);
            contentObjectNumbers.Add(contentObj);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObj} 0 R >>");
            var stream = BuildPdfPageStream(pages[i]);
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pageObjectNumbers.Count} >>";

        using var ms = new MemoryStream();
        void Write(string value)
        {
            var b = Encoding.ASCII.GetBytes(value);
            ms.Write(b, 0, b.Length);
        }

        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string BuildPdfPageStream(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        sb.AppendLine("/F1 10 Tf");
        sb.AppendLine("50 790 Td");
        foreach (var line in lines)
        {
            sb.Append('(').Append(EscapePdf(line)).AppendLine(") Tj");
            sb.AppendLine("0 -14 Td");
        }
        sb.Append("ET");
        return sb.ToString();
    }

    private static IEnumerable<string> WrapLines(string text, int max)
    {
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            while (line.Length > max)
            {
                var split = line.LastIndexOf(' ', Math.Min(max, line.Length - 1));
                if (split < 20) split = max;
                yield return line[..split];
                line = line[split..].TrimStart();
            }
            yield return line;
        }
    }

    private static string EscapePdf(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string ToPdfSafeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(ch is >= ' ' and <= '~' or '\n' or '\r' or '\t' ? ch : '?');
        return sb.ToString();
    }
}
