using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The hard server-side cap on one AI role-play: the pure time arithmetic, the guards in the live
/// voice control plane (mint, turns, transcript, recorder upload), the Gemini token expiry, the
/// OpenAI hang-up, and the sweeper that finishes a role-play the client never ended. Time is driven
/// by a mutable clock for the live voice service and by explicit "now" arguments for the sweeper.
/// </summary>
public sealed class SpeakingRolePlayDurationCapTests
{
    private const string OpenAiHost = "api.openai.com";
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    // ── Pure arithmetic ──────────────────────────────────────────────

    [Theory]
    [InlineData(0, 300)]
    [InlineData(-5, 300)]
    [InlineData(120, 120)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    [InlineData(100000, 600)]
    public void EffectiveSeconds_UsesTheCardTime_DefaultsToThreeHundred_AndCapsAtTheCeiling(int cardSeconds, int expected)
    {
        Assert.Equal(expected, SpeakingRolePlayLimits.EffectiveSeconds(cardSeconds, new LiveVoiceOptions()));
        // No options at all behaves like the defaults.
        Assert.Equal(expected, SpeakingRolePlayLimits.EffectiveSeconds(cardSeconds, null));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(179, 180)]
    [InlineData(600, 600)]
    [InlineData(1800, 1800)]
    [InlineData(5000, 1800)]
    public void TheCeilingOption_IsClampedBetween180And1800(int configured, int expected)
    {
        var options = new LiveVoiceOptions { MaxRoleplaySeconds = configured };

        Assert.Equal(expected, SpeakingRolePlayLimits.CeilingSeconds(options));
        Assert.Equal(expected, SpeakingRolePlayLimits.EffectiveSeconds(100000, options));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(30, 30)]
    [InlineData(999, 120)]
    public void TheGraceOption_IsClampedBetweenZeroAnd120(int configured, int expected)
        => Assert.Equal(expected, SpeakingRolePlayLimits.GraceSeconds(new LiveVoiceOptions { HardStopGraceSeconds = configured }));

    [Theory]
    [InlineData(0, 60)]
    [InlineData(900, 900)]
    [InlineData(99999, 3600)]
    public void TheFlushOption_IsClampedBetween60And3600(int configured, int expected)
        => Assert.Equal(expected, SpeakingRolePlayLimits.FlushSeconds(new LiveVoiceOptions { TranscriptFlushGraceSeconds = configured }));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(99, 10)]
    public void TheSessionCountOption_IsClampedBetweenOneAndTen(int configured, int expected)
        => Assert.Equal(expected, SpeakingRolePlayLimits.MaxProviderSessions(new LiveVoiceOptions { MaxProviderSessionsPerRolePlay = configured }));

    [Fact]
    public void Resolve_DerivesTheDeadlineAndHardStopFromTheStartTime()
    {
        var window = SpeakingRolePlayLimits.Resolve(T0, 300, new LiveVoiceOptions());

        Assert.Equal(T0, window.StartedAt);
        Assert.Equal(300, window.EffectiveSeconds);
        Assert.Equal(T0.AddSeconds(300), window.DeadlineAt);
        Assert.Equal(T0.AddSeconds(330), window.HardStopAt);

        var capped = SpeakingRolePlayLimits.Resolve(T0, 100000, new LiveVoiceOptions { HardStopGraceSeconds = 45 });
        Assert.Equal(T0.AddSeconds(600), capped.DeadlineAt);
        Assert.Equal(T0.AddSeconds(645), capped.HardStopAt);
    }

    [Fact]
    public void Resolve_FallsBackToUpdatedAt_ForAnActiveSessionThatNeverRecordedItsStart()
    {
        var session = new SpeakingSession { RolePlayStartedAt = null, UpdatedAt = T0.AddMinutes(-10) };

        var window = SpeakingRolePlayLimits.Resolve(session, 300, new LiveVoiceOptions());

        Assert.Equal(T0.AddMinutes(-10), window.StartedAt);
        Assert.Equal(T0.AddMinutes(-10).AddSeconds(330), window.HardStopAt);
    }

    [Fact]
    public void IsWithinWriteWindow_AcceptsActiveUntilTheHardStopPlusFlush_AndFinishedUntilItsEndPlusFlush()
    {
        var options = new LiveVoiceOptions();
        var active = new SpeakingSession { State = SpeakingSessionState.Active, RolePlayStartedAt = T0, UpdatedAt = T0 };
        var finished = new SpeakingSession
        {
            State = SpeakingSessionState.Finished,
            RolePlayStartedAt = T0,
            EndedAt = T0.AddSeconds(300),
            UpdatedAt = T0,
        };

        // 300 s card + 30 s grace + 900 s flush = 1230 s.
        Assert.True(SpeakingRolePlayLimits.IsWithinWriteWindow(active, 300, T0.AddSeconds(1230), options));
        Assert.False(SpeakingRolePlayLimits.IsWithinWriteWindow(active, 300, T0.AddSeconds(1231), options));
        Assert.True(SpeakingRolePlayLimits.IsWithinWriteWindow(finished, 300, T0.AddSeconds(1200), options));
        Assert.False(SpeakingRolePlayLimits.IsWithinWriteWindow(finished, 300, T0.AddSeconds(1201), options));
        foreach (var state in new[] { SpeakingSessionState.Prep, SpeakingSessionState.WarmUp, SpeakingSessionState.Cancelled, SpeakingSessionState.Expired })
        {
            var other = new SpeakingSession { State = state, UpdatedAt = T0 };
            Assert.False(SpeakingRolePlayLimits.IsWithinWriteWindow(other, 300, T0, options));
        }
    }

    // ── Gemini token expiry ──────────────────────────────────────────

