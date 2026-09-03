namespace OetLearner.Api.Services.Scoring;

/// <summary>
/// TOEFL iBT sub-test raw and scaled scores.
/// </summary>
public sealed record ToeflModuleScores(
    int Reading,
    int Listening,
    int Speaking,
    int Writing);

/// <summary>
/// Overall TOEFL iBT result structure.
/// </summary>
public sealed record ToeflOverallResult(
    int Overall,
    ToeflModuleScores ModuleScores,
    string Level,
    bool IsPassing);

/// <summary>
/// TOEFL iBT scoring contract (0–120 scale, 4 subtests 0–30).
/// </summary>
public interface IToeflScoring
{
    int ClampScore(int score);
    int ClampSectionScore(int score);
    int ScaleSectionScore(double rawScore, int maxRaw);
    ToeflOverallResult ComputeOverall(int reading, int listening, int speaking, int writing);
    string GetSectionLevel(string subtestCode, int score);
    string GetReadinessBand(int totalScore);
    bool IsPassing(int totalScore, int threshold = 80);
}

/// <summary>
/// Canonical implementation of TOEFL iBT scoring.
/// </summary>
public sealed class ToeflScoring(ILogger<ToeflScoring> logger) : IToeflScoring
{
    public const int MinScore = 0;
    public const int MaxScore = 120;
    public const int MinSectionScore = 0;
    public const int MaxSectionScore = 30;
    public const int DefaultPassThreshold = 80;

    public int ClampScore(int score)
    {
        return Math.Clamp(score, MinScore, MaxScore);
    }

    public int ClampSectionScore(int score)
    {
        return Math.Clamp(score, MinSectionScore, MaxSectionScore);
    }

    public int ScaleSectionScore(double rawScore, int maxRaw)
    {
        if (maxRaw <= 0) return MinSectionScore;
        var ratio = rawScore / maxRaw;
        var scaled = (int)Math.Round(ratio * MaxSectionScore);
        return ClampSectionScore(scaled);
    }

    public ToeflOverallResult ComputeOverall(int reading, int listening, int speaking, int writing)
    {
        var r = ClampSectionScore(reading);
        var l = ClampSectionScore(listening);
        var s = ClampSectionScore(speaking);
        var w = ClampSectionScore(writing);

        var overall = ClampScore(r + l + s + w);
        var level = GetOverallLevel(overall);
        var passing = IsPassing(overall, DefaultPassThreshold);

        logger.LogInformation("TOEFL overall calculated: Total={Overall} (R={Reading}, L={Listening}, S={Speaking}, W={Writing}) Passing={Passing}",
            overall, r, l, s, w, passing);

        return new ToeflOverallResult(overall, new ToeflModuleScores(r, l, s, w), level, passing);
    }

    public string GetSectionLevel(string subtestCode, int score)
    {
        var clamped = ClampSectionScore(score);
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();

        return sub switch
        {
            "reading" => clamped switch
            {
                >= 24 => "Advanced",
                >= 18 => "High-Intermediate",
                >= 10 => "Low-Intermediate",
                _ => "Below Low-Intermediate"
            },
            "listening" => clamped switch
            {
                >= 22 => "Advanced",
                >= 17 => "High-Intermediate",
                >= 9 => "Low-Intermediate",
                _ => "Below Low-Intermediate"
            },
            "speaking" => clamped switch
            {
                >= 25 => "Advanced",
                >= 20 => "High-Intermediate",
                >= 16 => "Low-Intermediate",
                _ => "Below Low-Intermediate"
            },
            "writing" => clamped switch
            {
                >= 24 => "Advanced",
                >= 17 => "High-Intermediate",
                >= 13 => "Low-Intermediate",
                _ => "Below Low-Intermediate"
            },
            _ => clamped switch
            {
                >= 24 => "Advanced",
                >= 18 => "High-Intermediate",
                >= 10 => "Low-Intermediate",
                _ => "Below Low-Intermediate"
            }
        };
    }

    public string GetOverallLevel(int totalScore)
    {
        var clamped = ClampScore(totalScore);
        return clamped switch
        {
            >= 95 => "Advanced (C1+)",
            >= 80 => "High-Intermediate (B2)",
            >= 60 => "Low-Intermediate (B1)",
            _ => "Below Low-Intermediate"
        };
    }

    public string GetReadinessBand(int totalScore)
    {
        var clamped = ClampScore(totalScore);
        return clamped switch
        {
            < 60 => "not_ready",
            < 70 => "developing",
            < DefaultPassThreshold => "borderline",
            < 95 => "exam_ready",
            _ => "strong"
        };
    }

    public bool IsPassing(int totalScore, int threshold = DefaultPassThreshold)
    {
        return ClampScore(totalScore) >= threshold;
    }
}
