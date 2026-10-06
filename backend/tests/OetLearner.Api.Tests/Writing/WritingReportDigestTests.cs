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
        @"^R\d{1,2}[:.\s]|\([A-Z]{1,4}\d?(?:-[A-Z]{1,3})?-\d|This affects|[Ee]xemplar", RegexOptions.CultureInvariant);

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
        // tests/writing-regression/runs is gitignored, so the findings of 13 real runs are checked in as a fixture.
        var runs = new Dictionary<string, List<RecordedFinding>>();
        var path = Path.Combine(FindRepoRoot(), "backend", "tests", "OetLearner.Api.Tests", "Writing", "Fixtures", "recorded-grader-findings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var run in doc.RootElement.GetProperty("runs").EnumerateObject())
        {
            var list = new List<RecordedFinding>();
            foreach (var f in run.Value.EnumerateArray())
            {
                string? S(string name) => f.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                list.Add(new RecordedFinding(S("ruleId") ?? string.Empty, S("severity") ?? "major", S("message")!, S("quote"), S("fixSuggestion"), S("criterionCode")!));
            }
            runs[run.Name] = list;
        }
        return runs;
    }

    private static WritingDigestFinding ToDigest(RecordedFinding f)
        => new(f.RuleId, f.Severity, f.Message, f.Quote, f.Fix, f.Criterion, null, true);

    // The clean priority text a Purpose finding of the letter would produce (priorities carry no label).
    private static List<string> PurposeTexts(List<RecordedFinding> findings)
        => findings
            .Where(f => f.Criterion == "purpose")
            .Select(f => WritingReportDigest.Clip(f.Message, WritingReportDigest.PriorityMaxChars))
            .ToList();

    [Fact]
    public void The_recorded_corpus_really_mixes_major_and_minor_findings()
    {
        var runs = LoadRuns();

        Assert.True(runs.Count >= 10, $"expected the recorded grader runs, found {runs.Count}");
        var mixed = runs.Values.Count(findings =>
            findings.Any(f => f.Severity is "critical" or "major") && findings.Any(f => f.Severity == "minor"));
        Assert.True(mixed >= 8, $"only {mixed} runs mix major and minor findings");
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
            var purposePriorities = priorities.Count(p => PurposeTexts(findings).Contains(p, StringComparer.OrdinalIgnoreCase));
            Assert.True(purposePriorities <= 1, $"{letter}: {purposePriorities} purpose priorities");
            foreach (var priority in priorities)
            {
                Assert.True(priority.Length <= WritingReportDigest.PriorityMaxChars + 20, $"{letter}: priority too long ({priority.Length})");
                // Priorities are plain sentences: no rule label, id or internal token.
                Assert.DoesNotMatch(Leaks, priority);
                Assert.False(WritingCandidateText.ContainsInternalToken(priority), $"{letter}: internal token in '{priority}'");
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

        Assert.Equal(1, priorities.Count(p => PurposeTexts(findings).Contains(p, StringComparer.OrdinalIgnoreCase)));
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

        // The full corrections list is tidied too: no rule labels, rule-id lists or "This affects" tails.
        Assert.All(response.Errors, e =>
        {
            Assert.DoesNotMatch(Leaks, e.WhyItMatters);
            Assert.DoesNotMatch(Leaks, e.Correction);
        });
        Assert.InRange(response.TopPriorities.Count, 1, 3);
        // The stored JSON is ignored when finding rows exist, and the projection sends plain sentences only.
        Assert.DoesNotContain("dup", response.TopPriorities);
        Assert.All(response.TopPriorities, p => Assert.False(WritingCandidateText.ContainsInternalToken(p), p));
        Assert.All(response.Errors, e =>
        {
            Assert.Null(e.RuleSource);
            Assert.Null(e.ProvenanceTag);
            Assert.Null(e.CandidateBehavior);
            Assert.Contains(e.Severity, new[] { "critical", "major", "minor", "advisory" });
        });
        Assert.Empty(response.BlockingCodes);
        Assert.Equal(string.Empty, response.ModelVersion);
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
            F("R03.4", "major", "The allergy history is omitted.", "content", "no allergy line"),
            F("R03.9", "minor", "The allergy history is omitted again.", "content", "no allergy line"),
        ]);

        // One Purpose priority at most, a repeat of the same wording collapses, and the text carries no label.
        Assert.Equal(new[] { "The purpose is never stated.", "The allergy history is omitted." }, priorities.ToArray());
    }

    [Fact]
    public void Advisory_and_coaching_findings_never_take_a_priority_slot()
    {
        WritingDigestFinding F(string severity, string message, bool scoreBearing)
            => new("AI:OW-007", severity, message, null, null, "language", null, scoreBearing);

        Assert.Empty(WritingReportDigest.ComposePriorities(
        [
            F("info", "Routine linker choices read more formally with a direct sentence.", true),
            F("advisory", "Blank line spacing differs from the usual house layout.", true),
            F("major", "The salutation spacing breaks the house layout.", false),
        ]));
    }

    [Fact]
    public void A_grader_cited_rule_id_missing_from_the_registry_is_score_bearing()
    {
        Assert.True(WritingReportDigest.IsScoreBearing("AI:OW-007"));
        Assert.True(WritingReportDigest.IsScoreBearing("AI.language"));
        Assert.False(WritingReportDigest.IsScoreBearing("AI:OW-007", "info"));
        // A registered coaching-only check stays advisory even when the grader cites it.
        Assert.False(WritingReportDigest.IsScoreBearing("BUILTIN.closure_contact_offer"));
    }

    [Fact]
    public void Stored_priorities_are_repaired_without_labels_or_repeats()
    {
        var repaired = WritingReportDigest.CleanStoredPriorities(
        [
            "AI:OW-007: The request to the reader is missing (OW-005). This affects Content.",
            "BUILTIN.numerical_values_have_units: The request to the reader is missing.",
            "OA-01: dup",
            "Purpose: state the reason for writing in the opening line.",
        ]);

        Assert.Equal(
            new[] { "The request to the reader is missing.", "Purpose: state the reason for writing in the opening line." },
            repaired.ToArray());
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

        // The coaching-only COACH finding is excluded; order is severity first, then position.
        Assert.Equal(
            new[]
            {
                "Discharge medication frequency contradicts the notes.",
                "Penicillin allergy omitted from the opening paragraph.",
                "Comma missing after however.",
            },
            priorities.ToArray());
    }

    [Theory]
    [InlineData("R1: The request is missing. This affects Purpose and Content.", "The request is missing.")]
    [InlineData("The wording changes meaning (OW-005, DH-W-016, OW-016). Content criterion.", "The wording changes meaning.")]
    [InlineData("Compare with the exemplar letter.", "Compare with the model answer letter.")]
    [InlineData("  Spaced \n out   text.  ", "Spaced out text.")]
    [InlineData("R4 medication reconciliation is missing (OA4-01, OW-023).", "medication reconciliation is missing.")]
    [InlineData("The action is missing. This affects Purpose and Content (Addendum Five R1: a required action is absent).", "The action is missing.")]
    [InlineData("Wording is informal (PRD-OT-08). Language criterion. Next sentence stays.", "Wording is informal. Next sentence stays.")]
    [InlineData("Detail is excess (OW-027, advisory only) for this reader.", "Detail is excess for this reader.")]
    [InlineData("BUILTIN.linker_avoid_words: avoid starting a sentence with But.", "avoid starting a sentence with But.")]
    [InlineData("The dose is correct per G-W-117.", "The dose is correct.")]
    [InlineData("The wording violates BUILTIN.numerical_values_have_units for the BMI.", "The wording violates the relevant guideline for the BMI.")]
    // Clinical tokens that only look like ids are left alone.
    [InlineData("Vitamin B-12 and G-6-PD were checked during the COVID-19 visit.", "Vitamin B-12 and G-6-PD were checked during the COVID-19 visit.")]
    public void Clean_removes_internal_labels_rule_ids_and_criterion_tails(string raw, string expected)
        => Assert.Equal(expected, WritingReportDigest.Clean(raw));

    [Fact]
    public void Tidy_never_returns_the_original_text()
    {
        Assert.Equal("Rewrite it clearly.", WritingReportDigest.Tidy("OA-01 (OW-005)", "Rewrite it clearly."));
        Assert.Equal(string.Empty, WritingReportDigest.Tidy("BUILTIN.internal_detector_error"));
    }

    [Theory]
    [InlineData("Missing the request. See OA2-07.", true)]
    [InlineData("claude-opus-5-5 graded this letter.", true)]
    [InlineData("The rulebook requires a Re: line.", true)]
    [InlineData("Mr Weir has long been overweight (BMI 28.4).", false)]
    public void ContainsInternalToken_detects_ids_tags_and_internal_words(string text, bool expected)
        => Assert.Equal(expected, WritingCandidateText.ContainsInternalToken(text));

    [Fact]
    public void Clip_keeps_whole_sentences_and_word_clips_an_overlong_first_sentence()
    {
        Assert.Equal("One. Two.", WritingReportDigest.Clip("One. Two. Three is much longer than the rest of it.", 12));
        var clipped = WritingReportDigest.Clip(string.Join(' ', Enumerable.Repeat("word", 100)), 40);
        Assert.True(clipped.Length <= 40);
        Assert.EndsWith("…", clipped);
    }
}