    [Fact]
    public void GeminiTokenTimes_AreTheEarlierOfTheHardStopPlusFifteenSecondsAndThirtyMinutes()
    {
        var options = new LiveVoiceOptions();
        var window = SpeakingRolePlayLimits.Resolve(T0, 300, options);

        // Minted at the start: covers the whole role-play plus grace, not now + 900 s.
        var atStart = LiveVoiceService.GeminiTokenTimes(T0, window, options);
        Assert.Equal(T0.AddSeconds(345), atStart.ExpiresAt);
        Assert.Equal(T0.AddSeconds(60), atStart.NewSessionExpiresAt);

        // Minted late: the hard stop, not a fixed lifetime, ends the token.
        var late = LiveVoiceService.GeminiTokenTimes(T0.AddSeconds(200), window, options);
        Assert.Equal(T0.AddSeconds(345), late.ExpiresAt);

        // The configured lifetime can never cut the role-play short (LIVEVOICE__GEMINITOKENLIFETIMESECONDS=90
        // ended every conversation after ~90 s on 25 Sep 2026), and a longer one never outlives the hard stop.
        foreach (var lifetime in new[] { int.MinValue, -1, 0, 10, 60, 90, 120, 900, 1800, int.MaxValue })
        {
            options.GeminiTokenLifetimeSeconds = lifetime;
            Assert.Equal(T0.AddSeconds(345), LiveVoiceService.GeminiTokenTimes(T0, window, options).ExpiresAt);
        }

        // The window to open the socket can never outlast the token.
        options.GeminiTokenLifetimeSeconds = 900;
        var nearTheEnd = LiveVoiceService.GeminiTokenTimes(T0.AddSeconds(320), window, options);
        Assert.Equal(T0.AddSeconds(345), nearTheEnd.ExpiresAt);
        Assert.Equal(T0.AddSeconds(345), nearTheEnd.NewSessionExpiresAt);

        // Thirty minutes from minting is the only other bound: it applies to the longest role-play the
        // server allows (1800 s card + 120 s grace, hard stop 1920 s), minted at its start.
        var longest = new LiveVoiceOptions { MaxRoleplaySeconds = 1800, HardStopGraceSeconds = 120 };
        var longWindow = SpeakingRolePlayLimits.Resolve(T0, 1800, longest);
        Assert.Equal(T0.AddSeconds(1800), LiveVoiceService.GeminiTokenTimes(T0, longWindow, longest).ExpiresAt);
    }

    [Fact]
    public async Task GeminiToken_ALowConfiguredLifetime_CanNeverCutTheRolePlayShort()
    {
        // 25 Sep 2026: a 90 s lifetime ended every conversation after ~90 s.
        var options = LiveVoiceTestKit.DefaultOptions();
        options.GeminiTokenLifetimeSeconds = 90;
        using var rig = LiveVoiceTestKit.Create(options);
        var now = rig.Clock.GetUtcNow();
        var session = await SeedAsync(rig);

        var token = await MintGeminiAsync(rig, session);

        // Started now: hard stop = now + 300 + 30, token = hard stop + 15 = now + 345.
        Assert.Equal(now.AddSeconds(345), token.ExpiresAt);
        using var body = JsonDocument.Parse(rig.Handler.Requests.Single().Body!);
        Assert.Equal(now.AddSeconds(345), ParseTime(body, "expireTime"));
        Assert.Equal(now.AddSeconds(60), ParseTime(body, "newSessionExpireTime"));
    }

    [Fact]
    public async Task GeminiToken_MintedTenSecondsIntoTheRolePlay_ExpiresAtTheHardStopPlusFifteenSeconds()
    {
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var session = await SeedAsync(rig, rolePlayStartedAt: now.AddSeconds(-10));

        var token = await MintGeminiAsync(rig, session);

        // Started 10 s ago: hard stop = start + 300 + 30, token = hard stop + 15 = now + 335.
        Assert.Equal(now.AddSeconds(335), token.ExpiresAt);
        Assert.Equal(now.AddSeconds(320), token.HardStopAt);
        using var body = JsonDocument.Parse(rig.Handler.Requests.Single().Body!);
        Assert.Equal(now.AddSeconds(335), ParseTime(body, "expireTime"));
        Assert.Equal(now.AddSeconds(60), ParseTime(body, "newSessionExpireTime"));
    }

    [Fact]
    public async Task GeminiToken_MintedAtTheStartOfTheRolePlay_StillCoversAllOfIt()
    {
        // Production 25 Sep 2026: a 90 s token cut every conversation off after ~90 s.
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var session = await SeedAsync(rig);

        var token = await MintGeminiAsync(rig, session);

        Assert.True(token.ExpiresAt >= now.AddSeconds(300 + 30));
        Assert.True(token.ExpiresAt < now.AddSeconds(900));
    }

    // ── Mint guards ──────────────────────────────────────────────────

