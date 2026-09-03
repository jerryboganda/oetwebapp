using OetLearner.Api.Services.Common;

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
    }
}

public interface IExamSessionDriverFactory
{
    IExamSessionDriver GetDriver(string? examTypeCode);
}

public sealed class ExamSessionDriverFactory(
    OetExamSessionDriver oetDriver,
    IeltsExamSessionDriver ieltsDriver,
    PteExamSessionDriver pteDriver,
    ToeflExamSessionDriver toeflDriver) : IExamSessionDriverFactory
{
    public IExamSessionDriver GetDriver(string? examTypeCode)
    {
        var normalized = ExamCodes.Normalize(examTypeCode);
        return normalized switch
        {
            "OET" => oetDriver,
            "IELTS" => ieltsDriver,
            "PTE" => pteDriver,
            "TOEFL" => toeflDriver,
            _ => oetDriver
        };
    }
}

