using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for real-time AI conversation practice. Responsibilities:
///
///   - Transition session state (preparing → active → evaluating).
///   - Transcribe learner audio via <see cref="IConversationAsrProviderSelector"/>.
///   - Generate in-role AI replies via <see cref="IConversationAiOrchestrator"/>
///     (grounded gateway — ungrounded prompts are refused by construction).
///   - Synthesise TTS for AI replies via <see cref="IConversationTtsProviderSelector"/>
///     and persist audio via <see cref="IConversationAudioService"/>.
///   - Persist every turn to <see cref="ConversationTurn"/> with the real
///     transcript/audio/confidence so evaluation has honest input.
/// </summary>
[Authorize]
public class ConversationHub(
    IServiceScopeFactory scopeFactory,
    ILogger<ConversationHub> logger) : Hub
{
    public async Task StartSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var options = sp.GetRequiredService<IOptions<ConversationOptions>>().Value;
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

        // Generate opening utterance via grounded gateway.
        try
        {
            var ctx = BuildAiContext(session, turnIndex: 0, elapsed: 0);
            var reply = await orchestrator.GenerateOpeningAsync(ctx, Context.ConnectionAborted);
            session.TurnCount++;
            var turnNumber = session.TurnCount;

            string? audioUrl = null;
            var tts = ttsSelector.TrySelect();
            if (tts is not null && !ttsSelector.TtsDisabled)
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
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Conversation TTS opening synthesis failed (non-fatal).");
                }
            }

            db.ConversationTurns.Add(new ConversationTurn
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                TurnNumber = turnNumber,
                Role = "ai",
                Content = reply.Text,
                AudioUrl = audioUrl,
                DurationMs = reply.Text.Split(' ').Length * 300,
                TimestampMs = 0,
                ConfidenceScore = 1.0,
                AnalysisJson = "{}",
                AiFeatureCode = AiFeatureCodes.ConversationOpening,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
            await db.SaveChangesAsync(Context.ConnectionAborted);

            await Clients.Caller.SendAsync("ReceiveAIResponse", turnNumber, reply.Text, new
            {
                audioUrl,
                emotionHint = reply.EmotionHint,
                appliedRuleIds = reply.AppliedRuleIds,
            });
        }
        catch (PromptNotGroundedException pnge)
        {
            logger.LogError(pnge, "Conversation opening refused — ungrounded prompt (bug in grounding plumbing).");
            await Clients.Caller.SendAsync("ConversationError", "UNGROUNDED", "AI grounding failed for this scenario. Please report this to support.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation opening generation failed");
            // Graceful fallback: emit a safe scripted opening.
            var fallback = "Hello — thank you for coming in today. How can I help you?";
            session.TurnCount++;
            db.ConversationTurns.Add(new ConversationTurn
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                TurnNumber = session.TurnCount,
                Role = "ai",
                Content = fallback,
                DurationMs = 3000,
                TimestampMs = 0,
                ConfidenceScore = 1.0,
                AnalysisJson = JsonSupport.Serialize(new { fallback = true, reason = ex.GetType().Name }),
                AiFeatureCode = AiFeatureCodes.ConversationOpening,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
            await db.SaveChangesAsync(Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveAIResponse", session.TurnCount, fallback, new { audioUrl = (string?)null, emotionHint = "neutral", appliedRuleIds = Array.Empty<string>() });
        }
    }

    public async Task SendAudio(string sessionId, string audioBase64, string? mimeType = null)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var asrSelector = sp.GetRequiredService<IConversationAsrProviderSelector>();
        var ttsSelector = sp.GetRequiredService<IConversationTtsProviderSelector>();
        var orchestrator = sp.GetRequiredService<IConversationAiOrchestrator>();
        var audio = sp.GetRequiredService<IConversationAudioService>();
        var options = sp.GetRequiredService<IOptions<ConversationOptions>>().Value;

        var session = await db.ConversationSessions.FindAsync(new object?[] { sessionId }, Context.ConnectionAborted);
        if (session == null || session.UserId != userId || session.State != "active")
        {
            await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
            return;
        }

        var audioMime = string.IsNullOrWhiteSpace(mimeType) ? "audio/webm" : mimeType;
        if (!options.AllowedMimeTypes.Contains(audioMime, StringComparer.OrdinalIgnoreCase))
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_MIME", $"Audio MIME '{audioMime}' is not allowed.");
            return;
        }

        byte[] audioBytes;
        try
        {
            audioBytes = Convert.FromBase64String(audioBase64);
        }
        catch (FormatException)
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_DECODE", "Audio payload was not valid base64.");
            return;
        }
        if (audioBytes.Length == 0 || audioBytes.Length > options.MaxAudioBytes)
        {
            await Clients.Caller.SendAsync("ConversationError", "AUDIO_SIZE", $"Audio size {audioBytes.Length} is out of bounds.");
            return;
        }

        ConversationAudioRef learnerAudioRef;
        using (var ms = new MemoryStream(audioBytes))
        {
            learnerAudioRef = await audio.WriteAsync(ms, audioMime, Context.ConnectionAborted);
        }

        // Transcribe using the active selector.
        ConversationAsrResult? asr = null;
        try
        {
            using var ms = new MemoryStream(audioBytes);
            var provider = asrSelector.Select();
            asr = await provider.TranscribeAsync(new ConversationAsrRequest(
                Audio: ms,
                AudioMimeType: audioMime,
                Locale: "en-GB",
                AudioBytes: audioBytes.LongLength), Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation STT failed for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ConversationError", "STT_ERROR", "Speech-to-text failed. Please try again.");
            return;
        }

        if (string.IsNullOrWhiteSpace(asr.Text))
        {
            await Clients.Caller.SendAsync("ConversationError", "STT_EMPTY", "We couldn't hear you clearly — try speaking again a little closer to the mic.");
            return;
        }

        session.TurnCount++;
        var learnerTurnNumber = session.TurnCount;
        var elapsedMs = (int)(DateTimeOffset.UtcNow - (session.StartedAt ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
        db.ConversationTurns.Add(new ConversationTurn
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TurnNumber = learnerTurnNumber,
            Role = "learner",
            Content = asr.Text,
            AudioUrl = learnerAudioRef.Url,
            DurationMs = asr.DurationMs,
            TimestampMs = elapsedMs,
            ConfidenceScore = asr.Confidence,
            AnalysisJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveTranscript", learnerTurnNumber, asr.Text, asr.Confidence, new { audioUrl = learnerAudioRef.Url });

        // Build context with accumulated transcript (including the new learner turn).
        var elapsedSeconds = session.StartedAt.HasValue
            ? (int)(DateTimeOffset.UtcNow - session.StartedAt.Value).TotalSeconds
            : 0;
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
            await Clients.Caller.SendAsync("ConversationError", "UNGROUNDED", "AI grounding failed. Please report to support.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation AI reply failed");
            await Clients.Caller.SendAsync("ConversationError", "AI_ERROR", "AI partner could not respond right now. Please try again.");
            return;
        }

        session.TurnCount++;
        var aiTurnNumber = session.TurnCount;

        string? aiAudioUrl = null;
        var tts = ttsSelector.TrySelect();
        if (tts is not null && !ttsSelector.TtsDisabled)
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Conversation TTS synthesis failed (non-fatal)");
            }
        }

        db.ConversationTurns.Add(new ConversationTurn
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TurnNumber = aiTurnNumber,
            Role = "ai",
            Content = reply.Text,
            AudioUrl = aiAudioUrl,
            DurationMs = reply.Text.Split(' ').Length * 300,
            TimestampMs = elapsedMs + 800,
            ConfidenceScore = 1.0,
            AnalysisJson = JsonSupport.Serialize(new { reply.EmotionHint, reply.ShouldEnd, reply.AppliedRuleIds }),
            AiFeatureCode = AiFeatureCodes.ConversationReply,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        session.TranscriptJson = await BuildTranscriptJsonAsync(db, sessionId, Context.ConnectionAborted);
        await db.SaveChangesAsync(Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ReceiveAIResponse", aiTurnNumber, reply.Text, new
        {
            audioUrl = aiAudioUrl,
            emotionHint = reply.EmotionHint,
            appliedRuleIds = reply.AppliedRuleIds,
        });

        if (reply.ShouldEnd || remaining <= 10)
        {
            await Clients.Caller.SendAsync("SessionShouldEnd", remaining);
        }
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
            ? (int)(now - session.StartedAt.Value).TotalSeconds
            : 0;

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
        logger.LogInformation("ConversationHub: Client disconnected. ConnectionId={ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ConversationAiContext BuildAiContext(
        ConversationSession session, int turnIndex, int elapsed, int? remaining = null)
    {
        Enum.TryParse<ExamProfession>(
            (session.Profession ?? "medicine").Replace("-", "").Replace("_", ""),
            ignoreCase: true,
            out var profession);
        if (profession == default) profession = ExamProfession.Medicine;

        return new ConversationAiContext(
            SessionId: session.Id,
            UserId: session.UserId,
            AuthAccountId: null,
            TenantId: null,
            Profession: profession,
            TaskTypeCode: session.TaskTypeCode,
            ScenarioJson: session.ScenarioJson,
            TranscriptJson: session.TranscriptJson,
            TurnIndex: turnIndex,
            ElapsedSeconds: elapsed,
            RemainingSeconds: remaining ?? 0,
            CandidateCountry: null);
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
            {
                return vid.GetString() ?? "";
            }
        }
        catch { /* fall through */ }
        return string.Empty;
    }
}
