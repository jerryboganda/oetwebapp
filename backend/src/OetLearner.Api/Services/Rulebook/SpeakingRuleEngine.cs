using System.Text.Json;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// .NET mirror of lib/rulebook/speaking-rules.ts. Mirrors the jargon,
/// monologue, weight-sensitivity, smoking-ladder, over-diagnosis,
/// stage-coverage, and Breaking Bad News detectors.
/// </summary>
public sealed class SpeakingRuleEngine(IRulebookLoader loader)
{
    public IReadOnlyList<LintFinding> Audit(SpeakingAuditInput input)
    {
        var book = loader.Load(RuleKind.Speaking, input.Profession);
        var applicable = WritingRuleEngine.RulesApplicableTo(book, input.CardType);
        var findings = new List<LintFinding>();

        foreach (var rule in applicable)
        {
            if (!string.IsNullOrWhiteSpace(rule.CheckId))
            {
                var det = DetectorFor(rule.CheckId!);
                if (det is not null) findings.AddRange(det(rule, input, book));
            }
            if (rule.ForbiddenPatterns is { Count: > 0 })
            {
                findings.AddRange(RunForbidden(rule, input));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<LintFinding>();
        foreach (var f in findings)
        {
            var key = $"{f.RuleId}|{f.Message}|{f.Quote}";
            if (!seen.Add(key)) continue;
            unique.Add(f);
        }
        unique.Sort((a, b) => SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity)));
        return unique;
    }

    private delegate IEnumerable<LintFinding> Detector(OetRule rule, SpeakingAuditInput input, OetRulebook book);

    private Detector? DetectorFor(string checkId) => checkId switch
    {
        "speaking_jargon_detector" => DetectJargon,
        "speaking_stage_coverage" => DetectStageCoverage,
        "speaking_monologue_detector" => DetectMonologue,
        "speaking_weight_sensitivity" => DetectWeightSensitivity,
        "speaking_smoking_ladder_order" => DetectSmokingLadder,
        "speaking_no_overdiagnosis" => DetectOverdiagnosis,
        "speaking_bbn_protocol_order" => DetectBbnProtocolOrder,
        "speaking_bbn_silence" => DetectBbnSilence,
        _ => null,
    };

    // -- detectors ---------------------------------------------------------

    private static IEnumerable<LintFinding> DetectJargon(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        if (book.Tables is null) yield break;
        if (!book.Tables.Value.TryGetProperty("forbiddenJargonTokens", out var tokensEl)) yield break;

        var glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (book.Tables.Value.TryGetProperty("laymanGlossary", out var gEl) && gEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in gEl.EnumerateArray())
            {
                var med = entry.GetProperty("medical").GetString();
                var plain = entry.GetProperty("plain").GetString();
                if (!string.IsNullOrEmpty(med) && !string.IsNullOrEmpty(plain)) glossary[med] = plain;
            }
        }

