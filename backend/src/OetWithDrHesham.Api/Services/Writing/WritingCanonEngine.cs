using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingCanonDetectionRequest(
    string UserId,
    Guid SubmissionId,
    string LetterContent,
    string LetterType,
    string Profession);

public sealed record WritingCanonDetectionResult(
    Guid SubmissionId,
    IReadOnlyList<WritingCanonViolation> Violations);

public interface IWritingCanonEngine
{
    Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct);
    Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct);
}

/// <summary>Pluggable structural matcher signature. Implementations live in
/// <see cref="WritingStructuralMatchers"/>.</summary>
public delegate IEnumerable<(int CharStart, int CharEnd, string Snippet, string? SuggestedFix)> WritingStructuralMatcher(
    string letter,
    JsonElement config);

/// <summary>
/// Compiled regex pool + LLM batch caller + named structural matcher registry.
/// Registered as a SINGLETON in DI: the regex cache and structural matcher map
/// are immutable after first use and entirely thread-safe.
/// </summary>
public sealed class WritingCanonEngine(
    IServiceScopeFactory scopeFactory,
    IAiGatewayService aiGateway,
    ILogger<WritingCanonEngine> logger,
    TimeProvider clock) : IWritingCanonEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<(string RuleId, int Version), Regex> _regexCache = new();

    private static readonly IReadOnlyDictionary<string, WritingStructuralMatcher> StructuralMatchers = WritingStructuralMatchers.BuildRegistry();

    public async Task<WritingCanonDetectionResult> DetectViolationsAsync(WritingCanonDetectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rules = await db.WritingCanonRules.AsNoTracking()
            .Where(r => r.Active)
            .ToListAsync(ct);

        var applicable = rules.Where(r => MatchesScope(r, request.LetterType, request.Profession)).ToList();
        var violations = new List<WritingCanonViolation>();
        var now = clock.GetUtcNow();

        var regexRules = applicable.Where(r => string.Equals(r.DetectionType, "regex", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var rule in regexRules)
        {
            foreach (var match in EvaluateRegexRule(rule, request.LetterContent))
            {
                violations.Add(BuildViolation(request, rule, match, now));
            }
        }

        var structuralRules = applicable.Where(r => string.Equals(r.DetectionType, "structural", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var rule in structuralRules)
        {
            foreach (var match in EvaluateStructuralRule(rule, request.LetterContent))
            {
                violations.Add(BuildViolation(request, rule, match, now));
            }
        }

        var llmRules = applicable.Where(r => string.Equals(r.DetectionType, "llm", StringComparison.OrdinalIgnoreCase)).ToList();
        if (llmRules.Count > 0)
        {
            try
            {
                var llmHits = await EvaluateLlmRulesAsync(request, llmRules, ct);
                foreach (var hit in llmHits)
                {
                    violations.Add(BuildViolation(request, hit.Rule, hit.Match, now));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Canon LLM detection failed for submission {SubmissionId}; regex+structural violations still applied.", request.SubmissionId);
            }
        }

        var deduped = violations
            .GroupBy(v => (v.RuleId, v.CharStart, v.CharEnd, v.Snippet))
            .Select(g => g.First())
            .ToList();

        if (deduped.Count > 0)
        {
            db.WritingCanonViolations.AddRange(deduped);
            await db.SaveChangesAsync(ct);
        }
        return new WritingCanonDetectionResult(request.SubmissionId, deduped);
    }

    private IEnumerable<DetectionMatch> EvaluateRegexRule(WritingCanonRule rule, string letter)
    {
        var (pattern, options, whitelist) = ParseRegexConfig(rule);
        if (string.IsNullOrWhiteSpace(pattern)) yield break;
        var regex = _regexCache.GetOrAdd((rule.Id, rule.Version), _ => CompileRegex(pattern!, options));
        foreach (Match match in regex.Matches(letter))
        {
            if (!match.Success) continue;
            if (IsWhitelisted(letter, match, whitelist)) continue;
            var snippet = Trim(letter.Substring(match.Index, Math.Min(match.Length, 240)));
            yield return new DetectionMatch(match.Index, match.Index + match.Length, snippet, null);
        }
    }

    private IEnumerable<DetectionMatch> EvaluateStructuralRule(WritingCanonRule rule, string letter)
    {
        var (matcherKey, config) = ParseStructuralConfig(rule);
        if (string.IsNullOrWhiteSpace(matcherKey) || !StructuralMatchers.TryGetValue(matcherKey!, out var matcher))
        {
            yield break;
        }
        foreach (var (start, end, snippet, suggestedFix) in matcher(letter, config))
        {
            yield return new DetectionMatch(start, end, Trim(snippet), suggestedFix);
        }
    }

    private async Task<IReadOnlyList<(WritingCanonRule Rule, DetectionMatch Match)>> EvaluateLlmRulesAsync(
        WritingCanonDetectionRequest request,
        IReadOnlyList<WritingCanonRule> rules,
        CancellationToken ct)
    {
        var hits = new List<(WritingCanonRule Rule, DetectionMatch Match)>();
        foreach (var rule in rules)
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = request.LetterType,
                Task = AiTaskMode.Coach,
            });
            var userInput = BuildLlmInput(rule, request.LetterContent);
            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingCanonDetectV1,
                PromptTemplateId = "writing.canon.detect.v1",
                UserId = request.UserId,
            }, ct);
            foreach (var match in ParseLlmResponse(result.Completion, request.LetterContent))
            {
                hits.Add((rule, match));
            }
        }
        return hits;
    }

    private static IEnumerable<DetectionMatch> ParseLlmResponse(string completion, string letter)
    {
        if (string.IsNullOrWhiteSpace(completion)) yield break;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) yield break;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(completion[start..(end + 1)]); }
        catch (JsonException) { }
        if (doc is null) yield break;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("violations", out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
            foreach (var v in arr.EnumerateArray())
            {
                var snippet = v.TryGetProperty("snippet", out var snEl) && snEl.ValueKind == JsonValueKind.String ? snEl.GetString() ?? string.Empty : string.Empty;
                var fix = v.TryGetProperty("suggested_fix", out var sfEl) && sfEl.ValueKind == JsonValueKind.String ? sfEl.GetString() : null;
                var charStart = v.TryGetProperty("char_start", out var csEl) && csEl.TryGetInt32(out var cs) ? cs : -1;
                var charEnd = v.TryGetProperty("char_end", out var ceEl) && ceEl.TryGetInt32(out var ce) ? ce : -1;
                if (charStart < 0 && snippet.Length > 0)
                {
                    charStart = letter.IndexOf(snippet, StringComparison.OrdinalIgnoreCase);
                    if (charStart >= 0) charEnd = charStart + snippet.Length;
                }
                if (charStart < 0 || charEnd <= charStart) continue;
                if (string.IsNullOrEmpty(snippet)) snippet = letter.Substring(charStart, Math.Min(charEnd - charStart, 240));
                yield return new DetectionMatch(charStart, charEnd, snippet, fix);
            }
        }
    }

    private static string BuildLlmInput(WritingCanonRule rule, string letter)
    {
        return string.Join('\n',
            $"Rule ID: {rule.Id}",
            $"Severity: {rule.Severity}",
            $"Rule text: {rule.RuleText}",
            "Correct examples:",
            rule.CorrectExamplesJson,
            "Incorrect examples:",
            rule.IncorrectExamplesJson,
            "Candidate letter:",
            "---",
            letter,
            "---",
            "Return JSON: { \"violations\": [{ \"char_start\": int, \"char_end\": int, \"snippet\": string, \"suggested_fix\": string }] }.");
    }

    private static WritingCanonViolation BuildViolation(WritingCanonDetectionRequest request, WritingCanonRule rule, DetectionMatch match, DateTimeOffset now)
    {
        return new WritingCanonViolation
        {
            Id = Guid.NewGuid(),
            SubmissionId = request.SubmissionId,
            RuleId = rule.Id,
            Severity = rule.Severity,
            Snippet = Trim(match.Snippet),
            CharStart = match.CharStart,
            CharEnd = match.CharEnd,
            LineNumber = ComputeLineNumber(request.LetterContent, match.CharStart),
            SuggestedFix = match.SuggestedFix,
            DetectedAt = now,
        };
    }

    private static (string? Pattern, RegexOptions Options, IReadOnlyList<string> Whitelist) ParseRegexConfig(WritingCanonRule rule)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rule.DetectionConfigJson) ? "{}" : rule.DetectionConfigJson);
            var pattern = doc.RootElement.TryGetProperty("pattern", out var pEl) && pEl.ValueKind == JsonValueKind.String ? pEl.GetString() : null;
            var ignoreCase = !doc.RootElement.TryGetProperty("ignoreCase", out var icEl) || (icEl.ValueKind == JsonValueKind.True);
            var multiline = doc.RootElement.TryGetProperty("multiline", out var mlEl) && mlEl.ValueKind == JsonValueKind.True;
            var whitelist = new List<string>();
            if (doc.RootElement.TryGetProperty("whitelist", out var wlEl) && wlEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in wlEl.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        var v = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) whitelist.Add(v);
                    }
                }
            }
            var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
            if (ignoreCase) options |= RegexOptions.IgnoreCase;
            if (multiline) options |= RegexOptions.Multiline;
            return (pattern, options, whitelist);
        }
        catch (JsonException)
        {
            return (null, RegexOptions.Compiled, Array.Empty<string>());
        }
    }

    private static (string? Matcher, JsonElement Config) ParseStructuralConfig(WritingCanonRule rule)
    {
        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rule.DetectionConfigJson) ? "{}" : rule.DetectionConfigJson);
            var matcher = doc.RootElement.TryGetProperty("matcher", out var mEl) && mEl.ValueKind == JsonValueKind.String ? mEl.GetString() : null;
            var config = doc.RootElement.TryGetProperty("config", out var cEl) ? cEl.Clone() : doc.RootElement.Clone();
            return (matcher, config);
        }
        catch (JsonException)
        {
            return (null, default);
        }
    }

    private static bool IsWhitelisted(string letter, Match match, IReadOnlyList<string> whitelist)
    {
        if (whitelist.Count == 0) return false;
        var contextStart = Math.Max(0, match.Index - 30);
        var contextEnd = Math.Min(letter.Length, match.Index + match.Length + 30);
        var context = letter.Substring(contextStart, contextEnd - contextStart);
        return whitelist.Any(w => context.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static Regex CompileRegex(string pattern, RegexOptions options)
    {
        try
        {
            return new Regex(pattern, options, TimeSpan.FromMilliseconds(150));
        }
        catch (ArgumentException)
        {
            return new Regex(@"^$", RegexOptions.Compiled);
        }
    }

    private static bool MatchesScope(WritingCanonRule rule, string letterType, string profession)
    {
        bool MatchesList(string json, string value)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new();
                if (list.Count == 0) return true;
                if (list.Any(v => string.Equals(v, "all", StringComparison.OrdinalIgnoreCase))) return true;
                return list.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException) { return true; }
        }
        return MatchesList(rule.AppliesToLetterTypesJson, letterType)
               && MatchesList(rule.AppliesToProfessionsJson, profession);
    }

    private static int ComputeLineNumber(string letter, int charIndex)
    {
        if (charIndex <= 0 || string.IsNullOrEmpty(letter)) return 1;
        var line = 1;
        for (var i = 0; i < charIndex && i < letter.Length; i++)
        {
            if (letter[i] == '\n') line++;
        }
        return line;
    }

    private static string Trim(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length <= 240 ? trimmed : trimmed[..240];
    }

    public async Task<WritingCanonRuleTestResponse?> TestRuleAsync(string adminUserId, string ruleId, WritingCanonRuleTestRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rule = await db.WritingCanonRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null) return null;

        var letter = request.Letter ?? string.Empty;
        var letterType = request.LetterType ?? "routine_referral";
        var profession = request.Profession ?? "medicine";

        var violations = new List<WritingCanonViolation>();
        var fakeSubmissionId = Guid.Empty;
        var now = clock.GetUtcNow();
        var fakeRequest = new WritingCanonDetectionRequest(adminUserId, fakeSubmissionId, letter, letterType, profession);

        switch ((rule.DetectionType ?? string.Empty).ToLowerInvariant())
        {
            case "regex":
                foreach (var match in EvaluateRegexRule(rule, letter))
                {
                    violations.Add(BuildViolation(fakeRequest, rule, match, now));
                }
                break;
            case "structural":
                foreach (var match in EvaluateStructuralRule(rule, letter))
                {
                    violations.Add(BuildViolation(fakeRequest, rule, match, now));
                }
                break;
            case "llm":
                try
                {
                    var llm = await EvaluateLlmRulesAsync(fakeRequest, new[] { rule }, ct);
                    foreach (var (r, m) in llm) violations.Add(BuildViolation(fakeRequest, r, m, now));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Canon test rule LLM call failed for {RuleId}", ruleId);
                }
                break;
        }
        var violationResponses = violations.Select(v => WritingV2ResponseMapper.ToResponse(v, rule.RuleText)).ToList();
        return new WritingCanonRuleTestResponse(ruleId, violations.Count > 0, violationResponses);
    }

    private readonly record struct DetectionMatch(int CharStart, int CharEnd, string Snippet, string? SuggestedFix);
}

