using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Release-gate tests for the full published Writing catalogue: every
/// candidate-facing scenario must resolve grading configuration (profession,
/// pack, canonical inputs, recipient, Model Answer) BEFORE a learner ever
/// submits. One failing published scenario blocks release.
/// </summary>
public sealed class WritingCatalogueCompatibilityTests
{
    private static readonly string[] Professions =
    [
        "medicine", "nursing", "dentistry", "pharmacy", "physiotherapy", "veterinary",
        "optometry", "radiography", "occupational_therapy", "speech_pathology", "podiatry",
        "dietetics", "other_allied_health",
    ];

    private static readonly string[] CatalogueLetterTypes =
        ["LT-RR", "LT-UR", "LT-DG", "LT-TR", "LT-NM", "LT-OT"];

    [Fact]
    public async Task Full_published_matrix_reports_zero_invalid()
    {
        await using var db = NewDb();
        SeedValidCatalogue(db, Professions, CatalogueLetterTypes);
        await db.SaveChangesAsync();

        var report = await Service(db).ScanPublishedCatalogueAsync();

        Assert.Equal(Professions.Length * CatalogueLetterTypes.Length, report.PublishedScenarios);
        Assert.Equal(0, report.Invalid);
        Assert.Equal(report.PublishedScenarios, report.PublishReady);
        Assert.All(report.Rows, r =>
        {
            Assert.True(r.PublishReady);
            Assert.Empty(r.BlockingCodes);
            Assert.True(r.CanonicalCaseNotesReady);
            Assert.True(r.ExactWritingTaskReady);
            Assert.True(r.RecipientMetadataReady);
            Assert.True(r.SavedModelAnswerReady);
            Assert.False(string.IsNullOrWhiteSpace(r.RulePackVersion));
        });
        var other = Assert.Single(report.Rows, r => r.LetterType == "LT-OT" && r.Profession == "nursing");
        Assert.True(other.OtherLetters);
        Assert.True(other.PublishReady);
    }

    [Fact]
    public async Task Invalid_published_task_reports_exact_blocking_codes()
    {
        await using var db = NewDb();
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = "Broken published task",
            Profession = "medicine",
            LetterType = "LT-RR",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.WritingScenarios.Add(scenario);
        await db.SaveChangesAsync();

        var report = await Service(db).ScanPublishedCatalogueAsync();

        Assert.Equal(1, report.PublishedScenarios);
        Assert.Equal(1, report.Invalid);
        var row = Assert.Single(report.Rows);
        Assert.False(row.PublishReady);
        Assert.Contains("written_task_required", row.BlockingCodes);
        Assert.Contains("case_note_pages", row.BlockingCodes);
        Assert.Contains("profession_pack_not_approved", row.BlockingCodes);
        Assert.Contains("model_answer_not_approved", row.BlockingCodes);
        Assert.Equal("block_unpublish_pending_admin", row.RecommendedAction);
    }

    [Fact]
    public async Task Quarantine_dry_run_leaves_published_tasks_visible()
    {
        await using var db = NewDb();
        db.WritingScenarios.Add(BrokenScenario());
        await db.SaveChangesAsync();

        var result = await Service(db).QuarantineInvalidPublishedAsync(dryRun: true);

        Assert.Equal(1, result.Scanned);
        Assert.Equal(0, result.Quarantined);
        Assert.Equal("would_archive", Assert.Single(result.Items).Outcome);
        Assert.Equal("published", (await db.WritingScenarios.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Quarantine_archives_invalid_published_tasks_without_deleting()
    {
        await using var db = NewDb();
        var broken = BrokenScenario();
        db.WritingScenarios.Add(broken);
        db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = broken.Id,
            Ordinal = 1,
            SentenceText = "Kept for history.",
            RelevanceLabel = "relevant",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await Service(db).QuarantineInvalidPublishedAsync(dryRun: false);

        Assert.Equal(1, result.Quarantined);
        Assert.Equal("archived", Assert.Single(result.Items).Outcome);
        Assert.Equal("archived", (await db.WritingScenarios.AsNoTracking().SingleAsync()).Status);
        // History is preserved: sentences and audit events survive quarantine.
        Assert.Equal(1, await db.WritingScenarioStructuredSentences.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.Action == "writing.catalogue.quarantined"));
    }

    [Fact]
    public async Task Quarantine_skips_tasks_repaired_before_execution()
    {
        await using var db = NewDb();
        SeedValidCatalogue(db, ["medicine"], ["LT-RR"]);
        await db.SaveChangesAsync();

        var result = await Service(db).QuarantineInvalidPublishedAsync(dryRun: false);

        Assert.Equal(1, result.Scanned);
        Assert.Equal(0, result.Quarantined);
        Assert.Empty(result.Items);
    }

    private static void SeedValidCatalogue(
        LearnerDbContext db,
        IReadOnlyList<string> professions,
        IReadOnlyList<string> letterTypes)
    {
        foreach (var profession in professions)
        {
            // Released packs per profession: exact packs for the letter types
            // with a genuine detailed-pack requirement, plus the generic
            // packs every other letter type (including Other Letters)
            // resolves through.
            foreach (var packLetterType in new[] { "routine_referral", "other", "transfer", "referral_to_gp" })
            {
                db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
                {
                    Id = Guid.NewGuid(),
                    Profession = profession,
                    LetterType = packLetterType,
                    VersionKey = $"{profession}-{packLetterType}-v1",
                    Status = WritingAssessmentReleaseStatus.Approved,
                    CandidateFacing = true,
                });
            }

            foreach (var letterType in letterTypes)
            {
                var scenarioId = Guid.NewGuid();
                db.WritingScenarios.Add(new WritingScenario
                {
                    Id = scenarioId,
                    Title = $"{profession} {letterType} task",
                    Profession = profession,
                    LetterType = letterType,
                    TaskPromptMarkdown = "Write to Dr Green requesting a review.",
                    Status = "published",
                    AuthorId = "admin-1",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
                {
                    Id = Guid.NewGuid(),
                    ScenarioId = scenarioId,
                    Ordinal = 1,
                    SentenceText = "Asthma; allergy status negative.",
                    RelevanceLabel = "relevant",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
                {
                    Id = Guid.NewGuid(),
                    ScenarioId = scenarioId,
                    Status = WritingAssessmentModelAnswerStatus.Ready,
                    IsCandidateVisible = true,
                    ModelAnswerText = "Exemplar letter text.",
                    ValidatorVersion = OetLearner.Api.Services.Rulebook.WritingRuleEngine.ValidatorVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
        }
    }

    private static WritingScenario BrokenScenario() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Broken published task",
        Profession = "medicine",
        LetterType = "LT-RR",
        Status = "published",
        AuthorId = "admin-1",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static WritingCataloguePreflightService Service(LearnerDbContext db)
        => new(db, TimeProvider.System);

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
