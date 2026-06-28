namespace OetLearner.Api.Services.Writing.Configuration;

/// <summary>
/// Configuration root for Writing Module V2. Bound from <c>appsettings.json</c>
/// section <c>Writing</c>. Defaults match the dev profile in the plan §WS5.6.
/// Cron toggles are honoured by every hosted service so tests can disable the
/// schedulers without injecting timers.
/// </summary>
public sealed class WritingV2Options
{
    public const string SectionName = "Writing";

    public bool CronsEnabled { get; set; } = true;
    public bool CoachEnabled { get; set; } = true;
    public decimal CoachDailyCostCapPerLearnerUsd { get; set; } = 0.5m;
    public int CoachMaxHintsPerSession { get; set; } = 80;
    public int CoachMinSecondsBetweenHints { get; set; } = 30;
    public string? GcvApiKey { get; set; }
    public bool OcrEnabled { get; set; }
    /// <summary>
    /// Filesystem path to the Tesseract <c>tessdata</c> directory (containing
    /// trained language data files such as <c>eng.traineddata</c>). When
    /// <c>null</c> the OCR service tries the standard Linux container path
    /// (<c>/usr/share/tesseract-ocr/5/tessdata</c>) and degrades to zero
    /// confidence if the directory or native binaries are missing.
    /// </summary>
    public string? TessdataPath { get; set; }
    public bool AppealsEnabled { get; set; } = true;
    public int TutorReviewQueueMaxDepth { get; set; } = 50;
    public int TutorReviewMaxWaitHours { get; set; } = 36;
    public int MaxDailyPlanRegenerationsPerDay { get; set; } = 1;
    public int GradeIdempotencyTtlHours { get; set; } = 24;
}
