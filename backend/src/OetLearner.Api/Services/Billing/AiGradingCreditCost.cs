namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Credit cost of AI-graded Writing / Speaking exams. Single source of truth so
/// the start-of-exam gate and the submit-time debit stay in lockstep.
/// </summary>
public static class AiGradingCreditCost
{
    /// <summary>
    /// One Writing activity costs one dedicated Writing or Flexible W/S unit.
    /// Shared AI credits cost <see cref="SharedWritingOrSpeaking"/> instead.
    /// </summary>
    public const int WritingExam = 1;

    /// <summary>
    /// A full AI Speaking exam is two activities — one per card at each card
    /// reveal in <c>SpeakingExamService</c>. Single-card practice stays at
    /// <see cref="SpeakingCard"/>.
    /// </summary>
    public const int SpeakingExam = 2;

    /// <summary>One AI Speaking card (practice or exam slot) is one activity.</summary>
    public const int SpeakingCard = 1;

    /// <summary>
    /// Shared pool cost for one Writing letter or one Speaking card.
    /// Dedicated Writing/Speaking and Flexible W/S still cost 1.
    /// </summary>
    public const int SharedWritingOrSpeaking = 2;

    /// <summary>One Listening exam / paper costs one Listening or Shared credit.</summary>
    public const int ListeningExam = 1;

    /// <summary>One Reading exam / paper costs one Reading or Shared credit.</summary>
    public const int ReadingExam = 1;
}
