using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Singleton document describing how OET sub-tests are scored. One row per
/// active version; the row with <see cref="IsActive"/>=true is what learners
/// see on their dashboard. Older versions are kept for audit.
/// </summary>
[Index(nameof(IsActive))]
public class ScoringPolicy
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Free-form markdown body shown to learners as "How am I graded?".
    /// </summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// JSON payload of the structured score-table. Shape (suggested):
    /// {
    ///   "listening": { "passing": { "default": 350, "us": 300 }, "rawToScaled": [{"raw":30,"scaled":350,"grade":"B"}] },
    ///   "reading":   { ... },
    ///   "writing":   { "passing": { "uk": 350, "us": 300, "qa": 300 } },
    ///   "speaking":  { "passing": { "default": 350 } }
    /// }
    /// </summary>
    public string PolicyJson { get; set; } = "{}";

    public bool IsActive { get; set; }

    [MaxLength(64)]
    public string? UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