/// <summary>Built-in structural matchers (spec §13.4). Matchers are pure
/// functions over the letter text + per-rule config JSON. They run inside the
/// engine and never touch the database. Keep them deterministic.</summary>
public static class WritingStructuralMatchers
{
    public static IReadOnlyDictionary<string, WritingStructuralMatcher> BuildRegistry()
    {
        return new Dictionary<string, WritingStructuralMatcher>(StringComparer.OrdinalIgnoreCase)
        {
            ["reLineFormat"] = MatchReLineFormat,
            ["paragraphOpenerToday"] = MatchParagraphOpenerToday,
            ["closurePattern"] = MatchClosurePattern,
            ["socialHistoryInDischarge"] = MatchSocialHistoryInDischarge,
            ["pronounRulePerParagraph"] = MatchPronounRulePerParagraph,
        };
    }

    private static IEnumerable<(int, int, string, string?)> MatchReLineFormat(string letter, JsonElement config)
    {
        var lines = letter.Split('\n');
        var idx = 0;
        var charCursor = 0;
        foreach (var raw in lines)
        {
            idx++;
            var trimmed = raw.TrimStart();
            if (idx >= 4 && trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
            {
                // good — exit early.
                yield break;
            }
            charCursor += raw.Length + 1;
        }
        yield return (0, Math.Min(60, letter.Length), "Missing 'Re:' line", "Add a Re: <patient name>, <relevant context> line above the salutation.");
    }

    private static IEnumerable<(int, int, string, string?)> MatchParagraphOpenerToday(string letter, JsonElement config)
    {
        var lines = letter.Split('\n');
        var bodyStarted = false;
        var charCursor = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();
            charCursor += raw.Length + 1;
            if (!bodyStarted && trimmed.StartsWith("Dear ", StringComparison.OrdinalIgnoreCase))
            {
                bodyStarted = true;
                continue;
            }
            if (!bodyStarted) continue;
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.Length < 10) continue;
            if (trimmed.StartsWith("I am writing", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Today", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Mr ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Mrs ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Ms ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("On ", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
            yield return (charCursor - raw.Length - 1, charCursor - 1, raw, "Open the first body paragraph with 'I am writing to refer ...' or a clear date/context anchor.");
            yield break;
        }
    }

    private static IEnumerable<(int, int, string, string?)> MatchClosurePattern(string letter, JsonElement config)
    {
        var closures = new[]
        {
            "Thank you for your assistance",
            "Thank you for your attention",
            "Please contact me",
            "Should you require further information",
        };
        if (closures.Any(c => letter.Contains(c, StringComparison.OrdinalIgnoreCase))) yield break;
        var startIndex = Math.Max(0, letter.Length - 240);
        yield return (startIndex, letter.Length, letter[startIndex..], "Add a closing sentence such as 'Thank you for your assistance' before the sign-off.");
    }

    private static IEnumerable<(int, int, string, string?)> MatchSocialHistoryInDischarge(string letter, JsonElement config)
    {
        var keywords = new[] { "drinks", "alcohol", "smokes", "smoking", "lives alone", "marital", "hobbies", "gardening" };
        foreach (var kw in keywords)
        {
            var idx = letter.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            yield return (idx, Math.Min(letter.Length, idx + kw.Length + 80), letter.Substring(idx, Math.Min(letter.Length - idx, 80)),
                "Omit unrelated social history from a discharge letter unless it directly affects care.");
        }
    }

    private static IEnumerable<(int, int, string, string?)> MatchPronounRulePerParagraph(string letter, JsonElement config)
    {
        var paragraphs = letter.Split(new[] { "\n\n" }, StringSplitOptions.None);
        var cursor = 0;
        foreach (var paragraph in paragraphs)
        {
            var idx = letter.IndexOf(paragraph, cursor, StringComparison.Ordinal);
            cursor = idx + paragraph.Length;
            var hasPronoun = Regex.IsMatch(paragraph, @"\b(he|she|they)\b", RegexOptions.IgnoreCase);
            var hasName = Regex.IsMatch(paragraph, @"\b(Mr|Mrs|Ms|Dr)\b", RegexOptions.IgnoreCase);
            if (hasPronoun && !hasName)
            {
                yield return (idx, idx + Math.Min(paragraph.Length, 240), paragraph, "Re-anchor the paragraph with the patient title (Mr/Mrs/Ms) before resuming pronouns.");
            }
        }
    }
}
