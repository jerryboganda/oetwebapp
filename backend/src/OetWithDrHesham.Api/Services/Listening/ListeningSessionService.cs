using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Listening;

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

    /// <summary>WS2 — how long a passed pathway sound-check
    /// (<see cref="Domain.LearnerListeningProfile.AudioCheckPassedAt"/>) stays
    /// valid as the gate for strict Listening exams. Sized to comfortably span
    /// a single exam sitting + the lead-up; a learner who passed the check
    /// within this window may start without re-running it. Independent of the
    /// per-attempt <c>TechReadinessTtlMs</c>, which gates a separate device
    /// probe.</summary>
    public const int AudioCheckTtlMs = 24 * 60 * 60 * 1000; // 24 hours

    private readonly LearnerDbContext _db;
    private readonly ListeningModePolicyResolver _modes;
    private readonly ListeningConfirmTokenService _tokens;
    private readonly ListeningSequenceService _sequences;
    private readonly TimeProvider _clock;

    public ListeningSessionService(
        LearnerDbContext db,
        ListeningModePolicyResolver modes,
        ListeningConfirmTokenService tokens,
        ListeningSequenceService sequences,
        TimeProvider clock)
    {
        _db = db;
        _modes = modes;
        _tokens = tokens;
        _sequences = sequences;
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
            attempt.WindowDurationMs = await ComputeWindowMsAsync(attempt.PaperId, nav.State, policy, mode, ct);
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

        // WS2 — strict Listening exams (Exam / OET@Home, OneWayLocks) require a
        // passed sound-check from the Listening flow before the learner can
        // leave intro. Practice / Learning / Paper / Diagnostic modes stay
        // ungated. Mirrors the tech-readiness gate above but reads the
        // learner's LearnerListeningProfile.AudioCheckPassedAt instead of the
        // per-attempt readiness snapshot.
        if (RequiresAudioCheck(nav.State, cmd.ToState, mode)
            && !await HasValidAudioCheckAsync(userId, now, ct))
        {
            return AdvanceResultDto.Rejected(
                "audio-check-required",
                "Pass the Listening sound check before starting this exam. Run the sound check, then return here to begin.");
        }

        // Free-nav modes (Paper / Learning / Diagnostic): direct apply, no token.
        if (!mode.OneWayLocks)
        {
            var previousState = nav.State;
            nav = nav with { State = cmd.ToState };
            _db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = userId,
                ActorName = userId,
                Action = "listening.session.advance",
                ResourceType = "ListeningAttempt",
                ResourceId = attemptId,
                Details = JsonSerializer.Serialize(new { fromState = previousState, toState = cmd.ToState, mode = attempt.Mode, freeNav = true }),
            });
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

            // H18: Consume the confirm token by immediately persisting a state
            // mutation (RowVersion bump). A concurrent replay that passes HMAC
            // validation will hit DbUpdateConcurrencyException on SaveChangesAsync
            // because the RowVersion will have already been incremented by the
            // first consumer.
            attempt.RowVersion++;
        }

        // Lock the from-state so we can never go back (R06.1).
        var newLocks = nav.Locks.Concat(new[] { nav.State }).Distinct().ToArray();
        var fromState = nav.State;
        nav = nav with { State = cmd.ToState, Locks = newLocks };

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "listening.session.advance",
            ResourceType = "ListeningAttempt",
            ResourceId = attemptId,
            Details = JsonSerializer.Serialize(new { fromState, toState = cmd.ToState, mode = attempt.Mode, freeNav = false }),
        });

        try
        {
            await PersistNavigationAsync(attempt, nav, policy, mode, now, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // H18: A concurrent request already consumed this token and advanced
            // the state. Reject the replay attempt cleanly instead of a 500.
            return AdvanceResultDto.Rejected("confirm-token-consumed",
                "This confirm token has already been used. Refresh session state.");
        }
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

        // 2026-05-27 audit fix — enforce Listening rules L-R10.1/R10.2/R10.3
        // BEFORE persisting readiness. Exam and home modes reject Bluetooth
        // audio devices, sub-1920×1080 resolution, and >125% display scale.
        var modePolicy = _modes.For(attempt.Mode);
        // OneWayLocks is true for exam + home + at-home strict modes — the
        // exact set the device gate must enforce. Learning / Practice modes
        // have OneWayLocks=false, which lets them iterate without device gates.
        var enforceDeviceGates = modePolicy.OneWayLocks;
        var outputBluetooth = TechReadinessAudioPolicy.LabelLooksBluetooth(cmd.AudioOutputDeviceLabel);
        var inputBluetooth = TechReadinessAudioPolicy.LabelLooksBluetooth(cmd.AudioInputDeviceLabel);
        var bluetoothDetected = outputBluetooth || inputBluetooth;

        var resolutionOk = cmd.ScreenWidth is null
            || cmd.ScreenHeight is null
            || (cmd.ScreenWidth >= TechReadinessAudioPolicy.MinScreenWidth
                && cmd.ScreenHeight >= TechReadinessAudioPolicy.MinScreenHeight);
        var scaleOk = cmd.DisplayScalePercent is null
            || cmd.DisplayScalePercent <= TechReadinessAudioPolicy.MaxDisplayScalePercent;

        if (enforceDeviceGates)
        {
            if (bluetoothDetected)
            {
                throw new InvalidOperationException(
                    "Listening rule L-R10.3 — wired headset or earphones required. " +
                    $"A Bluetooth/wireless audio device was detected ({cmd.AudioOutputDeviceLabel ?? cmd.AudioInputDeviceLabel}). " +
                    "Disconnect the wireless device and connect a wired one before continuing.");
            }
            if (!resolutionOk)
            {
                throw new InvalidOperationException(
                    "Listening rule L-R10.1 — minimum screen resolution is 1920×1080. " +
                    $"Detected {cmd.ScreenWidth}×{cmd.ScreenHeight}. Adjust your display before continuing.");
            }
            if (!scaleOk)
            {
                throw new InvalidOperationException(
                    "Listening rule L-R10.2 — display scale must be 100% or at most 125%. " +
                    $"Detected {cmd.DisplayScalePercent}%. Reduce your display scale before continuing.");
            }
        }

        var snapshot = new TechReadinessSnapshot(
            AudioOk: cmd.AudioOk,
            DurationMs: cmd.DurationMs,
            CheckedAt: now,
            AudioOutputDeviceLabel: cmd.AudioOutputDeviceLabel,
            AudioInputDeviceLabel: cmd.AudioInputDeviceLabel,
            ScreenWidth: cmd.ScreenWidth,
            ScreenHeight: cmd.ScreenHeight,
            DisplayScalePercent: cmd.DisplayScalePercent);

        attempt.TechReadinessJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        attempt.LastActivityAt = now;

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "listening.session.tech_readiness_recorded",
            ResourceType = "ListeningAttempt",
            ResourceId = attemptId,
            Details = JsonSerializer.Serialize(new
            {
                audioOk = cmd.AudioOk,
                durationMs = cmd.DurationMs,
                outputDevice = cmd.AudioOutputDeviceLabel,
                inputDevice = cmd.AudioInputDeviceLabel,
                bluetoothDetected,
                resolutionOk,
                scaleOk,
            }),
        });

        await _db.SaveChangesAsync(ct);

        return new TechReadinessDto(
            AudioOk: snapshot.AudioOk,
            DurationMs: snapshot.DurationMs,
            CheckedAt: snapshot.CheckedAt,
            TtlMs: policy.TechReadinessTtlMs,
            AudioOutputDeviceLabel: snapshot.AudioOutputDeviceLabel,
            AudioInputDeviceLabel: snapshot.AudioInputDeviceLabel,
            BluetoothAudioDetected: bluetoothDetected,
            ResolutionMeetsMinimum: resolutionOk,
            DisplayScaleAcceptable: scaleOk);
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

    /// <summary>WS2 — the sound-check gate only fires on the very first
    /// strict transition (<c>intro → a1_preview</c>) for OneWayLocks modes
    /// (Exam / OET@Home). Free-nav modes (Paper / Learning / Diagnostic) are
    /// never gated.</summary>
    private static bool RequiresAudioCheck(
        string fromState,
        string toState,
        IListeningModePolicy mode)
        => mode.OneWayLocks
           && string.Equals(fromState, ListeningFsmTransitions.Intro, StringComparison.Ordinal)
           && string.Equals(toState, ListeningFsmTransitions.A1Preview, StringComparison.Ordinal);

    /// <summary>WS2 — true when the learner has a pathway sound-check that
    /// passed within <see cref="AudioCheckTtlMs"/>. Reads the same
    /// <see cref="Domain.LearnerListeningProfile"/> row the Listening flow
    /// writes <c>AudioCheckPassedAt</c> onto. A missing profile (learner who
    /// never onboarded the Listening pathway) fails closed.</summary>
    private async Task<bool> HasValidAudioCheckAsync(
        string userId, DateTimeOffset now, CancellationToken ct)
    {
        var passedAt = await _db.LearnerListeningProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.AudioCheckPassedAt)
            .FirstOrDefaultAsync(ct);

        return passedAt is { } at && at.AddMilliseconds(AudioCheckTtlMs) >= now;
    }

    /// <summary>
    /// Resolve the per-state window in milliseconds.
    ///
    /// WS4: the window is read from the paper's authored exam-sequence when one
    /// exists; otherwise it is read from the canonical sequence the
    /// <see cref="ListeningSequenceService"/> derives from the effective policy.
    /// The derived sequence carries the SAME base durations the legacy switch
    /// produced, so a paper with a null <c>ListeningSequenceJson</c> is
    /// byte-identical to the prior behaviour. The <c>ExtraTimePct</c>
    /// multiplier is applied here exactly as it was before — once, on top of
    /// the base value — so neither the authored nor the derived path
    /// double-counts entitlement.
    /// </summary>
    private async Task<int> ComputeWindowMsAsync(
        string paperId, string state,
        EffectiveListeningPolicy p, IListeningModePolicy mode, CancellationToken ct)
    {
        var sequence = await _sequences.GetAsync(paperId, ct)
                       ?? _sequences.DeriveFromPolicy(p, mode);

        // The matching sequence item's duration is the base (pre extra-time)
        // window. If a hand-edited sequence somehow lacks the state, fall back
        // to the canonical base value so a live attempt can never stall.
        var baseMs = ListeningSequenceService.WindowMsForState(sequence, state)
                     ?? ListeningSequenceService.BaseWindowMs(state, p, mode);

        return ApplyExtraTime(p, baseMs);
    }

    /// <summary>Extra-time entitlement multiplier — byte-identical to the
    /// legacy inline <c>Apply</c> in the old <c>ComputeWindowMs</c>.</summary>
    private static int ApplyExtraTime(EffectiveListeningPolicy p, int ms)
        => p.ExtraTimePct > 0
            ? (int)Math.Round(ms * (1.0 + p.ExtraTimePct / 100.0))
            : ms;

    private async Task PersistNavigationAsync(
        ListeningAttempt attempt, NavigationState nav,
        EffectiveListeningPolicy policy, IListeningModePolicy mode,
        DateTimeOffset now, CancellationToken ct)
    {
        attempt.NavigationStateJson = JsonSerializer.Serialize(nav);
        attempt.WindowStartedAt = now;
        attempt.WindowDurationMs = await ComputeWindowMsAsync(attempt.PaperId, nav.State, policy, mode, ct);
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
            UnansweredWarningRequired: mode.UnansweredWarningRequired,
            AnnotationsJson: a.AnnotationsJson);
    }

    // ─────────────────────────────────────────────────────────────────────
    // R08 — annotation persistence (highlights + strikethroughs).
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Hard cap on the per-attempt annotations payload. Larger
    /// payloads are rejected with <c>listening_annotations_too_large</c>. The
    /// limit is intentionally generous (64 KB) but bounded so a hostile
    /// client cannot stuff arbitrary blobs onto the attempt row.</summary>
    public const int MaxAnnotationsBytes = 64 * 1024;

    public async Task SaveAnnotationsAsync(
        string attemptId, string userId, string? annotationsJson, CancellationToken ct)
    {
        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        if (attempt.Status != ListeningAttemptStatus.InProgress)
        {
            throw ApiException.Conflict(
                "listening_annotations_attempt_not_in_progress",
                "Annotations can only be saved while the attempt is in progress.");
        }

        var normalized = string.IsNullOrWhiteSpace(annotationsJson) ? null : annotationsJson;
        if (normalized is not null)
        {
            // Use UTF-8 byte length so single multibyte chars (e.g. learner
            // typed a non-Latin highlight label) count fairly.
            var bytes = System.Text.Encoding.UTF8.GetByteCount(normalized);
            if (bytes > MaxAnnotationsBytes)
            {
                throw ApiException.Validation(
                    "listening_annotations_too_large",
                    $"Annotations payload is {bytes} bytes; limit is {MaxAnnotationsBytes}.");
            }

            // Cheap shape guard — must parse as JSON. Avoids storing junk that
            // would crash the frontend on hydrate.
            try { using var _ = JsonDocument.Parse(normalized); }
            catch (JsonException)
            {
                throw ApiException.Validation(
                    "listening_annotations_invalid_json",
                    "Annotations payload must be valid JSON.");
            }
        }

        attempt.AnnotationsJson = normalized;
        attempt.LastActivityAt = _clock.GetUtcNow();

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = _clock.GetUtcNow(),
            ActorId = userId,
            ActorName = userId,
            Action = "listening.session.annotations_saved",
            ResourceType = "ListeningAttempt",
            ResourceId = attemptId,
            Details = JsonSerializer.Serialize(new { bytesStored = normalized is not null ? System.Text.Encoding.UTF8.GetByteCount(normalized) : 0 }),
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetAnnotationsAsync(
        string attemptId, string userId, CancellationToken ct)
    {
        var attempt = await LoadOwnedAttemptAsync(attemptId, userId, ct);
        return attempt.AnnotationsJson;
    }
}

