using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// maps <see cref="AiOperationClass"/> / feature codes onto per-class budget
/// scopes and the hard day/month ceilings the owner approved.
///
/// <para>
/// Scoring may borrow unused Interactive then Admin headroom when its own
/// class is exhausted. Lower classes must NEVER reserve the scoring scope —
/// that would let a coach reply starve a grading call.
/// </para>
/// </summary>
public static class AiBudgetClasses
{
    public const string GlobalScope = "global";
    public const string ScoringCriticalScope = "class:ScoringCritical";
    public const string InteractiveLearningScope = "class:InteractiveLearning";
    public const string AdminBatchScope = "class:AdminBatch";

    public const decimal ScoringDailyLimitUsd = 3.50m;
    public const decimal ScoringMonthlyLimitUsd = 35.00m;
    public const decimal InteractiveDailyLimitUsd = 1.00m;
    public const decimal InteractiveMonthlyLimitUsd = 10.00m;
    public const decimal AdminDailyLimitUsd = 0.50m;
    public const decimal AdminMonthlyLimitUsd = 5.00m;
    public const decimal PlatformDailyCapUsd = 5.00m;

    /// <summary>Borrow order when scoring's own class is exhausted. Lower
    /// classes are never donors of scoring, and never borrowers of scoring.</summary>
    public static readonly AiOperationClass[] ScoringBorrowOrder =
    [
        AiOperationClass.InteractiveLearning,
        AiOperationClass.AdminBatch,
    ];

    public static string ScopeFor(AiOperationClass operationClass) => operationClass switch
    {
        AiOperationClass.ScoringCritical => ScoringCriticalScope,
        AiOperationClass.InteractiveLearning => InteractiveLearningScope,
        AiOperationClass.AdminBatch => AdminBatchScope,
        _ => InteractiveLearningScope,
    };

    public static decimal MonthlyLimitUsd(AiOperationClass operationClass) => operationClass switch
    {
        AiOperationClass.ScoringCritical => ScoringMonthlyLimitUsd,
        AiOperationClass.InteractiveLearning => InteractiveMonthlyLimitUsd,
        AiOperationClass.AdminBatch => AdminMonthlyLimitUsd,
        _ => InteractiveMonthlyLimitUsd,
    };

    public static decimal DailyLimitUsd(AiOperationClass operationClass) => operationClass switch
    {
        AiOperationClass.ScoringCritical => ScoringDailyLimitUsd,
        AiOperationClass.InteractiveLearning => InteractiveDailyLimitUsd,
        AiOperationClass.AdminBatch => AdminDailyLimitUsd,
        _ => InteractiveDailyLimitUsd,
    };

    /// <summary>
    /// Scoring may borrow unused Interactive then Admin. Interactive and Admin
    /// must never borrow, and nobody may borrow the scoring scope.
    /// </summary>
    public static bool CanBorrow(AiOperationClass borrower, AiOperationClass donor)
    {
        if (borrower != AiOperationClass.ScoringCritical) return false;
        if (donor == AiOperationClass.ScoringCritical) return false;
        return donor is AiOperationClass.InteractiveLearning or AiOperationClass.AdminBatch;
    }

    /// <summary>Fallback class when no feature-policy row is available.
    /// Scoring-critical: writing/speaking/listening/pronunciation grade/score
    /// features (plus the explicit scoring constants). Admin batch:
    /// <see cref="AiFeatureCodes.AdminContentGeneration"/> and similar
    /// admin/class/tutor authoring codes. Everything else is interactive.</summary>
    public static AiOperationClass ClassForFeature(string? featureCode)
    {
        if (string.IsNullOrWhiteSpace(featureCode)) return AiOperationClass.InteractiveLearning;

        var code = featureCode.Trim();
        if (IsScoringFeature(code)) return AiOperationClass.ScoringCritical;
        if (IsAdminBatchFeature(code)) return AiOperationClass.AdminBatch;
        return AiOperationClass.InteractiveLearning;
    }

    private static bool IsScoringFeature(string code)
    {
        if (string.Equals(code, AiFeatureCodes.WritingGrade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.WritingSampleScore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.SpeakingGrade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.MockFullGrade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.PronunciationScore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.PronunciationLinguisticScore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.ListeningPartAScore, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.ConversationEvaluation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.WritingDrillGradeV1, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.WritingAppealV1, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return code.Contains("grade", StringComparison.OrdinalIgnoreCase)
            || code.Contains(".score", StringComparison.OrdinalIgnoreCase)
            || code.EndsWith(".score", StringComparison.OrdinalIgnoreCase)
            || code.Contains(".score.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdminBatchFeature(string code)
    {
        if (string.Equals(code, AiFeatureCodes.AdminContentGeneration, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminGrammarDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminPronunciationDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminVocabularyDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminConversationDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminListeningDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminReadingDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminWritingDraft, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminListeningSkillTag, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AdminListeningTranscriptSegment, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, AiFeatureCodes.AiAssistantAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return code.StartsWith("admin.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("class.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("tutor.", StringComparison.OrdinalIgnoreCase);
    }
}
