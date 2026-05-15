using System.Text.RegularExpressions;

namespace OetLearner.Api.Security;

public static class SafeHtmlSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex ScriptBlocks = new(
        @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex DangerousTags = new(
        @"<\/?(?:script|iframe|object|embed|form|meta|link|base|style)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex EventHandlers = new(
        @"\son\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex StyleAttributes = new(
        @"\sstyle\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex DangerousUrls = new(
        @"\s(?:href|src|action|formaction|xlink:href)\s*=\s*(?:""\s*(?:javascript|vbscript):[^""]*""|'\s*(?:javascript|vbscript):[^']*'|(?:javascript|vbscript):[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex NonImageDataUrls = new(
        @"\s(?:href|src|action|formaction)\s*=\s*(?:""\s*data:(?!image\/)[^""]*""|'\s*data:(?!image\/)[^']*'|data:(?!image\/)[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    public static string SanitizeLimitedHtml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        return NonImageDataUrls.Replace(
            DangerousUrls.Replace(
                StyleAttributes.Replace(
                    EventHandlers.Replace(
                        DangerousTags.Replace(
                            ScriptBlocks.Replace(input, string.Empty),
                            string.Empty),
                        string.Empty),
                    string.Empty),
                string.Empty),
            string.Empty);
    }
}
