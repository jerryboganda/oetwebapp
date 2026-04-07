using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using System.Security.Claims;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for real-time AI conversation practice.
/// Handles audio streaming, transcription, and AI response generation.
/// </summary>
[Authorize]
public class ConversationHub(
    IServiceScopeFactory scopeFactory,
    SpeechToTextService speechToText,
    ILogger<ConversationHub> logger) : Hub
{
    private static readonly string[] AIResponses = new[]
    {
        "I see, thank you for explaining that. Can you tell me more about what happened after the procedure?",
        "That's very helpful. Now, regarding the follow-up appointment, when would you like to schedule that?",
        "I understand. And what about the pain medication — has the current dosage been effective?",
        "Thank you for that information. Let me ask about your mobility. How have you been getting around?",
        "I appreciate you sharing that. Could you describe any side effects you've noticed from the medication?",
        "That makes sense. Now, in terms of your daily activities, what has been most challenging?",
        "Thank you. I'd also like to know about your diet since the operation. Have you been able to eat normally?",
        "I see. And have you had any concerns about going home? Is there anything you'd like us to address?",
        "That's good to hear. Let me also check — do you have support at home for the first few days?",
        "Thank you for the thorough update. I think we have a good picture of the current situation."
    };

    public async Task StartSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        logger.LogInformation("ConversationHub: User {UserId} starting session {SessionId}", userId, sessionId);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var session = await db.ConversationSessions.FindAsync(sessionId);
        if (session == null || session.UserId != userId)
        {
            await Clients.Caller.SendAsync("ConversationError", "SESSION_NOT_FOUND", "Session not found.");
            return;
        }

        session.State = "active";
        session.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        await Clients.Caller.SendAsync("SessionStateChanged", "active");
    }

    public async Task SendAudio(string sessionId, string audioBase64)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            // Transcribe audio
            var transcription = await speechToText.TranscribeAudioChunkAsync(audioBase64, sessionId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            var session = await db.ConversationSessions.FindAsync(sessionId);
            if (session == null || session.UserId != userId || session.State != "active")
            {
                await Clients.Caller.SendAsync("ConversationError", "INVALID_SESSION", "Session is not active.");
                return;
            }

            session.TurnCount++;
            var learnerTurnNumber = session.TurnCount;

            // Save learner turn
            var learnerTurn = new ConversationTurn
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                TurnNumber = learnerTurnNumber,
                Role = "learner",
                Content = transcription.Text,
                DurationMs = transcription.DurationMs,
                TimestampMs = (int)(DateTimeOffset.UtcNow - (session.StartedAt ?? DateTimeOffset.UtcNow)).TotalMilliseconds,
                ConfidenceScore = transcription.Confidence,
                AnalysisJson = "{}"
            };
            db.ConversationTurns.Add(learnerTurn);

            // Send transcript to client
            await Clients.Caller.SendAsync("ReceiveTranscript", learnerTurnNumber, transcription.Text, transcription.Confidence);

            // Generate AI response
            session.TurnCount++;
            var aiTurnNumber = session.TurnCount;
            var aiResponseIndex = (session.TurnCount / 2 - 1) % AIResponses.Length;
            var aiText = AIResponses[Math.Abs(aiResponseIndex)];

            var aiTurn = new ConversationTurn
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                TurnNumber = aiTurnNumber,
                Role = "ai",
                Content = aiText,
                DurationMs = aiText.Split(' ').Length * 250,
                TimestampMs = (int)(DateTimeOffset.UtcNow - (session.StartedAt ?? DateTimeOffset.UtcNow)).TotalMilliseconds + 1500,
                AnalysisJson = "{}"
            };
            db.ConversationTurns.Add(aiTurn);

            // Update transcript JSON
            var turns = db.ConversationTurns
                .Where(t => t.SessionId == sessionId)
                .OrderBy(t => t.TurnNumber)
                .Select(t => new { t.TurnNumber, t.Role, t.Content })
                .ToList();
            // Include the just-added entities too
            session.TranscriptJson = JsonSupport.Serialize(turns);

            await db.SaveChangesAsync();

            // Small delay to simulate AI "thinking"
            await Task.Delay(800);

            // Send AI response to client
            await Clients.Caller.SendAsync("ReceiveAIResponse", aiTurnNumber, aiText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ConversationHub: Error processing audio for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ConversationError", "PROCESSING_ERROR", "Failed to process audio. Please try again.");
        }
    }

    public async Task EndSession(string sessionId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var session = await db.ConversationSessions.FindAsync(sessionId);
        if (session == null || session.UserId != userId) return;

        var now = DateTimeOffset.UtcNow;
        session.State = "evaluating";
        session.CompletedAt = now;
        session.DurationSeconds = session.StartedAt.HasValue
            ? (int)(now - session.StartedAt.Value).TotalSeconds
            : 0;

        // Queue evaluation job
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.ConversationEvaluation,
            ResourceId = sessionId,
            State = AsyncState.Queued,
            AvailableAt = now.AddSeconds(3),
            CreatedAt = now,
            LastTransitionAt = now,
            StatusReasonCode = "queued",
            StatusMessage = "Conversation evaluation queued."
        });

        await db.SaveChangesAsync();

        await Clients.Caller.SendAsync("SessionStateChanged", "evaluating");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("ConversationHub: Client disconnected. ConnectionId={ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