        var produced = 0;
        foreach (var turn in input.Transcript.Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var tEl in tokensEl.EnumerateArray())
            {
                var token = tEl.GetString();
                if (string.IsNullOrEmpty(token)) continue;
                var re = new Regex($@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase);
                var m = re.Match(turn.Text);
                if (!m.Success) continue;
                var context = turn.Text.Substring(m.Index, Math.Min(80, turn.Text.Length - m.Index)).ToLowerInvariant();
                var hasExplanation = Regex.IsMatch(context, @"i'?m sorry for the medical term|which means|that is|in plain|basically|in other words", RegexOptions.IgnoreCase);
                if (hasExplanation) continue;
                var fix = glossary.TryGetValue(token, out var p) ? p : "plain English";
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Candidate used medical jargon '{token}' without a plain-English explanation.",
                    Quote: turn.Text.Substring(Math.Max(0, m.Index - 10), Math.Min(turn.Text.Length - Math.Max(0, m.Index - 10), token.Length + 20)),
                    FixSuggestion: fix);
                if (++produced >= 10) yield break;
            }
        }
    }

    private static IEnumerable<LintFinding> DetectStageCoverage(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        var combined = string.Join("\n", input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Text));

        var empathy = new Regex(@"(sorry to hear|must have been|i can imagine|that sounds)", RegexOptions.IgnoreCase);
        var recap = new Regex(@"(to recap|to summari[sz]e|let me summari[sz]e|so to recap)", RegexOptions.IgnoreCase);
        var closure = new Regex(@"(don'?t hesitate|please come back|take care|all the best|wish you well)", RegexOptions.IgnoreCase);

        if (rule.Id == "RULE_15" && !empathy.IsMatch(combined))
            yield return new LintFinding(rule.Id, rule.Severity, "No empathy statement detected before moving to questions (RULE 15).");
        if (rule.Id == "RULE_20" && !recap.IsMatch(combined))
            yield return new LintFinding(rule.Id, rule.Severity, "No recap/summary detected before closing (RULE 20).");
        if (rule.Id == "RULE_21" && !closure.IsMatch(combined))
            yield return new LintFinding(rule.Id, rule.Severity, "No warm closure detected at the end of the consultation (RULE 21).");
    }

    private static IEnumerable<LintFinding> DetectMonologue(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        var max = 120;
        if (rule.Params.HasValue && rule.Params.Value.TryGetProperty("maxConsecutiveCandidateWords", out var mc) && mc.TryGetInt32(out var m)) max = m;

        var run = 0;
        var longest = 0;
        foreach (var t in input.Transcript)
        {
            if (string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            {
                run += Regex.Matches(t.Text, @"\b\w+\b").Count;
                if (run > longest) longest = run;
            }
            else run = 0;
        }
        if (longest > max)
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Candidate spoke for {longest} consecutive words without patient input. Keep the conversation a ping-pong dialogue (RULE 22).");
    }

    private static IEnumerable<LintFinding> DetectWeightSensitivity(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        var combined = string.Join("\n", input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Text));
        if (Regex.IsMatch(combined, @"\bwhat is your weight\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(combined, @"\bhow much do you weigh\b", RegexOptions.IgnoreCase))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Never ask 'What is your weight?' directly. Approach the topic sensitively (RULE 23).");
    }

    private static IEnumerable<LintFinding> DetectSmokingLadder(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        var turns = input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cessationIdx = -1;
        var reductionIdx = -1;
        for (int i = 0; i < turns.Count; i++)
        {
            var text = turns[i].Text.ToLowerInvariant();
            if (cessationIdx == -1 && Regex.IsMatch(text, @"\b(quit smoking|stop smoking|cessation|completely stop|quit completely)\b"))
                cessationIdx = i;
            if (reductionIdx == -1 && Regex.IsMatch(text, @"\b(reduce|cut down|smoke less|fewer cigarettes)\b"))
                reductionIdx = i;
        }
        if (reductionIdx != -1 && (cessationIdx == -1 || reductionIdx < cessationIdx))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Smoking negotiation must start with complete cessation (Black Area) before offering reduction (Grey Zone) — you started with 'reduce' (RULE 27).");
    }

    private static IEnumerable<LintFinding> DetectOverdiagnosis(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        var combined = string.Join("\n", input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Text)).ToLowerInvariant();
        if (Regex.IsMatch(combined, @"\byou have hypertension\b"))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not diagnose hypertension from a single reading (RULE 32).");
        if (Regex.IsMatch(combined, @"\byou have diabetes\b") && !Regex.IsMatch(combined, @"\bdiagnosed (with|as)\b"))
            yield return new LintFinding(rule.Id, rule.Severity,
                "Do not diagnose diabetes from one reading (RULE 32).");
    }

    private static IEnumerable<LintFinding> DetectBbnProtocolOrder(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        if (!string.Equals(input.CardType, "breaking_bad_news", StringComparison.OrdinalIgnoreCase)) yield break;

        var step = 0;
        if (rule.Params.HasValue && rule.Params.Value.TryGetProperty("step", out var st) && st.TryGetInt32(out var s)) step = s;

        var markers = new Dictionary<int, Regex>
        {
            [1] = new Regex(@"(anyone you'd like to have here|someone with you|support system|partner or family with you)", RegexOptions.IgnoreCase),
            [2] = new Regex(@"(not quite what we had hoped|something more serious|results aren'?t quite|brace yourself)", RegexOptions.IgnoreCase),
            [3] = new Regex(@"(showing signs of cancer|i(?:'| a)m very sorry to tell you|diagnosed with|results have come back positive)", RegexOptions.IgnoreCase),
            [5] = new Regex(@"(i can see this is|take all the time you need|lot to take in|i'?m so sorry)", RegexOptions.IgnoreCase),
            [6] = new Regex(@"(early stage|effective treatment|we have options|there is hope|treatment plan)", RegexOptions.IgnoreCase),
            [7] = new Regex(@"(here for you|someone who can be with you today|call me any time|my number|ongoing support)", RegexOptions.IgnoreCase),
        };
        if (!markers.TryGetValue(step, out var re)) yield break;

        var text = string.Join("\n", input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Text));
        if (!re.IsMatch(text))
        {
            var names = new Dictionary<int, string>
            {
                [1] = "Step 1 — ask about support system",
                [2] = "Step 2 — warning shots",
                [3] = "Step 3 — deliver the diagnosis",
                [5] = "Step 5 — respond to the emotional reaction",
                [6] = "Step 6 — give hope and next steps",
                [7] = "Step 7 — end with support system",
            };
            var label = names.GetValueOrDefault(step, $"Step {step}");
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Breaking Bad News protocol: {label} not detected ({rule.Id}).");
        }
    }

    private static IEnumerable<LintFinding> DetectBbnSilence(OetRule rule, SpeakingAuditInput input, OetRulebook book)
    {
        if (!string.Equals(input.CardType, "breaking_bad_news", StringComparison.OrdinalIgnoreCase)) yield break;
        var minSec = 3;
        if (rule.Params.HasValue && rule.Params.Value.TryGetProperty("minSilenceSeconds", out var mn) && mn.TryGetInt32(out var m)) minSec = m;

        var observed = input.SilenceAfterDiagnosisMs;
        if (observed is null)
            yield return new LintFinding(rule.Id, rule.Severity,
                "Silence after diagnosis could not be measured — ensure audio timing is captured. Target 3–4 seconds (RULE 44).");
        else if (observed < minSec * 1000)
            yield return new LintFinding(rule.Id, rule.Severity,
                $"Silence after diagnosis was {(observed.Value / 1000.0):F1}s — below the 3–4 second minimum (RULE 44).");
    }

    private static IEnumerable<LintFinding> RunForbidden(OetRule rule, SpeakingAuditInput input)
    {
        if (rule.ForbiddenPatterns is null) yield break;
        var text = string.Join("\n", input.Transcript
            .Where(t => string.Equals(t.Speaker, "candidate", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Text));
        foreach (var pat in rule.ForbiddenPatterns)
        {
            Regex re;
            try { re = new Regex(pat, RegexOptions.IgnoreCase); }
            catch { continue; }
            var m = re.Match(text);
            if (m.Success)
                yield return new LintFinding(rule.Id, rule.Severity,
                    $"Rule {rule.Id}: \"{m.Value}\" is not acceptable.",
                    Quote: m.Value, Start: m.Index, End: m.Index + m.Length);
        }
    }

    private static int SeverityRank(RuleSeverity s) => s switch
    {
        RuleSeverity.Critical => 0,
        RuleSeverity.Major => 1,
        RuleSeverity.Minor => 2,
        _ => 3,
    };
}
