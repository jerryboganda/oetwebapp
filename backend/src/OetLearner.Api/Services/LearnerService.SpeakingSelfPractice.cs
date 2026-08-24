using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

// Wave 5 of docs/SPEAKING-MODULE-PLAN.md.
//
// Deep-link from a speaking task into the AI-patient Conversation
// module. The plan's hard requirement is "no new AI provider, no new
// grounding code" — so this method delegates to ConversationService
// which already wires through `IAiGatewayService.BuildGroundedPrompt`
// (Kind=Conversation, Task=GenerateConversationOpening) and applies the
// existing entitlement caps via `IConversationEntitlementService`.
public partial class LearnerService
{
    public async Task<object> StartSpeakingSelfPracticeAsync(
        string userId,
        string contentId,
        ConversationService conversation,
        CancellationToken ct)
    {
        var content = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentId, ct)
            ?? throw ApiException.NotFound("speaking_task_not_found",
                "That speaking task does not exist.");
        if (!string.Equals(content.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("SPEAKING_SELF_PRACTICE_WRONG_SUBTEST",
                "Self-practice is only available for speaking tasks.");
        }
        if (content.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("speaking_task_not_published",
                "Speaking task is not currently available.");
        }

        // Build the conversation request using the speaking task's own
        // metadata. Task type is the standard OET role-play surface
        // ("oet-roleplay") which the conversation gateway grounds against
        // the conversation rulebook.
        var sessionId = $"cs-{Guid.NewGuid():N}";
        var request = new ConversationCreateSessionRequest(
            ContentId: content.Id,
            ExamFamilyCode: content.ExamFamilyCode,
            TaskTypeCode: "oet-roleplay",
            Profession: content.ProfessionId,
            Difficulty: content.Difficulty,
            SessionId: sessionId);

        // Credit gate: a live AI conversation has no separate "submit for
        // grading" moment (unlike Writing), so the wallet is debited here at
        // session-start against the conversation session id. Accounts that
        // have never purchased an AI package bypass harmlessly inside
        // DeductGradingCreditAsync. If debit fails after the session row is
        // created, the unused session is deleted.
        var sessionPayload = await conversation.CreateSessionAsync(userId, request, ct);
        string? feedbackMessage = null;
        if (aiPackageCreditService is not null)
        {
            var creditResult = await aiPackageCreditService.DeductGradingCreditAsync(userId, "speaking", sessionId, ct);
            if (!creditResult.Debited)
            {
                var unused = await db.ConversationSessions.FirstOrDefaultAsync(row => row.Id == sessionId, ct);
                if (unused is not null)
                {
                    db.ConversationSessions.Remove(unused);
                    await db.SaveChangesAsync(ct);
                }

                creditResult.EnsureDebited();
            }

            feedbackMessage = creditResult.FeedbackMessage;
        }

        // Wrap the conversation payload with the deep-link affordances
        // the speaking front-end needs — primarily the route the user
        // should be sent to.
        return new
        {
            session = sessionPayload,
            redirectPath = sessionPayload is null
                ? null
                : ResolveRedirectPath(sessionPayload),
            feedbackMessage,
        };
    }

    private static string? ResolveRedirectPath(object payload)
    {
        // ConversationService.MapSession returns an anonymous object with
        // an `id` property. Fish it out via reflection rather than
        // changing the conversation surface — keeps the deep-link wave
        // contained.
        var idProp = payload.GetType().GetProperty("id");
        var sessionId = idProp?.GetValue(payload) as string;
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : $"/conversation/{sessionId}";
    }
}