// ─────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────

public sealed record NavigationState(string State, string[] Locks);

public sealed record AdvanceCommand(string ToState, string? ConfirmToken);

public sealed record TechReadinessCommand(
    bool AudioOk,
    int DurationMs,
    // 2026-05-27 audit fix — Listening rule L-R10.3 (wired headset required).
    // The client enumerates `navigator.mediaDevices` and reports the label of
    // the active output device. Bluetooth / wireless devices are detected by
    // matching the label against `BluetoothDeviceLabelPattern` and the request
    // is rejected when the attempt is in exam or home mode.
    string? AudioOutputDeviceLabel = null,
    string? AudioInputDeviceLabel = null,
    int? ScreenWidth = null,
    int? ScreenHeight = null,
    int? DisplayScalePercent = null);

public sealed record TechReadinessSnapshot(
    bool AudioOk,
    int DurationMs,
    DateTimeOffset CheckedAt,
    string? AudioOutputDeviceLabel = null,
    string? AudioInputDeviceLabel = null,
    int? ScreenWidth = null,
    int? ScreenHeight = null,
    int? DisplayScalePercent = null);

public sealed record TechReadinessDto(
    bool AudioOk,
    int DurationMs,
    DateTimeOffset CheckedAt,
    int TtlMs,
    string? AudioOutputDeviceLabel = null,
    string? AudioInputDeviceLabel = null,
    bool BluetoothAudioDetected = false,
    bool ResolutionMeetsMinimum = true,
    bool DisplayScaleAcceptable = true);

