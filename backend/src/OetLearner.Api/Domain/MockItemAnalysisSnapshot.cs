using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Mocks V2 Wave 3 — per-item difficulty / distractor analysis snapshot.
/// One row per (MockBundleId, ItemId). Admins refresh on-demand from the
/// bundle dashboard; the service aggregates from <see cref="ReadingAnswer"/>
/// and listening answers across submitted attempts.
/// </summary>
[Index(nameof(MockBundleId))]
[Index(nameof(MockBundleId), nameof(SubtestCode))]
[Index(nameof(MockBundleId), nameof(ItemId), IsUnique = true,
    Name = "UX_MockItemAnalysis_Bundle_Item")]
public class MockItemAnalysisSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleId { get; set; } = default!;

    /// <summary>Canonical item identifier (reading question id, listening item id, etc.).</summary>
    [MaxLength(64)]
    public string ItemId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    /// <summary>Optional source label for the admin (e.g. "Reading Part A · Q3").</summary>
    [MaxLength(160)]
    public string? Label { get; set; }

    public int TotalAttempts { get; set; }
    public int CorrectCount { get; set; }
    public double Difficulty { get; set; } // 0..1, p-value (proportion correct)

    /// <summary>JSON map { distractorCategory: pickCount }.</summary>
    public string DistractorJson { get; set; } = "{}";

    /// <summary>"too_easy" | "too_hard" | "tempting_distractor" | null.</summary>
    [MaxLength(32)]
    public string? Flag { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
