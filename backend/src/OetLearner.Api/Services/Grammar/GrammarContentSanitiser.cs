using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Server-side first line of defence against admin-authored HTML/script
/// content sneaking into grammar lessons. Strips <script>, <iframe>,
/// inline event handlers, and javascript: URIs. The learner renderer
/// adds a second DOMPurify pass for paranoia. Markdown-level constructs
/// (links, bold, headings) are preserved.
/// </summary>
public static class GrammarContentSanitiser
{
    private static readonly Regex ScriptTag = new(
        @"<\s*script\b[^>]*>.*?<\s*/\s*script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex StyleTag = new(
        @"<\s*style\b[^>]*>.*?<\s*/\s*style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex IframeTag = new(
        @"<\s*iframe\b[^>]*>.*?<\s*/\s*iframe\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OnHandler = new(
        @"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase);

    private static readonly Regex JsUri = new(
        @"javascript\s*:",
        RegexOptions.IgnoreCase);

    private static readonly Regex DataUri = new(
        @"data\s*:\s*text\s*/\s*html",
        RegexOptions.IgnoreCase);

    public static string Sanitise(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var s = input;
        s = ScriptTag.Replace(s, "");
        s = StyleTag.Replace(s, "");
        s = IframeTag.Replace(s, "");
        s = OnHandler.Replace(s, "");
        s = JsUri.Replace(s, "blocked:");
        s = DataUri.Replace(s, "blocked:");
        return s.Trim();
    }
}
