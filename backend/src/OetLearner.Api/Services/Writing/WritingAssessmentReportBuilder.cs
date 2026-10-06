using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingAssessmentCriterionInput(
    string CriterionCode,
    short Score,
    short MaximumScore,
    string StrengthObservation,
    string LimitationObservation,
    IReadOnlyList<string> Evidence,
    string ImprovementAction);

public sealed record WritingAssessmentReportBuildInput(
    Guid SubmissionId,
    string OriginalLetterHash,
    string OriginalLetter,
    WritingAssessmentPreflightResult Preflight,
    IReadOnlyList<WritingAssessmentRuleFinding> RuleFindings,
    WritingFactMap FactMap,
    IReadOnlyList<WritingAssessmentCriterionInput> Criteria,
    int? EstimatedPracticeScore,
    string ModelVersion,
    string CalibrationSetVersion);

public sealed record WritingAssessmentReportBuildResult(
    WritingAssessmentReportV11 Report,
    WritingAssessmentModelAnswer ModelAnswer)
{
    public ICollection<WritingAssessmentFactEvidence> Facts => Report.Facts;
    public ICollection<WritingAssessmentError> Errors => Report.Errors;
    public ICollection<WritingAssessmentCriterionEvidence> Criteria => Report.Criteria;
    public WritingAssessmentV11Status Status => Report.Status;
    public bool CandidateNumericScoreEnabled => Report.CandidateNumericScoreEnabled;
    public string TopPrioritiesJson => Report.TopPrioritiesJson;
}

/// <summary>
/// The plain-English wording of the six criterion cards. Used when a report is written AND when an older
/// report is read, whose stored text was internal jargon ("case-note fact map", "deterministic rule
/// findings", "targeted revision") that must never reach a candidate. Nothing here names a process, and
/// a criterion with full marks never claims a limitation.
/// </summary>
internal static class WritingCriterionCopy
{
    public const string NoLimitation = "No significant limitation was identified for this criterion.";

    private sealed record Copy(
        string Covers,
        string Strength,
        string Limitation,
        string Improvement,
        string WhyItMatters,
        string LegacyStrength,
        string LegacyLimitation);

    private static readonly Dictionary<string, Copy> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["purpose"] = new(
            "how clearly the letter states its purpose and what the reader is asked to do",
            "The purpose of the letter and the request to the reader are clear.",
            "The purpose or the request to the reader could be stated more clearly.",
            "State the purpose immediately.",
            "The reader needs to see at once why you are writing and what you are asking them to do.",
            "The task and recipient request were reviewed.",
            "Purpose limitations require targeted revision."),
        ["content"] = new(
            "whether the letter includes the relevant facts from the case notes and leaves out the rest",
            "The letter includes the information the reader needs.",
            "Some relevant information is missing, or some less relevant detail is included.",
            "Select only recipient-relevant facts.",
            "The reader should receive the relevant facts from the case notes, accurately and without unnecessary detail.",
            "The case-note fact map was reviewed.",
            "Content selection requires targeted revision."),
        ["conciseness_clarity"] = new(
            "how concise and clear the wording is",
            "The letter is concise and easy to follow.",
            "Some wording is repetitive or unclear.",
            "Remove repetition and excess detail.",
            "Concise, clear wording lets the reader find and act on the key information quickly.",
            "Word count and sentence clarity were reviewed.",
            "Clarity limitations require targeted revision."),
        ["genre_style"] = new(
            "whether the register and style suit a formal letter to this reader",
            "The register and style suit the reader.",
            "The register or style does not always suit a formal letter to this reader.",
            "Use recipient-appropriate register.",
            "A formal, professional register is expected in a letter to another health professional.",
            "The letter type and recipient register were reviewed.",
            "Genre limitations require targeted revision."),
        ["organisation_layout"] = new(
            "the layout of the letter and the order of its paragraphs",
            "The letter is clearly organised and laid out.",
            "The layout or the order of the paragraphs could be clearer.",
            "Use clear chronological paragraphs.",
            "A clear layout and paragraph order help the reader follow the letter.",
            "Paragraph sequence and layout were reviewed.",
            "Organisation limitations require targeted revision."),
        ["language"] = new(
            "the accuracy of grammar, vocabulary and punctuation",
            "The language is accurate and clear.",
            "Some grammar, vocabulary or punctuation errors affect the writing.",
            "Correct the highest-impact language errors.",
            "Accurate, precise language keeps the meaning clear for the reader.",
            "Deterministic language and punctuation rules were reviewed.",
            "Language limitations require targeted revision."),
    };

    private static Copy For(string? code)
        => code is not null && ByCode.TryGetValue(code.Trim(), out var copy) ? copy : ByCode["language"];

    /// <summary>What the card says about a strength: a positive statement for a strong score, otherwise what the criterion covers.</summary>
    public static string Strength(string? code, int score, int max)
    {
        var copy = For(code);
        return max > 0 && score * 10 >= max * 7 ? copy.Strength : $"This criterion looks at {copy.Covers}.";
    }

    /// <summary>Never a limitation on full marks.</summary>
    public static string Limitation(string? code, int score, int max)
        => score >= max ? NoLimitation : For(code).Limitation;

    public static string Improvement(string? code) => For(code).Improvement;

    /// <summary>The plain explanation used when a finding has no message of its own.</summary>
    public static string WhyItMatters(string? criterionCode) => For(criterionCode).WhyItMatters;

    /// <summary>True when <paramref name="text"/> is the internal-jargon strength an older report stored for this criterion.</summary>
    public static bool IsLegacyStrength(string? code, string? text)
        => !string.IsNullOrWhiteSpace(text) && string.Equals(text.Trim(), For(code).LegacyStrength, StringComparison.Ordinal);

    public static bool IsLegacyLimitation(string? code, string? text)
        => !string.IsNullOrWhiteSpace(text) && string.Equals(text.Trim(), For(code).LegacyLimitation, StringComparison.Ordinal);
}

