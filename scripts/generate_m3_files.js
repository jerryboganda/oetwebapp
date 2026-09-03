const fs = require('fs');
const path = require('path');

const files = {
  'backend/src/OetLearner.Api/Services/Scoring/ToeflScoring.cs': `namespace OetLearner.Api.Services.Scoring;

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
`,

  'backend/src/OetLearner.Api/Services/Scoring/OetScoringStrategy.cs': `namespace OetLearner.Api.Services.Scoring;

public sealed class OetScoringStrategy : IExamScoringStrategy
{
    public string ExamTypeCode => "OET";
    public double MinScore => 0;
    public double MaxScore => 500;
    public double DefaultPassThreshold => 350;
    public string GradeScaleName => "A–E (0–500)";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("A", 450, 500, "Very High"),
        new("B", 350, 449, "High (Pass)"),
        new("C+", 300, 349, "Satisfactory"),
        new("C", 200, 299, "Adequate"),
        new("D", 100, 199, "Limited"),
        new("E", 0, 99, "Minimal")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();
        double scaled;
        if (sub is "reading" or "listening")
        {
            var max = maxRawScore > 0 ? maxRawScore : OetScoring.LrRawMax;
            scaled = OetScoring.CalculateRawToScaled(rawScore, max);
        }
        else if (sub == "writing")
        {
            var max = maxRawScore > 0 ? maxRawScore : OetScoring.WritingMaxRawTotal;
            scaled = OetScoring.CalculateWritingScaledScore(rawScore, max);
        }
        else
        {
            var max = maxRawScore > 0 ? maxRawScore : OetScoring.SpeakingMaxRawTotal;
            scaled = OetScoring.CalculateSpeakingScaledScore(rawScore, max);
        }

        var scaledInt = (int)Math.Round(scaled);
        var gradeEnum = OetScoring.GradeFromScaled(scaledInt);
        var gradeLabel = OetScoring.GradeLabel(gradeEnum);

        bool? isPass;
        string? passReason;
        if (sub == "writing" && !string.IsNullOrWhiteSpace(countryCode))
        {
            var res = OetScoring.IsWritingPass(scaledInt, countryCode);
            isPass = res.Passed;
            passReason = res.Reason;
        }
        else
        {
            isPass = scaledInt >= (int)DefaultPassThreshold;
            passReason = isPass.Value ? "Met standard grade B threshold (350+)" : "Below grade B threshold (350)";
        }

        var formatted = FormatScoreDisplay(subtestCode, scaledInt, gradeLabel);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            scaledInt,
            gradeLabel,
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        var sub = (subtestCode ?? "").Trim().ToLowerInvariant();
        if (sub == "writing" && !string.IsNullOrWhiteSpace(countryCode))
        {
            var res = OetScoring.IsWritingPass(scaledScore, countryCode);
            return res.Passed ?? (scaledScore >= 350);
        }
        return scaledScore >= DefaultPassThreshold;
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        var grade = gradeLetter ?? OetScoring.GradeLabel(OetScoring.GradeFromScaled(scaledScore));
        return $"{scaledScore} / {grade}";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        var band = OetScoring.SpeakingReadinessBandFromScaled((int)Math.Round(scaledScore));
        return OetScoring.SpeakingReadinessBandCode(band);
    }
}
`,

  'backend/src/OetLearner.Api/Services/Scoring/IeltsScoringStrategy.cs': `namespace OetLearner.Api.Services.Scoring;

public sealed class IeltsScoringStrategy(IIeltsMockEngine? mockEngine = null) : IExamScoringStrategy
{
    public string ExamTypeCode => "IELTS";
    public double MinScore => 0;
    public double MaxScore => 9.0;
    public double DefaultPassThreshold => 7.0;
    public string GradeScaleName => "0–9 Band Scale";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("Band 9", 9.0, 9.0, "Expert"),
        new("Band 8", 8.0, 8.9, "Very Good"),
        new("Band 7", 7.0, 7.9, "Good (Pass)"),
        new("Band 6", 6.0, 6.9, "Competent"),
        new("Band 5", 5.0, 5.9, "Modest"),
        new("Band 4", 4.0, 4.9, "Limited")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        double band;
        if (maxRawScore > 0 && maxRawScore != 9)
        {
            var ratio = (double)rawScore / maxRawScore;
            band = Math.Clamp(ratio * 9.0, 0, 9.0);
        }
        else
        {
            band = Math.Clamp(rawScore, 0, 9.0);
        }

        var roundedBand = Math.Round(band * 2, MidpointRounding.AwayFromZero) / 2.0;
        var gradeLabel = $"Band {roundedBand:0.0}";
        var isPass = roundedBand >= DefaultPassThreshold;
        var passReason = isPass ? "Met IELTS 7.0 benchmark" : "Below IELTS 7.0 benchmark";
        var formatted = FormatScoreDisplay(subtestCode, (int)(roundedBand * 10), gradeLabel);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            roundedBand,
            gradeLabel,
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return band >= DefaultPassThreshold;
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return $"{band:0.0} Band Score";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        var band = scaledScore > 9 ? scaledScore / 10.0 : scaledScore;
        return band switch
        {
            < 5.0 => "not_ready",
            < 6.0 => "developing",
            < 7.0 => "borderline",
            < 8.0 => "exam_ready",
            _ => "strong"
        };
    }
}
`,

  'backend/src/OetLearner.Api/Services/Scoring/PteScoringStrategy.cs': `namespace OetLearner.Api.Services.Scoring;

public sealed class PteScoringStrategy(IPteScoring pteScoring) : IExamScoringStrategy
{
    public string ExamTypeCode => "PTE";
    public double MinScore => 10;
    public double MaxScore => 90;
    public double DefaultPassThreshold => 65;
    public string GradeScaleName => "10–90 Scale";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("80–90", 80, 90, "Expert"),
        new("65–79", 65, 79, "Advanced (Pass)"),
        new("50–64", 50, 64, "Upper Intermediate"),
        new("36–49", 36, 49, "Intermediate"),
        new("10–35", 10, 35, "Beginner")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var max = maxRawScore > 0 ? maxRawScore : 90;
        var scaled = pteScoring.ScaleToPte(rawScore, max);
        var level = pteScoring.GetSkillLevel(scaled);
        var isPass = pteScoring.IsPassing(scaled, (int)DefaultPassThreshold);
        var passReason = isPass ? "Met PTE 65 benchmark" : "Below PTE 65 benchmark";
        var formatted = FormatScoreDisplay(subtestCode, scaled);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            scaled,
            $"Score {scaled} ({level})",
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        return pteScoring.IsPassing(scaledScore, (int)DefaultPassThreshold);
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        return $"{scaledScore} / 90";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        return scaledScore switch
        {
            < 36 => "not_ready",
            < 50 => "developing",
            < 65 => "borderline",
            < 80 => "exam_ready",
            _ => "strong"
        };
    }
}
`,

  'backend/src/OetLearner.Api/Services/Scoring/ToeflScoringStrategy.cs': `namespace OetLearner.Api.Services.Scoring;

public sealed class ToeflScoringStrategy(IToeflScoring toeflScoring) : IExamScoringStrategy
{
    public string ExamTypeCode => "TOEFL";
    public double MinScore => 0;
    public double MaxScore => 120;
    public double DefaultPassThreshold => 80;
    public string GradeScaleName => "0–120 Scale (4 x 0–30)";

    private static readonly GradeBandDefinition[] Bands =
    [
        new("95–120", 95, 120, "Advanced (C1+)"),
        new("80–94", 80, 94, "High-Intermediate (B2 - Pass)"),
        new("60–79", 60, 79, "Low-Intermediate (B1)"),
        new("0–59", 0, 59, "Below Low-Intermediate")
    ];

    public ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null)
    {
        var max = maxRawScore > 0 ? maxRawScore : 30;
        var scaled = toeflScoring.ScaleSectionScore(rawScore, max);
        var level = toeflScoring.GetSectionLevel(subtestCode, scaled);
        var isPass = scaled >= 20;
        var passReason = isPass ? "Met TOEFL section benchmark (20+)" : "Below TOEFL section benchmark (20)";
        var formatted = FormatScoreDisplay(subtestCode, scaled);

        return new ExamScoreResult(
            ExamTypeCode,
            subtestCode,
            rawScore,
            scaled,
            $"Score {scaled} ({level})",
            isPass,
            passReason,
            formatted,
            Bands);
    }

    public IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode) => Bands;

    public bool IsPass(string subtestCode, int scaledScore, string? countryCode = null)
    {
        var threshold = scaledScore > 30 ? (int)DefaultPassThreshold : 20;
        return toeflScoring.IsPassing(scaledScore, threshold);
    }

    public string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null)
    {
        if (scaledScore > 30)
        {
            return $"{scaledScore} / 120";
        }
        return $"{scaledScore} / 30";
    }

    public string GetReadinessBandCode(double scaledScore)
    {
        return toeflScoring.GetReadinessBand((int)Math.Round(scaledScore));
    }
}
`,

  'backend/src/OetLearner.Api/Services/Scoring/IExamScoringStrategyFactory.cs': `using OetLearner.Api.Services.Common;

namespace OetLearner.Api.Services.Scoring;

public interface IExamScoringStrategyFactory
{
    IExamScoringStrategy GetStrategy(string? examTypeCode);
}

public sealed class ExamScoringStrategyFactory(
    OetScoringStrategy oetStrategy,
    IeltsScoringStrategy ieltsStrategy,
    PteScoringStrategy pteStrategy,
    ToeflScoringStrategy toeflStrategy) : IExamScoringStrategyFactory
{
    public IExamScoringStrategy GetStrategy(string? examTypeCode)
    {
        var normalized = ExamCodes.Normalize(examTypeCode);
        return normalized switch
        {
            "OET" => oetStrategy,
            "IELTS" => ieltsStrategy,
            "PTE" => pteStrategy,
            "TOEFL" => toeflStrategy,
            _ => oetStrategy
        };
    }
}
`,

  'backend/src/OetLearner.Api/Services/ExamSession/IExamSessionDriver.cs': `using OetLearner.Api.Services.Common;

namespace OetLearner.Api.Services.ExamSession;

public sealed record ExamSessionTimingRules(
    int TotalDurationMinutes,
    int PartADurationMinutes,
    int PartBDurationMinutes,
    int PartCDurationMinutes,
    bool EnforceHardLock,
    bool AllowSectionReview);

public sealed record ExamSessionConfig(
    string ExamTypeCode,
    string SubtestCode,
    string DeliveryMode,
    ExamSessionTimingRules TimingRules,
    IReadOnlyList<string> RequiredSectionCodes);

public sealed record ExamSessionValidationResult(
    bool IsValid,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> Warnings);

public interface IExamSessionDriver
{
    string ExamTypeCode { get; }
    ExamSessionConfig CreateSessionConfig(string subtestCode, string deliveryMode);
    ExamSessionValidationResult ValidateTransition(string fromSection, string toSection, TimeSpan elapsed);
    TimeSpan GetRemainingTime(string sectionCode, TimeSpan elapsed);
    bool IsSectionLocked(string sectionCode, TimeSpan elapsed);
}

public sealed class OetExamSessionDriver : IExamSessionDriver
{
    public string ExamTypeCode => "OET";

    public ExamSessionConfig CreateSessionConfig(string subtestCode, string deliveryMode)
    {
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();
        var timings = sub switch
        {
            "reading" => new ExamSessionTimingRules(60, 15, 45, 45, true, false),
            "listening" => new ExamSessionTimingRules(45, 15, 15, 15, true, false),
            "writing" => new ExamSessionTimingRules(45, 5, 40, 0, false, true),
            "speaking" => new ExamSessionTimingRules(20, 3, 5, 5, true, false),
            _ => new ExamSessionTimingRules(60, 15, 45, 45, true, false)
        };

        var sections = sub == "reading" || sub == "listening"
            ? (IReadOnlyList<string>)new[] { "A", "B", "C" }
            : new[] { "main" };

        return new ExamSessionConfig(ExamTypeCode, subtestCode, deliveryMode, timings, sections);
    }

    public ExamSessionValidationResult ValidateTransition(string fromSection, string toSection, TimeSpan elapsed)
    {
        var errors = new List<string>();
        if (fromSection.Equals("A", StringComparison.OrdinalIgnoreCase) && elapsed.TotalMinutes > 15)
        {
            errors.Add("Part A is locked after 15 minutes and cannot be reopened.");
        }
        return new ExamSessionValidationResult(errors.Count == 0, errors, Array.Empty<string>());
    }

    public TimeSpan GetRemainingTime(string sectionCode, TimeSpan elapsed)
    {
        if (sectionCode.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            var remaining = TimeSpan.FromMinutes(15) - elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
        var totalRemaining = TimeSpan.FromMinutes(60) - elapsed;
        return totalRemaining < TimeSpan.Zero ? TimeSpan.Zero : totalRemaining;
    }

    public bool IsSectionLocked(string sectionCode, TimeSpan elapsed)
    {
        if (sectionCode.Equals("A", StringComparison.OrdinalIgnoreCase))
        {
            return elapsed.TotalMinutes >= 15;
        }
        return elapsed.TotalMinutes >= 60;
    }
}

public sealed class IeltsExamSessionDriver : IExamSessionDriver
{
    public string ExamTypeCode => "IELTS";

    public ExamSessionConfig CreateSessionConfig(string subtestCode, string deliveryMode)
    {
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();
        var timings = sub switch
        {
            "reading" => new ExamSessionTimingRules(60, 20, 20, 20, false, true),
            "listening" => new ExamSessionTimingRules(30, 10, 10, 10, true, false),
            "writing" => new ExamSessionTimingRules(60, 20, 40, 0, false, true),
            "speaking" => new ExamSessionTimingRules(15, 5, 5, 5, true, false),
            _ => new ExamSessionTimingRules(60, 20, 20, 20, false, true)
        };

        var sections = sub == "writing" ? new[] { "Task1", "Task2" } : new[] { "Section1", "Section2", "Section3" };
        return new ExamSessionConfig(ExamTypeCode, subtestCode, deliveryMode, timings, sections);
    }

    public ExamSessionValidationResult ValidateTransition(string fromSection, string toSection, TimeSpan elapsed)
    {
        return new ExamSessionValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
    }

    public TimeSpan GetRemainingTime(string sectionCode, TimeSpan elapsed)
    {
        var totalRemaining = TimeSpan.FromMinutes(60) - elapsed;
        return totalRemaining < TimeSpan.Zero ? TimeSpan.Zero : totalRemaining;
    }

    public bool IsSectionLocked(string sectionCode, TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 60;
    }
}

public sealed class PteExamSessionDriver : IExamSessionDriver
{
    public string ExamTypeCode => "PTE";

    public ExamSessionConfig CreateSessionConfig(string subtestCode, string deliveryMode)
    {
        var timings = new ExamSessionTimingRules(120, 30, 30, 30, true, false);
        return new ExamSessionConfig(ExamTypeCode, subtestCode, deliveryMode, timings, new[] { "SpeakingWriting", "Reading", "Listening" });
    }

    public ExamSessionValidationResult ValidateTransition(string fromSection, string toSection, TimeSpan elapsed)
    {
        return new ExamSessionValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
    }

    public TimeSpan GetRemainingTime(string sectionCode, TimeSpan elapsed)
    {
        var totalRemaining = TimeSpan.FromMinutes(120) - elapsed;
        return totalRemaining < TimeSpan.Zero ? TimeSpan.Zero : totalRemaining;
    }

    public bool IsSectionLocked(string sectionCode, TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 120;
    }
}

public sealed class ToeflExamSessionDriver : IExamSessionDriver
{
    public string ExamTypeCode => "TOEFL";

    public ExamSessionConfig CreateSessionConfig(string subtestCode, string deliveryMode)
    {
        var sub = (subtestCode ?? "reading").Trim().ToLowerInvariant();
        var timings = sub switch
        {
            "reading" => new ExamSessionTimingRules(35, 35, 0, 0, false, true),
            "listening" => new ExamSessionTimingRules(36, 36, 0, 0, true, false),
            "speaking" => new ExamSessionTimingRules(16, 16, 0, 0, true, false),
            "writing" => new ExamSessionTimingRules(29, 29, 0, 0, false, true),
            _ => new ExamSessionTimingRules(116, 35, 36, 16, true, false)
        };

        return new ExamSessionConfig(ExamTypeCode, subtestCode, deliveryMode, timings, new[] { "Reading", "Listening", "Speaking", "Writing" });
    }

    public ExamSessionValidationResult ValidateTransition(string fromSection, string toSection, TimeSpan elapsed)
    {
        return new ExamSessionValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
    }

    public TimeSpan GetRemainingTime(string sectionCode, TimeSpan elapsed)
    {
        var totalRemaining = TimeSpan.FromMinutes(116) - elapsed;
        return totalRemaining < TimeSpan.Zero ? TimeSpan.Zero : totalRemaining;
    }

    public bool IsSectionLocked(string sectionCode, TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 116;
{
            "OET" => oetDriver,
            "IELTS" => ieltsDriver,
            "PTE" => pteDriver,
            "TOEFL" => toeflDriver,
            _ => oetDriver
        };
    }
}
`,

  'lib/toefl-scoring.ts': `// ============================================================================
// TOEFL iBT Canonical Scoring Module — SINGLE SOURCE OF TRUTH
// ============================================================================
//
// Verified from ETS TOEFL iBT official sources:
//   - Total score scale: 0–120 (sum of 4 section scores)
//   - Four sub-tests: Reading, Listening, Speaking, Writing (0–30 scale each)
//   - Section performance levels mapped to CEFR proficiency bands
//   - Default healthcare/academic target: 80–84 (common benchmark for nursing/boards)
//
// References:
//   - https://www.ets.org/toefl/test-takers/ibt/scores/understand-scores.html
// ============================================================================

import type { SharedReadinessBand } from './exam-family-scoring';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** TOEFL iBT sub-tests. */
export type ToeflSubtest = 'reading' | 'listening' | 'speaking' | 'writing';

/** TOEFL iBT section performance level (CEFR-aligned). */
export type ToeflSectionBand =
  | 'advanced'              // CEFR C1 or higher
  | 'high_intermediate'     // CEFR B2
  | 'low_intermediate'      // CEFR B1
  | 'below_low_intermediate'; // Below B1

/** TOEFL iBT overall and section score report. */
export interface ToeflScoreReport {
  overall: number;
  reading: number;
  listening: number;
  speaking: number;
  writing: number;
  bands?: Partial<Record<ToeflSubtest, ToeflSectionBand>>;
}

/** Result of a TOEFL score determination. */
export interface ToeflScoreResult {
  score: number;
  scoreDisplay: string;
  meetsTarget: boolean | null;
  targetScore: number | null;
  readinessBand: SharedReadinessBand;
}

// ---------------------------------------------------------------------------
// Invariants / Constants
// ---------------------------------------------------------------------------

/** Total score bounds. */
export const TOEFL_SCORE_MIN = 0 as const;
export const TOEFL_SCORE_MAX = 120 as const;

/** Section score bounds (0–30). */
export const TOEFL_SECTION_MIN = 0 as const;
export const TOEFL_SECTION_MAX = 30 as const;

/** Default healthcare registration / university benchmark target. */
export const TOEFL_DEFAULT_TARGET_SCORE = 80 as const;

/** Section proficiency thresholds [Advanced Min, High-Intermediate Min, Low-Intermediate Min]. */
export const TOEFL_SECTION_THRESHOLDS: Record<
  ToeflSubtest,
  { advanced: number; highIntermediate: number; lowIntermediate: number }
> = {
  reading: { advanced: 24, highIntermediate: 18, lowIntermediate: 10 },
  listening: { advanced: 22, highIntermediate: 17, lowIntermediate: 9 },
  speaking: { advanced: 25, highIntermediate: 20, lowIntermediate: 16 },
  writing: { advanced: 24, highIntermediate: 17, lowIntermediate: 13 },
} as const;

// ---------------------------------------------------------------------------
// Pure Scoring & Normalization Helpers
// ---------------------------------------------------------------------------

/** Clamp a TOEFL total score to the valid 0–120 range. */
export function clampToeflScore(value: number): number {
  if (!Number.isFinite(value)) return TOEFL_SCORE_MIN;
  return Math.max(TOEFL_SCORE_MIN, Math.min(TOEFL_SCORE_MAX, Math.round(value)));
}

/** Clamp a TOEFL section score to the valid 0–30 range. */
export function clampToeflSectionScore(value: number): number {
  if (!Number.isFinite(value)) return TOEFL_SECTION_MIN;
  return Math.max(TOEFL_SECTION_MIN, Math.min(TOEFL_SECTION_MAX, Math.round(value)));
}

/** Validate that a value is a valid TOEFL total score (0–120 integer). */
export function isValidToeflScore(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (!Number.isFinite(num)) return false;
  if (num < TOEFL_SCORE_MIN || num > TOEFL_SCORE_MAX) return false;
  return Number.isInteger(num);
}

/** Validate that a value is a valid TOEFL section score (0–30 integer). */
export function isValidToeflSectionScore(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (!Number.isFinite(num)) return false;
  if (num < TOEFL_SECTION_MIN || num > TOEFL_SECTION_MAX) return false;
  return Number.isInteger(num);
}

/** Compute overall score by summing 4 clamped section scores. */
export function toeflOverallScore(sections: {
  reading: number;
  listening: number;
  speaking: number;
  writing: number;
}): number {
  const r = clampToeflSectionScore(sections.reading);
  const l = clampToeflSectionScore(sections.listening);
  const s = clampToeflSectionScore(sections.speaking);
  const w = clampToeflSectionScore(sections.writing);
  return clampToeflScore(r + l + s + w);
}

/** Determine section performance level (CEFR-aligned band). */
export function toeflSectionBand(subtest: ToeflSubtest, score: number): ToeflSectionBand {
  const s = clampToeflSectionScore(score);
  const thresholds = TOEFL_SECTION_THRESHOLDS[subtest] || TOEFL_SECTION_THRESHOLDS.reading;

  if (s >= thresholds.advanced) return 'advanced';
  if (s >= thresholds.highIntermediate) return 'high_intermediate';
  if (s >= thresholds.lowIntermediate) return 'low_intermediate';
  return 'below_low_intermediate';
}

/** Human-readable label for a TOEFL section band. */
export function toeflSectionBandLabel(band: ToeflSectionBand): string {
  switch (band) {
    case 'advanced': return 'Advanced';
    case 'high_intermediate': return 'High-Intermediate';
    case 'low_intermediate': return 'Low-Intermediate';
    case 'below_low_intermediate': return 'Below Low-Intermediate';
  }
}

/**
 * Map a TOEFL total score (0–120) to a shared readiness band.
 *
 *   < 60  -> not_ready
 *   < 70  -> developing
 *   < 80  -> borderline
 *   < 95  -> exam_ready
 *   >= 95 -> strong
 */
export function toeflReadinessBand(score: number): SharedReadinessBand {
  const s = clampToeflScore(score);
  if (s < 60) return 'not_ready';
  if (s < 70) return 'developing';
  if (s < TOEFL_DEFAULT_TARGET_SCORE) return 'borderline';
  if (s < 95) return 'exam_ready';
  return 'strong';
}

/** Human-readable label for a TOEFL readiness band. */
export function toeflReadinessBandLabel(band: SharedReadinessBand): string {
  switch (band) {
    case 'not_ready': return 'Not ready';
    case 'developing': return 'Developing';
    case 'borderline': return 'Borderline';
    case 'exam_ready': return 'Exam-ready';
    case 'strong': return 'Strong';
  }
}

/** Format a TOEFL score for display (e.g. "92/120" or "92"). */
export function formatToeflScoreDisplay(score: number): string {
  return \`\${clampToeflScore(score)}/\${TOEFL_SCORE_MAX}\`;
}

/** Format a TOEFL grade / score label for display. */
export function formatToeflGradeDisplay(score: number): string {
  return \`Score \${clampToeflScore(score)}\`;
}

// ---------------------------------------------------------------------------
// Invariant Self-Check IIFE
// ---------------------------------------------------------------------------
(() => {
  const minClamped = clampToeflScore(-10);
  const maxClamped = clampToeflScore(150);
  if (minClamped !== 0 || maxClamped !== 120) {
    throw new Error(\`[toefl-scoring] Invariant failure: Score clamping [\${minClamped}, \${maxClamped}] != [0, 120]\`);
  }

  const overall = toeflOverallScore({ reading: 25, listening: 20, speaking: 22, writing: 23 });
  if (overall !== 90) {
    throw new Error(\`[toefl-scoring] Invariant failure: Overall 25+20+22+23 = \${overall} != 90\`);
  }

  const rBand = toeflSectionBand('reading', 24);
  if (rBand !== 'advanced') {
    throw new Error(\`[toefl-scoring] Invariant failure: Reading 24 should be advanced, got \${rBand}\`);
  }

  const readiness = toeflReadinessBand(80);
  if (readiness !== 'exam_ready') {
    throw new Error(\`[toefl-scoring] Invariant failure: Score 80 should be exam_ready, got \${readiness}\`);
  }
})();
`,

  'lib/exam-family-scoring.ts': `// ============================================================================
// Exam-Family Scoring Strategy Pattern & Dispatcher — Shared-Core Microkernel
// ============================================================================
//
// This module implements the Extensible Strategy Pattern for multi-exam scoring
// (OET, IELTS, PTE, TOEFL). Shared-core workflows MUST use these strategy
// abstractions instead of hardcoding OET assumptions.
//
// OET-specific code should still import from \`lib/scoring.ts\` directly when
// the context is known to be OET-only.
// ============================================================================

import type { ExamFamilyCode } from './mock-data';
import { oetGradeFromScaled, oetGradeLabel, OET_SCALED_MIN, OET_SCALED_MAX } from './scoring';
import {
  ieltsBandDisplay,
  ieltsRoundBand,
  IELTS_BAND_MIN,
  IELTS_BAND_MAX,
  IELTS_DEFAULT_TARGET_BAND,
} from './ielts-scoring';
import {
  clampPteScore,
  pteReadinessBand,
  pteReadinessBandLabel,
  PTE_SCORE_MIN,
  PTE_SCORE_MAX,
  PTE_DEFAULT_TARGET_SCORE,
} from './pte-scoring';
import {
  clampToeflScore,
  toeflReadinessBand,
  toeflReadinessBandLabel,
  formatToeflScoreDisplay,
  formatToeflGradeDisplay,
  TOEFL_SCORE_MIN,
  TOEFL_SCORE_MAX,
  TOEFL_DEFAULT_TARGET_SCORE,
} from './toefl-scoring';

// ---------------------------------------------------------------------------
// Shared Types & Strategy Interface
// ---------------------------------------------------------------------------

/** Readiness band for any exam family, normalized to a shared vocabulary. */
export type SharedReadinessBand = 'not_ready' | 'developing' | 'borderline' | 'exam_ready' | 'strong';

/**
 * Strategy interface for exam-family-specific scoring, normalization, and presentation behavior.
 */
export interface IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode;
  readonly label: string;
  readonly scoreHint: { hint: string; placeholder: string };
  readonly minScore: number;
  readonly maxScore: number;
  readonly defaultTarget: number;
  formatScore(score: number, subtest?: string): string;
  formatGrade(score: number, subtest?: string): string;
  normalizeTargetScore(value: string | number | null | undefined): number | null;
  getReadinessBand(score: number): SharedReadinessBand;
  isPass(score: number, countryCode?: string | null): boolean;
}

// ---------------------------------------------------------------------------
// Concrete Strategy Implementations
// ---------------------------------------------------------------------------

/** OET Canonical Scoring Strategy */
export class OetScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'oet';
  readonly label = 'OET';
  readonly scoreHint = { hint: 'OET scores use the 0 to 500 scale.', placeholder: 'e.g. 350' };
  readonly minScore = OET_SCALED_MIN;
  readonly maxScore = OET_SCALED_MAX;
  readonly defaultTarget = 350;

  formatScore(score: number): string {
    return \`\${Math.round(score)}/\${OET_SCALED_MAX}\`;
  }

  formatGrade(score: number): string {
    const grade = oetGradeFromScaled(Math.round(score));
    return oetGradeLabel(grade);
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const rounded = Math.round(num);
    if (rounded < OET_SCALED_MIN || rounded > OET_SCALED_MAX) return null;
    return rounded;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    const s = Math.round(score);
    if (s < 250) return 'not_ready';
    if (s < 300) return 'developing';
    if (s < 350) return 'borderline';
    if (s < 420) return 'exam_ready';
    return 'strong';
  }

  isPass(score: number, countryCode?: string | null): boolean {
    const s = Math.round(score);
    const cc = countryCode?.trim().toUpperCase();
    if (cc === 'US' || cc === 'QA') {
      return s >= 300;
    }
    return s >= 350;
  }
}

/** IELTS Canonical Scoring Strategy */
export class IeltsScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'ielts';
  readonly label = 'IELTS';
  readonly scoreHint = { hint: 'IELTS scores use the 0 to 9 band scale (0.5 increments).', placeholder: 'e.g. 7.0' };
  readonly minScore = IELTS_BAND_MIN;
  readonly maxScore = IELTS_BAND_MAX;
  readonly defaultTarget = IELTS_DEFAULT_TARGET_BAND;

  formatScore(score: number): string {
    return ieltsBandDisplay(score);
  }

  formatGrade(score: number): string {
    return \`Band \${ieltsBandDisplay(score)}\`;
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const band = ieltsRoundBand(num);
    if (band < IELTS_BAND_MIN || band > IELTS_BAND_MAX) return null;
    return band;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    const b = ieltsRoundBand(score);
    if (b < 5.0) return 'not_ready';
    if (b < 5.5) return 'developing';
    if (b < IELTS_DEFAULT_TARGET_BAND) return 'borderline';
    if (b < 7.5) return 'exam_ready';
    return 'strong';
  }

  isPass(score: number): boolean {
    return ieltsRoundBand(score) >= IELTS_DEFAULT_TARGET_BAND;
  }
}

/** PTE Academic Scoring Strategy */
export class PteScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'pte';
  readonly label = 'PTE';
  readonly scoreHint = { hint: 'PTE scores use the 10 to 90 scale.', placeholder: 'e.g. 65' };
  readonly minScore = PTE_SCORE_MIN;
  readonly maxScore = PTE_SCORE_MAX;
  readonly defaultTarget = PTE_DEFAULT_TARGET_SCORE;

  formatScore(score: number): string {
    return String(clampPteScore(score));
  }

  formatGrade(score: number): string {
    return \`Score \${clampPteScore(score)}\`;
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const clamped = Math.round(num);
    if (clamped < PTE_SCORE_MIN || clamped > PTE_SCORE_MAX) return null;
    return clamped;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    return pteReadinessBand(score);
  }

  isPass(score: number): boolean {
    return clampPteScore(score) >= PTE_DEFAULT_TARGET_SCORE;
  }
}

/** TOEFL iBT Scoring Strategy */
export class ToeflScoringStrategy implements IExamScoringStrategy {
  readonly examFamily: ExamFamilyCode = 'toefl';
  readonly label = 'TOEFL';
  readonly scoreHint = { hint: 'TOEFL scores use the 0 to 120 scale (4 sub-tests 0–30).', placeholder: 'e.g. 80' };
  readonly minScore = TOEFL_SCORE_MIN;
  readonly maxScore = TOEFL_SCORE_MAX;
  readonly defaultTarget = TOEFL_DEFAULT_TARGET_SCORE;

  formatScore(score: number): string {
    return formatToeflScoreDisplay(score);
  }

  formatGrade(score: number): string {
    return formatToeflGradeDisplay(score);
  }

  normalizeTargetScore(value: string | number | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;
    const num = typeof value === 'string' ? parseFloat(value) : value;
    if (!Number.isFinite(num)) return null;
    const clamped = Math.round(num);
    if (clamped < TOEFL_SCORE_MIN || clamped > TOEFL_SCORE_MAX) return null;
    return clamped;
  }

  getReadinessBand(score: number): SharedReadinessBand {
    return toeflReadinessBand(score);
  }

  isPass(score: number): boolean {
    return clampToeflScore(score) >= TOEFL_DEFAULT_TARGET_SCORE;
  }
}

// ---------------------------------------------------------------------------
// Strategy Registry
// ---------------------------------------------------------------------------

const registry: Record<string, IExamScoringStrategy> = {
  oet: new OetScoringStrategy(),
  ielts: new IeltsScoringStrategy(),
  pte: new PteScoringStrategy(),
  toefl: new ToeflScoringStrategy(),
};

/**
 * Register or override a strategy for an exam family.
 */
export function registerExamScoringStrategy(strategy: IExamScoringStrategy): void {
  registry[strategy.examFamily.toLowerCase()] = strategy;
}

/**
 * Resolve an IExamScoringStrategy instance for the given exam family code.
 * Defaults to OET if unknown or omitted.
 */
export function getExamScoringStrategy(examFamily: ExamFamilyCode | string | null | undefined): IExamScoringStrategy {
  const key = (examFamily || 'oet').toLowerCase().trim();
  return registry[key] || registry['oet'];
}

// ---------------------------------------------------------------------------
// Backward-Compatible Exported Wrappers
// ---------------------------------------------------------------------------

/**
 * Format a score for display according to the exam family's conventions.
 *
 *   OET   -> "380/500" (scaled score)
 *   IELTS -> "7.0"     (band score)
 *   PTE   -> "65"      (10–90 score)
 *   TOEFL -> "90/120"  (0–120 score)
 */
export function formatScoreDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  return getExamScoringStrategy(examFamily).formatScore(score);
}

/**
 * Format a grade label for display according to the exam family.
 *
 *   OET   -> "Grade B"
 *   IELTS -> "Band 7.0"
 *   PTE   -> "Score 65"
 *   TOEFL -> "Score 90"
 */
export function formatGradeDisplay(
  examFamily: ExamFamilyCode,
  score: number,
): string {
  return getExamScoringStrategy(examFamily).formatGrade(score);
}

/**
 * Validate that a goal/target score string is valid for the given exam family.
 * Returns a normalized number or null if invalid.
 */
export function normalizeTargetScore(
  examFamily: ExamFamilyCode,
  value: string | number | null | undefined,
): number | null {
  return getExamScoringStrategy(examFamily).normalizeTargetScore(value);
}

/**
 * Map a score to a shared readiness band, using exam-family-specific thresholds.
 */
export function sharedReadinessBand(
  examFamily: ExamFamilyCode,
  score: number,
): SharedReadinessBand {
  return getExamScoringStrategy(examFamily).getReadinessBand(score);
}

/** Human-readable label for a shared readiness band. */
export function sharedReadinessBandLabel(band: SharedReadinessBand): string {
  switch (band) {
    case 'not_ready': return 'Not ready';
    case 'developing': return 'Developing';
    case 'borderline': return 'Borderline';
    case 'exam_ready': return 'Exam-ready';
    case 'strong': return 'Strong';
  }
}

/**
 * Human-friendly label for an exam family code.
 */
export function examFamilyLabel(code: ExamFamilyCode): string {
  return getExamScoringStrategy(code).label;
}

/**
 * Score hint / placeholder text for an exam family.
 */
export function examFamilyScoreHint(code: ExamFamilyCode): { hint: string; placeholder: string } {
  return getExamScoringStrategy(code).scoreHint;
}
`,

  'tests/unit/toefl-scoring.test.ts': `import { describe, it, expect } from 'vitest';
import {
  clampToeflScore,
  clampToeflSectionScore,
  isValidToeflScore,
  isValidToeflSectionScore,
  toeflOverallScore,
  toeflSectionBand,
  toeflSectionBandLabel,
  toeflReadinessBand,
  toeflReadinessBandLabel,
  formatToeflScoreDisplay,
  formatToeflGradeDisplay,
  TOEFL_SCORE_MIN,
  TOEFL_SCORE_MAX,
  TOEFL_SECTION_MIN,
  TOEFL_SECTION_MAX,
} from '../../lib/toefl-scoring';

describe('TOEFL iBT Scoring Engine (lib/toefl-scoring.ts)', () => {
  describe('Bounds and Clamping', () => {
    it('clamps total score to 0..120 range', () => {
      expect(clampToeflScore(-10)).toBe(TOEFL_SCORE_MIN);
      expect(clampToeflScore(0)).toBe(0);
      expect(clampToeflScore(85.4)).toBe(85);
      expect(clampToeflScore(85.6)).toBe(86);
      expect(clampToeflScore(120)).toBe(120);
      expect(clampToeflScore(150)).toBe(TOEFL_SCORE_MAX);
      expect(clampToeflScore(NaN)).toBe(TOEFL_SCORE_MIN);
      expect(clampToeflScore(Infinity)).toBe(TOEFL_SCORE_MIN);
    });

    it('clamps section score to 0..30 range', () => {
      expect(clampToeflSectionScore(-5)).toBe(TOEFL_SECTION_MIN);
      expect(clampToeflSectionScore(0)).toBe(0);
      expect(clampToeflSectionScore(24.2)).toBe(24);
      expect(clampToeflSectionScore(30)).toBe(30);
      expect(clampToeflSectionScore(35)).toBe(TOEFL_SECTION_MAX);
      expect(clampToeflSectionScore(NaN)).toBe(TOEFL_SECTION_MIN);
    });
  });

  describe('Validation', () => {
    it('validates total scores correctly', () => {
      expect(isValidToeflScore(0)).toBe(true);
      expect(isValidToeflScore(120)).toBe(true);
      expect(isValidToeflScore(80)).toBe(true);
      expect(isValidToeflScore('85')).toBe(true);
      expect(isValidToeflScore(-1)).toBe(false);
      expect(isValidToeflScore(121)).toBe(false);
      expect(isValidToeflScore(85.5)).toBe(false);
      expect(isValidToeflScore('abc')).toBe(false);
      expect(isValidToeflScore(null)).toBe(false);
      expect(isValidToeflScore(undefined)).toBe(false);
      expect(isValidToeflScore('')).toBe(false);
    });

    it('validates section scores correctly', () => {
      expect(isValidToeflSectionScore(0)).toBe(true);
      expect(isValidToeflSectionScore(30)).toBe(true);
      expect(isValidToeflSectionScore(25)).toBe(true);
      expect(isValidToeflSectionScore('22')).toBe(true);
      expect(isValidToeflSectionScore(-1)).toBe(false);
      expect(isValidToeflSectionScore(31)).toBe(false);
      expect(isValidToeflSectionScore(20.5)).toBe(false);
      expect(isValidToeflSectionScore(null)).toBe(false);
    });
  });

  describe('Section Band Mapping (CEFR Proficiency Levels)', () => {
    it('evaluates Reading section levels', () => {
      expect(toeflSectionBand('reading', 24)).toBe('advanced');
      expect(toeflSectionBand('reading', 30)).toBe('advanced');
      expect(toeflSectionBand('reading', 18)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 23)).toBe('high_intermediate');
      expect(toeflSectionBand('reading', 10)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 17)).toBe('low_intermediate');
      expect(toeflSectionBand('reading', 9)).toBe('below_low_intermediate');
      expect(toeflSectionBand('reading', 0)).toBe('below_low_intermediate');
    });

    it('evaluates Listening section levels', () => {
      expect(toeflSectionBand('listening', 22)).toBe('advanced');
      expect(toeflSectionBand('listening', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('listening', 9)).toBe('low_intermediate');
      expect(toeflSectionBand('listening', 8)).toBe('below_low_intermediate');
    });

    it('evaluates Speaking section levels', () => {
      expect(toeflSectionBand('speaking', 25)).toBe('advanced');
      expect(toeflSectionBand('speaking', 20)).toBe('high_intermediate');
      expect(toeflSectionBand('speaking', 16)).toBe('low_intermediate');
      expect(toeflSectionBand('speaking', 15)).toBe('below_low_intermediate');
    });

    it('evaluates Writing section levels', () => {
      expect(toeflSectionBand('writing', 24)).toBe('advanced');
      expect(toeflSectionBand('writing', 17)).toBe('high_intermediate');
      expect(toeflSectionBand('writing', 13)).toBe('low_intermediate');
      expect(toeflSectionBand('writing', 12)).toBe('below_low_intermediate');
    });

    it('formats section band labels accurately', () => {
      expect(toeflSectionBandLabel('advanced')).toBe('Advanced');
      expect(toeflSectionBandLabel('high_intermediate')).toBe('High-Intermediate');
      expect(toeflSectionBandLabel('low_intermediate')).toBe('Low-Intermediate');
      expect(toeflSectionBandLabel('below_low_intermediate')).toBe('Below Low-Intermediate');
    });
  });

  describe('Readiness Band Mapping & Formatting', () => {
    it('maps total score to shared readiness bands', () => {
      expect(toeflReadinessBand(50)).toBe('not_ready');
      expect(toeflReadinessBand(59)).toBe('not_ready');
      expect(toeflReadinessBand(60)).toBe('developing');
      expect(toeflReadinessBand(69)).toBe('developing');
      expect(toeflReadinessBand(70)).toBe('borderline');
      expect(toeflReadinessBand(79)).toBe('borderline');
      expect(toeflReadinessBand(80)).toBe('exam_ready');
      expect(toeflReadinessBand(94)).toBe('exam_ready');
      expect(toeflReadinessBand(95)).toBe('strong');
      expect(toeflReadinessBand(120)).toBe('strong');
    });

    it('returns human-readable readiness labels', () => {
      expect(toeflReadinessBandLabel('not_ready')).toBe('Not ready');
      expect(toeflReadinessBandLabel('developing')).toBe('Developing');
      expect(toeflReadinessBandLabel('borderline')).toBe('Borderline');
      expect(toeflReadinessBandLabel('exam_ready')).toBe('Exam-ready');
      expect(toeflReadinessBandLabel('strong')).toBe('Strong');
    });

    it('formats display strings accurately', () => {
      expect(formatToeflScoreDisplay(92)).toBe('92/120');
      expect(formatToeflGradeDisplay(92)).toBe('Score 92');
    });
  });

  describe('Overall Score Aggregation', () => {
    it('sums all four section scores with proper clamping', () => {
      expect(toeflOverallScore({ reading: 25, listening: 20, speaking: 22, writing: 23 })).toBe(90);
      expect(toeflOverallScore({ reading: 30, listening: 30, speaking: 30, writing: 30 })).toBe(120);
      expect(toeflOverallScore({ reading: 0, listening: 0, speaking: 0, writing: 0 })).toBe(0);
      expect(toeflOverallScore({ reading: 35, listening: 35, speaking: 35, writing: 35 })).toBe(120);
      expect(toeflOverallScore({ reading: -5, listening: -5, speaking: -5, writing: -5 })).toBe(0);
    });
  });
});
`,

  'tests/unit/exam-family-scoring.test.ts': `import { describe, it, expect } from 'vitest';
import {
  getExamScoringStrategy,
  OetScoringStrategy,
  IeltsScoringStrategy,
  PteScoringStrategy,
  ToeflScoringStrategy,
  formatScoreDisplay,
  formatGradeDisplay,
  normalizeTargetScore,
  sharedReadinessBand,
  sharedReadinessBandLabel,
  examFamilyLabel,
  examFamilyScoreHint,
} from '../../lib/exam-family-scoring';

describe('Exam Family Scoring Strategy Pattern (lib/exam-family-scoring.ts)', () => {
  describe('Strategy Resolution & Polymorphism', () => {
    it('resolves OetScoringStrategy for "oet"', () => {
      const strategy = getExamScoringStrategy('oet');
      expect(strategy).toBeInstanceOf(OetScoringStrategy);
      expect(strategy.examFamily).toBe('oet');
      expect(strategy.label).toBe('OET');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(500);
      expect(strategy.defaultTarget).toBe(350);
    });

    it('resolves IeltsScoringStrategy for "ielts"', () => {
      const strategy = getExamScoringStrategy('ielts');
      expect(strategy).toBeInstanceOf(IeltsScoringStrategy);
      expect(strategy.examFamily).toBe('ielts');
      expect(strategy.label).toBe('IELTS');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(9);
      expect(strategy.defaultTarget).toBe(7.0);
    });

    it('resolves PteScoringStrategy for "pte"', () => {
      const strategy = getExamScoringStrategy('pte');
      expect(strategy).toBeInstanceOf(PteScoringStrategy);
      expect(strategy.examFamily).toBe('pte');
      expect(strategy.label).toBe('PTE');
      expect(strategy.minScore).toBe(10);
      expect(strategy.maxScore).toBe(90);
      expect(strategy.defaultTarget).toBe(65);
    });

    it('resolves ToeflScoringStrategy for "toefl"', () => {
      const strategy = getExamScoringStrategy('toefl');
      expect(strategy).toBeInstanceOf(ToeflScoringStrategy);
      expect(strategy.examFamily).toBe('toefl');
      expect(strategy.label).toBe('TOEFL');
      expect(strategy.minScore).toBe(0);
      expect(strategy.maxScore).toBe(120);
      expect(strategy.defaultTarget).toBe(80);
    });

    it('defaults to OET strategy when unknown code or null is provided', () => {
      expect(getExamScoringStrategy('unknown')).toBeInstanceOf(OetScoringStrategy);
      expect(getExamScoringStrategy('')).toBeInstanceOf(OetScoringStrategy);
      expect(getExamScoringStrategy(null as unknown as string)).toBeInstanceOf(OetScoringStrategy);
    });
  });

  describe('OET Strategy Execution', () => {
    const oet = new OetScoringStrategy();

    it('formats score display on 0-500 scale', () => {
      expect(oet.formatScore(380)).toBe('380/500');
      expect(oet.formatScore(350)).toBe('350/500');
    });

    it('formats grade display with OET letter grades', () => {
      expect(oet.formatGrade(460)).toBe('Grade A');
      expect(oet.formatGrade(380)).toBe('Grade B');
      expect(oet.formatGrade(320)).toBe('Grade C+');
      expect(oet.formatGrade(250)).toBe('Grade C');
      expect(oet.formatGrade(150)).toBe('Grade D');
      expect(oet.formatGrade(50)).toBe('Grade E');
    });

    it('normalizes target scores', () => {
      expect(oet.normalizeTargetScore('350')).toBe(350);
      expect(oet.normalizeTargetScore(400)).toBe(400);
      expect(oet.normalizeTargetScore(-10)).toBe(null);
      expect(oet.normalizeTargetScore(550)).toBe(null);
      expect(oet.normalizeTargetScore('invalid')).toBe(null);
    });

    it('evaluates pass determinations with country awareness', () => {
      expect(oet.isPass(350, 'GB')).toBe(true);
      expect(oet.isPass(340, 'GB')).toBe(false);
      expect(oet.isPass(300, 'US')).toBe(true);
      expect(oet.isPass(300, 'QA')).toBe(true);
      expect(oet.isPass(300, 'AU')).toBe(false);
    });
  });

  describe('IELTS Strategy Execution', () => {
    const ielts = new IeltsScoringStrategy();

    it('formats score display with 0.5 half-band precision', () => {
      expect(ielts.formatScore(7.0)).toBe('7.0');
      expect(ielts.formatScore(7.25)).toBe('7.5');
      expect(ielts.formatScore(6.75)).toBe('7.0');
    });

    it('formats grade display with Band prefix', () => {
      expect(ielts.formatGrade(7.0)).toBe('Band 7.0');
      expect(ielts.formatGrade(8.5)).toBe('Band 8.5');
    });

    it('normalizes target scores in 0..9 range', () => {
      expect(ielts.normalizeTargetScore('7.5')).toBe(7.5);
      expect(ielts.normalizeTargetScore(8)).toBe(8);
      expect(ielts.normalizeTargetScore(-1)).toBe(null);
      expect(ielts.normalizeTargetScore(10)).toBe(null);
    });

    it('evaluates pass determinations against standard 7.0 benchmark', () => {
      expect(ielts.isPass(7.0)).toBe(true);
      expect(ielts.isPass(7.5)).toBe(true);
      expect(ielts.isPass(6.5)).toBe(false);
    });
  });

  describe('PTE Strategy Execution', () => {
    const pte = new PteScoringStrategy();

    it('formats score display on 10-90 scale', () => {
      expect(pte.formatScore(65)).toBe('65');
      expect(pte.formatScore(5)).toBe('10');
      expect(pte.formatScore(95)).toBe('90');
    });

    it('formats grade display with Score prefix', () => {
      expect(pte.formatGrade(65)).toBe('Score 65');
    });

    it('normalizes target scores in 10..90 range', () => {
      expect(pte.normalizeTargetScore('65')).toBe(65);
      expect(pte.normalizeTargetScore(5)).toBe(null);
      expect(pte.normalizeTargetScore(95)).toBe(null);
    });

    it('evaluates pass determinations against 65 target', () => {
      expect(pte.isPass(65)).toBe(true);
      expect(pte.isPass(79)).toBe(true);
      expect(pte.isPass(64)).toBe(false);
    });
  });

  describe('TOEFL Strategy Execution', () => {
    const toefl = new ToeflScoringStrategy();

    it('formats score display on 0-120 scale', () => {
      expect(toefl.formatScore(92)).toBe('92/120');
      expect(toefl.formatScore(120)).toBe('120/120');
    });

    it('formats grade display with Score prefix', () => {
      expect(toefl.formatGrade(92)).toBe('Score 92');
    });

    it('normalizes target scores in 0..120 range', () => {
      expect(toefl.normalizeTargetScore('80')).toBe(80);
      expect(toefl.normalizeTargetScore(-5)).toBe(null);
      expect(toefl.normalizeTargetScore(130)).toBe(null);
    });

    it('evaluates pass determinations against 80 target', () => {
      expect(toefl.isPass(80)).toBe(true);
      expect(toefl.isPass(100)).toBe(true);
      expect(toefl.isPass(79)).toBe(false);
    });
  });

  describe('Backward Compatibility Helper Functions', () => {
    it('dispatches formatScoreDisplay correctly across all 4 exam families', () => {
      expect(formatScoreDisplay('oet', 380)).toBe('380/500');
      expect(formatScoreDisplay('ielts', 7.0)).toBe('7.0');
      expect(formatScoreDisplay('pte', 65)).toBe('65');
      expect(formatScoreDisplay('toefl', 90)).toBe('90/120');
    });

    it('dispatches formatGradeDisplay correctly across all 4 exam families', () => {
      expect(formatGradeDisplay('oet', 380)).toBe('Grade B');
      expect(formatGradeDisplay('ielts', 7.0)).toBe('Band 7.0');
      expect(formatGradeDisplay('pte', 65)).toBe('Score 65');
      expect(formatGradeDisplay('toefl', 90)).toBe('Score 90');
    });

    it('dispatches normalizeTargetScore correctly', () => {
      expect(normalizeTargetScore('oet', '350')).toBe(350);
      expect(normalizeTargetScore('ielts', '7.0')).toBe(7.0);
      expect(normalizeTargetScore('pte', '65')).toBe(65);
      expect(normalizeTargetScore('toefl', '80')).toBe(80);
    });

    it('dispatches sharedReadinessBand and labels', () => {
      expect(sharedReadinessBand('oet', 380)).toBe('exam_ready');
      expect(sharedReadinessBand('ielts', 7.0)).toBe('exam_ready');
      expect(sharedReadinessBand('pte', 70)).toBe('exam_ready');
      expect(sharedReadinessBand('toefl', 85)).toBe('exam_ready');

      expect(sharedReadinessBandLabel('exam_ready')).toBe('Exam-ready');
    });

    it('provides labels and score hints for all 4 exam families', () => {
      expect(examFamilyLabel('oet')).toBe('OET');
      expect(examFamilyLabel('ielts')).toBe('IELTS');
      expect(examFamilyLabel('pte')).toBe('PTE');
      expect(examFamilyLabel('toefl')).toBe('TOEFL');

      expect(examFamilyScoreHint('toefl').placeholder).toBe('e.g. 80');
    });
  });
});
`,

  'backend/tests/OetLearner.Api.Tests/MultiExamStrategyTests.cs': `using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services;
using OetLearner.Api.Services.ExamSession;
using OetLearner.Api.Services.Scoring;
using Xunit;

namespace OetLearner.Api.Tests;

public class MultiExamStrategyTests
{
    private readonly OetScoringStrategy _oetStrategy = new();
    private readonly IeltsScoringStrategy _ieltsStrategy = new();
    private readonly PteScoringStrategy _pteStrategy = new(new PteScoring(NullLogger<PteScoring>.Instance));
    private readonly ToeflScoringStrategy _toeflStrategy = new(new ToeflScoring(NullLogger<ToeflScoring>.Instance));

    private readonly ExamScoringStrategyFactory _factory;
    private readonly ExamSessionDriverFactory _driverFactory;

    public MultiExamStrategyTests()
    {
        _factory = new ExamScoringStrategyFactory(_oetStrategy, _ieltsStrategy, _pteStrategy, _toeflStrategy);
        _driverFactory = new ExamSessionDriverFactory(
            new OetExamSessionDriver(),
            new IeltsExamSessionDriver(),
            new PteExamSessionDriver(),
            new ToeflExamSessionDriver());
    }

    [Fact]
    public void Factory_ResolvesAllFourStrategiesCorrectly()
    {
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("OET"));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("oet"));
        Assert.IsType<IeltsScoringStrategy>(_factory.GetStrategy("IELTS"));
        Assert.IsType<IeltsScoringStrategy>(_factory.GetStrategy("ielts"));
        Assert.IsType<PteScoringStrategy>(_factory.GetStrategy("PTE"));
        Assert.IsType<PteScoringStrategy>(_factory.GetStrategy("pte"));
        Assert.IsType<ToeflScoringStrategy>(_factory.GetStrategy("TOEFL"));
        Assert.IsType<ToeflScoringStrategy>(_factory.GetStrategy("toefl"));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy(null));
        Assert.IsType<OetScoringStrategy>(_factory.GetStrategy("unknown"));
    }

    [Fact]
    public void OetStrategy_CalculatesScoreAndMaintains30Of42Benchmark()
    {
        var passScore = _oetStrategy.CalculateScore("reading", 30, 42);
        Assert.Equal(350, passScore.ScaledScore);
        Assert.Equal("Grade B", passScore.GradeLabel);
        Assert.True(passScore.IsPass);

        var zeroScore = _oetStrategy.CalculateScore("listening", 0, 42);
        Assert.Equal(0, zeroScore.ScaledScore);
        Assert.Equal("Grade E", zeroScore.GradeLabel);
        Assert.False(zeroScore.IsPass);

        var maxScore = _oetStrategy.CalculateScore("reading", 42, 42);
        Assert.Equal(500, maxScore.ScaledScore);
        Assert.Equal("Grade A", maxScore.GradeLabel);
        Assert.True(maxScore.IsPass);
    }

    [Fact]
    public void OetStrategy_WritingCountryResolution()
    {
        // 300 scaled score is Grade C+
        Assert.True(_oetStrategy.IsPass("writing", 300, "US"));
        Assert.True(_oetStrategy.IsPass("writing", 300, "QA"));
        Assert.False(_oetStrategy.IsPass("writing", 300, "GB"));
        Assert.False(_oetStrategy.IsPass("writing", 300, "AU"));
        Assert.True(_oetStrategy.IsPass("writing", 350, "GB"));
    }

    [Fact]
    public void IeltsStrategy_CalculatesBandsAndHalfBandRounding()
    {
        var result = _ieltsStrategy.CalculateScore("reading", 32, 40);
        Assert.InRange(result.ScaledScore, 7.0, 7.5);
        Assert.StartsWith("Band ", result.GradeLabel);
        Assert.True(result.IsPass);

        Assert.True(_ieltsStrategy.IsPass("overall", 70)); // scaled 7.0
        Assert.False(_ieltsStrategy.IsPass("overall", 65)); // scaled 6.5
    }

    [Fact]
    public void PteStrategy_CalculatesScoresOn10To90Scale()
    {
        var result = _pteStrategy.CalculateScore("reading", 65, 90);
        Assert.Equal(65, result.ScaledScore);
        Assert.True(result.IsPass);

        Assert.True(_pteStrategy.IsPass("overall", 65));
        Assert.False(_pteStrategy.IsPass("overall", 64));
    }

    [Fact]
    public void ToeflStrategy_CalculatesScoresOn0To120Scale()
    {
        var sectionResult = _toeflStrategy.CalculateScore("reading", 25, 30);
        Assert.Equal(25, sectionResult.ScaledScore);
        Assert.Contains("Advanced", sectionResult.GradeLabel);
        Assert.True(sectionResult.IsPass);

        Assert.True(_toeflStrategy.IsPass("overall", 80));
        Assert.False(_toeflStrategy.IsPass("overall", 79));
        Assert.True(_toeflStrategy.IsPass("reading", 22));
    }

    [Fact]
    public void DriverFactory_ResolvesAllDriversCorrectly()
    {
        var oetDriver = _driverFactory.GetDriver("OET");
        Assert.IsType<OetExamSessionDriver>(oetDriver);
        var oetConfig = oetDriver.CreateSessionConfig("reading", "exam");
        Assert.Equal(60, oetConfig.TimingRules.TotalDurationMinutes);
        Assert.Equal(15, oetConfig.TimingRules.PartADurationMinutes);
        Assert.True(oetConfig.TimingRules.EnforceHardLock);

        var ieltsDriver = _driverFactory.GetDriver("IELTS");
        Assert.IsType<IeltsExamSessionDriver>(ieltsDriver);

        var pteDriver = _driverFactory.GetDriver("PTE");
        Assert.IsType<PteExamSessionDriver>(pteDriver);

        var toeflDriver = _driverFactory.GetDriver("TOEFL");
        Assert.IsType<ToeflExamSessionDriver>(toeflDriver);
    }

    [Fact]
    public void ScoringService_DelegatesPolymorphicallyThroughFactory()
    {
        var service = new ScoringService(_factory);
        Assert.Contains("Grade B", service.FormatScoreDisplay("OET", 380));
        Assert.Contains("Band Score", service.FormatScoreDisplay("IELTS", 7.5));
        Assert.Contains("/ 90", service.FormatScoreDisplay("PTE", 65));
        Assert.Contains("/ 120", service.FormatScoreDisplay("TOEFL", 90));
    }
}
`
};

for (const [relPath, content] of Object.entries(files)) {
  const fullPath = path.resolve(relPath);
  fs.mkdirSync(path.dirname(fullPath), { recursive: true });
  fs.writeFileSync(fullPath, content.trim() + '\n', 'utf8');
  console.log('Created: ' + relPath);
}
