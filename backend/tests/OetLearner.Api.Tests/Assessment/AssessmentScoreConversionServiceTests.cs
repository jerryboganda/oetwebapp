using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Tests.Assessment;

public sealed class AssessmentScoreConversionServiceTests
{
    [Fact]
    public void Validator_requires_exactly_one_row_for_each_raw_score_zero_to_forty_two()
    {
        var incomplete = Enumerable.Range(0, 42)
            .Select(raw => new AssessmentScoreTableRowInput(raw, raw * 10, null))
            .ToArray();

        var result = AssessmentScoreTableValidator.Validate("listening", incomplete);

        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_43_rows", result.ErrorCode);
    }

    [Fact]
    public void Validator_rejects_duplicate_raw_scores_even_when_row_count_is_43()
    {
        var rows = Enumerable.Range(0, 43)
            .Select(raw => new AssessmentScoreTableRowInput(raw == 42 ? 41 : raw, 100, null))
            .ToArray();

        var result = AssessmentScoreTableValidator.Validate("reading", rows);

        Assert.False(result.IsValid);
        Assert.Equal("score_table_duplicate_raw_score", result.ErrorCode);
    }

    [Fact]
    public async Task Resolver_returns_unavailable_without_an_effective_owner_table()
    {
        await using var db = NewDb();

        var result = await new AssessmentScoreConversionService(db)
            .ResolveAsync("listening", 30);

        Assert.False(result.IsAvailable);
        Assert.Null(result.ConvertedScore);
        Assert.Equal("score_table_not_configured", result.ErrorCode);
    }

    [Fact]
    public async Task Resolver_uses_exact_row_and_table_version_without_interpolation()
    {
        await using var db = NewDb();
        var table = CreateEffectiveTable("table-listening-v1", "listening", "v1");
        table.Rows.Single(row => row.RawScore == 30).ConvertedScore = 347;
        db.AssessmentScoreConversionTables.Add(table);
        await db.SaveChangesAsync();

        var result = await new AssessmentScoreConversionService(db)
            .ResolveAsync("listening", 30);

        Assert.True(result.IsAvailable);
        Assert.Equal(347, result.ConvertedScore);
        Assert.Equal("table-listening-v1", result.TableId);
        Assert.Equal("v1", result.TableVersionKey);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Attempt_snapshot_without_a_table_stays_raw_only_after_a_table_is_added()
    {
        await using var db = NewDb();
        var snapshot = AssessmentScoreConversionSnapshot.Capture(
            AssessmentScoreConversionResult.Unavailable(
                "reading",
                "default",
                0,
                "score_table_not_configured"))
            .Serialize();

        db.AssessmentScoreConversionTables.Add(CreateEffectiveTable("table-reading-later", "reading", "later"));
        await db.SaveChangesAsync();

        var result = await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
            new AssessmentScoreConversionService(db),
            "reading",
            rawScore: 30,
            snapshotJson: snapshot,
            legacyTableId: null,
            scopeKey: "default");

        Assert.False(result.IsAvailable);
        Assert.Null(result.ConvertedScore);
        Assert.Equal("score_table_not_configured", result.ErrorCode);
    }

    [Fact]
    public async Task Attempt_snapshot_uses_the_pinned_table_even_when_a_newer_table_is_effective()
    {
        await using var db = NewDb();
        var pinned = CreateEffectiveTable("table-reading-pinned", "reading", "pinned");
        pinned.Rows.Single(row => row.RawScore == 30).ConvertedScore = 333;
        var newer = CreateEffectiveTable("table-reading-newer", "reading", "newer");
        newer.EffectiveFrom = DateTimeOffset.UtcNow;
        newer.Rows.Single(row => row.RawScore == 30).ConvertedScore = 444;
        db.AssessmentScoreConversionTables.AddRange(pinned, newer);
        await db.SaveChangesAsync();

        var snapshot = new AssessmentScoreConversionSnapshot(
            "reading", "default", pinned.Id, pinned.VersionKey, null).Serialize();
        var result = await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
            new AssessmentScoreConversionService(db),
            "reading",
            rawScore: 30,
            snapshotJson: snapshot,
            legacyTableId: null,
            scopeKey: "default");

