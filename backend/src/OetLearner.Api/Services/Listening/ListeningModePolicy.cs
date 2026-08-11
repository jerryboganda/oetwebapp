using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — strategy interface for the 5 player modes. Each mode is a
/// stateless singleton answering "what is allowed in this mode?" so the FSM
/// in <see cref="ListeningSessionService"/> can branch behaviour without a
/// switch statement on the enum. Per planner Wave 2 §2.
///
/// All values are derived from the rulebook (R-codes in
/// <c>PRD-LISTENING-V2.md</c> §4 / docs/LISTENING-RULEBOOK-CITATIONS.md).
/// </summary>
public interface IListeningModePolicy
{
    /// <summary>Stable string code matching <see cref="ListeningAttemptMode"/>
    /// so DI keys, JSON contracts, and admin tooling all line up.</summary>
    string Mode { get; }

    /// <summary>R06 — section can never be re-entered after advance.</summary>
    bool OneWayLocks { get; }

    /// <summary>R06.10 — section advance requires confirm-token round trip.</summary>
    bool ConfirmDialogRequired { get; }

    /// <summary>R06.11 — show inline list of unanswered Qs before advance.</summary>
    bool UnansweredWarningRequired { get; }

    /// <summary>R03 — learner may pause audio (drill / learning only).</summary>
    bool AudioPauseAllowed { get; }

    /// <summary>R03 — learner may scrub the audio scrubber (learning only).</summary>
    bool AudioSeekAllowed { get; }

    /// <summary>R03 — replay audio after first play.</summary>
    bool ReplayAllowed { get; }

    /// <summary>R09 — transcript visible while reviewing answers.</summary>
    bool TranscriptVisibleOnReview { get; }

    /// <summary>Whether the current mode permits navigation beyond the active section.</summary>
    bool FreeNavigation { get; }

    /// <summary>R10 — audio sound check required before <c>intro→a1_preview</c>.
    /// Device, resolution, scale, and network signals remain advisory guidance
    /// and are never represented by this launch gate.</summary>
    bool RequiresTechReadiness { get; }

    /// <summary>R08 — annotations (highlights, strikethroughs) survive section advance.</summary>
    bool AnnotationsPersistOnAdvance { get; }

    /// <summary>OET-Home only — fullscreen + tab-focus telemetry.</summary>
    bool FullscreenEnforced { get; }

    /// <summary>Legacy review-window contract. Always null for computer-based modes.</summary>
    int? FinalReviewAllPartsMs { get; }
}

internal sealed record CbtModePolicy : IListeningModePolicy
{
    public string Mode => "Exam";
    public bool OneWayLocks => true;
    public bool ConfirmDialogRequired => true;
    public bool UnansweredWarningRequired => true;
    public bool AudioPauseAllowed => false;
    public bool AudioSeekAllowed => false;
    public bool ReplayAllowed => false;
    public bool TranscriptVisibleOnReview => false;
    public bool FreeNavigation => false;
    public bool RequiresTechReadiness => true;
    public bool AnnotationsPersistOnAdvance => true;
    public bool FullscreenEnforced => false;
    public int? FinalReviewAllPartsMs => null;
}

internal sealed record OetHomeModePolicy : IListeningModePolicy
{
    public string Mode => "Home";
    public bool OneWayLocks => true;
    public bool ConfirmDialogRequired => true;
    public bool UnansweredWarningRequired => true;
    public bool AudioPauseAllowed => false;
    public bool AudioSeekAllowed => false;
    public bool ReplayAllowed => false;
    public bool TranscriptVisibleOnReview => false;
    public bool FreeNavigation => false;
    public bool RequiresTechReadiness => true;
    public bool AnnotationsPersistOnAdvance => true;
    public bool FullscreenEnforced => true;
    public int? FinalReviewAllPartsMs => null;
}

internal sealed record LearningModePolicy : IListeningModePolicy
{
    public string Mode => "Learning";
    public bool OneWayLocks => false;
    public bool ConfirmDialogRequired => false;
    public bool UnansweredWarningRequired => false;
    // Audio is non-pausable in every mode (owner directive 2026-06-27). Note:
    // the learner DTO derives canPause/canScrub/onePlayOnly inline in
    // ListeningLearnerService, not from these flags — kept false here so the
    // interface contract stays truthful for any future consumer.
    public bool AudioPauseAllowed => false;
    public bool AudioSeekAllowed => false;
    public bool ReplayAllowed => false;
    public bool TranscriptVisibleOnReview => true;
    public bool FreeNavigation => true;
    public bool RequiresTechReadiness => false;
    public bool AnnotationsPersistOnAdvance => true;
    public bool FullscreenEnforced => false;
    public int? FinalReviewAllPartsMs => null;
}

internal sealed record DiagnosticModePolicy : IListeningModePolicy
{
    public string Mode => "Diagnostic";
    public bool OneWayLocks => false;
    public bool ConfirmDialogRequired => false;
    public bool UnansweredWarningRequired => true;
    public bool AudioPauseAllowed => false;
    public bool AudioSeekAllowed => false;
    public bool ReplayAllowed => false;
    public bool TranscriptVisibleOnReview => false;
    public bool FreeNavigation => true;
    public bool RequiresTechReadiness => false;
    public bool AnnotationsPersistOnAdvance => false;
    public bool FullscreenEnforced => false;
    public int? FinalReviewAllPartsMs => null;
}

/// <summary>
/// Resolves the right <see cref="IListeningModePolicy"/> for an attempt.
/// Registered as a singleton in DI; the policies themselves carry no state.
/// </summary>
public sealed class ListeningModePolicyResolver
{
    private readonly Dictionary<ListeningAttemptMode, IListeningModePolicy> _byMode = new()
    {
        [ListeningAttemptMode.Exam] = new CbtModePolicy(),
        // Drill / MiniTest / ErrorBank are legacy modes that map to Learning
        // semantics per planner Wave 2 §1(f). Backfill keeps the original enum
        // value on disk for analytics; runtime treats them as Learning.
        [ListeningAttemptMode.Learning] = new LearningModePolicy(),
        [ListeningAttemptMode.Drill] = new LearningModePolicy(),
        [ListeningAttemptMode.MiniTest] = new LearningModePolicy(),
        [ListeningAttemptMode.ErrorBank] = new LearningModePolicy(),
        [ListeningAttemptMode.Home] = new OetHomeModePolicy(),
        [ListeningAttemptMode.Diagnostic] = new DiagnosticModePolicy(),
    };

    public IListeningModePolicy For(ListeningAttemptMode mode)
    {
        if (mode == ListeningAttemptMode.Paper)
        {
            throw ApiException.Validation(
                "listening_paper_mode_disabled",
                "Listening is computer-based only; paper simulation is not supported.");
        }

        return _byMode.TryGetValue(mode, out var p) ? p : _byMode[ListeningAttemptMode.Exam];
    }
}