    [Fact]
    public async Task Mint_AtOrAfterTheDeadline_IsRejectedForBothProviders_WithoutAProviderCall()
    {
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var overdue = await SeedAsync(rig, rolePlayStartedAt: now.AddSeconds(-300));
        var justInTime = await SeedAsync(rig, rolePlayStartedAt: now.AddSeconds(-299));

        var openAi = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, overdue));
        var gemini = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, overdue));

        Assert.Equal("live_voice_time_limit_reached", openAi.ErrorCode);
        Assert.Equal(409, openAi.StatusCode);
        Assert.Equal("live_voice_time_limit_reached", gemini.ErrorCode);
        Assert.Equal("The time for this role-play has ended.", gemini.Message);
        Assert.Empty(rig.Handler.Requests);
        // One second short of the deadline still mints.
        Assert.Equal("openai", (await MintOpenAiAsync(rig, justInTime)).Provider);
    }

    [Fact]
    public async Task Mint_GuardsRunBeforeContentPreparation_SoARejectedCallStaysCheap()
    {
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var withTimeLeft = await SeedAsync(rig, needsOwnerInput: true);
        var pastDeadline = await SeedAsync(rig, rolePlayStartedAt: now.AddSeconds(-400), needsOwnerInput: true);

        var notReady = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, withTimeLeft));
        var timeUp = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, pastDeadline));

        Assert.Equal("live_voice_content_not_ready", notReady.ErrorCode);
        Assert.Equal("live_voice_time_limit_reached", timeUp.ErrorCode);
    }

    [Fact]
    public async Task Mint_BeyondTheProviderSessionLimit_IsRejected_WithoutAProviderCall()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        await MintOpenAiAsync(rig, session);
        await MintGeminiAsync(rig, session);
        await MintOpenAiAsync(rig, session);
        var callsBefore = rig.Handler.Requests.Count;

        var openAi = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        var gemini = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, session));

        Assert.Equal("live_voice_session_limit_reached", openAi.ErrorCode);
        Assert.Equal(409, gemini.StatusCode);
        Assert.Equal("live_voice_session_limit_reached", gemini.ErrorCode);
        Assert.Equal(callsBefore, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task TheSessionLimit_CountsRecordedSessionsOnly_SoRefusedCreationsDoNotBurnIt()
    {
        var options = LiveVoiceTestKit.DefaultOptions();
        options.MaxProviderSessionsPerRolePlay = 2;
        using var rig = LiveVoiceTestKit.Create(options);
        var session = await SeedAsync(rig);
        rig.Handler.Route(_ => ScriptedHttpHandler.Reply(
            HttpStatusCode.TooManyRequests,
            LiveVoiceTestKit.OpenAiError("insufficient_quota", "quota")));
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));
        }

        rig.Handler.Route(new HappyProviderRoute().Respond);
        await MintOpenAiAsync(rig, session);
        await MintGeminiAsync(rig, session);
        var third = await Assert.ThrowsAsync<ApiException>(() => MintOpenAiAsync(rig, session));

        Assert.Equal("live_voice_session_limit_reached", third.ErrorCode);
    }

    [Fact]
    public async Task Mint_AfterTheSessionFinished_IsRejectedAsNotActive()
    {
        using var rig = LiveVoiceTestKit.Create();
        var now = rig.Clock.GetUtcNow();
        var finished = await SeedAsync(rig, SpeakingSessionState.Finished, endedAt: now.AddSeconds(-5));

        var ex = await Assert.ThrowsAsync<ApiException>(() => MintGeminiAsync(rig, finished));

        Assert.Equal("live_voice_session_not_active", ex.ErrorCode);
        Assert.Empty(rig.Handler.Requests);
    }

    // ── Write windows ────────────────────────────────────────────────

    [Fact]
    public async Task Writes_AreAcceptedThroughGraceAndTheFlushWindow_AndRejectedAfter()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var token = await MintGeminiAsync(rig, session);
        var now = rig.Clock.GetUtcNow();

        // Past the 300 s deadline but inside the grace: still saved.
        await UpdateSessionAsync(rig, session, s => s.RolePlayStartedAt = now.AddSeconds(-320));
        await SaveTranscriptAsync(rig, session, token.ProviderSessionId);
        // 300 + 30 + 900 s after the start is the last accepted second for an unfinished session.
        await UpdateSessionAsync(rig, session, s => s.RolePlayStartedAt = now.AddSeconds(-1230));
        await SaveTranscriptAsync(rig, session, token.ProviderSessionId);
        await UpdateSessionAsync(rig, session, s => s.RolePlayStartedAt = now.AddSeconds(-1231));

        var lateTranscript = await Assert.ThrowsAsync<ApiException>(() => SaveTranscriptAsync(rig, session, token.ProviderSessionId));
        var lateTurn = await Assert.ThrowsAsync<ApiException>(() => SaveTurnAsync(rig, session, token.ProviderSessionId));

        Assert.Equal("live_voice_transcript_window_closed", lateTranscript.ErrorCode);
        Assert.Equal(409, lateTranscript.StatusCode);
        Assert.Equal("live_voice_transcript_window_closed", lateTurn.ErrorCode);
    }

    [Fact]
    public async Task Writes_AfterTheSessionFinished_AreAcceptedForFifteenMinutes_ThenRejected()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var token = await MintGeminiAsync(rig, session);
        var now = rig.Clock.GetUtcNow();

        await UpdateSessionAsync(rig, session, s =>
        {
            s.State = SpeakingSessionState.Finished;
            s.EndedAt = now.AddSeconds(-900);
        });
        // Card A's late flush while the exam is already in card B's prep is the case this protects.
        await SaveTranscriptAsync(rig, session, token.ProviderSessionId);
        await UpdateSessionAsync(rig, session, s => s.EndedAt = now.AddSeconds(-901));

        var closed = await Assert.ThrowsAsync<ApiException>(() => SaveTranscriptAsync(rig, session, token.ProviderSessionId));

        Assert.Equal("live_voice_transcript_window_closed", closed.ErrorCode);
    }

    [Fact]
    public async Task WriteGuards_RunBeforeContentPreparation()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig, needsOwnerInput: true, rolePlayStartedAt: rig.Clock.GetUtcNow().AddSeconds(-5000));

        var ex = await Assert.ThrowsAsync<ApiException>(() => SaveTranscriptAsync(rig, session, "any-provider-session"));

        Assert.Equal("live_voice_transcript_window_closed", ex.ErrorCode);
    }

    [Fact]
    public async Task Transcript_OnceGradingHasTakenIt_IsFrozen_ButTheFirstLateFlushIsAccepted()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var token = await MintGeminiAsync(rig, session);
        var now = rig.Clock.GetUtcNow();
        await UpdateSessionAsync(rig, session, s =>
        {
            s.State = SpeakingSessionState.Finished;
            s.RolePlayStartedAt = now.AddSeconds(-400);
            s.EndedAt = now.AddSeconds(-60);
        });
        var canonical = new SpeakingCanonicalAssessmentService(
            rig.Db, null!, null!, rig.Clock, NullLogger<SpeakingCanonicalAssessmentService>.Instance);
        await canonical.EnqueueAsync(session.SessionId, CancellationToken.None);
        var operation = await rig.Db.AiOperations.SingleAsync(o => o.ResourceId == session.SessionId);
        operation.State = AiOperationState.Leased;
        await rig.Db.SaveChangesAsync();

        // No transcript yet: the flush IS the first transcript, so a lease on the grader does not block it.
        await SaveTranscriptAsync(rig, session, token.ProviderSessionId, "First and only transcript");
        // A transcript exists and the grader holds it: a later write would silently replace what is graded.
        var frozen = await Assert.ThrowsAsync<ApiException>(
            () => SaveTranscriptAsync(rig, session, token.ProviderSessionId, "Changed after grading started"));
        Assert.Equal("live_voice_transcript_window_closed", frozen.ErrorCode);

        // While the operation only waits for a retry the transcript is not frozen yet.
        operation.State = AiOperationState.RetryScheduled;
        await rig.Db.SaveChangesAsync();
        await SaveTranscriptAsync(rig, session, token.ProviderSessionId, "Still allowed while waiting");

        // A graded session is frozen whatever the operation says.
        var latest = await rig.Db.SpeakingTranscripts.AsNoTracking()
            .SingleAsync(t => t.SpeakingSessionId == session.SessionId && t.IsLatest);
        rig.Db.SpeakingAiAssessments.Add(new SpeakingAiAssessment
        {
            Id = $"spa_{Guid.NewGuid():N}",
            SpeakingSessionId = session.SessionId,
            TranscriptId = latest.Id,
            Provider = "ai_gateway",
            ModelId = "gateway-default",
            EstimatedScaledScore = 360,
            ReadinessBand = "developing",
            GeneratedAt = now,
            IsAdvisory = true,
        });
        await rig.Db.SaveChangesAsync();
        var graded = await Assert.ThrowsAsync<ApiException>(
            () => SaveTranscriptAsync(rig, session, token.ProviderSessionId, "Too late"));
        Assert.Equal("live_voice_transcript_window_closed", graded.ErrorCode);
    }

    [Fact]
    public async Task RecorderUpload_IsRejectedOutsideTheWriteWindow_AndTheDeclaredDurationIsClamped()
    {
        // The recording service reads the real clock, so the seeds are relative to it.
        var now = DateTimeOffset.UtcNow;
        var options = Options.Create(new LiveVoiceOptions());
        using var rig = LiveVoiceTestKit.Create(now: now);
        var tooLate = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddMinutes(-30), endedAt: now.AddSeconds(-901));
        var inWindow = await LiveVoiceTestKit.SeedReadySessionAsync(rig.Db, now, rolePlayStartedAt: now.AddSeconds(-100));
        var recordings = new SpeakingSessionRecordingService(
            rig.Db,
            new StubFileStorage(),
            new SpeakingTranscriptionPipeline(rig.Db, null!, NullLogger<SpeakingTranscriptionPipeline>.Instance),
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingSessionRecordingService>.Instance,
            liveVoiceOptions: options);

        var closed = await Assert.ThrowsAsync<ApiException>(() => recordings.ReceiveAsync(
            tooLate.UserId, tooLate.SessionId, new MemoryStream(new byte[] { 1, 2, 3, 4 }), "audio/webm", 4, 250, CancellationToken.None));
        var stored = await recordings.ReceiveAsync(
            inWindow.UserId, inWindow.SessionId, new MemoryStream(new byte[] { 1, 2, 3, 4 }), "audio/webm", 4, 999999, CancellationToken.None);

        Assert.Equal("live_voice_transcript_window_closed", closed.ErrorCode);
        Assert.True(stored);
        var recording = await rig.Db.SpeakingRecordings.AsNoTracking().SingleAsync(r => r.SpeakingSessionId == inWindow.SessionId);
        // 600 s ceiling + 30 s grace + 900 s flush.
        Assert.Equal(1530, recording.DurationSeconds);
    }

    [Fact]
    public async Task RecorderUpload_ARepeatAfterTheWindowClosed_StillAnswersAlreadyReceived()
    {
        var now = DateTimeOffset.UtcNow;
        using var rig = LiveVoiceTestKit.Create(now: now);
        var landed = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddMinutes(-30), endedAt: now.AddSeconds(-901));
        rig.Db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = SpeakingSessionRecordingService.RecordingIdFor(landed.SessionId),
            SpeakingSessionId = landed.SessionId,
            MediaAssetId = "smed_landed",
            Sha256 = string.Empty,
            CreatedAt = now.AddMinutes(-20),
        });
        await rig.Db.SaveChangesAsync();
        var recordings = new SpeakingSessionRecordingService(
            rig.Db,
            new StubFileStorage(),
            new SpeakingTranscriptionPipeline(rig.Db, null!, NullLogger<SpeakingTranscriptionPipeline>.Instance),
            Options.Create(new SpeakingComplianceOptions()),
            NullLogger<SpeakingSessionRecordingService>.Instance,
            liveVoiceOptions: Options.Create(new LiveVoiceOptions()));

        // The upload landed but its response was lost; the retry arrives after the window closed
        // (EndedAt + 900 s is one second in the past). The client treats only recording_already_received
        // as success, so this must not become live_voice_transcript_window_closed.
        var stored = await recordings.ReceiveAsync(
            landed.UserId, landed.SessionId, new MemoryStream(new byte[] { 1, 2, 3, 4 }), "audio/webm", 4, 250, CancellationToken.None);

        Assert.False(stored);
        Assert.Equal(1, await rig.Db.SpeakingRecordings.CountAsync(r => r.SpeakingSessionId == landed.SessionId));
    }

    // ── Clock and detail ─────────────────────────────────────────────

    [Fact]
    public async Task Clock_UsesTheCeiling_ExposesTheHardStop_AndStaysReadOnly()
    {
        var now = DateTimeOffset.UtcNow;
        using var rig = LiveVoiceTestKit.Create(now: now);
        var running = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, rolePlayStartedAt: now.AddSeconds(-10), rolePlaySeconds: 100000);
        var overdue = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, rolePlayStartedAt: now.AddSeconds(-700), rolePlaySeconds: 100000);
        var finished = await LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddMinutes(-9), endedAt: now.AddMinutes(-2));
        var sessions = new SpeakingSessionService(rig.Db, liveVoiceOptions: Options.Create(new LiveVoiceOptions()));

        var clock = await sessions.GetClockAsync(running.UserId, running.SessionId, CancellationToken.None);
        var detail = await sessions.GetSessionForLearnerAsync(running.UserId, running.SessionId, CancellationToken.None);
        var expired = await sessions.GetClockAsync(overdue.UserId, overdue.SessionId, CancellationToken.None);
        var done = await sessions.GetClockAsync(finished.UserId, finished.SessionId, CancellationToken.None);

        // 100000 s on the card, capped at 600 s.
        var startedAt = now.AddSeconds(-10);
        Assert.Equal(startedAt.AddSeconds(600), clock.StageEndsAt);
        Assert.Equal(startedAt.AddSeconds(630), clock.HardStopAt);
        // Started 10 s ago against a 600 s cap. The wide range only absorbs slow test start-up;
        // an uncapped 100000 s card would report tens of thousands.
        Assert.InRange(clock.SecondsRemaining!.Value, 400, 590);
        Assert.False(clock.Expired);
        Assert.Equal(startedAt.AddSeconds(600), detail.RolePlayEndsAt);
        Assert.True(expired.Expired);
        Assert.Equal(0, expired.SecondsRemaining);
        // Reading the clock never finishes the session (the sweeper does).
        Assert.Equal(
            SpeakingSessionState.Active,
            (await rig.Db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == overdue.SessionId)).State);
        Assert.Null(done.HardStopAt);
    }

    // ── OpenAI hang-up ───────────────────────────────────────────────

    [Fact]
    public async Task HangUp_EndsEveryOpenAiSessionInOrder_ToleratesA404_AndIgnoresGemini()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var first = await MintOpenAiAsync(rig, session);
        await MintGeminiAsync(rig, session);
        var second = await MintOpenAiAsync(rig, session);
        var callsBefore = rig.Handler.Requests.Count;
        rig.Handler.Route(request => request.Uri.AbsolutePath.Contains(first.ProviderSessionId, StringComparison.Ordinal)
            ? ScriptedHttpHandler.Reply(
                HttpStatusCode.NotFound,
                LiveVoiceTestKit.OpenAiError("session_id_not_found", "already ended"))
            : ScriptedHttpHandler.Reply(HttpStatusCode.OK, "{}"));

        var ended = await rig.Service.CloseProviderSessionsAsync(session.SessionId, CancellationToken.None);

        // A 404 means the session is already gone, which is the goal.
        Assert.Equal(2, ended);
        var hangUps = rig.Handler.Requests.Skip(callsBefore).ToArray();
        Assert.Equal(
            new[]
            {
                $"https://{OpenAiHost}/v1/live/sessions/{first.ProviderSessionId}/hangup",
                $"https://{OpenAiHost}/v1/live/sessions/{second.ProviderSessionId}/hangup",
            },
            hangUps.Select(r => r.Uri.ToString()).ToArray());
        Assert.All(hangUps, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal($"Bearer {LiveVoiceTestKit.OpenAiKey}", r.Authorization);
            Assert.Null(r.GoogApiKey);
        });
    }

    [Fact]
    public async Task HangUp_NeverThrows_AndNeverLogsTheKeyOrTheProviderText()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        await MintOpenAiAsync(rig, session);
        await MintOpenAiAsync(rig, session);

        rig.Handler.Route(_ => ScriptedHttpHandler.Reply(
            HttpStatusCode.InternalServerError,
            LiveVoiceTestKit.OpenAiError("server_error", "SECRET-PROVIDER-TEXT")));
        var afterServerError = await rig.Service.CloseProviderSessionsAsync(session.SessionId, CancellationToken.None);

        rig.Handler.Route(_ => throw new HttpRequestException($"connection reset {LiveVoiceTestKit.OpenAiKey}"));
        var afterTransportFault = await rig.Service.CloseProviderSessionsAsync(session.SessionId, CancellationToken.None);

        rig.Handler.Route(_ => throw new TaskCanceledException("timed out", new TimeoutException()));
        var afterTimeout = await rig.Service.CloseProviderSessionsAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(0, afterServerError);
        Assert.Equal(0, afterTransportFault);
        Assert.Equal(0, afterTimeout);
        var logs = rig.Log.AllText;
        Assert.Contains("hang-up", logs, StringComparison.Ordinal);
        // A hang-up that did not end the session is a Warning carrying only its status (or error type).
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning
            && e.Text.Contains("hang-up returned HTTP 500", StringComparison.Ordinal)
            && e.Text.Contains(session.SessionId, StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning
            && e.Text.Contains("HttpRequestException", StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET-PROVIDER-TEXT", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("connection reset", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HangUp_LogsTheHttpStatusOfEveryCall_SoAWrongEndpointCannotHideBehindA404()
    {
        using var rig = LiveVoiceTestKit.Create();
        var session = await SeedAsync(rig);
        var first = await MintOpenAiAsync(rig, session);
        var second = await MintOpenAiAsync(rig, session);
        rig.Handler.Route(request => request.Uri.AbsolutePath.Contains(first.ProviderSessionId, StringComparison.Ordinal)
            ? ScriptedHttpHandler.Reply(
                HttpStatusCode.NotFound,
                LiveVoiceTestKit.OpenAiError("session_id_not_found", "SECRET-PROVIDER-TEXT"))
            : ScriptedHttpHandler.Reply(HttpStatusCode.OK, "{}"));

        var ended = await rig.Service.CloseProviderSessionsAsync(session.SessionId, CancellationToken.None);

        // Both count as ended (a 404 means already gone), and BOTH leave a status line at Information.
        Assert.Equal(2, ended);
        var hangUpLogs = rig.Log.Entries.Where(e => e.Text.Contains("hang-up", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, hangUpLogs.Length);
        Assert.All(hangUpLogs, entry => Assert.Equal(LogLevel.Information, entry.Level));
        Assert.Single(hangUpLogs, entry => entry.Text.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.Single(hangUpLogs, entry => entry.Text.Contains("HTTP 200", StringComparison.Ordinal));
        // The Speaking session id only: never the key, the provider text or OpenAI's own session id.
        Assert.All(hangUpLogs, entry => Assert.Contains(session.SessionId, entry.Text, StringComparison.Ordinal));
        var logs = rig.Log.AllText;
        Assert.DoesNotContain("SECRET-PROVIDER-TEXT", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveVoiceTestKit.OpenAiKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(first.ProviderSessionId, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(second.ProviderSessionId, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HangUp_IsANoOp_WhenOpenAiIsNotConfigured_OrTheSessionHasNoOpenAiSession()
    {
        var geminiOnly = new LiveVoiceOptions { GeminiApiKey = LiveVoiceTestKit.GeminiKey };
        using var noOpenAi = LiveVoiceTestKit.Create(geminiOnly);
        var first = await SeedAsync(noOpenAi);
        Assert.Equal(0, await noOpenAi.Service.CloseProviderSessionsAsync(first.SessionId, CancellationToken.None));
        Assert.Empty(noOpenAi.Handler.Requests);

        using var rig = LiveVoiceTestKit.Create();
        var geminiOnlySession = await SeedAsync(rig);
        await MintGeminiAsync(rig, geminiOnlySession);
        var callsBefore = rig.Handler.Requests.Count;
        Assert.Equal(0, await rig.Service.CloseProviderSessionsAsync(geminiOnlySession.SessionId, CancellationToken.None));
        Assert.Equal(callsBefore, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task TheRetentionSweep_WipesTheRawOpenAiSessionId_WithTheRestOfTheRow()
    {
        var now = DateTimeOffset.UtcNow;
        await using var provider = BuildSweeperServices(closer: null);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.SpeakingPatientTurns.Add(new SpeakingPatientTurn
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = "sps_retention",
                ClientTurnId = "live-voice-session:retention",
                SequenceNumber = 1,
                Role = LiveVoiceService.LiveVoiceSessionRole,
                Text = "openai:gpt-live-1",
                ResponseJson = JsonSerializer.Serialize(new
                {
                    provider = "openai",
                    providerSessionIdHash = "abc",
                    providerSessionId = "sess_raw_should_be_wiped",
                }),
                CreatedAt = now.AddDays(-40),
            });
            await db.SaveChangesAsync();
        }
        var retention = new SpeakingAudioRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SpeakingAudioRetentionWorker>.Instance);

        var swept = await retention.SweepLiveVoiceTurnsOnceAsync(CancellationToken.None);

        Assert.Equal(1, swept);
        await using var verify = provider.CreateAsyncScope();
        var row = await verify.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .SpeakingPatientTurns.AsNoTracking().SingleAsync(t => t.SessionId == "sps_retention");
        Assert.DoesNotContain("sess_raw_should_be_wiped", row.ResponseJson, StringComparison.Ordinal);
        Assert.Equal(string.Empty, row.Text);
    }

    // ── The sweeper ──────────────────────────────────────────────────

    [Fact]
    public async Task Sweep_FinishesAnOverdueRolePlay_AnchorsTheEndAtTheDeadline_AndDoesNotGradeAbandonedPractice()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddSeconds(-400);
        var seeded = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: start);

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(1, finished);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var session = await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == seeded.SessionId);
            Assert.Equal(SpeakingSessionState.Finished, session.State);
            // Anchored to the deadline (start + 300 s), not to when the sweep happened to run.
            Assert.Equal(start.AddSeconds(300), session.EndedAt);
            Assert.Equal(300, session.ElapsedSeconds);
            Assert.Equal(now, session.UpdatedAt);

            var attempt = await db.Attempts.AsNoTracking().SingleAsync(a => a.Id == seeded.AttemptId);
            Assert.Equal(AttemptState.Submitted, attempt.State);
            Assert.Equal(start.AddSeconds(300), attempt.SubmittedAt);
            Assert.Equal(300, attempt.ElapsedSeconds);

            // Abandoned practice is not graded: no credit hold committed, no free-sample use consumed.
            Assert.Empty(await db.AiOperations.AsNoTracking().ToListAsync());

            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(e => e.Action == "SpeakingRolePlayHardStopped");
            Assert.Equal(seeded.SessionId, audit.ResourceId);
            Assert.Equal("SpeakingSession", audit.ResourceType);
            Assert.Equal("system", audit.ActorId);
        }
        Assert.Equal(new[] { seeded.SessionId }, closer.Closed);

        // Idempotent: a finished session is never a candidate again.
        Assert.Equal(0, await worker.SweepOverdueRolePlaysAsync(now.AddMinutes(1), CancellationToken.None));
        await using var again = provider.CreateAsyncScope();
        var againDb = again.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await againDb.AiOperations.CountAsync());
        Assert.Equal(1, await againDb.AuditEvents.CountAsync(e => e.Action == "SpeakingRolePlayHardStopped"));
        Assert.Single(closer.Closed);
    }

    [Fact]
    public async Task Sweep_LeavesEveryoneElseAlone()
    {
        await using var provider = BuildSweeperServices(new RecordingSessionCloser());
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var fresh = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-100));
        var inGrace = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-320));
        var tutor = await SeedSweepSessionAsync(
            provider, now, rolePlayStartedAt: now.AddSeconds(-900), mode: SpeakingSessionMode.LiveTutor);
        var inPrep = await SeedSweepSessionAsync(provider, now, SpeakingSessionState.Prep);
        // Abandoned long before the sweep existed: grading such a backlog at once would flood the grader.
        var ancient = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddDays(-3));
        var overdue = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(1, finished);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        async Task<SpeakingSessionState> StateOf(SeededLiveVoiceSession s)
            => (await db.SpeakingSessions.AsNoTracking().SingleAsync(x => x.Id == s.SessionId)).State;
        Assert.Equal(SpeakingSessionState.Active, await StateOf(fresh));
        Assert.Equal(SpeakingSessionState.Active, await StateOf(inGrace));
        Assert.Equal(SpeakingSessionState.Active, await StateOf(tutor));
        Assert.Equal(SpeakingSessionState.Prep, await StateOf(inPrep));
        Assert.Equal(SpeakingSessionState.Active, await StateOf(ancient));
        Assert.Equal(SpeakingSessionState.Finished, await StateOf(overdue));
        Assert.Equal(0, await db.AiOperations.CountAsync());
    }

    [Fact]
    public async Task Sweep_FinishesAnActiveSessionWithNoStartTime_ThroughItsUpdatedAt()
    {
        await using var provider = BuildSweeperServices(new RecordingSessionCloser());
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var anomaly = await SeedSweepSessionAsync(
            provider, now, clearRolePlayStart: true, updatedAt: now.AddMinutes(-10));

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(1, finished);
        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == anomaly.SessionId);
        Assert.Equal(SpeakingSessionState.Finished, session.State);
        Assert.Equal(now.AddMinutes(-10).AddSeconds(300), session.EndedAt);
    }

    [Fact]
    public async Task Sweep_CapsACardWithAnAbsurdTime_AtTheCeiling()
    {
        await using var provider = BuildSweeperServices(new RecordingSessionCloser());
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddSeconds(-700);
        var absurd = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: start, rolePlaySeconds: 100000);
        var stillRunning = await SeedSweepSessionAsync(
            provider, now, rolePlayStartedAt: now.AddSeconds(-500), rolePlaySeconds: 100000);

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(1, finished);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var capped = await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == absurd.SessionId);
        Assert.Equal(SpeakingSessionState.Finished, capped.State);
        Assert.Equal(start.AddSeconds(600), capped.EndedAt);
        Assert.Equal(600, capped.ElapsedSeconds);
        Assert.Equal(
            SpeakingSessionState.Active,
            (await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == stillRunning.SessionId)).State);
    }

    [Fact]
    public async Task Sweep_DoesNotClobberOrReAuditASessionTheLearnerAlreadyEnded()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var learnerEnded = now.AddSeconds(-200);
        var ended = await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddSeconds(-500), endedAt: learnerEnded);

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);
        bool won;
        await using (var scope = provider.CreateAsyncScope())
        {
            won = await scope.ServiceProvider.GetRequiredService<SpeakingSessionService>()
                .FinalizeAtHardStopAsync(ended.SessionId, now, CancellationToken.None);
        }

        Assert.Equal(0, finished);
        Assert.False(won);
        await using var verify = provider.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(learnerEnded, (await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == ended.SessionId)).EndedAt);
        Assert.Empty(await db.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AiOperations.AsNoTracking().ToListAsync());
        Assert.Empty(closer.Closed);
    }

    [Fact]
    public async Task Sweep_AFailedHangUp_NeverStopsTheRestOfTheBatch()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var first = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));
        var second = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-401));
        closer.Fault = id => id == first.SessionId || id == second.SessionId
            ? new InvalidOperationException("the provider is down")
            : null;

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        // The state change already happened; only the hang-up failed.
        Assert.Equal(2, finished);
        Assert.Equal(2, closer.Closed.Count);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(2, await db.SpeakingSessions.CountAsync(s => s.State == SpeakingSessionState.Finished));
        Assert.Equal(0, await db.AiOperations.CountAsync());
    }

    [Fact]
    public async Task SweepOnce_RunsTheRolePlayPass_EvenWithoutAnyExam()
    {
        await using var provider = BuildSweeperServices(new RecordingSessionCloser());
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var overdue = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));

        var examsChanged = await worker.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(0, examsChanged);
        await using var scope = provider.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == overdue.SessionId);
        Assert.Equal(SpeakingSessionState.Finished, session.State);
    }

    [Fact]
    public async Task Sweep_IsABackstopForAnExamChildTheExamClockDidNotEnd()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var child = await SeedSweepSessionAsync(
            provider,
            now,
            rolePlayStartedAt: now.AddSeconds(-400),
            mode: SpeakingSessionMode.AiExam,
            examSessionId: "exam-that-never-advanced");

        var finished = await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(1, finished);
        Assert.Equal(new[] { child.SessionId }, closer.Closed);
        // An exam card is graded (unlike abandoned standalone practice): its result is part of the exam.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var operation = await db.AiOperations.AsNoTracking().SingleAsync(o => o.ResourceId == child.SessionId);
        Assert.Equal("speaking_session", operation.ResourceType);
        Assert.Equal(AiFeatureCodes.SpeakingGrade, operation.FeatureCode);
    }

    [Fact]
    public async Task SweepOnce_AFailingExamPass_NeverSkipsTheHardStopPassOrTheHangUpPass()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        // An unfinished exam makes the exam pass resolve SpeakingExamService, which this provider does
        // not register, so the exam pass throws before it advances anything.
        await SeedUnfinishedExamAsync(provider, now);
        var overdue = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));
        var endedEarly = await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddSeconds(-500), endedAt: now.AddSeconds(-440));

        var examsChanged = await worker.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(0, examsChanged);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(
            SpeakingSessionState.Finished,
            (await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == overdue.SessionId)).State);
        // Hung up exactly once each: the finalised one by the hard-stop pass, the other by the hang-up pass.
        Assert.Equal(
            new[] { overdue.SessionId, endedEarly.SessionId }.OrderBy(id => id, StringComparer.Ordinal),
            closer.Closed.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sweep_AFailureAfterTheSwap_StillGetsItsProviderSessionsHungUp_ByTheHangUpPass()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer, failGradingHandOff: true);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        // An exam card is the only kind the hard stop hands to grading, and the grading queue is down.
        var child = await SeedSweepSessionAsync(
            provider,
            now,
            rolePlayStartedAt: now.AddSeconds(-400),
            mode: SpeakingSessionMode.AiExam,
            examSessionId: "exam-with-a-down-grading-queue");

        var examsChanged = await worker.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(0, examsChanged);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        // The swap and the audit row committed before the hand-off threw, so the first pass never
        // sees the session again; the hang-up pass does, because it is Finished and past its hard stop.
        Assert.Equal(
            SpeakingSessionState.Finished,
            (await db.SpeakingSessions.AsNoTracking().SingleAsync(s => s.Id == child.SessionId)).State);
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "SpeakingRolePlayHardStopped"));
        Assert.Equal(new[] { child.SessionId }, closer.Closed);
    }

    [Fact]
    public async Task HangUpEnded_ClosesAFinishedRolePlayOnce_WhoeverEndedIt_AndOnlyPastItsHardStop()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        // Ended by the exam clock at its deadline (no hang-up of its own), now 70 s past the hard stop.
        var endedByTheExamClock = await SeedSweepSessionAsync(
            provider,
            now,
            SpeakingSessionState.Finished,
            rolePlayStartedAt: now.AddSeconds(-400),
            endedAt: now.AddSeconds(-100),
            mode: SpeakingSessionMode.AiExam,
            examSessionId: "exam-ended-by-its-clock");
        // Ended by the learner's /end early: a browser that kept its OpenAI connection open still bills.
        var endedEarly = await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddSeconds(-500), endedAt: now.AddSeconds(-440));
        // Not due yet: inside the grace that lets the client stop and save its own transcript.
        var inGrace = await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddSeconds(-300), endedAt: now.AddSeconds(-240));
        // Still Active: the finalizer's business, not this pass's.
        await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));
        // Long past the horizon: a restart must not re-hang-up yesterday.
        await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddHours(-2), endedAt: now.AddHours(-2).AddSeconds(60));

        var hungUp = await worker.HangUpEndedRolePlaysAsync(now, CancellationToken.None);

        Assert.Equal(2, hungUp);
        Assert.Equal(
            new[] { endedByTheExamClock.SessionId, endedEarly.SessionId }.OrderBy(id => id, StringComparer.Ordinal),
            closer.Closed.OrderBy(id => id, StringComparer.Ordinal));
        // Attempted once per process; the one inside its grace only becomes due 30 s after `now`.
        Assert.Equal(0, await worker.HangUpEndedRolePlaysAsync(now.AddSeconds(5), CancellationToken.None));
        Assert.Equal(2, closer.Closed.Count);
        Assert.Equal(1, await worker.HangUpEndedRolePlaysAsync(now.AddSeconds(31), CancellationToken.None));
        Assert.Contains(inGrace.SessionId, closer.Closed);
    }

    [Fact]
    public async Task HangUpEnded_DoesNotRepeatTheHangUpTheSweepJustMade_AndSurvivesAFailingCloser()
    {
        var closer = new RecordingSessionCloser();
        await using var provider = BuildSweeperServices(closer);
        var worker = NewWorker(provider);
        var now = DateTimeOffset.UtcNow;
        var overdue = await SeedSweepSessionAsync(provider, now, rolePlayStartedAt: now.AddSeconds(-400));

        Assert.Equal(1, await worker.SweepOverdueRolePlaysAsync(now, CancellationToken.None));
        Assert.Equal(0, await worker.HangUpEndedRolePlaysAsync(now, CancellationToken.None));
        Assert.Equal(new[] { overdue.SessionId }, closer.Closed);

        // A closer that throws is logged, never propagated: the sweep loop must keep running.
        var later = await SeedSweepSessionAsync(
            provider, now, SpeakingSessionState.Finished, rolePlayStartedAt: now.AddSeconds(-400), endedAt: now.AddSeconds(-100));
        closer.Fault = id => id == later.SessionId ? new InvalidOperationException("the provider is down") : null;
        Assert.Equal(1, await worker.HangUpEndedRolePlaysAsync(now, CancellationToken.None));
        Assert.Equal(2, closer.Closed.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Task<SeededLiveVoiceSession> SeedAsync(
        LiveVoiceRig rig,
        SpeakingSessionState state = SpeakingSessionState.Active,
        DateTimeOffset? rolePlayStartedAt = null,
        DateTimeOffset? endedAt = null,
        bool needsOwnerInput = false)
        => LiveVoiceTestKit.SeedReadySessionAsync(
            rig.Db,
            rig.Clock.GetUtcNow(),
            state,
            rolePlayStartedAt: rolePlayStartedAt,
            endedAt: endedAt,
            needsOwnerInput: needsOwnerInput);

    private static Task<LiveVoiceOpenAiOfferResponse> MintOpenAiAsync(LiveVoiceRig rig, SeededLiveVoiceSession session)
        => rig.Service.CreateOpenAiOfferAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceOpenAiOfferRequest(LiveVoiceTestKit.OfferSdp),
            CancellationToken.None);

    private static Task<LiveVoiceGeminiTokenResponse> MintGeminiAsync(LiveVoiceRig rig, SeededLiveVoiceSession session)
        => rig.Service.CreateGeminiTokenAsync(session.UserId, session.SessionId, CancellationToken.None);

    private static Task<LiveVoiceTranscriptResponse> SaveTranscriptAsync(
        LiveVoiceRig rig,
        SeededLiveVoiceSession session,
        string providerSessionId,
        string text = "Hello doctor")
        => rig.Service.PersistTranscriptAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTranscriptRequest(
                "gemini",
                providerSessionId,
                new[] { new LiveVoiceTranscriptSegment("candidate", 0, 500, text) }),
            CancellationToken.None);

    private static Task<LiveVoiceTurnResponse> SaveTurnAsync(
        LiveVoiceRig rig,
        SeededLiveVoiceSession session,
        string providerSessionId)
        => rig.Service.PersistTurnAsync(
            session.UserId,
            session.SessionId,
            new LiveVoiceTurnRequest("gemini", providerSessionId, "Hello doctor", "Hello", $"turn-{Guid.NewGuid():N}", 1),
            CancellationToken.None);

    private static async Task UpdateSessionAsync(LiveVoiceRig rig, SeededLiveVoiceSession session, Action<SpeakingSession> mutate)
    {
        var tracked = await rig.Db.SpeakingSessions.SingleAsync(s => s.Id == session.SessionId);
        mutate(tracked);
        await rig.Db.SaveChangesAsync();
    }

    private static DateTimeOffset ParseTime(JsonDocument document, string property)
        => DateTimeOffset.Parse(
            document.RootElement.GetProperty(property).GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static ServiceProvider BuildSweeperServices(
        RecordingSessionCloser? closer,
        LiveVoiceOptions? options = null,
        bool failGradingHandOff = false)
    {
        var databaseName = $"hard-stop-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IOptions<LiveVoiceOptions>>(Options.Create(options ?? new LiveVoiceOptions()));
        services.AddScoped<ISpeakingCanonicalAssessmentService>(sp =>
        {
            if (failGradingHandOff)
            {
                return new GradingQueueDown();
            }

            return new SpeakingCanonicalAssessmentService(
                sp.GetRequiredService<LearnerDbContext>(),
                null!,
                null!,
                TimeProvider.System,
                NullLogger<SpeakingCanonicalAssessmentService>.Instance);
        });
        services.AddScoped(sp => new SpeakingSessionService(
            sp.GetRequiredService<LearnerDbContext>(),
            canonical: sp.GetRequiredService<ISpeakingCanonicalAssessmentService>(),
            liveVoiceOptions: sp.GetRequiredService<IOptions<LiveVoiceOptions>>()));
        if (closer is not null)
        {
            services.AddSingleton<ILiveVoiceProviderSessionCloser>(closer);
        }
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SpeakingExamAutoAdvanceWorker NewWorker(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SpeakingExamAutoAdvanceWorker>.Instance);

    /// <summary>A non-terminal exam: the exam pass has to resolve <c>SpeakingExamService</c> for it.</summary>
    private static async Task SeedUnfinishedExamAsync(ServiceProvider provider, DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.SpeakingExamSessions.Add(new SpeakingExamSession
        {
            Id = $"exam_{Guid.NewGuid():N}",
            UserId = "exam-owner",
            CardAId = "exam-card-a",
            CardBId = "exam-card-b",
            State = SpeakingExamState.PrepA,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The grading hand-off is down: the hard stop only ever reaches <c>EnqueueAsync</c>.</summary>
    private sealed class GradingQueueDown : ISpeakingCanonicalAssessmentService
    {
        public string ComputeIdentityHash(
            string sessionId,
            string cardId,
            string transcriptHash,
            string rubricVersion,
            string promptVersion)
            => throw new NotSupportedException();

        public Task<SpeakingFinalizationTicket> EnqueueAsync(string sessionId, CancellationToken ct)
            => throw new InvalidOperationException("the grading queue is down");

        public Task ExecuteQueuedAsync(string operationId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task AssessNowAsync(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SpeakingFinalizationTicket> EnqueueExamCombinedAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task RetryExamCombinedAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<string> GetExamCombinedStateAsync(string examId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> UsesV11Async(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SpeakingAssessmentState> GetStateAsync(string sessionId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static async Task<SeededLiveVoiceSession> SeedSweepSessionAsync(
        ServiceProvider provider,
        DateTimeOffset now,
        SpeakingSessionState state = SpeakingSessionState.Active,
        DateTimeOffset? rolePlayStartedAt = null,
        bool clearRolePlayStart = false,
        DateTimeOffset? endedAt = null,
        DateTimeOffset? updatedAt = null,
        int rolePlaySeconds = 300,
        SpeakingSessionMode mode = SpeakingSessionMode.AiSelfPractice,
        string? examSessionId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        return await LiveVoiceTestKit.SeedReadySessionAsync(
            scope.ServiceProvider.GetRequiredService<LearnerDbContext>(),
            now,
            state,
            rolePlayStartedAt,
            clearRolePlayStart,
            endedAt,
            updatedAt,
            rolePlaySeconds,
            mode,
            examSessionId);
    }
}
