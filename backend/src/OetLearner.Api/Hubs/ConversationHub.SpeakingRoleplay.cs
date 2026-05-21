using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
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

            await Clients.Caller.SendAsync("PatientUtterance", new
            {
                speaker = "interlocutor",
                phase = "warmup",
                text = opening,
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

        await Clients.Caller.SendAsync("PatientUtterance", new
        {
            speaker = "patient",
            phase = "roleplay",
            text = script.OpeningResponse,
            timestamp = DateTimeOffset.UtcNow,
        });
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
                        await hubContext.Clients.Group(groupName).SendAsync(
                            "AssessmentReady",
                            new { assessmentId = assessment.AssessmentId, sessionId = speakingSessionId },
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
}
