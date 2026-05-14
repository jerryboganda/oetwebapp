using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Hubs;

[Authorize(Policy = "LearnerOnly")]
public class ConversationHub(
    IServiceScopeFactory scopeFactory,
    ConversationRealtimeTurnStore realtimeTurns,
    ILogger<ConversationHub> logger) : Hub
{
    public async Task<bool> AcknowledgeAudioConsent(string sessionId, string consentVersion)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var options = await scope.ServiceProvider.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId)
        {
            await Clients.Caller.SendAsync("ConversationError", "SESSION_NOT_FOUND", "Session not found.");
            return false;
        }

        if (!string.Equals(consentVersion, options.RealtimeSttConsentVersion, StringComparison.Ordinal))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_VERSION", "The audio consent policy has changed. Please refresh and accept the latest policy.");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        session.AudioConsentVersion = consentVersion;
        session.RecordingConsentAcceptedAt = now;
        session.VendorConsentAcceptedAt = now;
        await db.SaveChangesAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("AudioConsentAccepted", consentVersion, now);
        return true;
    }

    public async Task StartSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);
        var orchestrator = sp.GetRequiredService<IConversationAiOrchestrator>();
        var ttsSelector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var audio = sp.GetRequiredService<IConversationAudioService>();

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId)
        {
            await Clients.Caller.SendAsync("ConversationError", "SESSION_NOT_FOUND", "Session not found.");
            return;
        }
        if (!options.Enabled)
        {
            await Clients.Caller.SendAsync("ConversationError", "CONVERSATION_DISABLED", "AI Conversation is currently disabled.");
            return;
        }

        if (session.State == "preparing" || session.State == "active")
        {
            session.State = "active";
            session.StartedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(Context.ConnectionAborted);
        }
        else
        {
            await Clients.Caller.SendAsync("ConversationError", "INVALID_STATE", $"Session cannot be started in state '{session.State}'.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        await Clients.Caller.SendAsync("SessionStateChanged", "active");

        var existingTurnCount = await db.ConversationTurns
            .CountAsync(t => t.SessionId == sessionId, Context.ConnectionAborted);
        if (existingTurnCount > 0)
        {
            await Clients.Caller.SendAsync("ConversationResumed", existingTurnCount);
            return;
        }

        try
        {
            var ctx = BuildAiContext(session, turnIndex: 0, elapsed: 0);
            var reply = await orchestrator.GenerateOpeningAsync(ctx, Context.ConnectionAborted);
            session.TurnCount++;
            var turnNumber = session.TurnCount;

            string? audioUrl = null;
            var tts = await ttsSelector.TrySelectAsync(Context.ConnectionAborted);
            if (tts is not null)
            {
                try
                {
                    var ttsResult = await tts.SynthesizeAsync(new ConversationTtsRequest(reply.Text, ResolveVoice(session), "en-GB"), Context.ConnectionAborted);
                    if (ttsResult.Audio.Length > 0)
                    {
                        var aref = await audio.WriteAsync(ttsResult.Audio, ttsResult.MimeType, Context.ConnectionAborted);
                        audioUrl = aref.Url;
                    }
                }
                catch (Exception ex) { logger.LogWarning(ex, "Opening TTS failed"); }
            }

            db.ConversationTurns.Add(new ConversationTurn
            {
                Id = Guid.NewGuid(), SessionId = sessionId, TurnNumber = turnNumber,
                Role = "ai", Content = reply.Text, AudioUrl = audioUrl,
                DurationMs = reply.Text.Split(' ').Length * 300, TimestampMs = 0,
                ConfidenceScore = 1.0, AnalysisJson = "{}",
                AiFeatureCode = AiFeatureCodes.ConversationOpening,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Context.ConnectionAborted);
            session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
            await db.SaveChangesAsync(Context.ConnectionAborted);

            await Clients.Caller.SendAsync("ReceiveAIResponse", turnNumber, reply.Text, new
            {
                audioUrl, emotionHint = reply.EmotionHint, appliedRuleIds = reply.AppliedRuleIds,
            });
        }
        catch (PromptNotGroundedException)
        {
            logger.LogError("Conversation opening refused — ungrounded.");
            await Clients.Caller.SendAsync("ConversationError", "UNGROUNDED", "AI grounding failed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation opening failed");
            var fallback = "Hello — thank you for coming in today. How can I help you?";
            session.TurnCount++;
            db.ConversationTurns.Add(new ConversationTurn
            {
                Id = Guid.NewGuid(), SessionId = sessionId, TurnNumber = session.TurnCount,
                Role = "ai", Content = fallback, DurationMs = 3000, TimestampMs = 0,
                ConfidenceScore = 1.0, AnalysisJson = JsonSupport.Serialize(new { fallback = true }),
                AiFeatureCode = AiFeatureCodes.ConversationOpening,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Context.ConnectionAborted);
            session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
            await db.SaveChangesAsync(Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveAIResponse", session.TurnCount, fallback,
                new { audioUrl = (string?)null, emotionHint = "neutral", appliedRuleIds = Array.Empty<string>() });
        }
    }

    public async Task SendAudio(string sessionId, string audioBase64, string? mimeType = null)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId || session.State != "active")
        {
            await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
            return;
        }

        if (!HasCurrentAudioConsent(session, options))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_REQUIRED", "Please accept the current recording and speech-processing consent before sending audio.");
            return;
        }

        var audioMime = string.IsNullOrWhiteSpace(mimeType) ? "audio/webm" : mimeType;
        if (!options.AllowedMimeTypes.Contains(audioMime, StringComparer.OrdinalIgnoreCase))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_MIME", $"Audio MIME '{audioMime}' not allowed.");
            return;
        }

        byte[] audioBytes;
        try { audioBytes = Convert.FromBase64String(audioBase64); }
        catch (FormatException)
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_DECODE", "Audio payload was not valid base64.");
            return;
        }
        if (audioBytes.Length == 0 || audioBytes.Length > options.MaxAudioBytes)
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_SIZE", $"Audio size {audioBytes.Length} out of bounds.");
            return;
        }

        await ProcessAudioTurnAsync(sp, db, session, sessionId, audioBytes, audioMime, null, null);
    }

    public async Task<string> BeginRealtimeTurn(string sessionId, string streamId, string? mimeType = null, string? locale = null)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return "denied";

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);
        var asrSelector = sp.GetRequiredService<IConversationAsrProviderSelector>();

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId || session.State != "active")
        {
            await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
            return "denied";
        }

        if (!HasCurrentAudioConsent(session, options))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_REQUIRED", "Please accept the current recording and speech-processing consent before starting audio capture.");
            return "denied";
        }

        if (!options.Enabled || !options.RealtimeSttEnabled)
        {
            if (options.RealtimeSttFallbackToBatch)
            {
                await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, "realtime_disabled", "batch");
                return "fallback";
            }

            await Clients.Caller.SendAsync("ConversationError", "REALTIME_DISABLED", "Realtime transcription is disabled and full-turn fallback is not allowed.");
            return "denied";
        }

        var audioMime = string.IsNullOrWhiteSpace(mimeType) ? "audio/webm" : mimeType;
        if (!options.AllowedMimeTypes.Contains(audioMime, StringComparer.OrdinalIgnoreCase))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_MIME", $"Audio MIME '{audioMime}' not allowed.");
            return "denied";
        }

        var realtimeProvider = await asrSelector.TrySelectRealtimeAsync(Context.ConnectionAborted);
        if (realtimeProvider is null)
        {
            if (options.RealtimeSttFallbackToBatch)
            {
                await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, "provider_unavailable", "batch");
                return "fallback";
            }

            await Clients.Caller.SendAsync("ConversationError", "REALTIME_PROVIDER_UNAVAILABLE", "Realtime transcription provider is unavailable and full-turn fallback is not allowed.");
            return "denied";
        }

        if (!realtimeTurns.TryBegin(
                Context.ConnectionId,
                userId,
                sessionId,
                streamId,
                audioMime,
                string.IsNullOrWhiteSpace(locale) ? options.ElevenLabsSttLanguage : locale,
                options.RealtimeSttMaxConcurrentStreamsPerUser,
                TimeSpan.FromSeconds(Math.Max(1, options.RealtimeSttTurnIdleTimeoutSeconds)),
                TimeSpan.FromSeconds(Math.Min(
                    Math.Max(1, options.MaxTurnDurationSeconds),
                    Math.Max(1, options.RealtimeSttMaxAudioSecondsPerSession))),
                out var errorCode))
        {
            await Clients.Caller.SendAsync("ConversationError", errorCode ?? "REALTIME_START", "Could not start realtime transcription.");
            return "denied";
        }

        var maxChunkBytes = Math.Min(options.RealtimeSttMaxChunkBytes, ConversationRealtimeTransportLimits.MaxBinaryChunkBytes);
        await Clients.Caller.SendAsync("RealtimeSttStarted", streamId, realtimeProvider.Name, maxChunkBytes);
        return "realtime";
    }

    public async Task SendRealtimeAudioChunk(string sessionId, string streamId, int sequence, string audioBase64, int? clientOffsetMs = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var options = await scope.ServiceProvider.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);
        var maxTurnAgeSeconds = Math.Min(
            Math.Max(1, options.MaxTurnDurationSeconds),
            Math.Max(1, options.RealtimeSttMaxAudioSecondsPerSession));
        var idleTimeout = TimeSpan.FromSeconds(Math.Max(1, options.RealtimeSttTurnIdleTimeoutSeconds));
        var maxStreamAge = TimeSpan.FromSeconds(maxTurnAgeSeconds);

        if (!realtimeTurns.TryValidateStream(Context.ConnectionId, sessionId, streamId, idleTimeout, maxStreamAge, out var streamErrorCode))
        {
            await Clients.Caller.SendAsync("ConversationError", streamErrorCode ?? "REALTIME_CHUNK", "Audio chunk could not be accepted.");
            return;
        }

        var maxChunkBytes = Math.Min(options.RealtimeSttMaxChunkBytes, ConversationRealtimeTransportLimits.MaxBinaryChunkBytes);
        var maxEncodedChars = ((maxChunkBytes + 2) / 3 * 4) + 8;
        if (string.IsNullOrWhiteSpace(audioBase64) || audioBase64.Length > maxEncodedChars)
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CHUNK_SIZE", "Audio chunk size is out of bounds.");
            return;
        }

        byte[] audioBytes;
        try { audioBytes = Convert.FromBase64String(audioBase64); }
        catch (FormatException)
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_DECODE", "Audio chunk was not valid base64.");
            return;
        }

        if (audioBytes.Length == 0 || audioBytes.Length > maxChunkBytes)
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CHUNK_SIZE", "Audio chunk size is out of bounds.");
            return;
        }

        if (!realtimeTurns.TryAppend(
                Context.ConnectionId,
                sessionId,
                streamId,
                sequence,
                audioBytes,
                options.MaxAudioBytes,
                idleTimeout,
                maxStreamAge,
                TimeSpan.FromMilliseconds(Math.Max(100, options.RealtimeSttPartialMinIntervalMs)),
                out var result,
                out var errorCode))
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            await Clients.Caller.SendAsync("ConversationError", errorCode ?? "REALTIME_CHUNK", "Audio chunk could not be accepted.");
            return;
        }

        if (result?.ShouldEmitPartial == true)
        {
            await Clients.Caller.SendAsync("RealtimeTranscriptPartial", streamId, "Listening...", 0.75, clientOffsetMs, null, result.LastSequence);
        }
    }

    public async Task<object> CompleteRealtimeTurn(string sessionId, string streamId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return new { status = "denied", streamId };

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);
        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId || session.State != "active")
        {
            await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
            return new { status = "denied", streamId };
        }

        if (!HasCurrentAudioConsent(session, options))
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_REQUIRED", "Please accept the current recording and speech-processing consent before submitting audio.");
            return new { status = "denied", streamId };
        }

        var maxTurnAgeSeconds = Math.Min(
            Math.Max(1, options.MaxTurnDurationSeconds),
            Math.Max(1, options.RealtimeSttMaxAudioSecondsPerSession));
        if (!realtimeTurns.TryComplete(
                Context.ConnectionId,
                sessionId,
                streamId,
                TimeSpan.FromSeconds(Math.Max(1, options.RealtimeSttTurnIdleTimeoutSeconds)),
                TimeSpan.FromSeconds(maxTurnAgeSeconds),
                out var snapshot) || snapshot is null)
        {
            var alreadyCommitted = await db.ConversationTurns.AsNoTracking()
                .AnyAsync(turn => turn.SessionId == sessionId && turn.TurnClientId == streamId, Context.ConnectionAborted);
            if (alreadyCommitted)
            {
                await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "duplicate");
                return new { status = "already-committed", streamId };
            }

            await Clients.Caller.SendAsync("ConversationError", "STREAM_NOT_FOUND", "Realtime turn was not found.");
            return new { status = "failed", streamId };
        }

        await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "committing");
        var committed = await ProcessAudioTurnAsync(sp, db, session, sessionId, snapshot.AudioBytes, snapshot.AudioMimeType, streamId, $"realtime:{streamId}");
        if (committed)
        {
            realtimeTurns.TryFinalize(Context.ConnectionId, sessionId, streamId);
        }
        else
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
        }

        return new { status = committed ? "committed" : "failed", streamId };
    }

    public async Task CancelRealtimeTurn(string sessionId, string streamId)
    {
        realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
        await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "cancelled");
    }

    public async Task EndSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId) return;
        if (session.State is "evaluated" or "evaluating") return;

        var now = DateTimeOffset.UtcNow;
        session.State = "evaluating";
        session.CompletedAt = now;
        session.DurationSeconds = session.StartedAt.HasValue
            ? (int)(now - session.StartedAt.Value).TotalSeconds : 0;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.ConversationEvaluation,
            ResourceId = sessionId,
            State = AsyncState.Queued,
            AvailableAt = now.AddSeconds(1),
            CreatedAt = now,
            LastTransitionAt = now,
            StatusReasonCode = "queued",
            StatusMessage = "Conversation evaluation queued.",
        });

        await db.SaveChangesAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("SessionStateChanged", "evaluating");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        realtimeTurns.CancelConnection(Context.ConnectionId);
        logger.LogInformation("ConversationHub: disconnected {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<bool> ProcessAudioTurnAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        ConversationSession session,
        string sessionId,
        byte[] audioBytes,
        string audioMime,
        string? turnClientId,
        string? providerEventId)
    {
        var asrSelector = sp.GetRequiredService<IConversationAsrProviderSelector>();
        var ttsSelector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var orchestrator = sp.GetRequiredService<IConversationAiOrchestrator>();
        var audio = sp.GetRequiredService<IConversationAudioService>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);

        if (!string.IsNullOrWhiteSpace(turnClientId) && await db.ConversationTurns
                .AnyAsync(turn => turn.SessionId == sessionId && turn.TurnClientId == turnClientId, Context.ConnectionAborted))
        {
            await Clients.Caller.SendAsync("RealtimeSttStopped", turnClientId, "duplicate");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(providerEventId) && await db.ConversationTurns
                .AnyAsync(turn => turn.SessionId == sessionId && turn.ProviderEventId == providerEventId, Context.ConnectionAborted))
        {
            await Clients.Caller.SendAsync("RealtimeSttStopped", turnClientId ?? providerEventId, "duplicate");
            return true;
        }

        ConversationAsrResult asr;
        try
        {
            using var asrStream = new MemoryStream(audioBytes);
            var provider = await asrSelector.SelectAsync(Context.ConnectionAborted);
            asr = await provider.TranscribeAsync(new ConversationAsrRequest(
                asrStream, audioMime, "en-GB", audioBytes.LongLength, EnableDiarization: true), Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "STT failed for {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ConversationError", "STT_ERROR", "Speech-to-text failed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(asr.Text))
        {
            await Clients.Caller.SendAsync("ConversationError", "STT_EMPTY", "We couldn't hear you clearly.");
            return false;
        }

        ConversationAudioRef learnerAudioRef;
        using (var audioStream = new MemoryStream(audioBytes))
        {
            learnerAudioRef = await audio.WriteAsync(audioStream, audioMime, Context.ConnectionAborted);
        }

        session.TurnCount++;
        var learnerTurnNumber = session.TurnCount;
        var elapsedMs = (int)(DateTimeOffset.UtcNow - (session.StartedAt ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
        db.ConversationTurns.Add(new ConversationTurn
        {
            Id = Guid.NewGuid(), SessionId = sessionId, TurnNumber = learnerTurnNumber,
            Role = "learner", Content = asr.Text, AudioUrl = learnerAudioRef.Url,
            DurationMs = asr.DurationMs, TimestampMs = elapsedMs,
            ConfidenceScore = asr.Confidence,
            AnalysisJson = JsonSupport.Serialize(new
            {
                asr.ProviderName,
                asr.ProviderResponseSummary,
                speakerSegments = asr.SpeakerSegments ?? Array.Empty<ConversationSpeakerSegment>(),
            }),
            TurnClientId = turnClientId,
            ProviderEventId = providerEventId,
            ProviderName = asr.ProviderName,
            FinalizedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveTranscript", learnerTurnNumber, asr.Text, asr.Confidence,
            new { audioUrl = learnerAudioRef.Url, speakerSegments = asr.SpeakerSegments ?? Array.Empty<ConversationSpeakerSegment>() });

        var elapsedSeconds = session.StartedAt.HasValue
            ? (int)(DateTimeOffset.UtcNow - session.StartedAt.Value).TotalSeconds : 0;
        var remaining = Math.Max(0, options.MaxSessionDurationSeconds - elapsedSeconds);

        session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);

        ConversationAiReply reply;
        try
        {
            var ctx = BuildAiContext(session, turnIndex: learnerTurnNumber, elapsed: elapsedSeconds, remaining: remaining);
            reply = await orchestrator.GenerateReplyAsync(ctx, Context.ConnectionAborted);
        }
        catch (PromptNotGroundedException)
        {
            await Clients.Caller.SendAsync("ConversationError", "UNGROUNDED", "AI grounding failed.");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI reply failed");
            await Clients.Caller.SendAsync("ConversationError", "AI_ERROR", "AI partner could not respond.");
            return true;
        }

        session.TurnCount++;
        var aiTurnNumber = session.TurnCount;

        string? aiAudioUrl = null;
        var tts = await ttsSelector.TrySelectAsync(Context.ConnectionAborted);
        if (tts is not null)
        {
            try
            {
                var ttsResult = await tts.SynthesizeAsync(new ConversationTtsRequest(reply.Text, ResolveVoice(session), "en-GB"), Context.ConnectionAborted);
                if (ttsResult.Audio.Length > 0)
                {
                    var aref = await audio.WriteAsync(ttsResult.Audio, ttsResult.MimeType, Context.ConnectionAborted);
                    aiAudioUrl = aref.Url;
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Reply TTS failed"); }
        }

        db.ConversationTurns.Add(new ConversationTurn
        {
            Id = Guid.NewGuid(), SessionId = sessionId, TurnNumber = aiTurnNumber,
            Role = "ai", Content = reply.Text, AudioUrl = aiAudioUrl,
            DurationMs = reply.Text.Split(' ').Length * 300, TimestampMs = elapsedMs + 800,
            ConfidenceScore = 1.0,
            AnalysisJson = JsonSupport.Serialize(new { reply.EmotionHint, reply.ShouldEnd, reply.AppliedRuleIds }),
            AiFeatureCode = AiFeatureCodes.ConversationReply,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Context.ConnectionAborted);
        session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
        await db.SaveChangesAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveAIResponse", aiTurnNumber, reply.Text, new
        {
            audioUrl = aiAudioUrl, emotionHint = reply.EmotionHint, appliedRuleIds = reply.AppliedRuleIds,
        });

        if (reply.ShouldEnd || remaining <= 10)
            await Clients.Caller.SendAsync("SessionShouldEnd", remaining);
        return true;
    }

    private static ConversationAiContext BuildAiContext(
        ConversationSession session, int turnIndex, int elapsed, int? remaining = null)
    {
        if (!Enum.TryParse<ExamProfession>(
                (session.Profession ?? "medicine").Replace("-", "").Replace("_", ""),
                ignoreCase: true, out var profession))
            profession = ExamProfession.Medicine;

        return new ConversationAiContext(
            session.Id, session.UserId, null, null, profession,
            session.TaskTypeCode, session.ScenarioJson, session.TranscriptJson,
            turnIndex, elapsed, remaining ?? 0, null);
    }

    private static async Task<string> BuildTranscriptJsonAsync(LearnerDbContext db, string sessionId, CancellationToken ct)
    {
        var turns = await db.ConversationTurns
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TurnNumber)
            .Select(t => new { turnNumber = t.TurnNumber, role = t.Role, content = t.Content, audioUrl = t.AudioUrl })
            .ToListAsync(ct);
        return JsonSupport.Serialize(turns);
    }

    private static string ResolveVoice(ConversationSession session)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(session.ScenarioJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("patientVoice", out var pv) &&
                pv.TryGetProperty("voiceId", out var vid) && vid.ValueKind == System.Text.Json.JsonValueKind.String)
                return vid.GetString() ?? "";
        }
        catch { }
        return string.Empty;
    }

    private static bool HasCurrentAudioConsent(ConversationSession session, ConversationOptions options)
        => session.RecordingConsentAcceptedAt.HasValue
           && session.VendorConsentAcceptedAt.HasValue
           && string.Equals(session.AudioConsentVersion, options.RealtimeSttConsentVersion, StringComparison.Ordinal);
}
