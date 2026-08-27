using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// Seeds the owner-effective default Reading/Listening marking policies into a
/// test database — the same rows production gets from the EF migration
/// <c>20260904130000_SeedDefaultAssessmentGovernance</c>. Test hosts never run
/// hosted migrations/migrators over InMemory/SQLite fixtures, so without this
/// every learner-facing attempt start fails closed with
/// <c>reading_marking_policy_unavailable</c> / <c>listening_marking_policy_unavailable</c>.
///
/// The seeded rows carry EffectiveFrom 2020-01-01 so any test that supplies its
/// own later-dated policy row still wins resolution (latest EffectiveFrom wins;
/// ties are an error). Tests asserting the not-configured failure path must use
/// a context where this seeder has NOT been applied.
/// </summary>
public static class AssessmentGovernanceSeeder
{
    public const string DefaultMarkingPolicyJson =
        """{"trimLeadingTrailingWhitespace":true,"collapseInternalWhitespace":false,"caseSensitive":true,"readingPartAMatchingPartialCredit":false,"listeningAudioReplayAllowed":false,"audioLockMode":"exam","technicalRequirementsGuidanceOnly":true}""";

    private static readonly DateTimeOffset DefaultEffectiveFrom = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AssessmentMarkingPolicyVersion CreateDefaultEffectivePolicy(string assessment)
        => new()
        {
            Id = $"{assessment}-default-v1",
            Assessment = assessment,
            ScopeKey = "default",
            VersionKey = "v1",
            PolicyJson = DefaultMarkingPolicyJson,
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = DefaultEffectiveFrom,
            ApprovedAt = DefaultEffectiveFrom,
            ApprovedByUserId = "test-owner",
            CreatedByUserId = "test-owner",
            CreatedAt = DefaultEffectiveFrom,
            UpdatedAt = DefaultEffectiveFrom,
        };

    /// <summary>Idempotently adds reading+listening defaults; callers decide when
    /// to SaveChanges (sync Build() paths call SaveChanges immediately).</summary>
    public static void SeedDefaultEffectivePolicies(LearnerDbContext db)
    {
        foreach (var assessment in new[] { "reading", "listening" })
        {
            var id = $"{assessment}-default-v1";
            if (!db.AssessmentMarkingPolicyVersions.Any(x => x.Id == id))
            {
                db.AssessmentMarkingPolicyVersions.Add(CreateDefaultEffectivePolicy(assessment));
            }
        }
    }

    private static readonly DateTimeOffset DefaultTableEffectiveFrom = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AssessmentScoreConversionTable CreateDefaultScoreTable(string assessment)
        => new()
        {
            Id = $"{assessment}-score-v1",
            Assessment = assessment,
            ScopeKey = "default",
            VersionKey = "v1",
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = DefaultTableEffectiveFrom,
            ApprovedAt = DefaultTableEffectiveFrom,
            ApprovedByUserId = "test-owner",
            CreatedByUserId = "test-owner",
            CreatedAt = DefaultTableEffectiveFrom,
            UpdatedAt = DefaultTableEffectiveFrom,
            Rows = Enumerable.Range(0, 43)
                .Select(raw =>
                {
                    var scaled = OetScoring.OetRawToScaled(raw);
                    return new AssessmentScoreConversionRow
                    {
                        Id = $"{assessment}-score-v1-row-{raw}",
                        RawScore = raw,
                        ConvertedScore = scaled,
                        Grade = OetScoring.OetGradeLetterFromScaled(scaled),
                        Passed = OetScoring.IsListeningReadingPassByRaw(raw),
                    };
                })
                .ToList(),
        };

    /// <summary>Idempotently adds reading+listening default score tables.</summary>
    public static void SeedDefaultScoreTables(LearnerDbContext db)
    {
        foreach (var assessment in new[] { "reading", "listening" })
        {
            var id = $"{assessment}-score-v1";
            if (!db.AssessmentScoreConversionTables.Any(x => x.Id == id))
            {
                db.AssessmentScoreConversionTables.Add(CreateDefaultScoreTable(assessment));
            }
        }
    }
}
