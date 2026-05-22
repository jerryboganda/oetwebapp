using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Tracks a bulk audio regeneration batch initiated from the Voice Design Studio admin page.
/// </summary>
public class AudioRegenerationBatch
{
    [Key, MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string AudioType { get; set; } = "all"; // all | listening | vocabulary | conversation

    [Required, MaxLength(32)]
    public string Scope { get; set; } = "all"; // all | missing | different-voice

    [Required, MaxLength(16)]
    public string Status { get; set; } = "running"; // running | completed | failed | cancelled

    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public int FailedItems { get; set; }

    [MaxLength(64)]
    public string VoiceId { get; set; } = "Cherry";

    [MaxLength(32)]
    public string ModelVariant { get; set; } = "flash";

    public double Speed { get; set; } = 1.0;
    public double Pitch { get; set; } = 0;

    [MaxLength(256)]
    public string Emotion { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Instructions { get; set; } = string.Empty;

    [MaxLength(128)]
    public string RequestedBy { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
