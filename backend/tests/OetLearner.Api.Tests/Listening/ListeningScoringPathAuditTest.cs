using System.Text.RegularExpressions;
using Xunit;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening V2 — mission-critical scoring-path audit. Source-scans the
/// Listening service tree for inline raw→scaled math (* 350 / / 42 / * 500
/// / * 8.33). The ONLY allowed scaling path is
/// <c>OetScoring.OetRawToScaled</c>. Any other inline formula is a CI fail.
///
/// Why a meta-test: AGENTS.md mission-critical invariant — Listening (like
/// Reading) is graded server-side and the 30/42 ≡ 350 anchor must never be
/// duplicated outside <c>OetScoring</c>. A reviewer-only check is not
/// sufficient because copy-paste of an old ad-hoc formula is the most likely
/// regression vector.
/// </summary>
public class ListeningScoringPathAuditTest
{
    [Fact]
    public void Listening_service_tree_uses_only_OetScoring_for_raw_to_scaled()
    {
        var listeningServicesDir = LocateListeningServicesDir();
        var offenders = new List<string>();

        // Forbidden inline-scaling regexes — each captures the canonical
        // 30/42 ≡ 350/500 anchor in any of its likely written forms.
        var patterns = new[]
        {
            new Regex(@"\*\s*350\b",   RegexOptions.Compiled),
            new Regex(@"/\s*42\b",     RegexOptions.Compiled),
            new Regex(@"\*\s*500\b",   RegexOptions.Compiled),
            new Regex(@"\*\s*8\.33",   RegexOptions.Compiled),
            new Regex(@"\*\s*8\.\d{2,}", RegexOptions.Compiled), // 8.333…
        };

        foreach (var file in Directory.EnumerateFiles(
            listeningServicesDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip the audit test itself.
            if (file.EndsWith("ListeningScoringPathAuditTest.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            // Allow OetScoring (the legitimate single source of truth).
            if (file.EndsWith("OetScoring.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var src = File.ReadAllText(file);
            var lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip line-comments / docstrings — comparing constants in
                // documentation is allowed.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") ||
                    trimmed.StartsWith("///")) continue;

                // Strip inline comments before pattern matching so e.g.
                // `const int N = ... // 42` is not flagged on the trailing
                // comment.
                var codeOnly = StripInlineComment(line);
                if (string.IsNullOrWhiteSpace(codeOnly)) continue;

                foreach (var p in patterns)
                {
                    if (p.IsMatch(codeOnly))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Inline raw→scaled math found in Listening service tree. " +
            "All scaling MUST route through OetScoring.OetRawToScaled. " +
            "Offenders:\n" + string.Join("\n", offenders));
    }

    private static string LocateListeningServicesDir()
    {
        // Walk up from the test bin dir to the repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "backend", "src", "OetLearner.Api", "Services", "Listening");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new DirectoryNotFoundException(
            "Could not locate backend/src/OetLearner.Api/Services/Listening from test bin dir.");
    }

    /// <summary>Strip everything after the first <c>//</c> that is not
    /// inside a string literal. Crude but sufficient for our line-by-line
    /// pattern audit (we do NOT need to parse C# precisely).</summary>
    private static string StripInlineComment(string line)
    {
        bool inString = false;
        bool inChar = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && !inChar) inString = !inString;
            else if (c == '\'' && !inString) inChar = !inChar;
            else if (!inString && !inChar && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                return line.Substring(0, i);
        }
        return line;
    }
}
