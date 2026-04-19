using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Grammar starter catalog — 50+ lessons across OET (Medicine) + IELTS General
/// + PTE Academic, every lesson grounded in rulebooks/grammar/medicine/rulebook.v1.json.
///
/// Each lesson's <c>sourceProvenance</c> names Dr. Hesham's Grammar Rulebook v1
/// and lists the specific rule IDs (G01.1, G02.3, etc.) the lesson drills.
/// This mirrors the contract enforced by <c>GrammarPublishGateService</c> —
/// i.e. seeded lessons pass the publish gate immediately.
/// </summary>
public static partial class SeedData
{
    private static void SeedGrammarStarterCatalog(LearnerDbContext db, DateTimeOffset now)
    {
        var specs = GrammarStarterSpecs();
        var lessons = new List<GrammarLesson>(specs.Length);
        foreach (var spec in specs)
        {
            var contentBlocks = new object[]
            {
                GrammarContentBlock($"{spec.Id}-intro", 1, "callout", spec.Intro),
                GrammarContentBlock($"{spec.Id}-example", 2, "example", spec.Example),
                GrammarContentBlock($"{spec.Id}-note", 3, "note", spec.Note),
            };

            var exercises = new object[spec.Exercises.Length];
            for (var i = 0; i < spec.Exercises.Length; i++)
            {
                var ex = spec.Exercises[i];
                exercises[i] = GrammarExerciseWithRules(
                    id: $"{spec.Id}-ex-{i + 1}",
                    sortOrder: i + 1,
                    type: ex.Type,
                    promptMarkdown: ex.Prompt,
                    options: ex.Options,
                    correctAnswer: ex.CorrectAnswer,
                    acceptedAnswers: ex.AcceptedAnswers,
                    explanationMarkdown: ex.Explanation,
                    difficulty: ex.Difficulty,
                    points: ex.Points,
                    appliedRuleIds: spec.RuleIds);
            }

            lessons.Add(CreateGrammarLessonWithRules(
                now: now,
                id: spec.Id,
                examTypeCode: spec.ExamTypeCode,
                title: spec.Title,
                description: spec.Description,
                category: spec.Category,
                level: spec.Level,
                estimatedMinutes: spec.EstimatedMinutes,
                sortOrder: spec.SortOrder,
                sourceProvenance: $"Dr. Hesham Grammar Rulebook v1 — rule IDs {string.Join(", ", spec.RuleIds)}",
                appliedRuleIds: spec.RuleIds,
                contentBlocks: contentBlocks,
                exercises: exercises,
                prerequisiteLessonId: spec.PrerequisiteLessonId));
        }

        db.GrammarLessons.AddRange(lessons);
    }

    private static GrammarLesson CreateGrammarLessonWithRules(
        DateTimeOffset now,
        string id,
        string examTypeCode,
        string title,
        string description,
        string category,
        string level,
        int estimatedMinutes,
        int sortOrder,
        string sourceProvenance,
        string[] appliedRuleIds,
        object[] contentBlocks,
        object[] exercises,
        string? prerequisiteLessonId = null)
    {
        var document = new
        {
            topicId = category,
            category,
            sourceProvenance,
            appliedRuleIds,
            prerequisiteLessonIds = prerequisiteLessonId is null ? Array.Empty<string>() : new[] { prerequisiteLessonId },
            contentBlocks,
            exercises,
            version = 1,
            updatedAt = now.ToString("O"),
        };

        return new GrammarLesson
        {
            Id = id,
            ExamTypeCode = examTypeCode,
            Title = title,
            Description = description,
            Category = category,
            Level = level,
            ContentHtml = JsonSupport.Serialize(document),
            ExercisesJson = JsonSupport.Serialize(exercises),
            EstimatedMinutes = estimatedMinutes,
            SortOrder = sortOrder,
            PrerequisiteLessonId = prerequisiteLessonId,
            Status = "active",
        };
    }

    private static object GrammarExerciseWithRules(
        string id,
        int sortOrder,
        string type,
        string promptMarkdown,
        object options,
        object correctAnswer,
        string[] acceptedAnswers,
        string explanationMarkdown,
        string difficulty,
        int points,
        string[] appliedRuleIds)
        => new
        {
            id,
            sortOrder,
            type,
            promptMarkdown,
            options,
            correctAnswer,
            acceptedAnswers,
            explanationMarkdown,
            difficulty,
            points,
            appliedRuleIds,
        };

    private sealed record GrammarSeedSpec(
        string Id,
        string ExamTypeCode,
        string Title,
        string Description,
        string Category,
        string Level,
        int EstimatedMinutes,
        int SortOrder,
        string[] RuleIds,
        string Intro,
        string Example,
        string Note,
        GrammarSeedExercise[] Exercises,
        string? PrerequisiteLessonId = null);

    private sealed record GrammarSeedExercise(
        string Type,
        string Prompt,
        object Options,
        object CorrectAnswer,
        string[] AcceptedAnswers,
        string Explanation,
        string Difficulty,
        int Points);
}
