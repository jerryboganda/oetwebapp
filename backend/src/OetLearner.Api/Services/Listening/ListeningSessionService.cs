using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — server-authoritative session FSM. Handles the per-attempt
/// navigation state machine, the two-step confirm-token advance protocol
/// (R06.10), and the audio-resume grace window. Extracted from the legacy
/// monolithic <c>ListeningLearnerService</c> per planner Wave 2 §2.
///
/// Storage model:
///   - The full FSM payload lives in <c>ListeningAttempt.NavigationStateJson</c>
///     (jsonb on Postgres, TEXT on SQLite). NEVER LINQ-into this column —
///     always materialise the row, parse client-side.
///   - <c>WindowStartedAt</c> + <c>WindowDurationMs</c> are the wall-clock
///     anchors used to compute remaining time on resume.
///   - <c>AudioCueTimelineJson</c> records cue events (audit only).
/// </summary>
public sealed class ListeningSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LearnerDbContext _db;
    private readonly ListeningModePolicyResolver _modes;
    private readonly ListeningConfirmTokenService _tokens;
    private readonly TimeProvider _clock;

    public ListeningSessionService(
        LearnerDbContext db,
        ListeningModePolicyResolver modes,
        ListeningConfirmTokenService tokens,
        TimeProvider clock)
    {
        _db = db;
        _modes = modes;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<SessionStateDto> GetStateAsync(string attemptId, string userId, CancellationToken ct)
    {
        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        var policy = await ResolveEffectivePolicyAsync(userId, ct);
        var mode = _modes.For(attempt.Mode);

        var nav = ParseNavigationState(attempt.NavigationStateJson);
        var shouldRepairNavigation = nav is null;
        nav ??= SeedDefaultNavigation(attempt, policy);

        // Persist the seeded state if it was missing or malformed — lazy backfill.
        if (shouldRepairNavigation)
        {
            attempt.NavigationStateJson = JsonSerializer.Serialize(nav);
            attempt.WindowStartedAt = _clock.GetUtcNow();
            attempt.WindowDurationMs = ComputeWindowMs(nav.State, policy, mode);
            await _db.SaveChangesAsync(ct);
        }

        return ToDto(attempt, nav, policy, mode);
    }

    public async Task<AdvanceResultDto> AdvanceAsync(
        string attemptId, string userId, AdvanceCommand cmd, CancellationToken ct)
    {
        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            return AdvanceResultDto.Rejected(
                "attempt-not-in-progress",
                $"Attempt {attemptId} is {attempt.Status} and cannot be advanced.");
        }

        var policy = await ResolveEffectivePolicyAsync(userId, ct);
        var mode = _modes.For(attempt.Mode);
        var nav = ParseNavigationState(attempt.NavigationStateJson)
                  ?? SeedDefaultNavigation(attempt, policy);
        var now = _clock.GetUtcNow();

        if (!ListeningFsmTransitions.IsClientReachableState(cmd.ToState))
        {
            return AdvanceResultDto.Rejected(
                "invalid-state",
                $"Cannot advance to unknown Listening state {cmd.ToState}.");
        }

        if (RequiresTechReadiness(nav.State, cmd.ToState, policy, mode)
            && !HasValidTechReadiness(attempt.TechReadinessJson, policy, now, out var readinessReason))
        {
            return AdvanceResultDto.Rejected(
                "tech-readiness-required",
                readinessReason);
        }

        // Free-nav modes (Paper / Learning / Diagnostic): direct apply, no token.
        if (!mode.OneWayLocks)
        {
            nav = nav with { State = cmd.ToState };
            await PersistNavigationAsync(attempt, nav, policy, mode, now, ct);
            return AdvanceResultDto.Applied(ToDto(attempt, nav, policy, mode));
        }

        // Strict modes: validate transition is the next-step on the linear path.
        var expectedNext = ListeningFsmTransitions.Next(nav.State);
        if (!string.Equals(cmd.ToState, expectedNext, StringComparison.Ordinal))
        {
            return AdvanceResultDto.Rejected(
                "invalid-transition",
                $"Cannot advance from {nav.State} to {cmd.ToState}; expected {expectedNext ?? "(end)"}.");
        }

        // R06.10 — two-step confirm-token protocol.
        if (mode.ConfirmDialogRequired)
        {
            if (string.IsNullOrWhiteSpace(cmd.ConfirmToken))
            {
                var token = _tokens.Issue(attempt.Id, nav.State, cmd.ToState, policy.ConfirmTokenTtlMs, now);
                return AdvanceResultDto.ConfirmRequired(token, policy.ConfirmTokenTtlMs);
            }

            var v = _tokens.Validate(cmd.ConfirmToken!, attempt.Id, nav.State, cmd.ToState, now);
            if (!v.IsValid)
                return AdvanceResultDto.Rejected("confirm-token-invalid", v.Reason ?? "invalid token");
        }

        // Lock the from-state so we can never go back (R06.1).
        var newLocks = nav.Locks.Concat(new[] { nav.State }).Distinct().ToArray();
        nav = nav with { State = cmd.ToState, Locks = newLocks };

        await PersistNavigationAsync(attempt, nav, policy, mode, now, ct);
        return AdvanceResultDto.Applied(ToDto(attempt, nav, policy, mode));
    }

    public async Task<TechReadinessDto> RecordTechReadinessAsync(
        string attemptId, string userId, TechReadinessCommand cmd, CancellationToken ct)
    {
        if (cmd.DurationMs < 0)
        {
            throw new ArgumentException("Probe duration must be zero or greater.", nameof(cmd));
        }

        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            throw new InvalidOperationException($"Attempt {attemptId} is {attempt.Status} and cannot accept readiness updates.");
        }

        var policy = await ResolveEffectivePolicyAsync(userId, ct);
        var now = _clock.GetUtcNow();
        var snapshot = new TechReadinessSnapshot(
            AudioOk: cmd.AudioOk,
            DurationMs: cmd.DurationMs,
            CheckedAt: now);

        attempt.TechReadinessJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        attempt.LastActivityAt = now;
        await _db.SaveChangesAsync(ct);

        return new TechReadinessDto(
            AudioOk: snapshot.AudioOk,
            DurationMs: snapshot.DurationMs,
            CheckedAt: snapshot.CheckedAt,
            TtlMs: policy.TechReadinessTtlMs);
    }

    public async Task<AudioResumeDto> AudioResumeAsync(
        string attemptId, string userId, int cuePointMs, CancellationToken ct)
    {
        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        var policy = await ResolveEffectivePolicyAsync(userId, ct);
        var mode = _modes.For(attempt.Mode);
        var nav = ParseNavigationState(attempt.NavigationStateJson)
                  ?? SeedDefaultNavigation(attempt, policy);
        var now = _clock.GetUtcNow();

        // Server already advanced past audio? Force-advance the client.
        var serverState = nav.State;
        if (!ListeningFsmTransitions.IsAudioState(serverState))
        {
            return new AudioResumeDto(
                Resume: false,
                ServerState: serverState,
                ResumeAtMs: 0,
                Reason: "server-past-audio");
        }

        // Inside grace window — resume cleanly.
        var elapsedMs = attempt.WindowStartedAt is { } ws
            ? (int)(now - ws).TotalMilliseconds
            : 0;
        var graceMs = 5000; // 5s tolerance per architect grace-window protocol.
        if (cuePointMs <= elapsedMs + graceMs)
        {
            return new AudioResumeDto(
                Resume: true,
                ServerState: serverState,
                ResumeAtMs: Math.Max(elapsedMs, cuePointMs),
                Reason: "in-window");
        }

        // Outside grace — force-advance to review and lock the audio state.
        var reviewState = ListeningFsmTransitions.Next(serverState);
        if (mode.OneWayLocks && reviewState is not null)
        {
            var locks = nav.Locks.Concat(new[] { serverState }).Distinct().ToArray();
            nav = nav with { State = reviewState, Locks = locks };
            await PersistNavigationAsync(attempt, nav, policy, mode, now, ct);
        }

        return new AudioResumeDto(
            Resume: false,
            ServerState: nav.State,
            ResumeAtMs: 0,
            Reason: "out-of-window");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task<ListeningAttempt> LoadOwnedAttemptAsync(string attemptId, string userId, CancellationToken ct)
    {
        var a = await _db.ListeningAttempts
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Attempt {attemptId} not found.");
        return a;
    }

    private async Task<EffectiveListeningPolicy> ResolveEffectivePolicyAsync(string userId, CancellationToken ct)
    {
        var policy = await _db.ListeningPolicies.FirstOrDefaultAsync(p => p.Id == "global", ct);
        var ovr = await _db.ListeningUserPolicyOverrides.FirstOrDefaultAsync(o => o.UserId == userId, ct);
        return ListeningPolicyResolver.Resolve(policy, ovr);
    }

    private static NavigationState? ParseNavigationState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<NavigationState>(json);
            return state is null || !ListeningFsmTransitions.IsKnownState(state.State) || state.Locks is null
                ? null
                : state;
        }
        catch { return null; }
    }

    private static NavigationState SeedDefaultNavigation(ListeningAttempt attempt, EffectiveListeningPolicy policy)
        => attempt.Status == ListeningAttemptStatus.InProgress
            ? new(State: ListeningFsmTransitions.Intro, Locks: Array.Empty<string>())
            : new(State: ListeningFsmTransitions.Submitted, Locks: ListeningFsmTransitions.ForwardPath.ToArray());

    private static bool RequiresTechReadiness(
        string fromState,
        string toState,
        EffectiveListeningPolicy policy,
        IListeningModePolicy mode)
        => policy.TechReadinessRequired
           && mode.RequiresTechReadiness
           && string.Equals(fromState, ListeningFsmTransitions.Intro, StringComparison.Ordinal)
           && string.Equals(toState, ListeningFsmTransitions.A1Preview, StringComparison.Ordinal);

    private static bool HasValidTechReadiness(
        string? json,
        EffectiveListeningPolicy policy,
        DateTimeOffset now,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            reason = "Audio readiness check is required before starting this Listening attempt.";
            return false;
        }

        TechReadinessSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TechReadinessSnapshot>(json, JsonOptions);
        }
        catch
        {
            reason = "Audio readiness check is invalid and must be repeated.";
            return false;
        }

        if (snapshot is null || !snapshot.AudioOk)
        {
            reason = "Audio readiness check must pass before starting this Listening attempt.";
            return false;
        }

        if (snapshot.CheckedAt.AddMilliseconds(policy.TechReadinessTtlMs) < now)
        {
            reason = "Audio readiness check has expired and must be repeated.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static int ComputeWindowMs(string state, EffectiveListeningPolicy p, IListeningModePolicy mode)
    {
        // Apply extra-time entitlement to all windows.
        int Apply(int ms) => p.ExtraTimePct > 0
            ? (int)Math.Round(ms * (1.0 + p.ExtraTimePct / 100.0))
            : ms;

        return state switch
        {
            ListeningFsmTransitions.A1Preview => Apply(p.PreviewMsA1),
            ListeningFsmTransitions.A2Preview => Apply(p.PreviewMsA2),
            ListeningFsmTransitions.C1Preview => Apply(p.PreviewMsC1),
            ListeningFsmTransitions.C2Preview => Apply(p.PreviewMsC2),
            ListeningFsmTransitions.A1Review => Apply(p.ReviewMsA1),
            ListeningFsmTransitions.A2Review => Apply(p.ReviewMsA2),
            ListeningFsmTransitions.C1Review => Apply(p.ReviewMsC1),
            ListeningFsmTransitions.C2Review => Apply(p.ReviewMsC2FinalCbt),
            ListeningFsmTransitions.C2FinalReview => Apply(
                mode.FinalReviewAllPartsMs ?? p.ReviewMsC2FinalCbt),
            ListeningFsmTransitions.BIntro => Apply(p.BetweenSectionTransitionMs),
            _ => 0,
        };
    }

    private async Task PersistNavigationAsync(
        ListeningAttempt attempt, NavigationState nav,
        EffectiveListeningPolicy policy, IListeningModePolicy mode,
        DateTimeOffset now, CancellationToken ct)
    {
        attempt.NavigationStateJson = JsonSerializer.Serialize(nav);
        attempt.WindowStartedAt = now;
        attempt.WindowDurationMs = ComputeWindowMs(nav.State, policy, mode);
        attempt.LastActivityAt = now;
        await _db.SaveChangesAsync(ct);
    }

    private static SessionStateDto ToDto(
        ListeningAttempt a, NavigationState nav,
        EffectiveListeningPolicy policy, IListeningModePolicy mode)
    {
        var remainingMs = (a.WindowStartedAt is { } ws && a.WindowDurationMs is { } dur)
            ? Math.Max(0, dur - (int)(DateTimeOffset.UtcNow - ws).TotalMilliseconds)
            : 0;

        return new SessionStateDto(
            AttemptId: a.Id,
            Mode: mode.Mode,
            State: nav.State,
            Locks: nav.Locks,
            WindowDurationMs: a.WindowDurationMs ?? 0,
            WindowRemainingMs: remainingMs,
            ConfirmRequired: mode.ConfirmDialogRequired,
            FreeNavigation: mode.FreeNavigation,
            OneWayLocks: mode.OneWayLocks,
            UnansweredWarningRequired: mode.UnansweredWarningRequired);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────

public sealed record NavigationState(string State, string[] Locks);

public sealed record AdvanceCommand(string ToState, string? ConfirmToken);

public sealed record TechReadinessCommand(bool AudioOk, int DurationMs);

public sealed record TechReadinessSnapshot(bool AudioOk, int DurationMs, DateTimeOffset CheckedAt);

public sealed record TechReadinessDto(bool AudioOk, int DurationMs, DateTimeOffset CheckedAt, int TtlMs);

public sealed record SessionStateDto(
    string AttemptId, string Mode, string State, string[] Locks,
    int WindowDurationMs, int WindowRemainingMs,
    bool ConfirmRequired, bool FreeNavigation, bool OneWayLocks,
    bool UnansweredWarningRequired);

public sealed record AdvanceResultDto(
    string Outcome,                          // "applied" | "confirm-required" | "rejected"
    SessionStateDto? State,
    string? ConfirmToken,
    int? ConfirmTokenTtlMs,
    string? RejectionReason,
    string? RejectionDetail)
{
    public static AdvanceResultDto Applied(SessionStateDto state)
        => new("applied", state, null, null, null, null);
    public static AdvanceResultDto ConfirmRequired(string token, int ttlMs)
        => new("confirm-required", null, token, ttlMs, null, null);
    public static AdvanceResultDto Rejected(string reason, string detail)
        => new("rejected", null, null, null, reason, detail);
}

public sealed record AudioResumeDto(bool Resume, string ServerState, int ResumeAtMs, string Reason);