        Assert.True(result.IsAvailable);
        Assert.Equal(333, result.ConvertedScore);
        Assert.Equal("pinned", result.TableVersionKey);
    }

    [Fact]
    public void Malformed_attempt_snapshot_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AssessmentScoreConversionSnapshot.Parse("{not-json"));

        Assert.Equal("assessment_score_conversion_snapshot_invalid_json", ex.Message);
    }

    [Fact]
    public async Task MarkUsed_locks_effective_table_for_future_auditability()
    {
        await using var db = NewDb();
        var table = CreateEffectiveTable("table-reading-v1", "reading", "v1");
        db.AssessmentScoreConversionTables.Add(table);
        await db.SaveChangesAsync();

        await new AssessmentScoreConversionService(db).MarkUsedAsync(table.Id);

        var stored = await db.AssessmentScoreConversionTables.SingleAsync(x => x.Id == table.Id);
        Assert.True(stored.HasBeenUsed);
        Assert.Equal(AssessmentGovernanceStatus.Locked, stored.Status);
        Assert.NotNull(stored.LockedAt);
    }

    [Fact]
    public async Task MarkingPolicyResolver_requires_an_owner_effective_version()
    {
        await using var db = NewDb();

        var result = await new AssessmentMarkingPolicyService(db)
            .ResolveAsync("reading");

        Assert.False(result.IsAvailable);
        Assert.Equal("marking_policy_not_configured", result.ErrorCode);
        Assert.Null(result.PolicyVersionKey);
    }

    [Fact]
    public async Task MarkingPolicyResolver_snapshots_exact_document_and_locks_on_use()
    {
        await using var db = NewDb();
        var policy = CreateEffectivePolicy("policy-reading-v1", "reading", "v1");
        db.AssessmentMarkingPolicyVersions.Add(policy);
        await db.SaveChangesAsync();

        var service = new AssessmentMarkingPolicyService(db);
        var result = await service.ResolveAsync("reading");

        Assert.True(result.IsAvailable);
        Assert.Equal(policy.Id, result.PolicyId);
        Assert.Equal("v1", result.PolicyVersionKey);
        Assert.True(result.Document.CollapseInternalWhitespace);
        Assert.False(result.Document.ReadingPartAMatchingPartialCredit);

        await service.MarkUsedAsync(policy.Id);
        var stored = await db.AssessmentMarkingPolicyVersions.SingleAsync(x => x.Id == policy.Id);
        Assert.True(stored.HasBeenUsed);
        Assert.Equal(AssessmentGovernanceStatus.Locked, stored.Status);
    }

    [Fact]
    public async Task MarkingPolicyResolver_fails_closed_for_malformed_effective_json()
    {
        await using var db = NewDb();
        var policy = CreateEffectivePolicy("policy-reading-invalid", "reading", "invalid");
        policy.PolicyJson = "{not-json";
        db.AssessmentMarkingPolicyVersions.Add(policy);
        await db.SaveChangesAsync();

        var result = await new AssessmentMarkingPolicyService(db)
            .ResolveAsync("reading");

        Assert.False(result.IsAvailable);
        Assert.Null(result.PolicyId);
        Assert.Null(result.PolicyVersionKey);
        Assert.Equal("marking_policy_invalid_json", result.ErrorCode);
    }

    private static AssessmentScoreConversionTable CreateEffectiveTable(
        string id,
        string assessment,
        string versionKey)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new AssessmentScoreConversionTable
        {
            Id = id,
            Assessment = assessment,
            ScopeKey = "default",
            VersionKey = versionKey,
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = now,
            CreatedByUserId = "owner",
            CreatedAt = now,
            UpdatedAt = now,
            Rows = Enumerable.Range(0, 43)
                .Select(raw => new AssessmentScoreConversionRow
                {
                    Id = $"{id}-row-{raw}",
                    RawScore = raw,
                    ConvertedScore = raw == 0 ? 0 : raw == 42 ? 500 : 350,
                })
                .ToList(),
        };
    }

    private static AssessmentMarkingPolicyVersion CreateEffectivePolicy(
        string id,
        string assessment,
        string versionKey)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new AssessmentMarkingPolicyVersion
        {
            Id = id,
            Assessment = assessment,
            ScopeKey = "default",
            VersionKey = versionKey,
            PolicyJson = new AssessmentMarkingPolicyDocument(
                CollapseInternalWhitespace: true,
                CaseSensitive: true,
                ReadingPartAMatchingPartialCredit: false).Serialize(),
            Status = AssessmentGovernanceStatus.Effective,
            EffectiveFrom = now,
            CreatedByUserId = "owner",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
