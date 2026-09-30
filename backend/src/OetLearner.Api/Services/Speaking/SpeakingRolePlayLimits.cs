using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// One role-play's server-side time budget: it started at <see cref="StartedAt"/>, the card's
/// (capped) time ends at <see cref="DeadlineAt"/>, and the server force-ends it at
/// <see cref="HardStopAt"/> (deadline plus grace).
/// </summary>
public readonly record struct SpeakingRolePlayWindow(
    DateTimeOffset StartedAt,
    int EffectiveSeconds,
    DateTimeOffset DeadlineAt,
    DateTimeOffset HardStopAt);

/// <summary>
/// Pure arithmetic for the hard server-side role-play cap. The persisted inputs are
/// <c>SpeakingSession.RolePlayStartedAt</c> (stamped once) and the card's own seconds, so the
/// learner endpoints, the live voice guards and the sweeper all derive the same deadline
/// and nothing here touches a clock or the database.
/// </summary>
public static class SpeakingRolePlayLimits
{
    /// <summary>The OET role-play length, used when a card carries no positive time.</summary>
    public const int DefaultRolePlaySeconds = 300;

    private static readonly LiveVoiceOptions Defaults = new();

    /// <summary>The longest role-play the server allows, whatever the card says.</summary>
    public static int CeilingSeconds(LiveVoiceOptions? options)
        => Math.Clamp((options ?? Defaults).MaxRoleplaySeconds, 180, 1800);

    public static int GraceSeconds(LiveVoiceOptions? options)
        => Math.Clamp((options ?? Defaults).HardStopGraceSeconds, 0, 120);

    public static int FlushSeconds(LiveVoiceOptions? options)
        => Math.Clamp((options ?? Defaults).TranscriptFlushGraceSeconds, 60, 3600);

    public static int MaxProviderSessions(LiveVoiceOptions? options)
        => Math.Clamp((options ?? Defaults).MaxProviderSessionsPerRolePlay, 1, 10);

    /// <summary>The card's role-play seconds (300 when it has none), capped at the ceiling.</summary>
    public static int EffectiveSeconds(int cardSeconds, LiveVoiceOptions? options)
        => Math.Min(cardSeconds > 0 ? cardSeconds : DefaultRolePlaySeconds, CeilingSeconds(options));

    /// <summary>
    /// The window of a started role-play. An Active row with no <c>RolePlayStartedAt</c> (a data
    /// anomaly) falls back to <c>UpdatedAt</c> so no session is immortal.
    /// </summary>
    public static SpeakingRolePlayWindow Resolve(SpeakingSession session, int cardSeconds, LiveVoiceOptions? options)
        => Resolve(session.RolePlayStartedAt ?? session.UpdatedAt, cardSeconds, options);

    public static SpeakingRolePlayWindow Resolve(DateTimeOffset startedAt, int cardSeconds, LiveVoiceOptions? options)
    {
        var seconds = EffectiveSeconds(cardSeconds, options);
        var deadline = startedAt.AddSeconds(seconds);
        return new SpeakingRolePlayWindow(startedAt, seconds, deadline, deadline.AddSeconds(GraceSeconds(options)));
    }

    /// <summary>
    /// Whether a turn, transcript or recording write is still accepted. An Active session accepts
    /// writes until the hard stop plus the flush window (the sweeper finalises it long before);
    /// a Finished one until its end plus the flush window. Grading freezes a Finished
    /// transcript separately (that needs the database).
    /// </summary>
    public static bool IsWithinWriteWindow(
        SpeakingSession session,
        int cardSeconds,
        DateTimeOffset now,
        LiveVoiceOptions? options)
    {
        var flush = TimeSpan.FromSeconds(FlushSeconds(options));
        return session.State switch
        {
            SpeakingSessionState.Active => now <= Resolve(session, cardSeconds, options).HardStopAt + flush,
            SpeakingSessionState.Finished => now <= (session.EndedAt ?? session.UpdatedAt) + flush,
            _ => false,
        };
    }
}
