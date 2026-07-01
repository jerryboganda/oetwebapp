using System.Collections.Concurrent;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Conversation.Asr;
using OetLearner.Api.Services.Conversation.Tts;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Hubs;

// Phase 2 (F) + Phase 3 + Phase 4 of the OET Speaking module roadmap.
//
// Extension hook that lets the typed Speaking session client (created
// via `POST /v1/speaking/sessions`) bootstrap an AI patient conversation
// from the existing ConversationHub plumbing.
//
// Phase 4 broadens the surface to include:
//   * A distinct warm-up persona prompt (no clinical script — friendly
//     identity-check questions) plus a server-driven warm-up question
//     pool (`SpeakingWarmUpSeed.GetQuestions`).
//   * Role-play persona prompts marked up with Anthropic-style
//     `cache_control` blocks so the multi-turn LLM loop can hit the
//     ephemeral prompt cache between turns.
//   * A server-side `TimeNearlyUp` + `TimeUp` broadcaster: when the
//     learner calls `StartRolePlayTimer`, the hub schedules two
//     `Task.Delay` fires that emit `TimeNearlyUp` at `T-30s` and `TimeUp`
//     at `T-0s`, auto-ending the session and triggering AI assessment.
//
// We deliberately stop at "seed the opening line + drive cues" — the
// full audio/STT/TTS round-trip continues to use the `StartSession`
// family already in the main hub. The bridge is the SignalR group
// `speaking-session:{sessionId}` which the frontend subscribes to for
// patient utterances + cues.
public partial class ConversationHub
{
    private const string SpeakingRoleplayGroupPrefix = "speaking-session:";

