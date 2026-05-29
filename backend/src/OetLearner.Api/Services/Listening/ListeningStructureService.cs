using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening structure service — Publish-gate validator
//
// Validates the canonical OET Listening shape before a ContentPaper flips
// to Published. Non-configurable invariant:
//
//   Part A = 24 items (two consultations, 12 items each; partCode A / A1 / A2)
//   Part B =  6 items (six workplace extracts, one 3-option MCQ each; partCode B)
//   Part C = 12 items (two presentations, 6 items each; partCode C / C1 / C2)
//   ───────────────
//   Total  = 42 items  (30 / 42 ≡ 350 / 500 pass anchor via OetScoring)
//
// Questions are authored in ContentPaper.ExtractedTextJson under the
// "listeningQuestions" key — the same shape the learner player consumes
// (lib/listening-sections.ts / ListeningLearnerService.ExtractQuestions).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningStructureService
{
    Task<ListeningValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct);
}

public sealed record ListeningValidationReport(
    bool IsPublishReady,
    IReadOnlyList<ListeningValidationIssue> Issues,
    ListeningValidationCounts Counts)
{
    /// <summary>Where the item counts came from: <c>"relational"</c> when the
    /// <c>ListeningQuestion</c> table had rows for the paper, otherwise
    /// <c>"json"</c> (legacy <c>ExtractedTextJson.listeningQuestions</c>).</summary>
    public string Source { get; init; } = "json";
}

public sealed record ListeningValidationIssue(
    string Code,
    string Severity, // "error" | "warning"
    string Message);

public sealed record ListeningValidationCounts(
    int PartACount, int PartBCount, int PartCCount, int TotalItems);

public sealed class ListeningStructureService(LearnerDbContext db) : IListeningStructureService
{
    /// <summary>Canonical OET Listening shape. Non-configurable invariant.</summary>
    public const int CanonicalPartACount = 24;
    public const int CanonicalPartBCount = 6;
    public const int CanonicalPartCCount = 12;
    public const int CanonicalTotalItems = CanonicalPartACount + CanonicalPartBCount + CanonicalPartCCount; // 42

