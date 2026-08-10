using System.Text.Json;
using OetLearner.Api.Domain;

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

public static class WritingAssessmentReportBuilder
{
    private static readonly string[] CriterionOrder =
    ["purpose", "content", "conciseness_clarity", "genre_style", "organisation_layout", "language"];

    public static IReadOnlyList<WritingAssessmentCriterionInput> DefaultCriteria(
        short purpose,
        short content,
        short conciseness,
        short genre,
        short organisation,
        short language)
        =>
        [
            new("purpose", purpose, 3, "The task and recipient request were reviewed.", "Purpose limitations require targeted revision.", ["task snapshot", "recipient and request parse"], "State the purpose immediately."),
            new("content", content, 7, "The case-note fact map was reviewed.", "Content selection requires targeted revision.", ["case-note fact map", "candidate inclusion and omission map"], "Select only recipient-relevant facts."),
            new("conciseness_clarity", conciseness, 7, "Word count and sentence clarity were reviewed.", "Clarity limitations require targeted revision.", ["word count", "sentence and linker checks"], "Remove repetition and excess detail."),
            new("genre_style", genre, 7, "The letter type and recipient register were reviewed.", "Genre limitations require targeted revision.", ["letter type", "recipient register"], "Use recipient-appropriate register."),
            new("organisation_layout", organisation, 7, "Paragraph sequence and layout were reviewed.", "Organisation limitations require targeted revision.", ["paragraph structure", "address, salutation, and closing"], "Use clear chronological paragraphs."),
            new("language", language, 7, "Deterministic language and punctuation rules were reviewed.", "Language limitations require targeted revision.", ["deterministic rule findings", "candidate letter language"], "Correct the highest-impact language errors."),
        ];

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
                Correction = finding.FixSuggestion ?? "Revise the highlighted wording against the cited rule.",
                RuleSource = finding.RuleId,
                WhyItMatters = finding.Message,
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

        var topPriorities = input.RuleFindings
            .OrderBy(x => SeverityRank(x.Severity))
            .ThenBy(x => x.StartOffset ?? int.MaxValue)
            .Select(x => $"{x.RuleId}: {x.Message}")
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        report.TopPrioritiesJson = JsonSerializer.Serialize(topPriorities);
        report.StrengthsJson = JsonSerializer.Serialize(criteria.Select(x => x.StrengthObservation).Take(3));
        report.StudyPlanJson = JsonSerializer.Serialize(criteria
            .OrderBy(x => x.Score / (double)Math.Max(1, x.MaximumScore))
            .Take(4)
            .Select(x => x.ImprovementAction));
        report.FeatureRecordJson = JsonSerializer.Serialize(new
        {
            criteria = criteria.Select(x => new { x.CriterionCode, x.Score, x.MaximumScore }),
            primaryErrors = input.RuleFindings.Select(x => new { x.RuleId, x.Severity, x.PrimaryCriterionCode }),
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

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 0,
        "major" => 1,
        "moderate" => 2,
        "minor" => 3,
        _ => 4,
    };
}
