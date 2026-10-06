using System.Text.RegularExpressions;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing.Review;

/// <summary>
/// The deterministic half of the secondary reviewer: the reviewer PROPOSES (<see cref="WritingReviewDecision"/>), this
/// class DECIDES. Pure: no I/O, no clock, no provider, so re-applying a stored reply reproduces the identical outcome
/// (the resume path depends on it). Every rule below is a limit on what a reply may change:
/// <list type="bullet">
/// <item>(a) criterion scores are bounded (Purpose 0-3, the rest 0-7, scaled 0-500); an out-of-range block is rejected whole;</item>
/// <item>(b) the raw total, the estimated band and the grade letter are always recomputed here, never read from the reply;</item>
/// <item>(c) a score change must be justified by a finding change and moves by a few points per pass;</item>
/// <item>(d) the /500 estimate is holistic: it moves by a capped amount, and a REVIEWER-CHANGED value is held inside a
///     corridor around the raw total. A /500 the reviewer did not touch is never rewritten;</item>
/// <item>(e) finding verdicts (confirmed, false_positive, severity_change, advisory, duplicate);</item>
/// <item>(f) a wording rewrite is accepted only if it stays free of internal tokens and invents no figure or drug;</item>
/// <item>(g) reviewer-added findings are capped, must be anchored in the letter or the case notes, and are never Critical
///     without case-note evidence;</item>
/// <item>(h) omission evidence comes from the fact map, the reviewer only rules each one material or not;</item>
/// <item>(i) every candidate-facing string is scrubbed of internal tokens (the count goes to the admin notes; a leak
///     never fails a run);</item>
/// <item>(j) the caller rebuilds the criterion cards, the priorities and the report rows from the outcome's findings, so
///     the summary, the cards and the corrections cannot contradict each other.</item>
/// </list>
/// There is NO 399 clip anywhere: a 400+ score that fails the enhanced checklist is recalibrated by a further reviewer
/// pass under the same limits, never clipped.
/// </summary>
public static class WritingReviewApplier
{
    private static readonly string[] Criteria =
        ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    private const string GenericMessage =
        "This wording needs to be corrected so that the letter reads as clear, formal clinical English.";

