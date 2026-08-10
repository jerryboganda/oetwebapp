using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingAssessmentPreflightTests
{
    [Fact]
    public async Task Missing_written_task_blocks_scoring_with_exact_missing_input()
    {
        await using var db = NewDb();
        var scenario = Scenario(taskPrompt: null);
        db.WritingScenarios.Add(scenario);
        db.WritingScenarioStructuredSentences.Add(Fact(scenario.Id, "Asthma; allergy status negative."));
        await db.SaveChangesAsync();
        var submission = Submission(scenario.Id);

        var result = await new WritingAssessmentPreflightService(db)
            .ValidateAsync(submission, CancellationToken.None);

        Assert.False(result.CanScore);
        Assert.Equal(WritingAssessmentV11Status.BlockedMissingInput, result.Status);
        Assert.Contains("written_task", result.MissingInputCodes);
    }

    [Fact]
    public async Task Unapproved_profession_pack_blocks_without_applying_medicine_rules()
    {
        await using var db = NewDb();
        var scenario = Scenario(profession: "nursing");
        db.WritingScenarios.Add(scenario);
        db.WritingScenarioStructuredSentences.Add(Fact(scenario.Id, "Patient has a wound requiring review."));
        await db.SaveChangesAsync();

        var result = await new WritingAssessmentPreflightService(db)
            .ValidateAsync(Submission(scenario.Id), CancellationToken.None);

        Assert.False(result.CanScore);
        Assert.Equal(WritingAssessmentV11Status.BlockedReleaseGate, result.Status);
        Assert.Contains("profession_pack_not_approved", result.ReleaseBlockCodes);
        Assert.DoesNotContain("medicine", result.AppliedRulePacks);
    }

    [Fact]
    public async Task Transfer_and_gp_letters_require_owner_approved_detailed_packs()
    {
        await using var db = NewDb();
        var scenario = Scenario(letterType: "transfer");
        db.WritingScenarios.Add(scenario);
        db.WritingScenarioStructuredSentences.Add(Fact(scenario.Id, "Transfer of care requested."));
        await db.SaveChangesAsync();

        var result = await new WritingAssessmentPreflightService(db)
            .ValidateAsync(Submission(scenario.Id), CancellationToken.None);

        Assert.False(result.CanScore);
        Assert.Contains("letter_type_pack_not_approved", result.ReleaseBlockCodes);
    }

    [Fact]
    public async Task Approved_medicine_pack_and_complete_inputs_allow_scoring()
    {
        await using var db = NewDb();
        var scenario = Scenario();
        db.WritingScenarios.Add(scenario);
        db.WritingScenarioStructuredSentences.Add(Fact(scenario.Id, "Asthma; allergy status negative."));
        db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
        {
            Id = Guid.NewGuid(),
            Profession = "medicine",
            LetterType = "routine_referral",
            VersionKey = "medicine-core-v11",
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateFacing = true,
        });
        await db.SaveChangesAsync();

        var result = await new WritingAssessmentPreflightService(db)
            .ValidateAsync(Submission(scenario.Id), CancellationToken.None);

        Assert.True(result.CanScore);
        Assert.Equal(WritingAssessmentV11Status.AwaitingPreflight, result.Status);
        Assert.Equal("medicine-core-v11", result.RulePackVersion);
    }

    private static WritingScenario Scenario(
        string? taskPrompt = "Write to Dr Green requesting a review.",
        string profession = "medicine",
        string letterType = "routine_referral") => new()
    {
        Id = Guid.NewGuid(),
        Title = "Assessment fixture",
        InternalCode = "TEST-WR-001",
        Profession = profession,
        LetterType = letterType,
        TaskPromptMarkdown = taskPrompt,
        Status = "published",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static WritingScenarioStructuredSentence Fact(Guid scenarioId, string text) => new()
    {
        Id = Guid.NewGuid(),
        ScenarioId = scenarioId,
        Ordinal = 1,
        SentenceText = text,
        RelevanceLabel = "relevant",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WritingSubmission Submission(Guid scenarioId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "learner-1",
        ScenarioId = scenarioId,
        LetterContent = "Dear Dr Green,\n\nI am writing to request a review of this patient.\n\nYours sincerely,\nDoctor",
        LetterContentHash = "hash",
        WordCount = 24,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-40),
        SubmittedAt = DateTimeOffset.UtcNow,
    };

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
