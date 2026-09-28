using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Helpers shared by the Writing V2 admin and pathway services.
/// </summary>
internal static class WritingServiceHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Stages an admin audit row on <paramref name="db"/>; the caller saves.</summary>
    public static void AddAuditEvent(LearnerDbContext db, TimeProvider clock, string actorId, string resourceType, string resourceId, string action, string? details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            ActorName = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            OccurredAt = clock.GetUtcNow(),
        });
    }

    public static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }
}
