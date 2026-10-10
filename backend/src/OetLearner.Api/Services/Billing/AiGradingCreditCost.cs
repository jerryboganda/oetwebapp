namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Cost of AI-graded Writing / Speaking exams. Single source of truth so the
/// start-of-exam gate and the submit-time debit stay in lockstep.
/// <see cref="WritingExam"/>, <see cref="SpeakingExam"/> and
/// <see cref="SpeakingCard"/> are ACTIVITY COUNTS (letters / cards), not credits;
/// credits = activities x <see cref="CreditsPerWritingOrSpeakingActivity"/>.
/// </summary>
public static class AiGradingCreditCost
{
    /// <summary>
    /// ACTIVITY COUNT, not a credit cost: one Writing letter (one AI-marked
    /// letter / case note) is one activity. Pass it as the debit
    /// <c>quantity</c>; the ledger charges
    /// <see cref="CreditsPerWritingOrSpeakingActivity"/> AI credits for it from
    /// any pool. FINAL 2026-09-06: 1 Writing letter = 2 AI credits.
    /// </summary>
    public const int WritingExam = 1;

    /// <summary>
    /// ACTIVITY COUNT, not a credit cost: a full AI Speaking exam is two cards
    /// (activities), one taken at each card reveal in <c>SpeakingExamService</c>.
    /// FINAL 2026-09-06: 2 cards = 4 AI credits
    /// (<c>SpeakingExam * CreditsPerWritingOrSpeakingActivity</c>). Single-card
    /// practice is <see cref="SpeakingCard"/>.
    /// </summary>
    public const int SpeakingExam = 2;

    /// <summary>
    /// ACTIVITY COUNT, not a credit cost: one AI Speaking card (practice or exam
    /// slot) is one activity, charged
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
