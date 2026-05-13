namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — server-authoritative FSM transition table. Mirrored by
/// <c>lib/listening/transitions.ts</c> on the frontend; the two are kept in
/// sync by <c>ListeningFsmTransitionsParityTests</c>.
///
/// State naming convention: <c>{partCode}_{phase}</c> where phase is one of
/// <c>preview | audio | review</c>. Special states: <c>intro</c>,
/// <c>transition</c>, <c>c2_final_review</c>, <c>submitted</c>,
/// <c>paywalled</c> (entitlement lapsed mid-attempt — autosave grace only).
/// </summary>
public static class ListeningFsmTransitions
{
    public const string Intro = "intro";
    public const string A1Preview = "a1_preview";
    public const string A1Audio = "a1_audio";
    public const string A1Review = "a1_review";
    public const string A2Preview = "a2_preview";
    public const string A2Audio = "a2_audio";
    public const string A2Review = "a2_review";
    public const string BIntro = "b_intro";
    public const string BAudio = "b_audio";
    public const string C1Preview = "c1_preview";
    public const string C1Audio = "c1_audio";
    public const string C1Review = "c1_review";
    public const string C2Preview = "c2_preview";
    public const string C2Audio = "c2_audio";
    public const string C2Review = "c2_review";
    public const string C2FinalReview = "c2_final_review";
    public const string Submitted = "submitted";
    public const string Paywalled = "paywalled";

    /// <summary>Linear forward path. Used by CBT / OET-Home strict modes.
    /// Paper / Learning / Diagnostic ignore this and allow free navigation.</summary>
    public static readonly IReadOnlyList<string> ForwardPath = new[]
    {
        Intro,
        A1Preview, A1Audio, A1Review,
        A2Preview, A2Audio, A2Review,
        BIntro, BAudio,
        C1Preview, C1Audio, C1Review,
        C2Preview, C2Audio, C2Review, C2FinalReview,
        Submitted,
    };

    public static string? Next(string current)
    {
        var idx = ForwardPath.ToList().IndexOf(current);
        if (idx < 0 || idx >= ForwardPath.Count - 1) return null;
        return ForwardPath[idx + 1];
    }

    /// <summary>Returns the part code (A1/A2/B/C1/C2) for a state, or null
    /// for intro / submitted / paywalled.</summary>
    public static string? PartFor(string state) => state switch
    {
        A1Preview or A1Audio or A1Review => "A1",
        A2Preview or A2Audio or A2Review => "A2",
        BIntro or BAudio => "B",
        C1Preview or C1Audio or C1Review => "C1",
        C2Preview or C2Audio or C2Review or C2FinalReview => "C2",
        _ => null,
    };

    public static bool IsAudioState(string state) => state.EndsWith("_audio");
    public static bool IsReviewState(string state) => state.EndsWith("_review") || state == C2FinalReview;
    public static bool IsPreviewState(string state) => state.EndsWith("_preview") || state == BIntro;
    public static bool IsKnownState(string? state)
        => !string.IsNullOrWhiteSpace(state) && (ForwardPath.Contains(state) || state == Paywalled);
    public static bool IsClientReachableState(string? state)
        => !string.IsNullOrWhiteSpace(state) && ForwardPath.Contains(state);
}