    /// <summary>
    /// Tracks active time-up countdowns so a learner cannot accidentally
    /// double-schedule by re-invoking <see cref="StartRolePlayTimer"/>.
    /// Keyed by session id — the value is a cancellation token source
    /// the hub uses to abort if the learner ends the session early.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveRolePlayTimers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Bootstraps the AI patient conversation for an existing typed
    /// Speaking session. Validates ownership, the session is in
    /// <c>warmup</c>, <c>prep</c> or <c>active</c>, joins the SignalR
    /// group keyed off the session id, and pushes the appropriate
    /// opening line so the learner UI can begin rendering the
    /// conversation transcript.
    /// </summary>
    public async Task StartSpeakingRoleplay(string speakingSessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "UNAUTHORIZED",
                "Please sign in again before starting the role-play.");
            return;
        }

        if (string.IsNullOrWhiteSpace(speakingSessionId))
        {
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "SESSION_ID_REQUIRED",
                "A Speaking session id is required.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var session = await db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, Context.ConnectionAborted);
        if (session is null || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // Avoid leaking session ids that belong to other learners.
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "SESSION_NOT_FOUND",
                "That Speaking session does not exist.");
            return;
        }

        if (session.State != SpeakingSessionState.WarmUp
            && session.State != SpeakingSessionState.Prep
            && session.State != SpeakingSessionState.Active)
        {
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "INVALID_STATE",
                $"The role-play cannot start in state '{SpeakingSessionStates.ToCode(session.State)}'.");
            return;
        }

        var card = await db.RolePlayCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, Context.ConnectionAborted);
        if (card is null)
        {
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "CARD_NOT_FOUND",
                "The role-play card linked to this session is missing.");
            return;
        }

        var groupName = SpeakingRoleplayGroupPrefix + speakingSessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        // ── Warm-up branch ──────────────────────────────────────────────
        if (session.State == SpeakingSessionState.WarmUp)
        {
            var warmUpPersona = BuildWarmUpPersonaPrompt(card);
            var questions = SpeakingWarmUpSeed.GetQuestions(card.ProfessionId);
            logger.LogInformation(
                "Speaking warm-up opening seeded for session {SessionId} (persona length={PersonaLength}, questions={QuestionCount}).",
                speakingSessionId,
                warmUpPersona.Length,
                questions.Count);

            var opening = questions.Count > 0
                ? questions[0]
                : "Hello! It is nice to meet you. To start, could you tell me a little about yourself?";

            var warmUpAudioUrl = await TrySynthesizeReplyAudioAsync(scope.ServiceProvider, opening, Context.ConnectionAborted);

            await Clients.Caller.SendAsync("PatientUtterance", new
            {
                speaker = "interlocutor",
                phase = "warmup",
                text = opening,
                audioUrl = warmUpAudioUrl,
                timestamp = DateTimeOffset.UtcNow,
            });
            return;
        }

        // ── Role-play branch (Prep or Active) ───────────────────────────
        var script = await db.InterlocutorScripts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, Context.ConnectionAborted);
        if (script is null)
        {
            await Clients.Caller.SendAsync(
                "SpeakingRoleplayError",
                "INTERLOCUTOR_SCRIPT_MISSING",
                "This role-play card has not been wired with an interlocutor script.");
            return;
        }

        // Compose the patient persona prompt up-front; downstream audio
        // turns can read it back from the session metadata when the full
        // ConversationHub pipeline takes over.
        var personaPrompt = BuildPatientPersonaPrompt(card, script);
        logger.LogInformation(
            "Speaking role-play opening seeded for session {SessionId} (persona length={PersonaLength}).",
            speakingSessionId,
            personaPrompt.Length);

        var openingAudioUrl = await TrySynthesizeReplyAudioAsync(scope.ServiceProvider, script.OpeningResponse, Context.ConnectionAborted);

        await Clients.Caller.SendAsync("PatientUtterance", new
        {
            speaker = "patient",
            phase = "roleplay",
            text = script.OpeningResponse,
            audioUrl = openingAudioUrl,
            timestamp = DateTimeOffset.UtcNow,
        });
    }

    // ════════════════════════════════════════════════════════════════════
    // WS2 — HEADLINE realtime AI role-player turn loop.
    //
    // The student talks to a realtime AI that listens (speech-to-text),
    // interprets, and replies *in character* as the patient/relative per
    // the hidden interlocutor card. This is the loop that turns the
    // "seed an opening line" bridge above into a genuine back-and-forth
    // conversation:
    //
    //   learner audio (or text) ──▶ STT (Conversation ASR, mock-fallback)
    //                          ──▶ grounded in-character LLM reply
    //                              (IConversationAiOrchestrator → gateway,
    //                               mock-fallback when no API key)
    //                          ──▶ TTS (Conversation TTS, mock-fallback)
    //                          ──▶ persist both turns into the session
    //                              transcript (SpeakingTranscript)
    //                          ──▶ stream caption + patient utterance back.
    //
    // CANDIDATE-SAFETY INVARIANT: the hidden `InterlocutorScript` / role
    // card never leaves the server. Only the AI's spoken reply text and a
    // TTS audio URL are emitted to the learner.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Receives one learner turn as recorded audio (base64) for the active
    /// role-play or warm-up, transcribes it, generates the AI patient's
    /// in-character reply, synthesises speech, persists the exchange, and
    /// streams the caption + patient utterance back to the caller.
    /// </summary>
    public Task SendSpeakingRoleplayTurn(string speakingSessionId, string audioBase64, string? mimeType)
        => ProcessSpeakingTurnAsync(speakingSessionId, audioBase64, transcribedText: null, mimeType);

    /// <summary>
    /// Text-input variant of <see cref="SendSpeakingRoleplayTurn"/> for
    /// keyboard accessibility and automated tests — bypasses STT and feeds
    /// the supplied text straight into the in-character AI reply loop.
    /// </summary>
    public Task SendSpeakingRoleplayText(string speakingSessionId, string text)
        => ProcessSpeakingTurnAsync(speakingSessionId, audioBase64: null, transcribedText: text, mimeType: null);

    private async Task ProcessSpeakingTurnAsync(
        string speakingSessionId,
        string? audioBase64,
        string? transcribedText,
        string? mimeType)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "UNAUTHORIZED",
                "Please sign in again before speaking.");
            return;
        }
        if (string.IsNullOrWhiteSpace(speakingSessionId))
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "SESSION_ID_REQUIRED",
                "A Speaking session id is required.");
            return;
        }

        var ct = Context.ConnectionAborted;
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();

        var session = await db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, ct);
        if (session is null || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // Avoid leaking session ids that belong to other learners.
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "SESSION_NOT_FOUND",
                "That Speaking session does not exist.");
            return;
        }
        if (session.State != SpeakingSessionState.WarmUp
            && session.State != SpeakingSessionState.Active)
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "INVALID_STATE",
                $"You can only speak while the role-play is active (current: '{SpeakingSessionStates.ToCode(session.State)}').");
            return;
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.RolePlayCardId, ct);
        if (card is null)
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "CARD_NOT_FOUND",
                "The role-play card linked to this session is missing.");
            return;
        }

        var isWarmUp = session.State == SpeakingSessionState.WarmUp;
        InterlocutorScript? script = null;
        if (!isWarmUp)
        {
            script = await db.InterlocutorScripts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);
            if (script is null)
            {
                await Clients.Caller.SendAsync("SpeakingRoleplayError", "INTERLOCUTOR_SCRIPT_MISSING",
                    "This role-play card has not been wired with an interlocutor script.");
                return;
            }
        }

        // ── 1. Resolve the learner utterance (STT or supplied text) ──
        string learnerText;
        var learnerConfidence = 1.0;
        if (!string.IsNullOrWhiteSpace(transcribedText))
        {
            learnerText = transcribedText.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(audioBase64))
        {
            byte[] audioBytes;
            try { audioBytes = Convert.FromBase64String(audioBase64); }
            catch (FormatException)
            {
                await Clients.Caller.SendAsync("SpeakingRoleplayError", "AUDIO_DECODE",
                    "Audio payload was not valid base64.");
                return;
            }
            if (audioBytes.Length == 0)
            {
                await Clients.Caller.SendAsync("SpeakingRoleplayError", "AUDIO_EMPTY",
                    "No audio was received.");
                return;
            }
            var audioMime = string.IsNullOrWhiteSpace(mimeType) ? "audio/webm" : mimeType;
            try
            {
                var asrSelector = sp.GetRequiredService<IConversationAsrProviderSelector>();
                var asrProvider = await asrSelector.SelectAsync(ct);
                using var asrStream = new MemoryStream(audioBytes);
                var asr = await asrProvider.TranscribeAsync(
                    new ConversationAsrRequest(asrStream, audioMime, "en-GB", audioBytes.LongLength, EnableDiarization: false),
                    ct);
                learnerText = (asr.Text ?? string.Empty).Trim();
                learnerConfidence = asr.Confidence;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaking STT failed for session {SessionId}.", speakingSessionId);
                await Clients.Caller.SendAsync("SpeakingRoleplayError", "STT_ERROR",
                    "We could not transcribe your speech. Please try again.");
                return;
            }
        }
        else
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "TURN_EMPTY",
                "Send either recorded audio or text.");
            return;
        }

        if (string.IsNullOrWhiteSpace(learnerText))
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "STT_EMPTY",
                "We couldn't hear you clearly. Please try again.");
            return;
        }

        // Echo the learner caption immediately so the transcript renders.
        await Clients.Caller.SendAsync("LearnerCaption", new
        {
            speaker = "candidate",
            text = learnerText,
            confidence = learnerConfidence,
            timestamp = DateTimeOffset.UtcNow,
        }, ct);

        // ── 2. Load + extend the running transcript ──
        var transcriptRow = await db.SpeakingTranscripts
            .Where(t => t.SpeakingSessionId == speakingSessionId && t.IsLatest)
            .OrderByDescending(t => t.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        var segments = ParseSpeakingSegments(transcriptRow?.SegmentsJson);

        var startedAt = isWarmUp ? session.WarmupStartedAt : (session.RolePlayStartedAt ?? session.PrepStartedAt);
        var nowMs = startedAt.HasValue
            ? (long)Math.Max(0, (DateTimeOffset.UtcNow - startedAt.Value).TotalMilliseconds)
            : 0L;
        segments.Add(new SpeakingTurnSegment("candidate", nowMs, nowMs, learnerText, learnerConfidence));
        var learnerTurnCount = segments.Count(s => string.Equals(s.Speaker, "candidate", StringComparison.Ordinal));

        // ── 3. Ask the grounded AI to reply in character ──
        var personaPrompt = isWarmUp
            ? BuildWarmUpPersonaPrompt(card)
            : BuildPatientPersonaPrompt(card, script!);
        var scenarioJson = JsonSerializer.Serialize(new
        {
            mode = isWarmUp ? "warmup" : "roleplay",
            persona = personaPrompt,
            role = card.InterlocutorRole,
            setting = card.Setting,
            scenarioTitle = card.ScenarioTitle,
        });
        var transcriptJson = JsonSerializer.Serialize(segments.Select(s => new
        {
            role = string.Equals(s.Speaker, "candidate", StringComparison.Ordinal) ? "learner" : "ai",
            text = s.Text,
        }));

        var elapsedSeconds = (int)(nowMs / 1000);
        var rolePlaySeconds = card.RolePlayTimeSeconds > 0 ? card.RolePlayTimeSeconds : 300;
        var remainingSeconds = Math.Max(0, rolePlaySeconds - elapsedSeconds);

        ConversationAiReply reply;
        try
        {
            var orchestrator = sp.GetRequiredService<IConversationAiOrchestrator>();
            var aiCtx = new ConversationAiContext(
                SessionId: speakingSessionId,
                UserId: userId,
                AuthAccountId: null,
                TenantId: null,
                Profession: ParseProfessionCode(card.ProfessionId),
                TaskTypeCode: isWarmUp ? "speaking_warmup" : "speaking_roleplay",
                ScenarioJson: scenarioJson,
                TranscriptJson: transcriptJson,
                TurnIndex: learnerTurnCount,
                ElapsedSeconds: elapsedSeconds,
                RemainingSeconds: remainingSeconds,
                CandidateCountry: null);
            reply = await orchestrator.GenerateReplyAsync(aiCtx, ct);
        }
        catch (PromptNotGroundedException)
        {
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "UNGROUNDED",
                "AI grounding failed. Please contact support.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Speaking AI reply failed for session {SessionId}.", speakingSessionId);
            await Clients.Caller.SendAsync("SpeakingRoleplayError", "AI_ERROR",
                "The patient could not respond. Please try again.");
            return;
        }

        var replyText = string.IsNullOrWhiteSpace(reply.Text)
            ? "Sorry, could you say that again?"
            : reply.Text.Trim();

        // ── 4. Synthesise the reply + persist both turns ──
        var replyMs = nowMs + 800;
        segments.Add(new SpeakingTurnSegment(
            isWarmUp ? "interlocutor" : "patient", replyMs, replyMs, replyText, 1.0));

        var replyAudioUrl = await TrySynthesizeReplyAudioAsync(sp, replyText, ct);

        await PersistSpeakingSegmentsAsync(db, transcriptRow, speakingSessionId, segments, ct);

        // ── 5. Stream the patient utterance back (hidden card stays server-side) ──
        await Clients.Caller.SendAsync("PatientUtterance", new
        {
            speaker = isWarmUp ? "interlocutor" : "patient",
            phase = isWarmUp ? "warmup" : "roleplay",
            text = replyText,
            audioUrl = replyAudioUrl,
            emotionHint = reply.EmotionHint,
            shouldEnd = reply.ShouldEnd,
            timestamp = DateTimeOffset.UtcNow,
        }, ct);

        if (reply.ShouldEnd)
        {
            await Clients.Caller.SendAsync("SpeakingShouldEnd", new
            {
                reason = "ai_closed",
                at = DateTimeOffset.UtcNow,
            }, ct);
        }
    }

    /// <summary>
    /// Schedules server-side <c>TimeNearlyUp</c> + <c>TimeUp</c> broadcasts
    /// for an active role-play session. The learner UI invokes this once
    /// it has called <c>POST /start-roleplay</c> and the clock has begun.
    /// Idempotent — calling twice cancels the prior timer before starting
    /// a fresh one. Phase 4 (4.2).
    /// </summary>
    public async Task StartRolePlayTimer(string speakingSessionId, int rolePlaySeconds)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(speakingSessionId))
        {
            return;
        }
        if (rolePlaySeconds <= 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, Context.ConnectionAborted);
        if (session is null || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            return;
        }
        if (session.State != SpeakingSessionState.Active)
        {
            return;
        }

        // Cancel any pre-existing timer for this session — replacing it
        // is intentional (e.g. learner refreshed the page).
        if (ActiveRolePlayTimers.TryRemove(speakingSessionId, out var existing))
        {
            try { existing.Cancel(); } catch { /* ignore */ }
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        ActiveRolePlayTimers[speakingSessionId] = cts;

        var groupName = SpeakingRoleplayGroupPrefix + speakingSessionId;
        var hubContext = Context.GetHttpContext()?.RequestServices
            .GetService<IHubContext<ConversationHub>>();

        // Fire-and-forget timer. We resolve a hub context above so the
        // background task can broadcast even after the originating
        // connection moves on.
        _ = Task.Run(async () =>
        {
            try
            {
                var nearlyUpAfter = Math.Max(0, rolePlaySeconds - 30);
                if (nearlyUpAfter > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(nearlyUpAfter), cts.Token);
                    if (hubContext is not null)
                    {
                        await hubContext.Clients.Group(groupName).SendAsync(
                            "TimeNearlyUp",
                            new { secondsLeft = 30, at = DateTimeOffset.UtcNow },
                            cts.Token);
                    }
                }

                var remainingAfterNearlyUp = Math.Min(rolePlaySeconds, 30);
                await Task.Delay(TimeSpan.FromSeconds(remainingAfterNearlyUp), cts.Token);

                if (hubContext is not null)
                {
                    await hubContext.Clients.Group(groupName).SendAsync(
                        "TimeUp",
                        new { at = DateTimeOffset.UtcNow },
                        cts.Token);
                }

                // Auto-end + kick off AI assessment. Best-effort; if it
                // races with a client-side EndSession the service-layer
                // guard will surface a Conflict which we swallow here so
                // the timer never throws unobserved.
                await using var endScope = scopeFactory.CreateAsyncScope();
                var sp = endScope.ServiceProvider;
                var sessions = sp.GetRequiredService<SpeakingSessionService>();
                try
                {
                    await sessions.EndSessionAsync(userId, speakingSessionId, cts.Token);
                }
                catch
                {
                    // Already ended by the client — that's fine.
                }

                // Fire AI assessment asynchronously so the client gets a
                // notification when the row is persisted.
                try
                {
                    var assessor = sp.GetRequiredService<SpeakingAiAssessmentService>();
                    var assessment = await assessor.RunAssessmentAsync(speakingSessionId, cts.Token);
                    if (hubContext is not null)
                    {
                        // Mock Speaking is human-marked: RunAssessmentAsync returns an
                        // empty AssessmentId (no AI row) and the session is routed to
                        // the tutor queue. Signal "AwaitingReview" instead of a score.
                        var awaitingReview = string.IsNullOrEmpty(assessment.AssessmentId);
                        await hubContext.Clients.Group(groupName).SendAsync(
                            awaitingReview ? "AwaitingReview" : "AssessmentReady",
                            new { assessmentId = assessment.AssessmentId, sessionId = speakingSessionId, awaitingReview },
                            cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "AI assessment kick-off failed inside role-play timer for session {SessionId}.",
                        speakingSessionId);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the learner ends early — nothing to do.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Speaking role-play timer for session {SessionId} crashed.",
                    speakingSessionId);
            }
            finally
            {
                if (ActiveRolePlayTimers.TryRemove(speakingSessionId, out var done) && ReferenceEquals(done, cts))
                {
                    done.Dispose();
                }
            }
        });
    }

    /// <summary>
    /// Cancels any in-flight role-play timer for the given session. The
    /// learner UI calls this when the user ends the session early so the
    /// background <c>TimeUp</c> broadcast does not double-fire.
    /// </summary>
    public Task CancelRolePlayTimer(string speakingSessionId)
    {
        if (string.IsNullOrWhiteSpace(speakingSessionId)) return Task.CompletedTask;
        if (ActiveRolePlayTimers.TryRemove(speakingSessionId, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Synthesises spoken audio for one AI utterance (opening line or reply)
    /// via the configured ElevenLabs voice and persists it, returning the
    /// authorised media URL the learner client fetches (with a bearer token)
    /// and plays. Returns <c>null</c> when TTS is disabled/unconfigured or the
    /// synthesis fails — the caller then falls back to text-only, and the
    /// frontend surfaces a "voice unavailable" hint. Passing an empty voice id
    /// lets the provider resolve the configured default (the Adam Stone voice).
    /// </summary>
    private async Task<string?> TrySynthesizeReplyAudioAsync(
        IServiceProvider sp, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var ttsSelector = sp.GetRequiredService<IConversationTtsProviderSelector>();
            var tts = await ttsSelector.TrySelectAsync(ct);
            if (tts is null) return null;

            var ttsResult = await tts.SynthesizeAsync(
                new ConversationTtsRequest(text, string.Empty, "en-GB"), ct);
            if (ttsResult.Audio.Length == 0) return null;

            var audio = sp.GetRequiredService<IConversationAudioService>();
            var aref = await audio.WriteAsync(ttsResult.Audio, ttsResult.MimeType, ct);
            return aref.Url;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speaking utterance TTS failed.");
            return null;
        }
    }

    /// <summary>
    /// Builds a textual persona prompt the AI patient should adopt for
    /// the role-play. Kept as a pure helper so it can be unit-tested
    /// without spinning up the hub. The string never leaks back to the
    /// learner — it lives in server memory / future LLM prompts only.
    /// </summary>
    internal static string BuildPatientPersonaPrompt(
        RolePlayCard card,
        InterlocutorScript script)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are role-playing a patient in an OET Speaking exam scenario.");
        sb.Append("Scenario: ").AppendLine(card.ScenarioTitle);
        sb.Append("Setting: ").AppendLine(card.Setting);
        sb.Append("You are the ").AppendLine(card.InterlocutorRole);
        if (!string.IsNullOrWhiteSpace(card.PatientName))
        {
            sb.Append("Your name is ").Append(card.PatientName);
            if (!string.IsNullOrWhiteSpace(card.PatientAge))
            {
                sb.Append(" (age ").Append(card.PatientAge).Append(')');
            }
            sb.AppendLine(".");
        }

        sb.Append("Emotional state: ").AppendLine(string.IsNullOrWhiteSpace(script.EmotionalState)
            ? card.PatientEmotion
            : script.EmotionalState);
        sb.Append("Resistance level: ").AppendLine(ResistanceLevels.ToCode(script.ResistanceLevel));

        sb.AppendLine("Open with this line, naturally and in character:");
        sb.AppendLine(script.OpeningResponse);

        var prompts = new[] { script.Prompt1, script.Prompt2, script.Prompt3 }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();
        if (prompts.Length > 0)
        {
            sb.AppendLine("Surface these cue prompts during the conversation when relevant:");
            foreach (var prompt in prompts)
            {
                sb.Append("  - ").AppendLine(prompt);
            }
        }

        if (!string.IsNullOrWhiteSpace(script.HiddenInformation))
        {
            sb.AppendLine("Hidden information (only reveal on direct questioning):");
            sb.AppendLine(script.HiddenInformation);
        }

        if (!string.IsNullOrWhiteSpace(script.ClosingCue))
        {
            sb.AppendLine("Closing cue (use when the candidate has satisfied your concerns):");
            sb.AppendLine(script.ClosingCue);
        }

        sb.AppendLine("Stay in character. Do not break the fourth wall. Use plain, patient-style language.");
        sb.AppendLine("Never coach the candidate. Never reveal that you are an AI.");
        sb.AppendLine("Escalate resistance subtly if the candidate dismisses your concerns or fails to acknowledge feelings.");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the warm-up persona prompt. Unlike the role-play persona
    /// this is intentionally generic: the warm-up is an unscored
    /// identity-check conversation in the spirit of the real OET
    /// exam intro — friendly, short, no clinical content.
    /// </summary>
    internal static string BuildWarmUpPersonaPrompt(RolePlayCard card)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an OET Speaking warm-up interlocutor.");
        sb.AppendLine("Your job is to put the candidate at ease with a brief, friendly conversation BEFORE the scored role-play begins.");
        sb.AppendLine("This warm-up is NOT scored — it is purely to verify identity and help the candidate settle in.");
        sb.AppendLine();
        sb.AppendLine("Persona rules:");
        sb.AppendLine("  - Be warm, polite, and unhurried.");
        sb.AppendLine("  - Ask one short question at a time.");
        sb.AppendLine("  - React naturally to the candidate's answer with a short acknowledgement before the next question.");
        sb.AppendLine("  - Do not introduce clinical scenarios.");
        sb.AppendLine("  - Do not mention scoring, criteria, or assessment.");
        sb.AppendLine("  - Stop after roughly 60-120 seconds of conversation, OR when the candidate signals they are ready to begin.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(card.ProfessionId))
        {
            sb.Append("Context: the candidate's profession is ")
              .Append(card.ProfessionId)
              .AppendLine(". Tailor questions to that profession when natural (e.g. work background, motivation, future plans).");
        }
        sb.AppendLine("Sample warm-up questions you may draw from (rotate, do not repeat verbatim):");
        var questions = SpeakingWarmUpSeed.GetQuestions(card.ProfessionId);
        foreach (var q in questions.Take(8))
        {
            sb.Append("  - ").AppendLine(q);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Wraps the persona system prompt in an Anthropic-style block list
    /// with ephemeral <c>cache_control</c> markers on the persona +
    /// rulebook context blocks. The Anthropic provider in
    /// <c>AiProviderRegistry</c> currently passes <c>system</c> as a
    /// flat string — this helper returns the structured shape future
    /// provider work can serialize directly.
    ///
    /// Returned as raw JSON so downstream callers do not need to take a
    /// dependency on a typed model. Phase 4 (4.5).
    /// </summary>
    internal static string BuildCachedSystemPromptJson(string personaPrompt, string? rulebookContext)
    {
        var blocks = new List<object>
        {
            new
            {
                type = "text",
                text = personaPrompt,
                cache_control = new { type = "ephemeral" },
            },
        };
        if (!string.IsNullOrWhiteSpace(rulebookContext))
        {
            blocks.Add(new
            {
                type = "text",
                text = rulebookContext,
                cache_control = new { type = "ephemeral" },
            });
        }
        return JsonSerializer.Serialize(blocks);
    }

    // ── WS2 turn-loop helpers ───────────────────────────────────────────

    /// <summary>One transcript segment in the canonical
    /// <c>{speaker, startMs, endMs, text, confidence, words[]}</c> shape the
    /// Speaking analytics + assessment layers read back.</summary>
    private sealed record SpeakingTurnSegment(
        string Speaker, long StartMs, long EndMs, string Text, double Confidence);

    /// <summary>Maps a free-form profession id (e.g. <c>"occupational-therapy"</c>)
    /// to the <see cref="ExamProfession"/> enum, mirroring
    /// <c>SpeakingAiAssessmentService.ParseProfession</c> so AI grounding is
    /// consistent between the live loop and post-hoc assessment.</summary>
    private static ExamProfession ParseProfessionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        var normalised = raw
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return Enum.TryParse<ExamProfession>(normalised, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    /// <summary>Parses an existing <c>SpeakingTranscript.SegmentsJson</c>
    /// array into the typed segment list, tolerating malformed / failure
    /// envelopes by returning an empty list.</summary>
    private static List<SpeakingTurnSegment> ParseSpeakingSegments(string? segmentsJson)
    {
        var list = new List<SpeakingTurnSegment>();
        if (string.IsNullOrWhiteSpace(segmentsJson)) return list;
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                var speaker = s.TryGetProperty("speaker", out var sp) ? sp.GetString() ?? "candidate" : "candidate";
                var startMs = s.TryGetProperty("startMs", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : 0L;
                var endMs = s.TryGetProperty("endMs", out var en) && en.ValueKind == JsonValueKind.Number ? en.GetInt64() : startMs;
                var text = s.TryGetProperty("text", out var tx) ? tx.GetString() ?? string.Empty : string.Empty;
                var conf = s.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number ? cf.GetDouble() : 1.0;
                list.Add(new SpeakingTurnSegment(speaker, startMs, endMs, text, conf));
            }
        }
        catch (JsonException)
        {
            // Malformed or failure-envelope JSON — start a fresh transcript.
            list.Clear();
        }
        return list;
    }

    /// <summary>Upserts the running role-play transcript so the latest row
    /// always holds the full ordered exchange. The AI assessment + analytics
    /// layers consume this single latest <see cref="SpeakingTranscript"/>.</summary>
    private static async Task PersistSpeakingSegmentsAsync(
        LearnerDbContext db,
        SpeakingTranscript? existing,
        string speakingSessionId,
        List<SpeakingTurnSegment> segments,
        CancellationToken ct)
    {
        var serialised = JsonSerializer.Serialize(segments.Select(s => new
        {
            speaker = s.Speaker,
            startMs = s.StartMs,
            endMs = s.EndMs,
            text = s.Text,
            confidence = s.Confidence,
            words = Array.Empty<string>(),
        }));
        var wordCount = segments.Sum(s => s.Text.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        var meanConfidence = segments.Count > 0 ? segments.Average(s => s.Confidence) : 0.0;
        var now = DateTimeOffset.UtcNow;

        if (existing is not null && string.Equals(existing.Provider, "live-roleplay", StringComparison.Ordinal))
        {
            existing.SegmentsJson = serialised;
            existing.WordCount = wordCount;
            existing.MeanConfidence = meanConfidence;
            existing.GeneratedAt = now;
        }
        else
        {
            // Demote any prior latest row from a different provider, then
            // start the live-roleplay transcript row.
            if (existing is not null)
            {
                existing.IsLatest = false;
            }
            db.SpeakingTranscripts.Add(new SpeakingTranscript
            {
                Id = Guid.NewGuid().ToString("n"),
                SpeakingSessionId = speakingSessionId,
                Provider = "live-roleplay",
                Language = "en",
                SegmentsJson = serialised,
                IsLatest = true,
                WordCount = wordCount,
                MeanConfidence = meanConfidence,
                GeneratedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
