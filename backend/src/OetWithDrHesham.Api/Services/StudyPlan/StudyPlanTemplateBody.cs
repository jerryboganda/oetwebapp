using System.Text.Json.Serialization;

namespace OetWithDrHesham.Api.Services.Planner;

/// <summary>
/// Strongly-typed view over the JSON document stored in
/// <see cref="Domain.StudyPlanTemplate.TemplateBodyJson"/>. The admin authoring UI
/// produces this shape; the generator consumes it.
/// </summary>
public sealed class StudyPlanTemplateBody
{
    [JsonPropertyName("weeks")]
    public List<StudyPlanTemplateWeek> Weeks { get; set; } = new();

    [JsonPropertyName("checkpoints")]
    public List<StudyPlanTemplateCheckpoint> Checkpoints { get; set; } = new();
}

public sealed class StudyPlanTemplateWeek
{
    [JsonPropertyName("weekIndex")]
    public int WeekIndex { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("days")]
    public List<StudyPlanTemplateDay> Days { get; set; } = new();
}

public sealed class StudyPlanTemplateDay
{
    [JsonPropertyName("dayOfWeek")]
    public string DayOfWeek { get; set; } = "mon";

    [JsonPropertyName("slots")]
    public List<StudyPlanTemplateSlot> Slots { get; set; } = new();
}

public sealed class StudyPlanTemplateSlot
{
    [JsonPropertyName("subtest")]
    public string Subtest { get; set; } = "reading";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "next-unattempted-paper";

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; } = 20;

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("rationaleHint")]
    public string? RationaleHint { get; set; }

    /// <summary>For slot kind = custom-content, the pinned ContentItem id.</summary>
    [JsonPropertyName("contentId")]
    public string? ContentId { get; set; }
}

public sealed class StudyPlanTemplateCheckpoint
{
    [JsonPropertyName("afterWeek")]
    public int AfterWeek { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "mini-mock";

    [JsonPropertyName("subtests")]
    public List<string> Subtests { get; set; } = new();
}

public static class StudyPlanSlotKinds
{
    public const string NextUnattemptedPaper = "next-unattempted-paper";
    public const string DrillByTag = "drill-by-tag";
    public const string SpacedRepReview = "spaced-rep-review";
    public const string WeakSkillFocus = "weak-skill-focus";
    public const string FullMock = "full-mock";
    public const string MiniMock = "mini-mock";
    public const string ExpertReviewSubmission = "expert-review-submission";
    public const string PronunciationDrill = "pronunciation-drill";
    public const string VocabularyFlashcards = "vocabulary-flashcards";
    public const string CustomContent = "custom-content";
}

public static class StudyPlanSubtestCodes
{
    public const string Reading = "reading";
    public const string Listening = "listening";
    public const string Writing = "writing";
    public const string Speaking = "speaking";
    public const string Vocabulary = "vocabulary";
    public const string Pronunciation = "pronunciation";
    public const string Mock = "mock";
}

public static class StudyPlanSections
{
    public const string Today = "today";
    public const string ThisWeek = "thisWeek";
    public const string NextCheckpoint = "nextCheckpoint";
    public const string WeakSkillFocus = "weakSkillFocus";
}
