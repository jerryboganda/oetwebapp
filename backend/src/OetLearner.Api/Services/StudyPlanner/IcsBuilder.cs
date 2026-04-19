using System.Text;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// Produces RFC-5545 iCalendar output for a learner's pending study plan items.
/// Each item becomes a VEVENT on its DueDate, with a 15-minute reminder.
/// </summary>
internal static class IcsBuilder
{
    public static string Build(StudyPlan plan, IReadOnlyList<StudyPlanItem> items)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//OET Prep//Study Planner//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append("X-WR-CALNAME:OET Study Plan\r\n");
        sb.Append("X-WR-TIMEZONE:UTC\r\n");
        foreach (var it in items)
        {
            var start = it.DueDate.ToDateTime(new TimeOnly(9, 0));
            var end = start.AddMinutes(Math.Max(5, it.DurationMinutes));
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{it.Id}@oetwithdrhesham.co.uk\r\n");
            sb.Append($"DTSTAMP:{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTSTART:{start:yyyyMMddTHHmmss}\r\n");
            sb.Append($"DTEND:{end:yyyyMMddTHHmmss}\r\n");
            sb.Append($"SUMMARY:{Escape(it.Title)}\r\n");
            sb.Append($"DESCRIPTION:{Escape(Summarise(it))}\r\n");
            sb.Append($"CATEGORIES:OET,{Escape(it.SubtestCode ?? "")}\r\n");
            sb.Append("STATUS:CONFIRMED\r\n");
            // 15 minute reminder
            sb.Append("BEGIN:VALARM\r\n");
            sb.Append("ACTION:DISPLAY\r\n");
            sb.Append($"DESCRIPTION:{Escape(it.Title)}\r\n");
            sb.Append("TRIGGER:-PT15M\r\n");
            sb.Append("END:VALARM\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Summarise(StudyPlanItem it)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(it.SubtestCode)) parts.Add($"Subtest: {it.SubtestCode}");
        parts.Add($"Duration: {it.DurationMinutes} min");
        if (!string.IsNullOrEmpty(it.Rationale)) parts.Add($"Why: {it.Rationale}");
        return string.Join(" — ", parts);
    }

    private static string Escape(string s) => (s ?? "")
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
