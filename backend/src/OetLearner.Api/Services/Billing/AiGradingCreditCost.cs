namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Credit cost of AI-graded Writing / Speaking exams. Single source of truth so
/// the start-of-exam gate and the submit-time debit stay in lockstep.
/// </summary>
public static class AiGradingCreditCost
{
    /// <summary>
    /// One Writing activity (one AI-marked letter / case note) costs
    /// <see cref="CreditsPerWritingOrSpeakingActivity"/> AI credits from any
    /// pool. FINAL 2026-09-06: 1 Writing letter = 2 AI credits.
    /// </summary>
    public const int WritingExam = 1;

    /// <summary>
    /// A full AI Speaking exam is two activities — one per card at each card
    /// reveal in <c>SpeakingExamService</c>. FINAL 2026-09-06: 2 cards =
    /// 4 AI credits. Single-card practice stays at
    /// <see cref="SpeakingCard"/>.
    /// </summary>
    public const int SpeakingExam = 2;

    /// <summary>
    /// One AI Speaking card (practice or exam slot) is one activity costing
    /// <see cref="CreditsPerWritingOrSpeakingActivity"/> AI credits.
    /// FINAL 2026-09-06: 1 Speaking card = 2 AI credits.
    /// </summary>
    public const int SpeakingCard = 1;

    /// <summary>
    /// FINAL 2026-09-06: one Writing letter or one Speaking card costs
    /// 2 AI credits from ANY pool (dedicated Writing/Speaking, Flexible W/S,
    /// or Shared). Single source of truth so the start-of-exam gate and the
    /// submit-time debit stay in lockstep.
    /// </summary>
    public const int CreditsPerWritingOrSpeakingActivity = 2;

    /// <summary>
    /// Shared pool cost for one Writing letter or one Speaking card.
    /// Kept for compatibility; equals
    /// <see cref="CreditsPerWritingOrSpeakingActivity"/> since the FINAL
    /// 2026-09-06 uniform 2-credit rule.
    /// </summary>
    public const int SharedWritingOrSpeaking = 2;

    /// <summary>One Listening exam / paper costs one Listening or Shared credit.</summary>
    public const int ListeningExam = 1;

    /// <summary>One Reading exam / paper costs one Reading or Shared credit.</summary>
    public const int ReadingExam = 1;
}
