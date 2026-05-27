using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// One letter in the 50-letter calibration test harness (spec §33).
/// Curated by Dr Ahmed and used to measure AI agreement (±2 points)
/// before promoting a new model build to production.
/// </summary>
public class WritingCalibrationLetter
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    /// <summary>Full letter content (markdown / plain text).</summary>
    public string LetterContent { get; set; } = string.Empty;

    /// <summary>
    /// One of <c>exemplar</c>, <c>learner</c>, <c>synthetic</c>. Stratifies
    /// the calibration corpus across the population that production grading
    /// actually sees.
    /// </summary>
    [MaxLength(16)]
    public string AuthorTier { get; set; } = "learner";

    /// <summary>
    /// Dr Ahmed's reference grade (JSON shape:
    /// <c>{c1,c2,c3,c4,c5,c6,rawTotal,bandLabel,notes}</c>).
    /// </summary>
    public string DrAhmedGradeJson { get; set; } = "{}";

    public DateTimeOffset AddedAt { get; set; }

    [MaxLength(64)]
    public string AddedById { get; set; } = default!;
}

/// <summary>
/// One execution of the calibration harness against every
/// <see cref="WritingCalibrationLetter"/> row. Summary stats only —
/// per-letter rows live in <see cref="WritingCalibrationResult"/>.
/// </summary>
public class WritingCalibrationRun
{
    public Guid Id { get; set; }

    public DateTimeOffset RunDate { get; set; }

    [MaxLength(64)]
    public string ModelVersion { get; set; } = default!;

    public int TotalLetters { get; set; }

    public int Within2PointsCount { get; set; }

    public double MeanAbsError { get; set; }

    public int BandAgreementCount { get; set; }

    public string NotesMarkdown { get; set; } = string.Empty;
}

/// <summary>
/// Per-letter result for a calibration run.
/// </summary>
public class WritingCalibrationResult
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public Guid CalibrationLetterId { get; set; }

    public string AiGradeJson { get; set; } = "{}";

    /// <summary>
    /// |Dr Ahmed RawTotal − AI RawTotal|. Lower is better; the §33
    /// release gate fires when ≥90% of letters score ≤2.
    /// </summary>
    public int AbsErrorRaw { get; set; }

    public bool BandMatch { get; set; }
}
