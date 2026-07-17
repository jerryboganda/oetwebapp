using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

// NOTE: WritingLesson (slug-based, SkillCode field) already exists in
// WritingPathwayEntities.cs from the prior pathway slice. To preserve that
// schema and avoid collisions, the Writing Module V2 introduces the
// extended W1–W8 lesson catalog as WritingLessonV2 + WritingLessonCompletionV2.
// Per spec §25.1.12 plus the plan's "Extend, don't rename" rule, the V2
// table carries the new SubSkill/OrderInCourse/QuizQuestionsJson columns
// alongside the legacy slug-based lessons during the rollout window.
public class WritingLessonV2
{
    public Guid Id { get; set; }

    [MaxLength(4)]
    public string SubSkill { get; set; } = default!;

    public int OrderInCourse { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    public string BodyMarkdown { get; set; } = default!;

    [MaxLength(512)]
    public string? VideoUrl { get; set; }

    public int EstimatedMinutes { get; set; } = 5;

    public string QuizQuestionsJson { get; set; } = "[]";

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingLessonCompletionV2
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid LessonId { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int? QuizScore { get; set; }

    public int QuizAttempts { get; set; }
}