public static class WritingAssessmentReportBuilder
{
    private static readonly string[] CriterionOrder =
    ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    // The text a rewrite has when the finding carries no suggested fix of its own.
    private const string DefaultCorrection = "Rewrite this wording so it reads as a clear, formal clinical letter.";

    public static IReadOnlyList<WritingAssessmentCriterionInput> DefaultCriteria(
        short purpose,
        short content,
        short conciseness,
        short genre,
        short organisation,
        short language)
        =>
        [
            Criterion("purpose", purpose, 3),
            Criterion("content", content, 7),
            Criterion("conciseness_clarity", conciseness, 7),
            Criterion("genre_style", genre, 7),
            Criterion("organisation_layout", organisation, 7),
            Criterion("language", language, 7),
        ];

    private static WritingAssessmentCriterionInput Criterion(string code, short score, short max)
        => new(
            code,
            score,
            max,
            WritingCriterionCopy.Strength(code, score, max),
            WritingCriterionCopy.Limitation(code, score, max),
            [],
            WritingCriterionCopy.Improvement(code));

    public static WritingAssessmentReportBuildResult Build(WritingAssessmentReportBuildInput input)
    {
        var criteria = input.Criteria
            .GroupBy(x => x.CriterionCode.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(group => group.Single())
            .ToArray();
        if (criteria.Length != CriterionOrder.Length
            || CriterionOrder.Any(code => !criteria.Any(x => x.CriterionCode.Equals(code, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("writing_assessment_six_criteria_required");
        }

        var now = DateTimeOffset.UtcNow;
        var report = new WritingAssessmentReportV11
        {
            Id = Guid.NewGuid(),
            SubmissionId = input.SubmissionId,
            Status = WritingAssessmentV11Status.RestrictedCalibration,
            Profession = input.Preflight.Profession,
            LetterType = input.Preflight.LetterType,
            RulePackVersion = input.Preflight.RulePackVersion,
            ModelVersion = input.ModelVersion,
            CalibrationSetVersion = input.CalibrationSetVersion,
            OriginalLetterHash = input.OriginalLetterHash,
            OriginalLetterSnapshot = input.OriginalLetter,
            TaskSnapshot = input.Preflight.TaskSnapshot,
            CaseNotesSnapshot = input.Preflight.CaseNotesSnapshot,
            EstimatedPracticeScore = input.EstimatedPracticeScore,
            ConfidenceLabel = "low",
            // Admin record only: the candidate projection never returns the confidence range.
            ConfidenceRange = "human review required",
            CandidateNumericScoreEnabled = false,
            CandidateReportVisible = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var fact in input.FactMap.Facts)
        {
            report.Facts.Add(new WritingAssessmentFactEvidence
            {
                Id = Guid.NewGuid(),
                ReportId = report.Id,
                Classification = fact.Classification,
                FactText = fact.FactText,
                SourceReference = fact.SourceReference,
                CandidateStatus = fact.CandidateStatus,
                CandidateExcerpt = fact.CandidateExcerpt,
                Explanation = fact.Explanation,
            });
        }

        foreach (var finding in input.RuleFindings)
        {
            if (!WritingAssessmentV11Invariants.IsPrimaryCriterion(finding.PrimaryCriterionCode))
                throw new InvalidOperationException("writing_assessment_primary_criterion_required");
            report.Errors.Add(new WritingAssessmentError
            {
                Id = Guid.NewGuid(),
                ReportId = report.Id,
                Category = finding.Category,
                Location = finding.StartOffset is { } start
                    ? $"character-offset:{start}-{finding.EndOffset ?? start}"
                    : "letter",
                CandidateWording = finding.Quote ?? string.Empty,
                // The rule id stays in RuleSource (admin only); the text a candidate can read is cleaned here
                // as well as when it is projected, so the stored row is already plain English.
                Correction = WritingCandidateText.Clean(finding.FixSuggestion, DefaultCorrection),
                RuleSource = finding.RuleId,
                WhyItMatters = WritingCandidateText.Clean(finding.Message, WritingCriterionCopy.WhyItMatters(finding.PrimaryCriterionCode)),
                Severity = finding.Severity,
                Confidence = "high",
                PrimaryCriterionCode = finding.PrimaryCriterionCode,
                SecondaryCriterionCodesJson = finding.SecondaryCriterionCodesJson,
                StartOffset = finding.StartOffset,
                EndOffset = finding.EndOffset,
            });
        }

        foreach (var criterion in criteria.OrderBy(x => Array.IndexOf(CriterionOrder, x.CriterionCode)))
        {
            report.Criteria.Add(new WritingAssessmentCriterionEvidence
            {
                Id = Guid.NewGuid(),
                ReportId = report.Id,
                CriterionCode = criterion.CriterionCode,
                Score = criterion.Score,
                MaximumScore = criterion.MaximumScore,
                StrengthObservation = criterion.StrengthObservation,
                LimitationObservation = criterion.LimitationObservation,
                EvidenceJson = JsonSerializer.Serialize(criterion.Evidence),
                ImprovementAction = criterion.ImprovementAction,
            });
        }

        // Top Priorities: score-bearing, non-advisory findings only (IsScoreBearing also reads the
        // severity), distinct, impact-ordered, label-free. The ids stay in the admin audit record below.
        var priorities = WritingReportDigest.ComposePriorityItems(input.RuleFindings.Select(x => new WritingDigestFinding(
            x.RuleId,
            x.Severity,
            x.Message,
            x.Quote,
            x.FixSuggestion,
            x.PrimaryCriterionCode,
            x.StartOffset,
            WritingReportDigest.IsScoreBearing(x.RuleId, x.Severity),
            x.Category)));
        report.TopPrioritiesJson = JsonSerializer.Serialize(priorities.Select(p => p.Text));
        // Strengths are the criteria that scored well; the study plan is the criteria that did not reach full marks.
        report.StrengthsJson = JsonSerializer.Serialize(criteria
            .Where(x => x.MaximumScore > 0 && x.Score * 10 >= x.MaximumScore * 7)
            .OrderByDescending(x => x.Score / (double)x.MaximumScore)
            .Take(3)
            .Select(x => x.StrengthObservation));
        report.StudyPlanJson = JsonSerializer.Serialize(criteria
            .Where(x => x.Score < x.MaximumScore)
            .OrderBy(x => x.Score / (double)Math.Max(1, (int)x.MaximumScore))
            .Take(4)
            .Select(x => x.ImprovementAction));
        report.FeatureRecordJson = JsonSerializer.Serialize(new
        {
            criteria = criteria.Select(x => new { x.CriterionCode, x.Score, x.MaximumScore }),
            primaryErrors = input.RuleFindings.Select(x => new
            {
                x.RuleId,
                x.Severity,
                // The detector's own severity before candidate calibration (null for grader findings).
                engineSeverity = x.EngineSeverity,
                x.PrimaryCriterionCode,
                // Ultimate Final §15.1 — rule provenance + score-bearing vs
                // coaching-only status travel with the audit record.
                provenanceTag = x.ProvenanceTag,
                candidateBehavior = x.CandidateBehavior,
            }),
            // Admin-only: which findings took the Top Priority slots, with their rule ids.
            topPriorities = priorities.Select(p => new { ruleId = p.RuleId, criterion = p.Criterion, severity = p.Severity }),
            facts = input.FactMap.Facts.Select(x => new { x.Classification, x.CandidateStatus, x.SourceReference }),
        });

        var modelAnswer = new WritingAssessmentModelAnswer
        {
            Id = Guid.NewGuid(),
            ReportId = report.Id,
            Status = WritingAssessmentModelAnswerStatus.HeldForReview,
            IsCandidateVisible = false,
            HoldReason = "model_answer_grounding_pending",
            CreatedAt = now,
            UpdatedAt = now,
        };

        return new WritingAssessmentReportBuildResult(report, modelAnswer);
    }
}