    // ponytail: a figure/drug heuristic, not a clinical parser. Numbers (with an optional unit), and words ending in
    // common drug suffixes. A rewrite using any of them must find it in the letter, case notes, task or the original
    // text. Upgrade path: a structured fact extractor shared with WritingFactMapService.
    private static readonly Regex FactToken = new(
        @"\d+(?:[.,:/-]\d+)*(?:\s?(?:mg|mcg|kg|g|ml|l|mmhg|bpm|iu|units?|cm|mm)\b|\s?%|\s?°\s?c\b)?"
        + @"|\b[a-z]{3,}(?:cillin|mycin|azole|olol|pril|sartan|statin|zepam|formin|prazole|oxacin|cycline|dipine|thiazide|tidine|setron|triptan|fenac|profen|codone|parin|barbital)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LeadingNumber = new(@"^\d+(?:[.,:/-]\d+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed class Row(WritingReviewFinding source)
    {
        public WritingReviewFinding Source { get; } = source;
        public WritingAssessmentRuleFinding Current { get; set; } = source.Finding;
        public bool Handled { get; set; }
        public bool Removed { get; set; }
        public bool Duplicate { get; set; }
        public bool FixRewritten { get; set; }
    }

    public static WritingReviewOutcome Apply(
        WritingReviewRequest request,
        WritingReviewDecision decision,
        WritingReviewOptions policy,
        bool enhancedPass)
    {
        var notes = new WritingReviewAdminNotes
        {
            Version = WritingReviewPrompt.Version,
            Mode = request.Mode.ToString().ToLowerInvariant(),
            Status = "reviewed",
            Decision = decision.Decision,
            Summary = decision.Summary,
            EnhancedNotes = decision.EnhancedNotes,
            PrimaryModel = request.PrimaryModel,
            Primary = request.Primary,
        };
        foreach (var anomaly in decision.Anomalies) notes.Rejected.Add(anomaly);
        if (enhancedPass) notes.Flags.Add("enhanced_pass");

        var rows = request.Findings.Select(f => new Row(f)).ToList();
        var byId = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in rows) byId[existing.Source.Id] = existing;

        var baseHaystack = Strip(request.Letter + "\n" + request.CaseNotesSnapshot + "\n" + request.TaskSnapshot);
        var relieved = new HashSet<string>(StringComparer.Ordinal);
        var overrodeCritical = false;

        // (e) Finding verdicts. Duplicates are applied last, so a duplicate target's own verdict is already known.
        foreach (var verdict in decision.Findings.OrderBy(x => x.Verdict == "duplicate" ? 1 : 0))
        {
            if (!byId.TryGetValue(verdict.Id, out var hit))
            {
                notes.Rejected.Add(verdict.Id + ":unknown_finding_id");
                continue;
            }

            if (hit.Handled)
            {
                notes.Rejected.Add(verdict.Id + ":repeated_verdict");
                continue;
            }

            hit.Handled = true;
            var finding = hit.Current;
            var kind = verdict.Verdict;
            switch (kind)
            {
                case "false_positive":
                    hit.Removed = true;
                    relieved.Add(finding.PrimaryCriterionCode);
                    overrodeCritical |= finding.Severity == "critical";
                    break;

                case "severity_change":
                {
                    var requested = NormaliseSeverity(verdict.Severity);
                    if (requested is null)
                    {
                        notes.Rejected.Add(verdict.Id + ":severity_change_invalid");
                        kind = "confirmed";
                        break;
                    }

                    // The candidate severity doctrine still binds: a reviewer cannot make a coaching-only or
                    // general-English check Critical, nor a registered coaching rule score-bearing.
                    var calibrated = WritingCandidateSeverityPolicy.Calibrate(
                        CheckIdOf(finding), requested, hit.Source.Origin != WritingReviewFindingOrigin.Rule);
                    if (calibrated != requested) notes.Rejected.Add(verdict.Id + ":severity_calibrated_to_" + calibrated);
                    if (calibrated != finding.Severity)
                    {
                        if (Rank(calibrated) > Rank(finding.Severity))
                        {
                            relieved.Add(finding.PrimaryCriterionCode);
                            overrodeCritical |= finding.Severity == "critical";
                        }

                        hit.Current = finding with
                        {
                            Severity = calibrated,
                            CandidateBehavior = calibrated == "info"
                                ? WritingCandidateBehaviors.CoachingOnly
                                : finding.CandidateBehavior,
                        };
                    }

                    break;
                }

                case "advisory":
                    if (finding.Severity == "critical" && finding.ProvenanceTag == WritingProvenanceTags.SourceFactTask)
                    {
                        // A Critical proven by the case notes or the task is never coaching.
                        notes.Rejected.Add(verdict.Id + ":advisory_refused_source_fact");
                        kind = "confirmed";
                        break;
                    }

                    if (finding.Severity != "info")
                    {
                        relieved.Add(finding.PrimaryCriterionCode);
                        overrodeCritical |= finding.Severity == "critical";
                        hit.Current = finding with { Severity = "info", CandidateBehavior = WritingCandidateBehaviors.CoachingOnly };
                    }

                    break;

                case "duplicate":
                    if (string.IsNullOrWhiteSpace(verdict.DuplicateOf)
                        || !byId.TryGetValue(verdict.DuplicateOf.Trim(), out var target)
                        || ReferenceEquals(target, hit)
                        || target.Removed
                        || target.Duplicate)
                    {
                        notes.Rejected.Add(verdict.Id + ":duplicate_target_invalid");
                        kind = "confirmed";
                        break;
                    }

                    hit.Duplicate = true;
                    break;

                case "confirmed":
                    break;

                default:
                    notes.Rejected.Add(verdict.Id + ":unknown_verdict_treated_as_confirmed");
                    kind = "confirmed";
                    break;
            }

            notes.Dispositions.Add(verdict.Id + ":" + kind);
            if (hit.Removed) continue;

            // (f) Wording rewrites.
            var message = AcceptRewrite(verdict.Message, hit.Current.Message, baseHaystack, notes, verdict.Id + ".message");
            var fix = AcceptRewrite(verdict.Fix, hit.Current.FixSuggestion, baseHaystack, notes, verdict.Id + ".fix");
            if (message is not null || fix is not null)
            {
                hit.Current = hit.Current with
                {
                    Message = message ?? hit.Current.Message,
                    FixSuggestion = fix ?? hit.Current.FixSuggestion,
                };
            }

            if (fix is not null) hit.FixRewritten = true;
        }

        // A Suggested fix the reviewer calls unsupported is dropped (unless it was just replaced by a vetted one).
        foreach (var unsupportedId in decision.UnsupportedFixIds)
        {
            if (byId.TryGetValue(unsupportedId, out var unsupported)
                && !unsupported.Removed
                && !unsupported.FixRewritten
                && unsupported.Current.FixSuggestion is not null)
            {
                unsupported.Current = unsupported.Current with { FixSuggestion = null };
                notes.Flags.Add(unsupportedId + ":fix_removed_unsupported");
            }
        }

        // (g) Findings the primary missed.
        var missingRefs = new HashSet<string>(request.MissingRequiredFacts.Select(m => m.Ref), StringComparer.OrdinalIgnoreCase);
        var normalisedSource = Collapse(request.CaseNotesSnapshot + "\n" + request.TaskSnapshot);
        var added = new List<WritingReviewFinding>();
        var number = 0;
        foreach (var proposal in decision.Added)
        {
            number++;
            var id = "a" + number;
            if (added.Count >= policy.MaxAddedFindings)
            {
                notes.Rejected.Add(id + ":added_over_limit");
                continue;
            }

            var criterion = NormaliseCriterion(proposal.Criterion);
            if (criterion is null)
            {
                notes.Rejected.Add(id + ":added_invalid_criterion");
                continue;
            }

            var quote = (proposal.Quote ?? string.Empty).Trim();
            var start = quote.Length == 0 ? -1 : IndexInLetter(request.Letter, quote);
            var evidenceMatches = EvidenceMatches(proposal.Evidence, normalisedSource, missingRefs);
            if (start < 0 && !evidenceMatches)
            {
                notes.Rejected.Add(id + ":added_unsupported");
                continue;
            }

            if (WritingCandidateText.ContainsInternalToken(proposal.Message)) notes.LeakCount++;
            var message = WritingCandidateText.Clean(proposal.Message);
            if (message.Length == 0 || WritingCandidateText.ContainsInternalToken(message))
            {
                notes.Rejected.Add(id + ":added_message_unusable");
                continue;
            }

            if (HasInventedToken(message, baseHaystack))
            {
                notes.Rejected.Add(id + ":added_message_unsupported_fact");
                continue;
            }

            string? fix = null;
            if (!string.IsNullOrWhiteSpace(proposal.Fix))
            {
                if (WritingCandidateText.ContainsInternalToken(proposal.Fix)) notes.LeakCount++;
                var cleanedFix = WritingCandidateText.Clean(proposal.Fix);
                if (cleanedFix.Length > 0
                    && !WritingCandidateText.ContainsInternalToken(cleanedFix)
                    && !HasInventedToken(cleanedFix, baseHaystack))
                {
                    fix = cleanedFix;
                }
                else
                {
                    notes.Rejected.Add(id + ":added_fix_dropped");
                }
            }

            var severity = NormaliseSeverity(proposal.Severity) ?? "minor";
            if (severity == "critical" && !evidenceMatches) severity = "major";

            var repeatsExisting = quote.Length > 0
                && rows.Any(x => !x.Removed
                    && string.Equals(x.Current.PrimaryCriterionCode, criterion, StringComparison.Ordinal)
                    && string.Equals((x.Current.Quote ?? string.Empty).Trim(), quote, StringComparison.OrdinalIgnoreCase));
            if (repeatsExisting)
            {
                notes.Rejected.Add(id + ":added_duplicate_of_existing");
                continue;
            }

            int? startOffset = start >= 0 ? start : (int?)null;
            var created = new WritingAssessmentRuleFinding(
                RuleId: "AI." + criterion,
                Category: WritingSubmissionEvaluationPipeline.AiCategoryForCriterion(criterion),
                Severity: severity,
                Message: message,
                Quote: quote.Length == 0 ? null : quote,
                FixSuggestion: fix,
                StartOffset: startOffset,
                EndOffset: startOffset is { } offset ? offset + quote.Length : (int?)null,
                PrimaryCriterionCode: criterion,
                ProvenanceTag: WritingProvenanceTags.OetOfficial,
                CandidateBehavior: WritingCandidateBehaviors.ScoreBearing);
            added.Add(WritingReviewFinding.From(id, WritingReviewFindingOrigin.Reviewer, created));
            notes.Dispositions.Add(id + ":added");
        }

        var active = rows
            .Where(x => !x.Removed && !x.Duplicate)
            .Select(x => (Id: x.Source.Id, Finding: x.Current))
            .Concat(added.Select(x => (Id: x.Id, Finding: x.Finding)))
            .ToList();

        // (a)-(d) Scores.
        var scores = ApplyScores(request, decision, policy, active, relieved, notes);
        notes.Final = scores;

        // The 400+ checklist, evaluated in code (the reviewer's own flags are only evidence).
        var materialOmission = decision.Omissions.Any(o => o.Material && missingRefs.Contains(o.FactRef));
        var failures = EnhancedFailuresFor(scores, active.Select(a => a.Finding), materialOmission, policy);
        notes.EnhancedFailures.AddRange(failures);

        // (i) Final scrub of what the candidate will read.
        var final = new List<WritingReviewFinding>();
        foreach (var kept in rows.Where(x => !x.Removed))
        {
            final.Add(new WritingReviewFinding(
                kept.Source.Id,
                kept.Source.Fingerprint,
                kept.Source.Origin,
                ScrubLeaks(kept.Current, notes)));
        }

        final.AddRange(added);

        // The pipeline groups duplicates by fingerprint. A grader that reports the very same finding twice gives the
        // duplicate and its target ONE fingerprint, and grouping it would hide the correction altogether: such a pair is
        // left as it is (two identical cards beat a missing one).
        var keptFingerprints = rows
            .Where(x => !x.Removed && !x.Duplicate)
            .Select(x => x.Source.Fingerprint)
            .ToHashSet(StringComparer.Ordinal);
        var duplicates = rows
            .Where(x => x.Duplicate && !x.Removed && !keptFingerprints.Contains(x.Source.Fingerprint))
            .Select(x => x.Source.Fingerprint)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tutorReasons = new List<string>();
        if (overrodeCritical) tutorReasons.Add(WritingJevReviewReasons.ReviewerOverride);

        return new WritingReviewOutcome(
            WritingReviewStatus.Reviewed,
            scores,
            final,
            duplicates,
            tutorReasons,
            notes,
            failures);
    }

    /// <summary>
    /// The 400+ checklist: no scored Critical or Major finding, no material omission, Purpose and every other criterion
    /// strong, and only limited minor imperfections. Empty below the threshold (or when everything holds).
    /// </summary>
    internal static IReadOnlyList<string> EnhancedFailuresFor(
        WritingReviewScores scores,
        IEnumerable<WritingAssessmentRuleFinding> active,
        bool materialOmission,
        WritingReviewOptions policy)
    {
        var failures = new List<string>();
        if (OetScoring.OetReportedScaledScore(scores.ScaledScore) < policy.EnhancedThreshold) return failures;

        var defects = active.Where(CountsAsDefect).ToList();
        if (defects.Any(f => f.Severity is "critical" or "major")) failures.Add("critical_or_major_finding");
        if (materialOmission) failures.Add("material_omission");
        if (scores.C1 < policy.Enhanced400MinPurpose) failures.Add("purpose_below_strong");
        if (scores.C2 < policy.Enhanced400MinOther
            || scores.C3 < policy.Enhanced400MinOther
            || scores.C4 < policy.Enhanced400MinOther
            || scores.C5 < policy.Enhanced400MinOther
            || scores.C6 < policy.Enhanced400MinOther)
        {
            failures.Add("criterion_below_strong");
        }

        if (defects.Count(f => f.Severity == "minor") > policy.Enhanced400MaxMinorFindings)
        {
            failures.Add("too_many_minor_findings");
        }

        return failures;
    }

    private static WritingReviewScores ApplyScores(
        WritingReviewRequest request,
        WritingReviewDecision decision,
        WritingReviewOptions policy,
        IReadOnlyList<(string Id, WritingAssessmentRuleFinding Finding)> active,
        HashSet<string> relieved,
        WritingReviewAdminNotes notes)
    {
        var primary = request.Primary;
        var current = new[] { primary.C1, primary.C2, primary.C3, primary.C4, primary.C5, primary.C6 };
        var result = (int[])current.Clone();

        if (decision.CriterionScores is { } proposed)
        {
            var block = new int[6];
            var complete = true;
            for (var index = 0; index < 6; index++)
            {
                if (proposed.TryGetValue(Criteria[index], out var value)) block[index] = value;
                else complete = false;
            }

            if (!complete)
            {
                notes.Rejected.Add("scores:incomplete_block");
            }
            else if (!InBounds(block))
            {
                // (a) A block with any out-of-range score is rejected whole.
                notes.Rejected.Add("scores:out_of_range");
            }
            else
            {
                for (var index = 0; index < 6; index++)
                {
                    var delta = block[index] - current[index];
                    if (delta == 0) continue;
                    var criterion = Criteria[index];
                    if (Math.Abs(delta) > policy.MaxCriterionDeltaPerPass)
                    {
                        notes.Rejected.Add($"scores.{criterion}:delta_over_cap");
                        continue;
                    }

                    if (delta < 0)
                    {
                        // (c) A decrease needs a surviving non-advisory finding in that criterion, cited by the reviewer.
                        var cited = new HashSet<string>(
                            decision.ScoreChanges
                                .Where(c => string.Equals(c.Criterion, criterion, StringComparison.OrdinalIgnoreCase))
                                .SelectMany(c => c.FindingIds),
                            StringComparer.OrdinalIgnoreCase);
                        var supported = active.Any(a => cited.Contains(a.Id)
                            && a.Finding.PrimaryCriterionCode == criterion
                            && CountsAsDefect(a.Finding));
                        if (!supported)
                        {
                            notes.Rejected.Add($"scores.{criterion}:decrease_unsupported");
                            continue;
                        }
                    }
                    else if (!relieved.Contains(criterion))
                    {
                        // An increase needs a finding in that criterion that was removed, made advisory or downgraded.
                        notes.Rejected.Add($"scores.{criterion}:increase_unsupported");
                        continue;
                    }

                    result[index] = block[index];
                }
            }
        }

        // (b)/(d) The raw total is recomputed; the /500 estimate is holistic and moves only as far as the caps allow.
        var raw = result.Sum();
        var scaled = primary.ScaledScore;
        var (lower, upper) = Corridor(raw, policy);
        if (decision.EstimatedScaledScore is { } proposedScaled && proposedScaled != primary.ScaledScore)
        {
            if (proposedScaled == 0 && primary.ScaledScore > 0)
            {
                // The reply schema shows 0 as its placeholder, so 0/500 for a letter that was really graded is a
                // template echo, never a verdict (a blank letter makes no provider call). Keep the primary value.
                notes.Rejected.Add("scaled:placeholder_zero");
            }
            else if (proposedScaled < OetScoring.ScaledMin || proposedScaled > OetScoring.ScaledMax)
            {
                notes.Rejected.Add("scaled:out_of_range");
            }
            else if (raw == primary.RawTotal
                && relieved.Count == 0
                && !notes.Dispositions.Any(d => !d.EndsWith(":confirmed", StringComparison.Ordinal))
                && primary.ScaledScore < policy.EnhancedThreshold
                && primary.ScaledScore >= lower
                && primary.ScaledScore <= upper)
            {
                // The /500 is holistic and the primary grader owns it: a different number with no criterion change, no
                // finding changed and no consistency problem is an opinion, not a correction. Inside the 400+ verification
                // zone the reviewer may recalibrate freely (no hard cap), so that zone is exempt.
                notes.Rejected.Add("scaled:unjustified");
            }
            else
            {
                var cap = Math.Abs(raw - primary.RawTotal) >= 3
                    ? policy.MaxScaledDeltaWhenRawMoves
                    : policy.MaxScaledDeltaPerPass;
                var target = proposedScaled;
                var delta = target - primary.ScaledScore;
                if (Math.Abs(delta) > cap)
                {
                    target = primary.ScaledScore + Math.Sign(delta) * cap;
                    notes.Flags.Add("scaled_delta_capped");
                }

                var inside = Math.Clamp(target, lower, upper);
                if (inside != target)
                {
                    notes.Flags.Add("consistency_enforced");
                    target = inside;
                }

                scaled = target;
            }
        }
        else if (scaled < lower || scaled > upper)
        {
            // The primary score is NEVER rewritten for this; it is only recorded.
            notes.Flags.Add("primary_outside_corridor");
        }

        return new WritingReviewScores(result[0], result[1], result[2], result[3], result[4], result[5], scaled);
    }

    private static bool InBounds(int[] block)
    {
        if (block[0] < 0 || block[0] > 3) return false;
        for (var index = 1; index < 6; index++)
        {
            if (block[index] < 0 || block[index] > 7) return false;
        }

        return true;
    }

    private static (int Lower, int Upper) Corridor(int raw, WritingReviewOptions policy)
    {
        var lower = Math.Clamp(policy.ScaledPerRawPoint * raw + policy.CorridorLowerOffset, OetScoring.ScaledMin, OetScoring.ScaledMax);
        var upper = raw >= policy.CorridorOpenFromRaw
            ? OetScoring.ScaledMax
            : Math.Clamp(policy.ScaledPerRawPoint * raw + policy.CorridorUpperOffset, OetScoring.ScaledMin, OetScoring.ScaledMax);
        return (lower, Math.Max(lower, upper));
    }

    /// <summary>A scored finding: not advisory and score-bearing under the registry (an unregistered grader id counts).</summary>
    private static bool CountsAsDefect(WritingAssessmentRuleFinding finding)
        => finding.Severity != "info" && WritingReportDigest.IsScoreBearing(finding.RuleId, finding.Severity);

    private static int Rank(string? severity) => severity switch
    {
        "critical" => 0,
        "major" => 1,
        "minor" => 2,
        "info" => 3,
        _ => 2,
    };

    private static string? NormaliseSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => "critical",
        "major" => "major",
        "minor" => "minor",
        _ => null,
    };

