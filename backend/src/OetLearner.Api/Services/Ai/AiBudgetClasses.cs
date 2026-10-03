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
///
/// <para>
/// Owner directive 2026-09-23: <see cref="AiOperationClass.AdminBatch"/> is
/// <b>budget-exempt</b> — admin-side AI (content authoring, extraction,
/// indexing, class-recording pipeline, the admin and expert assistants) that
/// does not directly serve a student carries NO day or month ceilings of any
/// kind. See <see cref="IsBudgetExempt"/>. The global month reserve, the kill
/// switch and the per-feature kill list still govern admin calls.
/// </para>
///
/// <para>
/// Owner directive 2026-10-02: every ceiling here refuses a call only while
/// <see cref="AiGlobalPolicy.EnforceSpendCaps"/> is on (default off). Spend
/// against them is booked either way — see <see cref="AiBudgetService"/>.
/// </para>
/// </summary>
public static class AiBudgetClasses
{
    public const string GlobalScope = "global";
    public const string ScoringCriticalScope = "class:ScoringCritical";
    public const string InteractiveLearningScope = "class:InteractiveLearning";
    public const string AdminBatchScope = "class:AdminBatch";

    // Owner decision 2026-09-26: max-reasoning Sonnet 5 grading costs about
    // $0.65-0.85 per Speaking role-play, so the old $3.50/$5 day caps allowed
    // only ~6 grades a day platform-wide. Raised to $30/day and $400/month.
    public const decimal ScoringDailyLimitUsd = 30.00m;
    public const decimal ScoringMonthlyLimitUsd = 400.00m;
    public const decimal InteractiveDailyLimitUsd = 1.00m;
    public const decimal InteractiveMonthlyLimitUsd = 10.00m;
    public const decimal PlatformDailyCapUsd = 30.00m;
    public const decimal PlatformMonthlyCapUsd = 50.00m;

    /// <summary>
    /// AdminBatch calls are exempt from the global day reserve and every
    /// class day/month reserve (owner directive 2026-09-23 — no budget caps
    /// on admin-side AI that does not directly serve students). They remain
    /// metered only by the global month reserve, kill switch and feature
    /// kill list, and every call is still fully recorded in AiUsageRecord.
    /// </summary>
    public static bool IsBudgetExempt(AiOperationClass operationClass)
        => operationClass == AiOperationClass.AdminBatch;

    /// <summary>
    /// Legacy <c>AiGlobalPolicy.MonthlyBudgetUsd = 0</c> meant "unlimited".
    /// W3 fail-closes on a non-positive ceiling, which would refuse every
    /// platform call. Treat 0/unset as the owner-approved $50 UTC month cap
    /// instead of denying the whole product.
    /// </summary>
    public static decimal EffectivePlatformMonthlyLimitUsd(decimal monthlyBudgetUsd, int hardKillPct)
    {
        var monthly = monthlyBudgetUsd > 0m ? monthlyBudgetUsd : PlatformMonthlyCapUsd;
        var pct = hardKillPct <= 0 ? 100 : Math.Clamp(hardKillPct, 0, 150);
        return monthly * pct / 100m;
    }

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
        // AdminBatch is budget-exempt (IsBudgetExempt) and never reaches the
        // period reservers; no monthly limit exists for it any more.
        AiOperationClass.AdminBatch => decimal.MaxValue,
        _ => InteractiveMonthlyLimitUsd,
    };

    public static decimal DailyLimitUsd(AiOperationClass operationClass) => operationClass switch
    {
        AiOperationClass.ScoringCritical => ScoringDailyLimitUsd,
        AiOperationClass.InteractiveLearning => InteractiveDailyLimitUsd,
        // AdminBatch is budget-exempt (IsBudgetExempt) and never reaches the
        // period reservers; no daily limit exists for it any more.
        AiOperationClass.AdminBatch => decimal.MaxValue,
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
            || string.Equals(code, AiFeatureCodes.WritingAppealV1, StringComparison.OrdinalIgnoreCase)
            // P0 (22 Sep 2026): a failed transcription is a failed grade — the
            // Speaking pipeline cannot grade what it cannot transcribe. Also
            // gets ScoringCritical's borrow-from-Interactive-then-Admin
            // headroom, so a busy Interactive month never permanently strands
            // every learner's Speaking submission the way a shared $10/month
            // Interactive-only bucket did (verified live: class_budget_exhausted
            // on OpenAiWhisperSpeakingProvider.TranscribeAsync for 3+ real
            // attempts on 2026-09-22, root-caused to this exact
            // misclassification).
            || string.Equals(code, AiFeatureCodes.SttSpeakingTranscribe, StringComparison.OrdinalIgnoreCase))
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
            || string.Equals(code, AiFeatureCodes.AiAssistantAdmin, StringComparison.OrdinalIgnoreCase)
            // P0 (22 Sep 2026): mirrors the SAME fix already made in
            // Services/Rulebook/AiFeaturePolicyRegistry.cs's ClassOverrides for
            // this exact feature/reason ("carries the writing. prefix so it
            // fell through to the InteractiveLearning default ... this is
            // content authoring, not learner interaction") — that registry
            // governs provider/feature-policy selection; THIS method governs
            // budget reservation (AiBudgetService.ReserveForCallAsync calls
            // ClassForFeature directly, never the registry) and was never
            // given the matching fix, so the two classifications silently
            // drifted apart. $80+/month of admin batch content authoring was
            // sharing (and exhausting) Speaking/Writing learners' $10/month
            // Interactive bucket as a result. Keep both overrides in sync if
            // either changes.
            || string.Equals(code, AiFeatureCodes.WritingModelAnswerPregenerate, StringComparison.OrdinalIgnoreCase)
            // Owner directive 2026-09-23: the expert assistant serves staff
            // experts/tutors, not students — admin-side, budget-exempt.
            || string.Equals(code, AiFeatureCodes.AiAssistantExpert, StringComparison.OrdinalIgnoreCase)
            // Jev dev/review triage never runs on a learner path. Mirrors the
            // ClassOverrides entry in AiFeaturePolicyRegistry (the other
            // "jev." judgments stay InteractiveLearning).
            || string.Equals(code, AiFeatureCodes.JevDevelopmentTriage, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return code.StartsWith("admin.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("class.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("tutor.", StringComparison.OrdinalIgnoreCase)
            // OCR serves admin content authoring (content-PDF fallback,
            // listening extraction) — only handwriting OCR runs on a
            // learner's own submission path.
            || (code.StartsWith("ocr.", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(code, AiFeatureCodes.OcrWritingHandwriting, StringComparison.OrdinalIgnoreCase));
    }
}
