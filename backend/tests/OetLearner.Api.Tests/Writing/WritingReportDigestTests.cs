using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner review 5 Oct 2026: three genuinely different priorities, short per-criterion
/// summaries, no internal labels and no "Exemplar". The fixtures are REAL saved
/// grader completions (tests/writing-regression/runs, claude-opus-5-5): realistic
/// letters with a mix of critical, major and minor findings, several per criterion.
/// </summary>
public sealed class WritingReportDigestTests
{
    private sealed record RecordedFinding(string RuleId, string Severity, string Message, string? Quote, string? Fix, string Criterion);

    private static readonly string[] Criteria =
        ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    private static readonly Regex Leaks = new(
        @"^R\d{1,2}[:.]|\([A-Z]{1,4}(?:-[A-Z]{1,3})?-\d|This affects|[Ee]xemplar", RegexOptions.CultureInvariant);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static Dictionary<string, List<RecordedFinding>> LoadRuns()
    {
        var runs = new Dictionary<string, List<RecordedFinding>>();
        var dir = Path.Combine(FindRepoRoot(), "tests", "writing-regression", "runs");
        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("parsed", out var parsed)
                || parsed.ValueKind != JsonValueKind.Object
                || !parsed.TryGetProperty("findings", out var findings)
                || findings.ValueKind != JsonValueKind.Array) continue;
            var list = new List<RecordedFinding>();
            foreach (var f in findings.EnumerateArray())
            {
                string? S(string name) => f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var criterion = S("criterionCode") ?? S("criterion");
                if (criterion is null || S("message") is null) continue;
                list.Add(new RecordedFinding(S("ruleId") ?? string.Empty, S("severity") ?? "major", S("message")!, S("quote"), S("fixSuggestion"), criterion));
            }
            if (list.Count > 0) runs[Path.GetFileNameWithoutExtension(path)] = list;
        }
        return runs;
    }

    private static WritingDigestFinding ToDigest(RecordedFinding f)
        => new(f.RuleId, f.Severity, f.Message, f.Quote, f.Fix, f.Criterion, null, true);

    [Fact]
    public void The_recorded_corpus_really_mixes_major_and_minor_findings()
    {
        var runs = LoadRuns();

        Assert.True(runs.Count >= 20, $"expected the saved grader runs, found {runs.Count}");
        var mixed = runs.Values.Count(findings =>
            findings.Any(f => f.Severity is "critical" or "major") && findings.Any(f => f.Severity == "minor"));
        Assert.True(mixed >= 5, $"only {mixed} runs mix major and minor findings");
        Assert.Contains(runs.Values, findings => findings.Count >= 25);
    }

    [Fact]
    public void Priorities_are_distinct_for_every_recorded_letter()
    {
        foreach (var (letter, findings) in LoadRuns())
        {
            var priorities = WritingReportDigest.ComposePriorities(findings.Select(ToDigest));

            Assert.InRange(priorities.Count, 1, 3);
            Assert.Equal(priorities.Count, priorities.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            // At most one priority is about the Purpose criterion.
            var purposePriorities = priorities.Count(p => findings.Any(f =>
                f.Criterion == "purpose" && p.StartsWith(f.RuleId + ": ", StringComparison.Ordinal)));
            Assert.True(purposePriorities <= 1, $"{letter}: {purposePriorities} purpose priorities");
            // No rule id twice.
            var ruleIds = priorities.Select(p => p.Split(':', 2)[0]).ToList();
            Assert.Equal(ruleIds.Count, ruleIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var priority in priorities)
            {
                Assert.True(priority.Length <= WritingReportDigest.PriorityMaxChars + 20, $"{letter}: priority too long ({priority.Length})");
                Assert.DoesNotMatch(Leaks, priority.Contains(": ") ? priority[(priority.IndexOf(": ", StringComparison.Ordinal) + 2)..] : priority);
            }
        }
    }

    [Fact]
    public void The_old_top_three_repeated_purpose_and_the_digest_does_not()
    {
        // Real completion for the nursing letter that showed "request missing" twice.
        var findings = LoadRuns()["WB81224E35-unseen-r1-1"];
        var oldTop = findings
            .OrderBy(f => f.Severity switch { "critical" => 0, "major" => 1, "minor" => 2, _ => 3 })
            .Take(3).ToList();
        Assert.True(oldTop.Count(f => f.Criterion == "purpose") >= 2, "fixture no longer shows the old duplicate");

        var priorities = WritingReportDigest.ComposePriorities(findings.Select(ToDigest));

        Assert.Equal(1, priorities.Count(p => findings.Any(f => f.Criterion == "purpose" && p.StartsWith(f.RuleId + ": ", StringComparison.Ordinal))));
        Assert.True(priorities.Count >= 2);
    }

    [Fact]
    public void Criterion_summaries_are_short_clean_and_follow_the_most_important_finding()
    {
        foreach (var (letter, findings) in LoadRuns())
        {
            foreach (var criterion in Criteria)
            {
                var own = WritingReportDigest
                    .Ordered(findings.Where(f => f.Criterion == criterion).Select(ToDigest)).ToList();
                var summary = WritingReportDigest.CriterionSummary(own);

                if (own.Count == 0) { Assert.Null(summary); continue; }
                Assert.False(string.IsNullOrWhiteSpace(summary), $"{letter}/{criterion}: empty summary");
                Assert.True(summary!.Length <= WritingReportDigest.SummaryMaxChars, $"{letter}/{criterion}: {summary.Length} chars");
                Assert.DoesNotMatch(Leaks, summary);
                var lead = WritingReportDigest.Clean(own[0].Message);
                Assert.StartsWith(lead[..Math.Min(20, lead.Length)], summary);
            }
        }
    }

    [Fact]
    public void The_stored_report_is_repaired_on_read_priorities_and_summaries_included()
    {
        // WB81224E35: purpose twice + others, mapped through the real builder and Map.
        var findings = LoadRuns()["WB81224E35-unseen-r1-1"];
        var ruleFindings = findings.Select((f, i) => new WritingAssessmentRuleFinding(
            f.RuleId, f.Criterion, f.Severity, f.Message, f.Quote, f.Fix, i * 10, i * 10 + 5, f.Criterion)).ToList();
        var preflight = new WritingAssessmentPreflightResult(
            true, WritingAssessmentV11Status.AwaitingPreflight, [], [], ["nursing:transfer:v1"],
            "nursing", "transfer", "v1", "Write to the community nurse.", "Notes.");
        var built = WritingAssessmentReportBuilder.Build(new WritingAssessmentReportBuildInput(
            Guid.NewGuid(), "hash", "The candidate letter", preflight, ruleFindings,
            WritingFactMapService.Build("Notes.", "The letter.", "nurse"),
            WritingAssessmentReportBuilder.DefaultCriteria(2, 5, 5, 6, 6, 6), 380, "writing.score.v11", "calibration-v1"));
        built.Report.Status = WritingAssessmentV11Status.CandidateReady;
        built.Report.CandidateReportVisible = true;

        // Simulate a report stored before the digest: duplicate priorities in the JSON.
        built.Report.TopPrioritiesJson = JsonSerializer.Serialize(new[] { "OA-01: dup", "DH-W-016: dup", "OW-008: dup" });
        var response = WritingAssessmentV11ResultService.Map(built.Report, null);

        Assert.True(response.TopPriorities.Count <= 3);
        Assert.NotEqual("OA-01: dup", response.TopPriorities.First());
        Assert.Equal(1, response.TopPriorities.Count(p => p.StartsWith("OA-01: ") || p.StartsWith("DH-W-016: ")));
        Assert.All(response.Criteria.Where(c => ruleFindings.Any(f => f.PrimaryCriterionCode == c.CriterionCode)),
            c => Assert.InRange(c.Summary!.Length, 1, WritingReportDigest.SummaryMaxChars));
        Assert.All(response.Criteria.Where(c => ruleFindings.All(f => f.PrimaryCriterionCode != c.CriterionCode)),
            c => Assert.Null(c.Summary));
    }

    [Fact]
    public void Fewer_than_three_priorities_are_returned_rather_than_a_repeat()
    {
        WritingDigestFinding F(string rule, string severity, string message, string criterion, string? quote = null)
            => new(rule, severity, message, quote, null, criterion, null, true);

        var priorities = WritingReportDigest.ComposePriorities(
        [
            F("AI.purpose", "critical", "The purpose is never stated.", "purpose"),
            F("OA-01", "major", "The request is missing. This affects Purpose and Content.", "content"),
            F("R03.4", "major", "Allergy omitted.", "content", "no allergy line"),
            F("R03.9", "minor", "Allergy omitted again.", "content", "no allergy line"),
        ]);

        Assert.Equal(new[] { "AI.purpose: The purpose is never stated.", "R03.4: Allergy omitted." }, priorities.ToArray());
    }

    [Fact]
    public void Severity_comes_first_then_score_bearing_then_position()
    {
        WritingDigestFinding F(string rule, string severity, string message, bool scoreBearing, int offset, string criterion)
            => new(rule, severity, message, null, null, criterion, offset, scoreBearing);

        var priorities = WritingReportDigest.ComposePriorities(
        [
            F("COACH", "major", "Salutation spacing breaks the house layout.", false, 1, "genre_style"),
            F("MINOR", "minor", "Comma missing after however.", true, 0, "language"),
            F("SCORE", "major", "Penicillin allergy omitted from the opening paragraph.", true, 9, "content"),
            F("CRIT", "critical", "Discharge medication frequency contradicts the notes.", true, 50, "organisation_layout"),
        ]);

        Assert.Equal(new[] { "CRIT", "SCORE", "COACH" }, priorities.Select(p => p.Split(':')[0]).ToArray());
    }

    [Theory]
    [InlineData("R1: The request is missing. This affects Purpose and Content.", "The request is missing.")]
    [InlineData("The wording changes meaning (OW-005, DH-W-016, OW-016). Content criterion.", "The wording changes meaning.")]
    [InlineData("Compare with the exemplar letter.", "Compare with the model answer letter.")]
    [InlineData("  Spaced \n out   text.  ", "Spaced out text.")]
    public void Clean_removes_internal_labels_rule_ids_and_criterion_tails(string raw, string expected)
        => Assert.Equal(expected, WritingReportDigest.Clean(raw));

    [Fact]
    public void Clip_keeps_whole_sentences_and_word_clips_an_overlong_first_sentence()
    {
        Assert.Equal("One. Two.", WritingReportDigest.Clip("One. Two. Three is much longer than the rest of it.", 12));
        var clipped = WritingReportDigest.Clip(string.Join(' ', Enumerable.Repeat("word", 100)), 40);
        Assert.True(clipped.Length <= 40);
        Assert.EndsWith("…", clipped);
    }
}