    private static string? NormaliseCriterion(string? criterion)
    {
        var key = (criterion ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return Criteria.Contains(key) ? key : null;
    }

    /// <summary>The registry check id behind a finding, or null for a rule-less grader finding.</summary>
    private static string? CheckIdOf(WritingAssessmentRuleFinding finding)
    {
        var id = (finding.RuleId ?? string.Empty).Trim();
        if (id.Length == 0 || id.StartsWith("AI.", StringComparison.OrdinalIgnoreCase)) return null;
        if (id.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)) id = id[3..];
        return WritingAssessmentV11RuleEngine.ResolveCheckId(id);
    }

    private static int IndexInLetter(string letter, string quote)
    {
        var index = letter.IndexOf(quote, StringComparison.Ordinal);
        return index >= 0 ? index : letter.IndexOf(quote, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EvidenceMatches(string? evidence, string normalisedSource, HashSet<string> missingRefs)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        var trimmed = evidence.Trim();
        if (missingRefs.Contains(trimmed)) return true;
        var normalised = Collapse(trimmed);
        // The evidence must QUOTE the notes or the task; a passing mention of one word is not proof.
        return normalised.Length >= 6 && normalisedSource.Contains(normalised, StringComparison.Ordinal);
    }

    /// <summary>A reviewer rewrite of candidate-facing text, or null when it is absent, unchanged or refused.</summary>
    private static string? AcceptRewrite(
        string? proposed,
        string? original,
        string baseHaystack,
        WritingReviewAdminNotes notes,
        string label)
    {
        if (string.IsNullOrWhiteSpace(proposed)) return null;
        var text = proposed.Trim();
        if (string.Equals(text, original?.Trim(), StringComparison.Ordinal)) return null;

        if (WritingCandidateText.ContainsInternalToken(text)) notes.LeakCount++;
        var cleaned = WritingCandidateText.Clean(text);
        if (cleaned.Length == 0 || WritingCandidateText.ContainsInternalToken(cleaned))
        {
            notes.Rejected.Add(label + ":rewrite_rejected_leak");
            return null;
        }

        // No new figure, unit, date or drug-like token: a Suggested fix may never invent a clinical fact.
        if (HasInventedToken(cleaned, baseHaystack + Strip(original)))
        {
            notes.Rejected.Add(label + ":rewrite_rejected_unsupported_fact");
            return null;
        }

        return cleaned;
    }

    private static bool HasInventedToken(string text, string haystack)
    {
        foreach (Match match in FactToken.Matches(text))
        {
            var token = Strip(match.Value);
            var number = LeadingNumber.Match(token);
            var core = number.Success ? number.Value : token;
            if (core.Length > 0 && !haystack.Contains(core, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>Lower-cased with every whitespace removed, so "37.8 °C" and "37.8°C" compare equal.</summary>
    private static string Strip(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : Whitespace.Replace(text.ToLowerInvariant(), string.Empty);

    private static string Collapse(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : Whitespace.Replace(text.ToLowerInvariant(), " ").Trim();

    /// <summary>(i) Candidate-facing text never carries an internal token; a leak is counted and cleaned, never fatal.</summary>
    private static WritingAssessmentRuleFinding ScrubLeaks(WritingAssessmentRuleFinding finding, WritingReviewAdminNotes notes)
    {
        var message = finding.Message;
        var fix = finding.FixSuggestion;
        if (WritingCandidateText.ContainsInternalToken(message))
        {
            notes.LeakCount++;
            message = WritingCandidateText.Clean(message, GenericMessage);
        }

        if (WritingCandidateText.ContainsInternalToken(fix))
        {
            notes.LeakCount++;
            fix = WritingCandidateText.CleanOrNull(fix);
        }

        return ReferenceEquals(message, finding.Message) && ReferenceEquals(fix, finding.FixSuggestion)
            ? finding
            : finding with { Message = message, FixSuggestion = fix };
    }
}
