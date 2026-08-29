using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// W5 — persisted Listening post-submit Q&amp;A turn. Unique
/// (<see cref="SessionId"/>, <see cref="ClientTurnId"/>) makes a duplicate
/// POST return the stored answer with no second provider call.
/// </summary>
public sealed class ListeningQnaTurn
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string ClientTurnId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string QuestionId { get; set; } = default!;

    public string Message { get; set; } = default!;

    public string Reply { get; set; } = default!;

    [MaxLength(64)]
    public string? AiOperationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
