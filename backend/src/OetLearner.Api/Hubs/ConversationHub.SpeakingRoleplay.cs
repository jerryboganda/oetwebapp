using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Hubs;

// Phase 2 (F) of the OET Speaking module roadmap.
//
// Extension hook that lets the typed Speaking session client (created
// via `POST /v1/speaking/sessions`) bootstrap an AI patient conversation
// from the existing ConversationHub plumbing. We deliberately stop at
// "seed the opening line" — the full audio/STT/TTS round-trip continues
// to use the `StartSession` / `SendAudio` family already in the main
// hub. The bridge is the SignalR group `speaking-session:{sessionId}`
// which the frontend subscribes to for patient utterances.
//
// This partial intentionally adds NO new audio pipeline; the existing
// realtime ASR/TTS code stays the single source of truth for streaming.
public partial class ConversationHub
{
    private const string SpeakingRoleplayGroupPrefix = "speaking-session:";

    /// <summary>
    /// Bootstraps the AI patient conversation for an existing typed
    /// Speaking session. Validates ownership, the session is in
    /// <c>prep</c> or <c>active</c>, joins the SignalR group keyed off
    /// the session id, and pushes the interlocutor's opening line so the
    /// learner UI can begin rendering the conversation transcript.
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

        if (session.State != SpeakingSessionState.Prep
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

        var groupName = SpeakingRoleplayGroupPrefix + speakingSessionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        await Clients.Caller.SendAsync("PatientUtterance", new
        {
            speaker = "patient",
            text = script.OpeningResponse,
            timestamp = DateTimeOffset.UtcNow,
        });
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
        return sb.ToString();
    }
}
