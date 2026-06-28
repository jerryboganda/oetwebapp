using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// A learner's saved yellow highlights on a scenario's Case Notes PDF. Persisted
/// per (UserId, ScenarioId) — independent of attempt/mode — so the marks pre-load
/// on every future attempt of the same scenario. The submission keeps its own
/// point-in-time snapshot (<see cref="WritingSubmission.CaseNoteHighlightsJson"/>)
/// for the results page and tutor review.
/// </summary>
public class WritingCaseNoteHighlight
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid ScenarioId { get; set; }

    /// <summary>JSON: <c>Record&lt;pageNumber, Highlight[]&gt;</c>. Defaults to an empty map.</summary>
    public string HighlightsJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