/// <summary>
/// Configuration for the audio-device gate. Centralised so a single edit
/// updates both `RecordTechReadinessAsync` and the contract tests.
/// </summary>
public static class TechReadinessAudioPolicy
{
    public const int MinScreenWidth = 1920;
    public const int MinScreenHeight = 1080;
    public const int MaxDisplayScalePercent = 125;

    /// <summary>
    /// Case-insensitive regex applied to `audioOutputDeviceLabel` /
    /// `audioInputDeviceLabel`. Matches well-known Bluetooth / wireless
    /// device labels that are forbidden in exam and home mode.
    /// </summary>
    public static readonly System.Text.RegularExpressions.Regex BluetoothDeviceLabelPattern =
        new(@"\b(bluetooth|airpods|beats|wireless|sony wf|sony wh|jabra|bose qc)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool LabelLooksBluetooth(string? label)
        => !string.IsNullOrWhiteSpace(label) && BluetoothDeviceLabelPattern.IsMatch(label!);
}

public sealed record SessionStateDto(
    string AttemptId, string Mode, string State, string[] Locks,
    int WindowDurationMs, int WindowRemainingMs,
    bool ConfirmRequired, bool FreeNavigation, bool OneWayLocks,
    bool UnansweredWarningRequired,
    /// <summary>Listening V2 — R08 highlights + strikethroughs payload last
    /// persisted by the learner. Null when no annotations have been saved.
    /// Frontend hydrates `useListeningAnnotations` from this on mount.</summary>
    string? AnnotationsJson);

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


