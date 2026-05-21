using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Phase 6 of the OET Speaking module roadmap.
//
// `InterlocutorTrainingModule` is a small training unit (article + quiz)
// tutors must complete before they can be calibrated. Modules are sorted
// within their stage (`Onboarding` runs first, `Refresher` is repeated
// quarterly).
//
// `InterlocutorTrainingProgress` records each tutor's completion of each
// module — one row per (tutor, module) pair.

public enum InterlocutorTrainingStage
{
    Onboarding = 0,
    Refresher = 1,
}

[Index(nameof(Stage), nameof(Status))]
public class InterlocutorTrainingModule
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    public int OrderIndex { get; set; }

    /// <summary>Long-form markdown. No MaxLength — modules routinely run
    /// to several thousand words including embedded examples.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>JSON array of `MediaAsset.Id` values referenced inline
    /// (videos, audio clips, PDF excerpts).</summary>
    public string MediaAssetIdsJson { get; set; } = "[]";

    /// <summary>When true, the tutor cannot be added to the calibration
    /// pool until this module is completed.</summary>
    public bool RequiredForCalibration { get; set; } = false;

    public InterlocutorTrainingStage Stage { get; set; } = InterlocutorTrainingStage.Onboarding;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

[Index(nameof(TutorId), nameof(ModuleId), IsUnique = true)]
public class InterlocutorTrainingProgress
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    [MaxLength(64)]
    public string ModuleId { get; set; } = default!;

    public InterlocutorTrainingModule? Module { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Quiz score (0..100). Null while the tutor has only
    /// opened the module without taking the quiz.</summary>
    public int? QuizScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
