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

        if (!options.Enabled)
        {
            await Clients.Caller.SendAsync("ConversationError", "CONVERSATION_DISABLED", "AI Conversation audio is currently disabled.");
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

        await ProcessAudioTurnAsync(sp, db, session, sessionId, audioBytes, audioMime, null, null, Context.ConnectionAborted);
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

        if (!options.Enabled)
        {
            await Clients.Caller.SendAsync("ConversationError", "CONVERSATION_DISABLED", "AI Conversation audio is currently disabled.");
            return "denied";
        }

        if (!options.RealtimeSttEnabled)
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
        if (!options.RealtimeSttAllowedMimeTypes.Contains(audioMime, StringComparer.OrdinalIgnoreCase))
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

        var maxTurnAgeSeconds = Math.Min(
            Math.Max(1, options.MaxTurnDurationSeconds),
            Math.Max(1, options.RealtimeSttMaxAudioSecondsPerSession));
        var idleTimeout = TimeSpan.FromSeconds(Math.Max(1, options.RealtimeSttTurnIdleTimeoutSeconds));
        var maxStreamAge = TimeSpan.FromSeconds(maxTurnAgeSeconds);
        if (!string.Equals(realtimeProvider.Name, "mock", StringComparison.OrdinalIgnoreCase))
        {
            var policyDenial = await ValidateRealProviderPolicyAsync(db, userId, realtimeProvider.Name, maxTurnAgeSeconds, options, Context.ConnectionAborted);
            if (policyDenial is not null)
            {
                if (options.RealtimeSttFallbackToBatch)
                {
                    await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, policyDenial, "batch");
                    return "fallback";
                }

                await Clients.Caller.SendAsync("ConversationError", policyDenial.ToUpperInvariant(), "Realtime vendor speech processing is not available for this session policy.");
                return "denied";
            }
        }

        await AbortProviderSessionsAsync(realtimeTurns.DetachExpiredProviderSessions(idleTimeout, maxStreamAge), "expired_before_begin");
        if (!realtimeTurns.TryBegin(
                Context.ConnectionId,
                userId,
                sessionId,
                streamId,
                audioMime,
                string.IsNullOrWhiteSpace(locale) ? options.ElevenLabsSttLanguage : locale,
                options.RealtimeSttMaxConcurrentStreamsPerUser,
                idleTimeout,
                maxStreamAge,
                out var errorCode))
        {
            if (options.RealtimeSttFallbackToBatch)
            {
                await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, errorCode?.ToLowerInvariant() ?? "start_unavailable", "batch");
                return "fallback";
            }

            await Clients.Caller.SendAsync("ConversationError", errorCode ?? "REALTIME_START", "Could not start realtime transcription.");
            return "denied";
        }

        try
        {
            var sink = new HubRealtimeTranscriptSink(Context.ConnectionId, sessionId, streamId, realtimeTurns, Clients.Caller, logger, options.RealtimeSttFallbackToBatch);
            var providerSession = await realtimeProvider.StartAsync(new ConversationRealtimeAsrStartRequest(
                sessionId,
                userId,
                streamId,
                audioMime,
                string.IsNullOrWhiteSpace(locale) ? options.ElevenLabsSttLanguage : locale,
                EnableDiarization: true,
                MaxTurnDurationSeconds: maxTurnAgeSeconds), sink, Context.ConnectionAborted);
            if (!realtimeTurns.TryAttachProviderSession(Context.ConnectionId, sessionId, streamId, providerSession))
            {
                await AbortProviderSessionAsync(providerSession, "stream_not_found", CancellationToken.None);
                await Clients.Caller.SendAsync("ConversationError", "REALTIME_START", "Could not start realtime transcription.");
                return "denied";
            }
        }
        catch (Exception ex)
        {
            realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
            logger.LogWarning(ex, "Realtime STT provider start failed for {SessionId}", sessionId);
            if (options.RealtimeSttFallbackToBatch)
            {
                await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, "provider_start_failed", "batch");
                return "fallback";
            }

            await Clients.Caller.SendAsync("ConversationError", "REALTIME_PROVIDER_START", "Realtime transcription could not start and full-turn fallback is not allowed.");
            return "denied";
        }

        var maxChunkBytes = Math.Min(options.RealtimeSttMaxChunkBytes, ConversationRealtimeTransportLimits.MaxBinaryChunkBytes);
        await Clients.Caller.SendAsync("RealtimeSttStarted", streamId, realtimeProvider.Name, maxChunkBytes);
        return "realtime";
    }

    public async Task SendRealtimeAudioChunk(string sessionId, string streamId, int sequence, string audioBase64, int? clientOffsetMs = null)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(Context.ConnectionAborted);
        if (string.IsNullOrWhiteSpace(userId))
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "unauthorized");
            await Clients.Caller.SendAsync("ConversationError", "UNAUTHORIZED", "Please sign in again before sending audio.");
            return;
        }

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId || session.State != "active")
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "invalid_session");
            await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
            return;
        }

        if (!HasCurrentAudioConsent(session, options))
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "consent_missing");
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_REQUIRED", "Please accept the current recording and speech-processing consent before sending audio.");
            return;
        }

        if (!options.Enabled)
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "conversation_disabled");
            await Clients.Caller.SendAsync("ConversationError", "CONVERSATION_DISABLED", "AI Conversation audio is currently disabled.");
            return;
        }

        var maxTurnAgeSeconds = Math.Min(
            Math.Max(1, options.MaxTurnDurationSeconds),
            Math.Max(1, options.RealtimeSttMaxAudioSecondsPerSession));
        var idleTimeout = TimeSpan.FromSeconds(Math.Max(1, options.RealtimeSttTurnIdleTimeoutSeconds));
        var maxStreamAge = TimeSpan.FromSeconds(maxTurnAgeSeconds);

        if (!options.RealtimeSttEnabled)
        {
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, "REALTIME_DISABLED", "Realtime transcription was disabled while this turn was recording.");
            return;
        }

        if (!realtimeTurns.TryValidateStream(Context.ConnectionId, sessionId, streamId, idleTimeout, maxStreamAge, out var streamErrorCode))
        {
            if (string.Equals(streamErrorCode, "STREAM_COMMITTING", StringComparison.Ordinal)) return;
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, streamErrorCode ?? "REALTIME_CHUNK", "Audio chunk could not be accepted.");
            return;
        }

        var maxChunkBytes = Math.Min(options.RealtimeSttMaxChunkBytes, ConversationRealtimeTransportLimits.MaxBinaryChunkBytes);
        var maxEncodedChars = ((maxChunkBytes + 2) / 3 * 4) + 8;
        if (string.IsNullOrWhiteSpace(audioBase64) || audioBase64.Length > maxEncodedChars)
        {
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, "AUDIO_CHUNK_SIZE", "Audio chunk size is out of bounds.");
            return;
        }

        byte[] audioBytes;
        try { audioBytes = Convert.FromBase64String(audioBase64); }
        catch (FormatException)
        {
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, "AUDIO_DECODE", "Audio chunk was not valid base64.");
            return;
        }

        if (audioBytes.Length == 0 || audioBytes.Length > maxChunkBytes)
        {
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, "AUDIO_CHUNK_SIZE", "Audio chunk size is out of bounds.");
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
            if (string.Equals(errorCode, "STREAM_COMMITTING", StringComparison.Ordinal)) return;
            await SendRealtimeChunkFailureAsync(options, sessionId, streamId, errorCode ?? "REALTIME_CHUNK", "Audio chunk could not be accepted.");
            return;
        }

        if (realtimeTurns.TryGetProviderSession(Context.ConnectionId, sessionId, streamId, out var providerSession) && providerSession is not null)
        {
            try
            {
                await providerSession.SendAudioAsync(new ConversationRealtimeAudioChunk(sequence, audioBytes, IsFinal: false, clientOffsetMs), Context.ConnectionAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Realtime STT provider chunk failed for {SessionId}", sessionId);
                await SendRealtimeChunkFailureAsync(options, sessionId, streamId, "REALTIME_PROVIDER_CHUNK", "Live captions stopped. Your answer can still transcribe normally.");
                return;
            }
        }
        else if (result?.ShouldEmitPartial == true)
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

        var realtimeDisabledDuringCommit = !options.RealtimeSttEnabled;
        if (realtimeDisabledDuringCommit && !options.RealtimeSttFallbackToBatch)
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "realtime_disabled");
            await Clients.Caller.SendAsync("ConversationError", "REALTIME_DISABLED", "Realtime transcription was disabled before this turn could be saved.");
            return new { status = "failed", streamId };
        }

        if (!HasCurrentAudioConsent(session, options))
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "consent_missing");
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_CONSENT_REQUIRED", "Please accept the current recording and speech-processing consent before submitting audio.");
            return new { status = "denied", streamId };
        }

        if (!options.Enabled)
        {
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "conversation_disabled");
            await Clients.Caller.SendAsync("ConversationError", "CONVERSATION_DISABLED", "AI Conversation audio is currently disabled.");
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
                out var snapshot,
                out var completeErrorCode) || snapshot is null)
        {
            if (string.Equals(completeErrorCode, "STREAM_COMMITTING", StringComparison.Ordinal))
            {
                await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "committing");
                return new { status = "committing", streamId };
            }

            var alreadyCommitted = await db.ConversationTurns.AsNoTracking()
                .AnyAsync(turn => turn.SessionId == sessionId && turn.TurnClientId == streamId, Context.ConnectionAborted);
            if (alreadyCommitted)
            {
                await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "duplicate");
                return new { status = "already-committed", streamId };
            }

            if (!string.Equals(completeErrorCode, "STREAM_NOT_FOUND", StringComparison.Ordinal))
            {
                await CancelRealtimeTurnInternalAsync(sessionId, streamId, completeErrorCode?.ToLowerInvariant() ?? "complete_failed");
            }

            await Clients.Caller.SendAsync("ConversationError", completeErrorCode ?? "STREAM_NOT_FOUND", "Realtime turn was not found.");
            return new { status = "failed", streamId };
        }

        await Clients.Caller.SendAsync("RealtimeSttStopped", streamId, "committing");
        ConversationAsrResult? realtimeAsr = null;
        var finalProviderEventId = $"realtime:{streamId}";
        IConversationRealtimeAsrSession? providerSession = null;
        using var commitCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, options.MaxTurnDurationSeconds + 30)));
        try
        {
            if (realtimeTurns.TryGetProviderSession(Context.ConnectionId, sessionId, streamId, out providerSession) && providerSession is not null)
            {
                if (realtimeDisabledDuringCommit)
                {
                    await AbortProviderSessionAsync(providerSession, "realtime_disabled", CancellationToken.None);
                    providerSession = null;
                }
                else
                {
                    try
                    {
                        await providerSession.CompleteAsync(commitCts.Token);
                        if (realtimeTurns.TryGetProviderFinal(Context.ConnectionId, sessionId, streamId, out var final)
                            && final is not null
                            && !string.IsNullOrWhiteSpace(final.Text)
                            && !string.Equals(final.ProviderName, "mock", StringComparison.OrdinalIgnoreCase))
                        {
                            finalProviderEventId = string.IsNullOrWhiteSpace(final.ProviderEventId) ? finalProviderEventId : final.ProviderEventId;
                            realtimeAsr = new ConversationAsrResult(
                                final.Text,
                                final.Confidence,
                                final.DurationMs,
                                snapshot.Locale,
                                final.ProviderName,
                                final.ProviderResponseSummary,
                                final.SpeakerSegments);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Realtime STT provider complete failed for {SessionId}", sessionId);
                    }
                }
            }

            if (realtimeAsr is null && !options.RealtimeSttFallbackToBatch)
            {
                await CancelRealtimeTurnInternalAsync(sessionId, streamId, "fallback_disabled", CancellationToken.None);
                await Clients.Caller.SendAsync("ConversationError", "REALTIME_FINAL_MISSING", "Realtime transcription did not return a final transcript and full-turn fallback is disabled.");
                return new { status = "failed", streamId };
            }

            if (realtimeAsr is null && IsRawPcmAudioMime(snapshot.AudioMimeType))
            {
                await CancelRealtimeTurnInternalAsync(sessionId, streamId, "pcm_batch_fallback_unavailable", CancellationToken.None);
                await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, "pcm_batch_fallback_unavailable", "batch");
                return new { status = "failed", streamId };
            }

            var committed = await ProcessAudioTurnAsync(sp, db, session, sessionId, snapshot.AudioBytes, snapshot.AudioMimeType, streamId, finalProviderEventId, commitCts.Token, realtimeAsr);
            if (committed)
            {
                if (providerSession is not null) await DisposeProviderSessionAsync(providerSession);
                realtimeTurns.TryFinalize(Context.ConnectionId, sessionId, streamId);
            }
            else
            {
                await CancelRealtimeTurnInternalAsync(sessionId, streamId, "commit_failed");
            }

            return new { status = committed ? "committed" : "failed", streamId };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime STT commit failed for {SessionId}", sessionId);
            await CancelRealtimeTurnInternalAsync(sessionId, streamId, "commit_exception", CancellationToken.None);
            if (!Context.ConnectionAborted.IsCancellationRequested)
            {
                await Clients.Caller.SendAsync("ConversationError", "COMMIT_FAILED", "Realtime turn could not be saved.");
            }

            return new { status = "failed", streamId };
        }
    }

    public async Task CancelRealtimeTurn(string sessionId, string streamId)
    {
        await CancelRealtimeTurnInternalAsync(sessionId, streamId, "client_cancelled");
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
        foreach (var providerSession in realtimeTurns.CancelConnection(Context.ConnectionId))
        {
            await AbortProviderSessionAsync(providerSession, "connection_closed", CancellationToken.None);
        }
        logger.LogInformation("ConversationHub: disconnected {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task SendRealtimeChunkFailureAsync(ConversationOptions options, string sessionId, string streamId, string code, string message)
    {
        await CancelRealtimeTurnInternalAsync(sessionId, streamId, code.ToLowerInvariant());
        if (options.RealtimeSttFallbackToBatch)
        {
            await Clients.Caller.SendAsync("RealtimeSttFallback", streamId, code.ToLowerInvariant(), "batch");
            return;
        }

        await Clients.Caller.SendAsync("ConversationError", code, message);
    }

    private async Task CancelRealtimeTurnInternalAsync(string sessionId, string streamId, string reason, CancellationToken? cancellationToken = null)
    {
        var ct = cancellationToken ?? Context.ConnectionAborted;
        if (realtimeTurns.TryGetProviderSession(Context.ConnectionId, sessionId, streamId, out var providerSession) && providerSession is not null)
        {
            await AbortProviderSessionAsync(providerSession, reason, ct);
        }

        realtimeTurns.TryCancel(Context.ConnectionId, sessionId, streamId);
    }

    private async Task AbortProviderSessionsAsync(IEnumerable<IConversationRealtimeAsrSession> providerSessions, string reason)
    {
        foreach (var providerSession in providerSessions)
        {
            await AbortProviderSessionAsync(providerSession, reason, Context.ConnectionAborted);
        }
    }

    private async Task AbortProviderSessionAsync(IConversationRealtimeAsrSession providerSession, string reason, CancellationToken ct)
    {
        try
        {
            await providerSession.AbortAsync(reason, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Realtime STT provider abort failed for reason {Reason}", reason);
        }

        await DisposeProviderSessionAsync(providerSession);
    }

    private async Task DisposeProviderSessionAsync(IConversationRealtimeAsrSession providerSession)
    {
        try
        {
            await providerSession.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Realtime STT provider dispose failed.");
        }
    }

    private async Task<bool> ProcessAudioTurnAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        ConversationSession session,
        string sessionId,
        byte[] audioBytes,
        string audioMime,
        string? turnClientId,
        string? providerEventId,
        CancellationToken ct,
        ConversationAsrResult? preTranscribed = null)
    {
        var asrSelector = sp.GetRequiredService<IConversationAsrProviderSelector>();
        var ttsSelector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var orchestrator = sp.GetRequiredService<IConversationAiOrchestrator>();
        var audio = sp.GetRequiredService<IConversationAudioService>();
        var options = await sp.GetRequiredService<IConversationOptionsProvider>().GetAsync(ct);

        if (!string.IsNullOrWhiteSpace(turnClientId) && await db.ConversationTurns
                .AnyAsync(turn => turn.SessionId == sessionId && turn.TurnClientId == turnClientId, ct))
        {
            await Clients.Caller.SendAsync("RealtimeSttStopped", turnClientId, "duplicate", ct);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(providerEventId) && await db.ConversationTurns
                .AnyAsync(turn => turn.SessionId == sessionId && turn.ProviderEventId == providerEventId, ct))
        {
            await Clients.Caller.SendAsync("RealtimeSttStopped", turnClientId ?? providerEventId, "duplicate", ct);
            return true;
        }

        ConversationAsrResult asr;
        if (preTranscribed is not null)
        {
            asr = preTranscribed;
        }
        else try
        {
            using var asrStream = new MemoryStream(audioBytes);
            var provider = await asrSelector.SelectAsync(ct);
            asr = await provider.TranscribeAsync(new ConversationAsrRequest(
                asrStream, audioMime, "en-GB", audioBytes.LongLength, EnableDiarization: true), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "STT failed for {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ConversationError", "STT_ERROR", "Speech-to-text failed.", ct);
            return false;
        }

        if (string.IsNullOrWhiteSpace(asr.Text))
        {
            await Clients.Caller.SendAsync("ConversationError", "STT_EMPTY", "We couldn't hear you clearly.", ct);
            return false;
        }

        ConversationAudioRef learnerAudioRef;
        using (var audioStream = new MemoryStream(audioBytes))
        {
            learnerAudioRef = await audio.WriteAsync(audioStream, audioMime, ct);
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
        await db.SaveChangesAsync(ct);

        await Clients.Caller.SendAsync("ReceiveTranscript", learnerTurnNumber, asr.Text, asr.Confidence,
            new { audioUrl = learnerAudioRef.Url, speakerSegments = asr.SpeakerSegments ?? Array.Empty<ConversationSpeakerSegment>() }, ct);

        var elapsedSeconds = session.StartedAt.HasValue
            ? (int)(DateTimeOffset.UtcNow - session.StartedAt.Value).TotalSeconds : 0;
        var remaining = Math.Max(0, options.MaxSessionDurationSeconds - elapsedSeconds);

        session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, ct);
        await db.SaveChangesAsync(ct);

        ConversationAiReply reply;
        try
        {
            var ctx = BuildAiContext(session, turnIndex: learnerTurnNumber, elapsed: elapsedSeconds, remaining: remaining);
            reply = await orchestrator.GenerateReplyAsync(ctx, ct);
        }
        catch (PromptNotGroundedException)
        {
            await Clients.Caller.SendAsync("ConversationError", "UNGROUNDED", "AI grounding failed.", ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI reply failed");
            await Clients.Caller.SendAsync("ConversationError", "AI_ERROR", "AI partner could not respond.", ct);
            return true;
        }

        session.TurnCount++;
        var aiTurnNumber = session.TurnCount;

        string? aiAudioUrl = null;
        var tts = await ttsSelector.TrySelectAsync(ct);
        if (tts is not null)
        {
            try
            {
                var ttsResult = await tts.SynthesizeAsync(new ConversationTtsRequest(reply.Text, ResolveVoice(session), "en-GB"), ct);
                if (ttsResult.Audio.Length > 0)
                {
                    var aref = await audio.WriteAsync(ttsResult.Audio, ttsResult.MimeType, ct);
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
        await db.SaveChangesAsync(ct);
        session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, ct);
        await db.SaveChangesAsync(ct);

        await Clients.Caller.SendAsync("ReceiveAIResponse", aiTurnNumber, reply.Text, new
        {
            audioUrl = aiAudioUrl, emotionHint = reply.EmotionHint, appliedRuleIds = reply.AppliedRuleIds,
        }, ct);

        if (reply.ShouldEnd || remaining <= 10)
            await Clients.Caller.SendAsync("SessionShouldEnd", remaining, ct);
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

    private sealed class HubRealtimeTranscriptSink(
        string connectionId,
        string sessionId,
        string streamId,
        ConversationRealtimeTurnStore store,
        IClientProxy caller,
        ILogger logger,
        bool fallbackToBatch) : IConversationRealtimeTranscriptSink
    {
        public Task OnPartialAsync(ConversationRealtimeTranscriptPartial partial, CancellationToken ct)
        {
            if (!string.Equals(partial.StreamId, streamId, StringComparison.Ordinal)) return Task.CompletedTask;
            return caller.SendAsync(
                "RealtimeTranscriptPartial",
                partial.StreamId,
                partial.Text,
                partial.Confidence,
                partial.StartMs,
                partial.EndMs,
                partial.Sequence,
                ct);
        }

        public Task OnFinalAsync(ConversationRealtimeTranscriptFinal final, CancellationToken ct)
        {
            if (string.Equals(final.StreamId, streamId, StringComparison.Ordinal))
            {
                store.TrySetProviderFinal(connectionId, sessionId, streamId, final);
            }

            return Task.CompletedTask;
        }

        public Task OnProviderErrorAsync(ConversationAsrException error, CancellationToken ct)
        {
            logger.LogWarning("Realtime STT provider error for {SessionId}: {Code}", sessionId, error.Code);
            store.TryCancel(connectionId, sessionId, streamId);
            return fallbackToBatch
                ? caller.SendAsync("RealtimeSttFallback", streamId, error.Code.ToLowerInvariant(), "batch", ct)
                : caller.SendAsync("ConversationError", error.Code, "Realtime transcription provider returned an error.", ct);
        }
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

    private static bool IsRawPcmAudioMime(string mimeType)
    {
        var normalized = mimeType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized is "audio/pcm" or "audio/l16" or "audio/raw" or "application/octet-stream";
    }

    private static async Task<string?> ValidateRealProviderPolicyAsync(
        LearnerDbContext db,
        string userId,
        string providerName,
        int reservedSeconds,
        ConversationOptions options,
        CancellationToken ct)
    {
        if (!options.RealtimeSttAssumeLearnersAdult) return "minor_policy_unconfigured";

        var sponsorLinks = await db.SponsorLearnerLinks.AsNoTracking()
            .Where(link => link.LearnerId == userId)
            .ToListAsync(ct);
        if (sponsorLinks.Count > 0)
        {
            if (sponsorLinks.Any(link => !link.LearnerConsented)) return "sponsor_learner_consent_required";
            if (!options.RealtimeSttAllowManagedLearnerRealProvider) return "managed_learner_vendor_not_allowed";
        }

        var now = DateTimeOffset.UtcNow;
        var dayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var userDailyMs = await db.ConversationTurns.AsNoTracking()
            .Where(turn => turn.ProviderName == providerName && turn.CreatedAt >= dayStart)
            .Join(db.ConversationSessions.AsNoTracking().Where(session => session.UserId == userId),
                turn => turn.SessionId,
                session => session.Id,
                (turn, _) => turn.DurationMs)
            .SumAsync(ct);
        if (options.RealtimeSttDailyAudioSecondsPerUser > 0 && userDailyMs / 1000.0 + reservedSeconds > options.RealtimeSttDailyAudioSecondsPerUser)
        {
            return "stt_daily_cap_exhausted";
        }

        if (options.RealtimeSttMonthlyBudgetCapUsd > 0)
        {
            if (options.RealtimeSttEstimatedCostUsdPerMinute <= 0) return "stt_pricing_unconfigured";
            var monthlyMs = await db.ConversationTurns.AsNoTracking()
                .Where(turn => turn.ProviderName == providerName && turn.CreatedAt >= monthStart)
                .SumAsync(turn => turn.DurationMs, ct);
            var projectedCost = ((decimal)monthlyMs / 1000m / 60m * options.RealtimeSttEstimatedCostUsdPerMinute)
                + ((decimal)reservedSeconds / 60m * options.RealtimeSttEstimatedCostUsdPerMinute);
            if (projectedCost > options.RealtimeSttMonthlyBudgetCapUsd) return "stt_monthly_budget_exhausted";
        }

        return null;
    }
}
