using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Rulebooks;

public interface IWritingRulebookCoverageValidator
{
    void ValidateForImport(string kind, string profession, JsonElement root);
    void ValidateForPublish(RulebookVersion version, IReadOnlyList<RulebookRuleRow> rules);
    void ValidateBook(OetRulebook book);
    string CanonicalChecksum(ExamProfession profession);
}

public sealed class WritingRulebookCoverageValidator(RulebookLoader embeddedLoader) : IWritingRulebookCoverageValidator
{
    private sealed record CandidateRule(
        string Id,
        string Section,
        string Title,
        string Body,
        string Severity,
        string? CheckId,
        string? ForbiddenPatternsJson);

    public void ValidateForImport(string kind, string profession, JsonElement root)
    {
        if (!IsWriting(kind)) return;
        var parsedProfession = ParseProfession(profession);
        if (!root.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
        {
            throw ApiException.Validation("writing_rulebook_coverage_failed", "Writing rulebook import must include a rules array.");
        }

        var candidates = rulesEl.EnumerateArray()
            .Select(rule => new CandidateRule(
                RequiredString(rule, "id"),
                RequiredString(rule, "section"),
                RequiredString(rule, "title"),
                RequiredString(rule, "body"),
                RequiredString(rule, "severity").Trim().ToLowerInvariant(),
                OptionalString(rule, "checkId"),
                rule.TryGetProperty("forbiddenPatterns", out var fp) && fp.ValueKind != JsonValueKind.Null ? fp.GetRawText() : null))
            .ToList();

        ValidateCandidates(parsedProfession, candidates);
    }

    public void ValidateForPublish(RulebookVersion version, IReadOnlyList<RulebookRuleRow> rules)
    {
        if (!IsWriting(version.Kind)) return;
        var profession = ParseProfession(version.Profession);
        var candidates = rules.Select(rule => new CandidateRule(
            rule.Code,
            rule.SectionCode,
            rule.Title,
            rule.Body,
            rule.Severity.Trim().ToLowerInvariant(),
            rule.CheckId,
            rule.ForbiddenPatternsJson)).ToList();

        ValidateCandidates(profession, candidates);
    }

    public void ValidateBook(OetRulebook book)
    {
        if (book.Kind != RuleKind.Writing) return;
        var candidates = book.Rules.Select(rule => new CandidateRule(
            rule.Id,
            rule.Section,
            rule.Title,
            rule.Body,
            rule.Severity.ToString().ToLowerInvariant(),
            rule.CheckId,
            rule.ForbiddenPatterns is { Count: > 0 } ? JsonSerializer.Serialize(rule.ForbiddenPatterns) : null)).ToList();

        ValidateCandidates(book.Profession, candidates);
    }

    public string CanonicalChecksum(ExamProfession profession)
    {
        var canonical = embeddedLoader.Load(RuleKind.Writing, profession);
        var payload = string.Join('\n', canonical.Rules.Select(rule =>
            $"{rule.Id}|{rule.Section}|{rule.Severity.ToString().ToLowerInvariant()}|{rule.CheckId ?? string.Empty}|{rule.ForbiddenPatterns?.Count ?? 0}"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void ValidateCandidates(ExamProfession profession, IReadOnlyList<CandidateRule> candidates)
    {
        var canonical = embeddedLoader.Load(RuleKind.Writing, profession);
        var canonicalById = canonical.Rules.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        var candidateById = new Dictionary<string, CandidateRule>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();

        foreach (var candidate in candidates)
        {
            if (!candidateById.TryAdd(candidate.Id, candidate))
            {
                issues.Add($"{candidate.Id}: duplicate rule id");
            }
        }

        foreach (var rule in canonical.Rules)
        {
            if (!candidateById.ContainsKey(rule.Id)) issues.Add($"{rule.Id}: missing canonical rule");
        }

        foreach (var candidate in candidates)
        {
            if (!canonicalById.TryGetValue(candidate.Id, out var canonicalRule))
            {
                issues.Add($"{candidate.Id}: extra rule outside canonical 172-rule baseline");
                continue;
            }

            var expectedSeverity = canonicalRule.Severity.ToString().ToLowerInvariant();
            if (!string.Equals(candidate.Severity, expectedSeverity, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{candidate.Id}: severity '{candidate.Severity}' differs from canonical '{expectedSeverity}'");
            }

            if (!string.Equals(candidate.Section, canonicalRule.Section, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{candidate.Id}: section '{candidate.Section}' differs from canonical '{canonicalRule.Section}'");
            }

            if (string.IsNullOrWhiteSpace(candidate.Title)) issues.Add($"{candidate.Id}: title is required");
            if (string.IsNullOrWhiteSpace(candidate.Body)) issues.Add($"{candidate.Id}: body is required");

            var expectedCheckId = NormalizeNullable(canonicalRule.CheckId);
            var candidateCheckId = NormalizeNullable(candidate.CheckId);
            if (!string.Equals(candidateCheckId, expectedCheckId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{candidate.Id}: checkId '{candidateCheckId}' differs from canonical '{expectedCheckId}'");
            }

            if (!string.IsNullOrWhiteSpace(candidate.CheckId) && !WritingRuleEngine.IsSupportedCheckId(candidate.CheckId))
            {
                issues.Add($"{candidate.Id}: unsupported checkId '{candidate.CheckId}'");
            }

            var candidateForbiddenPatterns = ReadForbiddenPatterns(candidate.Id, candidate.ForbiddenPatternsJson, issues);
            var canonicalForbiddenPatterns = canonicalRule.ForbiddenPatterns ?? new List<string>();
            if (!candidateForbiddenPatterns.SequenceEqual(canonicalForbiddenPatterns, StringComparer.Ordinal))
            {
                issues.Add($"{candidate.Id}: forbiddenPatterns differ from canonical contract");
            }

            var hasForbidden = candidateForbiddenPatterns.Count > 0;
            var hasDeterministic = !string.IsNullOrWhiteSpace(candidate.CheckId);
            var hasStructuredAi = !string.IsNullOrWhiteSpace(candidate.Title) && !string.IsNullOrWhiteSpace(candidate.Body);
            if (expectedSeverity == "critical" && !hasDeterministic && !hasForbidden && !hasStructuredAi)
            {
                issues.Add($"{candidate.Id}: critical rule lacks deterministic, forbidden-pattern, or structured AI coverage");
            }
        }

        if (candidateById.Count != canonical.Rules.Count)
        {
            issues.Add($"rule count {candidateById.Count} differs from canonical {canonical.Rules.Count}");
        }

        if (issues.Count > 0)
        {
            var checksum = CanonicalChecksum(profession);
            var sample = string.Join("; ", issues.Take(8));
            throw ApiException.Validation(
                "writing_rulebook_coverage_failed",
                $"Writing rulebook coverage gate failed ({issues.Count} issue(s), canonical checksum {checksum}): {sample}");
        }
    }

    private static IReadOnlyList<string> ReadForbiddenPatterns(string ruleId, string? raw, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"{ruleId}: forbiddenPatterns must be an array");
                return [];
            }

            var patterns = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    issues.Add($"{ruleId}: forbiddenPatterns entries must be non-empty strings");
                    return [];
                }
                patterns.Add(item.GetString()!);
            }

            return patterns;
        }
        catch (JsonException ex)
        {
            issues.Add($"{ruleId}: forbiddenPatterns JSON is invalid ({ex.Message})");
            return [];
        }
    }

    private static string NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool IsWriting(string? kind)
        => string.Equals(kind?.Trim(), "writing", StringComparison.OrdinalIgnoreCase);

    private static ExamProfession ParseProfession(string raw)
    {
        if (RulebookProfessionParser.TryParse(raw, out var profession)) return profession;
        throw ApiException.Validation("invalid_profession", $"Invalid Writing profession '{raw}'.");
    }

    private static string RequiredString(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw ApiException.Validation("missing_field", $"Missing or non-string '{field}'.");

    private static string? OptionalString(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}