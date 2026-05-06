using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

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
        var request = new ConversationCreateSessionRequest(
            ContentId: content.Id,
            ExamFamilyCode: content.ExamFamilyCode,
            TaskTypeCode: "oet-roleplay",
            Profession: content.ProfessionId,
            Difficulty: content.Difficulty);

        var sessionPayload = await conversation.CreateSessionAsync(userId, request, ct);

        // Wrap the conversation payload with the deep-link affordances
        // the speaking front-end needs — primarily the route the user
        // should be sent to.
        return new
        {
            session = sessionPayload,
            redirectPath = sessionPayload is null
                ? null
                : ResolveRedirectPath(sessionPayload),
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