    private static readonly HashSet<string> AllowedDistractorCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "too_strong",
        "too_weak",
        "wrong_speaker",
        "opposite_meaning",
        "reused_keyword",
        "out_of_scope",
    };

    private static readonly HashSet<string> LegalAttestationValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "original-authoring-attested",
        "licensed-content-attested",
        "permission-attested",
        "copyright-cleared",
    };

    public async Task<ListeningValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        var issues = new List<ListeningValidationIssue>();
        if (!HasSourceLegalAttestation(paper.SourceProvenance))
        {
            issues.Add(new(
                Code: "listening_source_provenance",
                Severity: "error",
                Message: "Source provenance and original/legal-content attestation are required before publishing a Listening paper."));
        }

        // Prefer the relational ListeningQuestion table when authoring has
        // migrated off the legacy ExtractedTextJson blob; fall back to the
        // JSON shape so older draft papers keep validating.
        var hasRelational = await db.Set<ListeningQuestion>()
            .AnyAsync(q => q.PaperId == paperId, ct);

        int partA, partB, partC, total;
        List<ListeningValidationIssue> structuralWarnings;
        string source;
        if (hasRelational)
        {
            (partA, partB, partC, total, structuralWarnings) = await CountItemsRelationalAsync(paperId, ct);
            source = "relational";
        }
        else
        {
            (partA, partB, partC, total, structuralWarnings) = CountItems(paper.ExtractedTextJson);
            source = "json";
        }
        issues.AddRange(structuralWarnings);

        if (total == 0)
        {
            issues.Add(new(
                Code: "listening_no_items",
                Severity: "error",
                Message: "No authored Listening questions were found in ExtractedTextJson.listeningQuestions."));
        }
        else
        {
            if (partA != CanonicalPartACount)
                issues.Add(new("listening_part_a_count", "error",
                    $"Part A has {partA} item(s); OET requires exactly {CanonicalPartACount} (12 per consultation)."));
            if (partB != CanonicalPartBCount)
                issues.Add(new("listening_part_b_count", "error",
                    $"Part B has {partB} item(s); OET requires exactly {CanonicalPartBCount} (one per workplace extract)."));
            if (partC != CanonicalPartCCount)
                issues.Add(new("listening_part_c_count", "error",
                    $"Part C has {partC} item(s); OET requires exactly {CanonicalPartCCount} (6 per presentation)."));
        }

        var isPublishReady = !issues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new ListeningValidationReport(
            isPublishReady,
            issues,
            new ListeningValidationCounts(partA, partB, partC, total))
        {
            Source = source,
        };
    }

    /// <summary>Tally items per part directly from the relational
    /// <see cref="ListeningQuestion"/> table by joining to
    /// <see cref="ListeningPart"/> and bucketing the <see cref="ListeningPartCode"/>
    /// (<c>A1/A2 → A</c>, <c>B → B</c>, <c>C1/C2 → C</c>).</summary>
    private async Task<(int partA, int partB, int partC, int total, List<ListeningValidationIssue> warnings)>
        CountItemsRelationalAsync(string paperId, CancellationToken ct)
    {
        var warnings = new List<ListeningValidationIssue>();

        var questions = await db.Set<ListeningQuestion>()
            .AsNoTracking()
            .Where(q => q.PaperId == paperId)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var partIds = questions
            .Select(q => q.ListeningPartId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var partRows = await db.Set<ListeningPart>()
            .AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PartCode, p.MaxRawScore })
            .ToListAsync(ct);
        var parts = partRows.ToDictionary(p => p.Id, p => p.PartCode, StringComparer.Ordinal);
        var partMaxRawScores = partRows.Sum(p => p.MaxRawScore);
        var extractIds = questions
            .Select(q => q.ListeningExtractId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var extracts = await db.Set<ListeningExtract>()
            .AsNoTracking()
            .Where(extract => extractIds.Contains(extract.Id))
            .ToDictionaryAsync(extract => extract.Id, StringComparer.Ordinal, ct);
        // Singleton (id = "global") Listening policy — drives the effective
        // preview-window resolution below. Null when no policy row is authored
        // yet; the resolver then falls back to ListeningPolicyDefaults.
        var policy = await db.Set<ListeningPolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == "global", ct);
        var rows = questions
            .Select(q => new
            {
                PartCode = parts.GetValueOrDefault(q.ListeningPartId),
                q.ListeningExtractId,
                q.QuestionNumber,
                q.QuestionType,
                q.Stem,
                q.CorrectAnswerJson,
                q.SkillTag,
                q.TranscriptEvidenceText,
                q.TranscriptEvidenceStartMs,
                q.TranscriptEvidenceEndMs,
                q.DifficultyLevel,
                Options = q.Options
                    .Select(o => new RelationalOption(o.OptionKey, o.Text, o.IsCorrect, o.DistractorCategory))
                    .ToArray(),
            })
            .ToList();

        var a1 = rows.Count(row => row.PartCode == ListeningPartCode.A1);
        var a2 = rows.Count(row => row.PartCode == ListeningPartCode.A2);
        var b = rows.Count(row => row.PartCode == ListeningPartCode.B);
        var c1 = rows.Count(row => row.PartCode == ListeningPartCode.C1);
        var c2 = rows.Count(row => row.PartCode == ListeningPartCode.C2);
        var a = a1 + a2;
        var c = c1 + c2;

        var duplicateNumbers = rows
            .GroupBy(row => row.QuestionNumber)
            .Count(group => group.Count() > 1);
        if (duplicateNumbers > 0)
        {
            warnings.Add(new("listening_duplicate_question_numbers", "error",
                $"Question numbers must be unique across the paper; {duplicateNumbers} duplicate number group(s) found."));
        }

        // 2026-05-27 audit fix — Listening rule L01.1 (contiguous numbering
        // 1..42). The existing duplicate check is necessary but not sufficient;
        // an authored paper could have all-unique numbers but skip 7 and still
        // pass.
        if (rows.Count > 0)
        {
            var expected = Enumerable.Range(1, 42).ToHashSet();
            var have = rows.Select(r => r.QuestionNumber).ToHashSet();
            var missing = expected.Except(have).ToArray();
            var extra = have.Where(n => n < 1 || n > 42).ToArray();
            if (missing.Length > 0 || extra.Length > 0)
            {
                var missingPart = missing.Length > 0 ? $"missing #{string.Join(", #", missing.OrderBy(n => n))}" : null;
                var extraPart = extra.Length > 0 ? $"out-of-range #{string.Join(", #", extra.OrderBy(n => n))}" : null;
                var detail = string.Join(" + ", new[] { missingPart, extraPart }.Where(s => s != null));
                warnings.Add(new("listening_numbering_not_contiguous", "error",
                    $"Listening rule L01.1 — every authored paper must have exactly questions 1..42; {detail}."));
            }
        }

        // 2026-05-27 audit fix — Listening rule L06.1 (every authored item
        // carries a transcript excerpt). The TranscriptEvidenceText column is
        // already on ListeningQuestion; this gate ensures authors populate it
        // before publish (needed for jump-to-evidence playback and tutor review).
        var missingTranscriptEvidence = rows.Count(row => string.IsNullOrWhiteSpace(row.TranscriptEvidenceText));
        if (missingTranscriptEvidence > 0)
        {
            warnings.Add(new("listening_transcript_evidence_missing", "error",
                $"Listening rule L06.1 — every item must carry a verbatim transcript excerpt that supports the correct answer; {missingTranscriptEvidence} item(s) have no TranscriptEvidenceText."));
        }

        if (a1 is not 0 and not 12 || a2 is not 0 and not 12)
        {
            warnings.Add(new("listening_part_a_split", "error",
                $"Part A requires two consultations with exactly 12 items each; found A1={a1}, A2={a2}."));
        }
        if (c1 is not 0 and not 6 || c2 is not 0 and not 6)
        {
            warnings.Add(new("listening_part_c_split", "error",
                $"Part C requires two presentations with exactly 6 items each; found C1={c1}, C2={c2}."));
        }

        var blankStems = rows.Count(row => string.IsNullOrWhiteSpace(row.Stem));
        if (blankStems > 0)
        {
            warnings.Add(new("listening_blank_stems", "error",
                $"Every Listening item requires learner-facing question text; {blankStems} item(s) have a blank stem."));
        }

        var blankAnswers = rows.Count(row => string.IsNullOrWhiteSpace(ReadJsonString(row.CorrectAnswerJson)));
        if (blankAnswers > 0)
        {
            warnings.Add(new("listening_blank_answers", "error",
                $"Every Listening item requires a non-empty correct answer; {blankAnswers} item(s) are missing one."));
        }

        // Part A is note-completion (short-answer) only per OET spec — MCQ
        // items in Part A break the canonical UX and grading rubric.
        var partAItemsWithWrongType = rows.Count(row =>
            (row.PartCode == ListeningPartCode.A1 || row.PartCode == ListeningPartCode.A2)
            && row.QuestionType != ListeningQuestionType.ShortAnswer);
        if (partAItemsWithWrongType > 0)
        {
            warnings.Add(new("listening_part_a_type", "error",
                $"Part A items must be short-answer (note-completion); {partAItemsWithWrongType} item(s) use a different question type."));
        }

        var partBItemsWithoutThreeOptions = rows.Count(row => row.PartCode == ListeningPartCode.B
            && !HasValidMcqShape(row.QuestionType, ReadJsonString(row.CorrectAnswerJson), row.Options));
        if (partBItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_b_mcq_shape", "error",
                $"Part B requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partBItemsWithoutThreeOptions} item(s) violate this."));
        }

        var partCItemsWithoutThreeOptions = rows.Count(row => (row.PartCode == ListeningPartCode.C1 || row.PartCode == ListeningPartCode.C2)
            && !HasValidMcqShape(row.QuestionType, ReadJsonString(row.CorrectAnswerJson), row.Options));
        if (partCItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_c_mcq_shape", "error",
                $"Part C requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partCItemsWithoutThreeOptions} item(s) violate this."));
        }

        var missingExtractLinks = rows.Count(row => string.IsNullOrWhiteSpace(row.ListeningExtractId)
            || !extracts.ContainsKey(row.ListeningExtractId!));
        if (missingExtractLinks > 0)
        {
            warnings.Add(new("listening_extract_links", "error",
                $"Every Listening question must be linked to an authored audio extract; {missingExtractLinks} item(s) are missing a valid extract link."));
        }

        var invalidExtractTimings = extracts.Values.Count(extract => !HasValidTimingWindow(extract.AudioStartMs, extract.AudioEndMs));
        if (invalidExtractTimings > 0)
        {
            warnings.Add(new("listening_extract_timing", "error",
                $"Every Listening extract requires valid audio cue timings; {invalidExtractTimings} extract(s) are missing start/end ms or have an invalid window."));
        }

        var missingExtractDifficulty = extracts.Values.Count(extract => !IsValidDifficulty(extract.DifficultyRating));
        if (missingExtractDifficulty > 0)
        {
            warnings.Add(new("listening_extract_difficulty", "error",
                $"Every Listening extract requires a difficulty rating from 1 to 5; {missingExtractDifficulty} extract(s) are missing or invalid."));
        }

        var missingSkillTags = rows.Count(row => string.IsNullOrWhiteSpace(row.SkillTag));
        if (missingSkillTags > 0)
        {
            warnings.Add(new("listening_skill_tags", "error",
                $"Every Listening question requires a skill tag; {missingSkillTags} item(s) are missing one."));
        }

        var invalidSkillTags = rows.Count(row => !string.IsNullOrWhiteSpace(row.SkillTag) && !ListeningSkillTags.IsValid(row.SkillTag));
        if (invalidSkillTags > 0)
        {
            warnings.Add(new("listening_skill_tags_invalid", "error",
                $"Every Listening skill tag must use the canonical vocabulary; {invalidSkillTags} item(s) have an unsupported value."));
        }

        var invalidEvidence = rows.Count(row => string.IsNullOrWhiteSpace(row.TranscriptEvidenceText)
            || !HasValidTimingWindow(row.TranscriptEvidenceStartMs, row.TranscriptEvidenceEndMs));
        if (invalidEvidence > 0)
        {
            warnings.Add(new("listening_transcript_evidence", "error",
                $"Every Listening question requires transcript evidence text with valid start/end timestamps; {invalidEvidence} item(s) are incomplete."));
        }

        var missingQuestionDifficulty = rows.Count(row => !IsValidDifficulty(row.DifficultyLevel));
        if (missingQuestionDifficulty > 0)
        {
            warnings.Add(new("listening_question_difficulty", "error",
                $"Every Listening question requires a difficulty level from 1 to 5; {missingQuestionDifficulty} item(s) are missing or invalid."));
        }

        var wrongOptionsMissingDistractorCategory = rows
            .Where(row => row.QuestionType == ListeningQuestionType.MultipleChoice3)
            .SelectMany(row => row.Options)
            .Count(option => !option.IsCorrect && option.DistractorCategory is null);
        if (wrongOptionsMissingDistractorCategory > 0)
        {
            warnings.Add(new("listening_distractor_categories", "error",
                $"Every wrong Part B/C option requires a distractor category; {wrongOptionsMissingDistractorCategory} option(s) are untagged."));
        }

        // WS-7c additive publish-gate rules. Each emits an "error" issue so a
        // violation blocks Publish; none mutate the count tally or the report
        // contract.
        warnings.AddRange(EvaluateExtractCueOverlap(parts, extracts.Values));
        warnings.AddRange(EvaluatePreviewWindows(parts.Values, policy));
        warnings.AddRange(EvaluateResultsCalc(
            authoredPoints: questions.Sum(q => q.Points),
            partMaxRawScores: partMaxRawScores));

        return (a, b, c, a + b + c, warnings);
    }

    // ─────────────────────────────────────────────────────────────────────
    // WS-7c — three additive Listening publish-validation rules. Kept as
    // pure helpers so CountItemsRelationalAsync stays readable; all three
    // emit "error"-severity issues that gate IsPublishReady.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>listening_extract_cue_overlap</c> — within each part, the authored
    /// extract cue windows <c>[AudioStartMs, AudioEndMs]</c> must each have
    /// <c>start &lt; end</c> and, taken in display order, be monotonic and
    /// non-overlapping (treated as half-open <c>[start, end)</c>, so an
    /// abutting boundary where one window's start equals the previous window's
    /// end is allowed). A later extract that starts before the previous extract
    /// ends would make jump-to-cue playback ambiguous, so it blocks publish.
    /// </summary>
    private static List<ListeningValidationIssue> EvaluateExtractCueOverlap(
        IReadOnlyDictionary<string, ListeningPartCode> partsById,
        IEnumerable<ListeningExtract> extracts)
    {
        var issues = new List<ListeningValidationIssue>();
        var offendingParts = new SortedSet<string>(StringComparer.Ordinal);

        var byPart = extracts.GroupBy(extract => extract.ListeningPartId, StringComparer.Ordinal);
        foreach (var partGroup in byPart)
        {
            var ordered = partGroup
                .OrderBy(extract => extract.DisplayOrder)
                .ThenBy(extract => extract.AudioStartMs ?? int.MaxValue)
                .ToArray();

            var partLabel = partsById.TryGetValue(partGroup.Key, out var code)
                ? code.ToString()
                : partGroup.Key;

            int? previousEnd = null;
            foreach (var extract in ordered)
            {
                var start = extract.AudioStartMs;
                var end = extract.AudioEndMs;

                // Each window must be a well-formed [start, end) with start < end.
                if (start is null || end is null || start.Value >= end.Value)
                {
                    offendingParts.Add(partLabel);
                    // A malformed window cannot anchor the monotonic check; skip
                    // advancing previousEnd so we do not double-count it as an
                    // overlap on top of being malformed.
                    continue;
                }

                // Monotonic + non-overlapping: this window must start at or
                // after the previous window's end.
                if (previousEnd is int prior && start.Value < prior)
                {
                    offendingParts.Add(partLabel);
                }

                previousEnd = end.Value;
            }
        }

        if (offendingParts.Count > 0)
        {
            issues.Add(new("listening_extract_cue_overlap", "error",
                $"Extract audio cue windows must be in-order and non-overlapping within each part (start < end, monotonic); part(s) {string.Join(", ", offendingParts)} violate this."));
        }

        return issues;
    }

    /// <summary>
    /// <c>listening_preview_window_missing</c> — each timed part present on the
    /// paper (A1/A2/C1/C2) must have a positive effective preview window. The
    /// effective value is resolved exactly like the runtime FSM: the singleton
    /// <see cref="ListeningPolicy"/> column when set, otherwise the canonical
    /// <see cref="ListeningPolicyDefaults"/> fallback. Emits when an authored
    /// policy zeroes (or negates) a window that a present part requires.
    /// </summary>
    private static List<ListeningValidationIssue> EvaluatePreviewWindows(
        IEnumerable<ListeningPartCode> presentParts,
        ListeningPolicy? policy)
    {
        var issues = new List<ListeningValidationIssue>();
        var present = presentParts.ToHashSet();
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var part in present)
        {
            // Part B is un-timed (per-question pause window, not a preview cue),
            // so only the four preview-bearing parts are checked.
            var key = part switch
            {
                ListeningPartCode.A1 => "A1",
                ListeningPartCode.A2 => "A2",
                ListeningPartCode.C1 => "C1",
                ListeningPartCode.C2 => "C2",
                _ => null,
            };
            if (key is null) continue;

            var effective = EffectivePreviewMs(part, policy);
            if (effective <= 0) missing.Add(key);
        }

        if (missing.Count > 0)
        {
            issues.Add(new("listening_preview_window_missing", "error",
                $"Every timed part requires a preview window; part(s) {string.Join(", ", missing)} resolve to a missing or zero preview window under the effective Listening policy."));
        }

        return issues;
    }

    /// <summary>
    /// <c>listening_results_calc</c> — results-calculation integrity. The
    /// authored points must sum to the OET Listening/Reading raw max
    /// (<see cref="OetScoring.ListeningReadingRawMax"/> = 42), the per-part
    /// max-raw totals must agree, and the canonical raw→scaled pass anchor
    /// (<see cref="OetScoring.ListeningReadingRawPass"/> → 350) must hold.
    /// Any inconsistency would mis-grade a published paper, so it blocks
    /// publish.
    /// </summary>
    private static List<ListeningValidationIssue> EvaluateResultsCalc(
        int authoredPoints,
        int partMaxRawScores)
    {
        var issues = new List<ListeningValidationIssue>();
        var problems = new List<string>();

        if (authoredPoints != OetScoring.ListeningReadingRawMax)
        {
            problems.Add($"authored points sum to {authoredPoints}, not {OetScoring.ListeningReadingRawMax}");
        }

        if (partMaxRawScores != OetScoring.ListeningReadingRawMax)
        {
            problems.Add($"per-part max-raw scores sum to {partMaxRawScores}, not {OetScoring.ListeningReadingRawMax}");
        }

        // Pass-anchor integrity: 30/42 must still scale to exactly the Grade B
        // pass constant (350). Defends against an OetScoring regression that
        // would silently move every published paper's pass line.
        var anchorScaled = OetScoring.OetRawToScaled(OetScoring.ListeningReadingRawPass);
        if (anchorScaled != OetScoring.ScaledPassGradeB)
        {
            problems.Add($"raw pass anchor {OetScoring.ListeningReadingRawPass}/{OetScoring.ListeningReadingRawMax} scales to {anchorScaled}, not the Grade B pass {OetScoring.ScaledPassGradeB}");
        }

        if (problems.Count > 0)
        {
            issues.Add(new("listening_results_calc", "error",
                $"Results-calc integrity check failed: {string.Join("; ", problems)}."));
        }

        return issues;
    }

    /// <summary>Resolve the effective preview window (ms) for a timed part,
    /// mirroring <see cref="ListeningPolicyResolver"/>: authored policy column
    /// when set, else the <see cref="ListeningPolicyDefaults"/> fallback.</summary>
    private static int EffectivePreviewMs(ListeningPartCode part, ListeningPolicy? policy) => part switch
    {
        ListeningPartCode.A1 => policy?.PreviewWindowMsA1 ?? ListeningPolicyDefaults.PreviewMsA1,
        ListeningPartCode.A2 => policy?.PreviewWindowMsA2 ?? ListeningPolicyDefaults.PreviewMsA2,
        ListeningPartCode.C1 => policy?.PreviewWindowMsC1 ?? ListeningPolicyDefaults.PreviewMsC1,
        ListeningPartCode.C2 => policy?.PreviewWindowMsC2 ?? ListeningPolicyDefaults.PreviewMsC2,
        _ => 0,
    };

    /// <summary>Parse ExtractedTextJson.listeningQuestions and tally items by
    /// normalized part. Accepts both granular (A1/A2/C1/C2) and legacy (A/C)
    /// partCodes. Also validates Part B items expose a 3-option MCQ.</summary>
    private static (int partA, int partB, int partC, int total, List<ListeningValidationIssue> warnings)
        CountItems(string? extractedTextJson)
    {
        var warnings = new List<ListeningValidationIssue>();
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return (0, 0, 0, 0, warnings);

        List<Dictionary<string, object?>> questions;
        List<Dictionary<string, object?>> extracts;
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault("listeningQuestions");
            if (raw is null) return (0, 0, 0, 0, warnings);
            questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                        ?? new List<Dictionary<string, object?>>();
            extracts = ReadObjectList(root?.GetValueOrDefault("listeningExtracts"));
        }
        catch (JsonException)
        {
            warnings.Add(new("listening_invalid_json", "error",
                "ExtractedTextJson is not valid JSON; cannot validate Listening structure."));
            return (0, 0, 0, 0, warnings);
        }

        int a = 0, b = 0, c = 0;
        int a1 = 0, a2 = 0, c1 = 0, c2 = 0;
        var partAItemsWithWrongType = 0;
        var partBItemsWithoutThreeOptions = 0;
        var partCItemsWithoutThreeOptions = 0;
        var blankAnswers = 0;
        var blankStems = 0;
        var numbers = new Dictionary<int, int>();
        var missingSkillTags = 0;
        var invalidSkillTags = 0;
        var invalidEvidence = 0;
        var missingQuestionDifficulty = 0;
        var wrongOptionsMissingDistractorCategory = 0;
        var wrongOptionsInvalidDistractorCategory = 0;

        foreach (var q in questions)
        {
            if (TryGetInt(q, "number") is int number)
            {
                numbers[number] = numbers.GetValueOrDefault(number) + 1;
            }
            if (string.IsNullOrWhiteSpace(ReadString(q, "text") ?? ReadString(q, "stem"))) blankStems++;
            if (string.IsNullOrWhiteSpace(ReadString(q, "correctAnswer"))) blankAnswers++;
            var skillTag = ReadString(q, "skillTag");
            if (string.IsNullOrWhiteSpace(skillTag)) missingSkillTags++;
            else if (!ListeningSkillTags.IsValid(skillTag)) invalidSkillTags++;

            if (string.IsNullOrWhiteSpace(ReadString(q, "transcriptEvidenceText") ?? ReadString(q, "transcriptExcerpt"))
                || !HasValidTimingWindow(TryGetInt(q, "transcriptEvidenceStartMs"), TryGetInt(q, "transcriptEvidenceEndMs")))
            {
                invalidEvidence++;
            }

            if (!IsValidDifficulty(TryGetInt(q, "difficultyLevel") ?? TryGetInt(q, "difficultyRating")))
            {
                missingQuestionDifficulty++;
            }

            var partCode = (q.GetValueOrDefault("partCode") ?? q.GetValueOrDefault("part"))?.ToString()
                ?.Trim().ToUpperInvariant() ?? "A";

            if (partCode.StartsWith("A", StringComparison.Ordinal))
            {
                a++;
                if (partCode == "A1") a1++;
                else if (partCode == "A2") a2++;
                // Part A is short-answer (note-completion) only — see relational
                // path for the matching rule.
                var qType = ReadString(q, "type") ?? ReadString(q, "questionType");
                if (!string.IsNullOrWhiteSpace(qType)
                    && !string.Equals(qType.Trim(), "short_answer", StringComparison.OrdinalIgnoreCase))
                {
                    partAItemsWithWrongType++;
                }
            }
            else if (partCode.StartsWith("B", StringComparison.Ordinal))
            {
                b++;
                if (!HasValidMcqShape(q)) partBItemsWithoutThreeOptions++;
                var distractorIssues = CountJsonDistractorCategoryIssues(q);
                wrongOptionsMissingDistractorCategory += distractorIssues.Missing;
                wrongOptionsInvalidDistractorCategory += distractorIssues.Invalid;
            }
            else if (partCode.StartsWith("C", StringComparison.Ordinal))
            {
                c++;
                if (partCode == "C1") c1++;
                else if (partCode == "C2") c2++;
                if (!HasValidMcqShape(q)) partCItemsWithoutThreeOptions++;
                var distractorIssues = CountJsonDistractorCategoryIssues(q);
                wrongOptionsMissingDistractorCategory += distractorIssues.Missing;
                wrongOptionsInvalidDistractorCategory += distractorIssues.Invalid;
            }
        }

        var duplicateNumbers = numbers.Count(kvp => kvp.Value > 1);
        if (duplicateNumbers > 0)
        {
            warnings.Add(new("listening_duplicate_question_numbers", "error",
                $"Question numbers must be unique across the paper; {duplicateNumbers} duplicate number group(s) found."));
        }

        if ((a1 > 0 || a2 > 0) && (a1 != 12 || a2 != 12))
        {
            warnings.Add(new("listening_part_a_split", "error",
                $"Part A requires two consultations with exactly 12 items each; found A1={a1}, A2={a2}."));
        }
        if ((c1 > 0 || c2 > 0) && (c1 != 6 || c2 != 6))
        {
            warnings.Add(new("listening_part_c_split", "error",
                $"Part C requires two presentations with exactly 6 items each; found C1={c1}, C2={c2}."));
        }

        if (blankStems > 0)
        {
            warnings.Add(new("listening_blank_stems", "error",
                $"Every Listening item requires learner-facing question text; {blankStems} item(s) have a blank stem."));
        }

        if (blankAnswers > 0)
        {
            warnings.Add(new("listening_blank_answers", "error",
                $"Every Listening item requires a non-empty correct answer; {blankAnswers} item(s) are missing one."));
        }

        if (partAItemsWithWrongType > 0)
        {
            warnings.Add(new("listening_part_a_type", "error",
                $"Part A items must be short-answer (note-completion); {partAItemsWithWrongType} item(s) use a different question type."));
        }

        if (partBItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_b_mcq_shape", "error",
                $"Part B requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partBItemsWithoutThreeOptions} item(s) violate this."));
        }

        if (partCItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_c_mcq_shape", "error",
                $"Part C requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partCItemsWithoutThreeOptions} item(s) violate this."));
        }

        if (extracts.Count == 0)
        {
            warnings.Add(new("listening_extract_timing", "error",
                "At least one authored Listening extract with audio cue timings is required before publishing."));
        }
        else
        {
            var invalidExtractTimings = extracts.Count(extract => !HasValidTimingWindow(TryGetInt(extract, "audioStartMs"), TryGetInt(extract, "audioEndMs")));
            if (invalidExtractTimings > 0)
            {
                warnings.Add(new("listening_extract_timing", "error",
                    $"Every Listening extract requires valid audio cue timings; {invalidExtractTimings} extract(s) are missing start/end ms or have an invalid window."));
            }

            var missingExtractDifficulty = extracts.Count(extract => !IsValidDifficulty(TryGetInt(extract, "difficultyRating") ?? TryGetInt(extract, "difficultyLevel")));
            if (missingExtractDifficulty > 0)
            {
                warnings.Add(new("listening_extract_difficulty", "error",
                    $"Every Listening extract requires a difficulty rating from 1 to 5; {missingExtractDifficulty} extract(s) are missing or invalid."));
            }
        }

        if (missingSkillTags > 0)
        {
            warnings.Add(new("listening_skill_tags", "error",
                $"Every Listening question requires a skill tag; {missingSkillTags} item(s) are missing one."));
        }

        if (invalidSkillTags > 0)
        {
            warnings.Add(new("listening_skill_tags_invalid", "error",
                $"Every Listening skill tag must use the canonical vocabulary; {invalidSkillTags} item(s) have an unsupported value."));
        }

        if (invalidEvidence > 0)
        {
            warnings.Add(new("listening_transcript_evidence", "error",
                $"Every Listening question requires transcript evidence text with valid start/end timestamps; {invalidEvidence} item(s) are incomplete."));
        }

        if (missingQuestionDifficulty > 0)
        {
            warnings.Add(new("listening_question_difficulty", "error",
                $"Every Listening question requires a difficulty level from 1 to 5; {missingQuestionDifficulty} item(s) are missing or invalid."));
        }

        if (wrongOptionsMissingDistractorCategory > 0)
        {
            warnings.Add(new("listening_distractor_categories", "error",
                $"Every wrong Part B/C option requires a distractor category; {wrongOptionsMissingDistractorCategory} option(s) are untagged."));
        }

        if (wrongOptionsInvalidDistractorCategory > 0)
        {
            warnings.Add(new("listening_distractor_categories_invalid", "error",
                $"Every wrong Part B/C option distractor category must use the canonical vocabulary; {wrongOptionsInvalidDistractorCategory} option(s) have unsupported values."));
        }

        return (a, b, c, a + b + c, warnings);
    }

    private static bool HasValidMcqShape(Dictionary<string, object?> question)
    {
        var type = ReadString(question, "type") ?? ReadString(question, "questionType");
        var options = ReadOptions(question);
        var correctAnswer = ReadString(question, "correctAnswer")?.Trim();
        if (!string.Equals(type?.Trim(), "multiple_choice_3", StringComparison.OrdinalIgnoreCase)
            || options.Count != 3
            || string.IsNullOrWhiteSpace(correctAnswer))
        {
            return false;
        }

        return ResolveJsonCorrectOptionIndex(options, correctAnswer) >= 0;
    }

    private static bool HasValidMcqShape(
        ListeningQuestionType questionType,
        string? correctAnswer,
        IReadOnlyCollection<RelationalOption> options)
    {
        var normalizedCorrectAnswer = correctAnswer?.Trim();
        if (questionType != ListeningQuestionType.MultipleChoice3
            || options.Count != 3
            || string.IsNullOrWhiteSpace(normalizedCorrectAnswer))
        {
            return false;
        }

        var correctOptions = options.Count(option => option.IsCorrect);
        var matchingOptions = options
            .Where(option => string.Equals(option.OptionKey.Trim(), normalizedCorrectAnswer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Text.Trim(), normalizedCorrectAnswer, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return correctOptions == 1
            && matchingOptions.Length == 1
            && matchingOptions[0].IsCorrect;
    }

    private static (int Missing, int Invalid) CountJsonDistractorCategoryIssues(Dictionary<string, object?> question)
    {
        var options = ReadOptions(question);
        var correctAnswer = ReadString(question, "correctAnswer")?.Trim();
        var correctOptionIndex = ResolveJsonCorrectOptionIndex(options, correctAnswer);
        var categories = ReadStringList(question.GetValueOrDefault("optionDistractorCategory"))
            ?? ReadStringList(question.GetValueOrDefault("perOptionDistractorCategory"))
            ?? Array.Empty<string?>();
        var missing = 0;
        var invalid = 0;

        for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            if (optionIndex == correctOptionIndex) continue;

            var category = optionIndex < categories.Count ? categories[optionIndex] : null;
            if (string.IsNullOrWhiteSpace(category)) missing++;
            else if (!AllowedDistractorCategories.Contains(category.Trim())) invalid++;
        }

        return (missing, invalid);
    }

    private static bool HasSourceLegalAttestation(string? sourceProvenance)
    {
        if (string.IsNullOrWhiteSpace(sourceProvenance)) return false;
        var tokens = sourceProvenance.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var separatorIndex = token.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0) continue;

            var key = token[..separatorIndex].Trim();
            var value = token[(separatorIndex + 1)..].Trim();
            if (string.Equals(key, "legal", StringComparison.OrdinalIgnoreCase)
                && LegalAttestationValues.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    private static int ResolveJsonCorrectOptionIndex(IReadOnlyList<string> options, string? correctAnswer)
    {
        if (options.Count != 3 || string.IsNullOrWhiteSpace(correctAnswer)) return -1;

        var normalized = correctAnswer.Trim();
        if (normalized.Length == 1)
        {
            var optionKeyIndex = char.ToUpperInvariant(normalized[0]) switch
            {
                'A' => 0,
                'B' => 1,
                'C' => 2,
                _ => -1,
            };
            if (optionKeyIndex >= 0)
            {
                var matchingTextCount = options.Count(option => string.Equals(option.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
                return matchingTextCount > 1 ? -1 : optionKeyIndex;
            }
        }

        var matches = options
            .Select((option, index) => new { Option = option.Trim(), Index = index })
            .Where(option => string.Equals(option.Option, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0].Index : -1;
    }

    private static List<Dictionary<string, object?>> ReadObjectList(object? raw)
    {
        if (raw is null) return new List<Dictionary<string, object?>>();
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                ?? new List<Dictionary<string, object?>>();
        }
        catch (JsonException)
        {
            return new List<Dictionary<string, object?>>();
        }
    }

    private static IReadOnlyList<string?>? ReadStringList(object? raw)
    {
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string?>>(JsonSerializer.Serialize(raw));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadOptions(Dictionary<string, object?> question)
    {
        var raw = question.GetValueOrDefault("options");
        if (raw is null) return Array.Empty<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string?>>(JsonSerializer.Serialize(raw));
            return list?
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Select(option => option!.Trim())
                .ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private sealed record RelationalOption(
        string OptionKey,
        string Text,
        bool IsCorrect,
        ListeningDistractorCategory? DistractorCategory);

    private static bool HasValidTimingWindow(int? startMs, int? endMs)
        => startMs is >= 0 && endMs is > 0 && endMs > startMs;

    private static bool IsValidDifficulty(int? value)
        => value is >= 1 and <= 5;

    private static string? ReadString(Dictionary<string, object?> question, string key)
        => question.GetValueOrDefault(key)?.ToString();

    private static int? TryGetInt(Dictionary<string, object?> question, string key)
    {
        var raw = question.GetValueOrDefault(key);
        return raw switch
        {
            int n => n,
            long n => checked((int)n),
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var n) => n,
            _ when int.TryParse(raw?.ToString(), out var n) => n,
            _ => null,
        };
    }

    private static string? ReadJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Number => doc.RootElement.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
