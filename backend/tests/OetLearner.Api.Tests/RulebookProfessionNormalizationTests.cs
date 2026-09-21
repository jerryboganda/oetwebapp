using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests;

public sealed class RulebookProfessionNormalizationTests
{
    [Fact]
    public void AdminMetadata_UsesCanonicalSeedProfessionIds()
    {
        Assert.Equal(
            new[]
            {
                "medicine",
                "nursing",
                "dentistry",
                "pharmacy",
                "physiotherapy",
                "veterinary",
                "optometry",
                "radiography",
                "occupational-therapy",
                "speech-pathology",
                "podiatry",
                "dietetics",
                "other-allied-health",
            },
            RulebookAdminService.ValidProfessions);
    }

    [Theory]
    [InlineData("occupational-therapy", ExamProfession.OccupationalTherapy)]
    [InlineData("speech-pathology", ExamProfession.SpeechPathology)]
    [InlineData("other-allied-health", ExamProfession.OtherAlliedHealth)]
    public void Parser_AcceptsCanonicalSeedProfessionIds(string value, ExamProfession expected)
    {
        Assert.True(RulebookProfessionParser.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DbLoader_ResolvesHyphenatedPublishedProfessionRows()
    {
        using var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"rulebook-profession-{Guid.NewGuid():N}")
            .Options);
        db.RulebookVersions.Add(new RulebookVersion
        {
            Id = "rb_writing_other-allied-health_test",
            Kind = "writing",
            Profession = "other-allied-health",
            Version = "db-test",
            Status = RulebookStatus.Published,
        });
        db.RulebookRuleRows.Add(new RulebookRuleRow
        {
            Id = "db-rule",
            RulebookVersionId = "rb_writing_other-allied-health_test",
            Code = "DB_RULE",
            SectionCode = "01",
            Title = "DB rule",
            Body = "DB rule body",
            Severity = "info",
            AppliesToJson = "\"all\"",
        });
        db.SaveChanges();

        var loader = new DbBackedRulebookLoader(
            new RulebookLoader(),
            db,
            new MemoryCache(new MemoryCacheOptions()));

        var book = loader.Load(RuleKind.Writing, ExamProfession.OtherAlliedHealth);

        Assert.Equal("db-test", book.Version);
        Assert.Contains(book.Rules, rule => rule.Id == "DB_RULE");

        var enumerated = loader.All().ToList();
        Assert.Contains(enumerated, candidate =>
            candidate.Kind == RuleKind.Writing
            && candidate.Profession == ExamProfession.OtherAlliedHealth
            && candidate.Version == "db-test");
    }
}
