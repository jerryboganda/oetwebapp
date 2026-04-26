using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class ContentDeduplicationServiceTests
{
    private static (LearnerDbContext db, ContentDeduplicationService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ContentDeduplicationService(db));
    }

    private static ContentItem MakeContent(
        string id,
        string title,
        string subtest = "writing",
        string profession = "medicine",
        string scenarioType = "discharge",
        ContentStatus status = ContentStatus.Published,
        int qualityScore = 50,
        DateTimeOffset? updatedAt = null)
        => new()
        {
            Id = id,
            ContentType = "task",
            SubtestCode = subtest,
            Title = title,
            Difficulty = "intermediate",
            EstimatedDurationMinutes = 10,
            ModeSupportJson = "[]",
            CriteriaFocusJson = "[]",
            PublishedRevisionId = $"rev-{id}",
            Status = status,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            DifficultyRating = 1500,
            SourceType = "manual",
            QaStatus = "approved",
            ProfessionId = profession,
            ScenarioType = scenarioType,
            QualityScore = qualityScore,
            FreshnessConfidence = "current",
        };

    // ── ComputeFingerprint (pure) ──────────────────────────────────────────

    [Fact]
    public void ComputeFingerprint_is_stable_for_identical_inputs()
    {
        var a = MakeContent("a", "Discharge letter for asthma");
        var b = MakeContent("b", "Discharge letter for asthma");
        Assert.Equal(ContentDeduplicationService.ComputeFingerprint(a),
                     ContentDeduplicationService.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_is_case_insensitive_and_whitespace_normalised()
    {
        var a = MakeContent("a", "Discharge Letter   for Asthma");
        var b = MakeContent("b", "  discharge letter for asthma  ");
        Assert.Equal(ContentDeduplicationService.ComputeFingerprint(a),
                     ContentDeduplicationService.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_strips_batch_suffix()
    {
        var a = MakeContent("a", "Discharge letter (batch 3)");
        var b = MakeContent("b", "Discharge letter");
        Assert.Equal(ContentDeduplicationService.ComputeFingerprint(a),
                     ContentDeduplicationService.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_strips_month_year_suffix()
    {
        var a = MakeContent("a", "Discharge letter (March 2026)");
        var b = MakeContent("b", "Discharge letter (Sep 2025)");
        var c = MakeContent("c", "Discharge letter");
        var fa = ContentDeduplicationService.ComputeFingerprint(a);
        Assert.Equal(fa, ContentDeduplicationService.ComputeFingerprint(b));
        Assert.Equal(fa, ContentDeduplicationService.ComputeFingerprint(c));
    }

    [Fact]
    public void ComputeFingerprint_differs_on_subtest()
    {
        var a = MakeContent("a", "Same title", subtest: "writing");
        var b = MakeContent("b", "Same title", subtest: "speaking");
        Assert.NotEqual(ContentDeduplicationService.ComputeFingerprint(a),
                        ContentDeduplicationService.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_treats_null_profession_as_general()
    {
        var withNull = MakeContent("a", "Same title");
        withNull.ProfessionId = null;
        var withGeneral = MakeContent("b", "Same title", profession: "general");
        Assert.Equal(ContentDeduplicationService.ComputeFingerprint(withNull),
                     ContentDeduplicationService.ComputeFingerprint(withGeneral));
    }

    [Fact]
    public void ComputeFingerprint_treats_null_scenarioType_as_none()
    {
        var withNull = MakeContent("a", "Same title");
        withNull.ScenarioType = null;
        var withNone = MakeContent("b", "Same title", scenarioType: "none");
        Assert.Equal(ContentDeduplicationService.ComputeFingerprint(withNull),
                     ContentDeduplicationService.ComputeFingerprint(withNone));
    }

    [Fact]
    public void ComputeFingerprint_returns_lowercase_hex_16_chars()
    {
        var fp = ContentDeduplicationService.ComputeFingerprint(MakeContent("a", "x"));
        Assert.Equal(16, fp.Length);
        Assert.Matches("^[0-9a-f]{16}$", fp);
    }

    // ── ScanForDuplicatesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ScanForDuplicatesAsync_groups_items_with_matching_fingerprint()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(
            MakeContent("a", "Discharge letter (batch 1)"),
            MakeContent("b", "Discharge letter (batch 2)"),
            MakeContent("c", "Different topic entirely"));
        await db.SaveChangesAsync();

        var result = await svc.ScanForDuplicatesAsync(default);

        Assert.Equal(1, result.GroupsFound);
        Assert.Equal(2, result.ItemsTagged);
        var a = await db.ContentItems.FindAsync("a");
        var b = await db.ContentItems.FindAsync("b");
        var c = await db.ContentItems.FindAsync("c");
        Assert.NotNull(a!.DuplicateGroupId);
        Assert.Equal(a.DuplicateGroupId, b!.DuplicateGroupId);
        Assert.StartsWith("dup-", a.DuplicateGroupId);
        Assert.Null(c!.DuplicateGroupId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_ignores_archived_and_superseded_items()
    {
        var (db, svc) = Build();
        var archived = MakeContent("a", "Same", status: ContentStatus.Archived);
        var superseded = MakeContent("b", "Same");
        superseded.FreshnessConfidence = "superseded";
        db.ContentItems.AddRange(archived, superseded, MakeContent("c", "Same"));
        await db.SaveChangesAsync();

        var result = await svc.ScanForDuplicatesAsync(default);

        // Only "c" remains eligible; one item alone is not a duplicate group.
        Assert.Equal(0, result.GroupsFound);
        Assert.Equal(0, result.ItemsTagged);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_is_idempotent()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(MakeContent("a", "Same"), MakeContent("b", "Same"));
        await db.SaveChangesAsync();

        var first = await svc.ScanForDuplicatesAsync(default);
        var second = await svc.ScanForDuplicatesAsync(default);

        Assert.Equal(1, first.GroupsFound);
        Assert.Equal(2, first.ItemsTagged);
        Assert.Equal(1, second.GroupsFound);
        Assert.Equal(0, second.ItemsTagged); // already tagged on first run
        await db.DisposeAsync();
    }

    // ── DesignateCanonicalAsync ────────────────────────────────────────────

    [Fact]
    public async Task DesignateCanonicalAsync_marks_canonical_current_and_others_superseded()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(
            MakeContent("a", "Same"),
            MakeContent("b", "Same"),
            MakeContent("c", "Same"));
        await db.SaveChangesAsync();
        var scan = await svc.ScanForDuplicatesAsync(default);
        var groupId = (await db.ContentItems.FindAsync("a"))!.DuplicateGroupId!;

        Assert.Equal(1, scan.GroupsFound);
        var ok = await svc.DesignateCanonicalAsync(groupId, "b", default);
        Assert.True(ok);

        var canonical = await db.ContentItems.FindAsync("b");
        var loserA = await db.ContentItems.FindAsync("a");
        var loserC = await db.ContentItems.FindAsync("c");

        Assert.Null(canonical!.SupersededById);
        Assert.Equal("current", canonical.FreshnessConfidence);

        foreach (var loser in new[] { loserA!, loserC! })
        {
            Assert.Equal("b", loser.SupersededById);
            Assert.Equal("superseded", loser.FreshnessConfidence);
            Assert.Equal(ContentStatus.Archived, loser.Status);
            Assert.NotNull(loser.ArchivedAt);
        }
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DesignateCanonicalAsync_returns_false_when_group_empty()
    {
        var (db, svc) = Build();
        Assert.False(await svc.DesignateCanonicalAsync("dup-missing", "a", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DesignateCanonicalAsync_returns_false_when_canonical_id_not_in_group()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(MakeContent("a", "Same"), MakeContent("b", "Same"));
        await db.SaveChangesAsync();
        await svc.ScanForDuplicatesAsync(default);
        var groupId = (await db.ContentItems.FindAsync("a"))!.DuplicateGroupId!;

        Assert.False(await svc.DesignateCanonicalAsync(groupId, "not-in-group", default));
        await db.DisposeAsync();
    }

    // ── RemoveFromGroupAsync ───────────────────────────────────────────────

    [Fact]
    public async Task RemoveFromGroupAsync_returns_false_when_item_missing()
    {
        var (db, svc) = Build();
        Assert.False(await svc.RemoveFromGroupAsync("nope", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RemoveFromGroupAsync_returns_false_when_item_has_no_group()
    {
        var (db, svc) = Build();
        db.ContentItems.Add(MakeContent("a", "Solo"));
        await db.SaveChangesAsync();
        Assert.False(await svc.RemoveFromGroupAsync("a", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RemoveFromGroupAsync_clears_DuplicateGroupId_and_dissolves_singleton_remnant()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(MakeContent("a", "Same"), MakeContent("b", "Same"));
        await db.SaveChangesAsync();
        await svc.ScanForDuplicatesAsync(default);

        var ok = await svc.RemoveFromGroupAsync("a", default);
        Assert.True(ok);

        var a = await db.ContentItems.FindAsync("a");
        var b = await db.ContentItems.FindAsync("b");
        Assert.Null(a!.DuplicateGroupId);
        // Only one item left in group → it should also be cleared.
        Assert.Null(b!.DuplicateGroupId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RemoveFromGroupAsync_keeps_group_when_two_or_more_remain()
    {
        var (db, svc) = Build();
        db.ContentItems.AddRange(
            MakeContent("a", "Same"),
            MakeContent("b", "Same"),
            MakeContent("c", "Same"));
        await db.SaveChangesAsync();
        await svc.ScanForDuplicatesAsync(default);
        var groupId = (await db.ContentItems.FindAsync("a"))!.DuplicateGroupId!;

        await svc.RemoveFromGroupAsync("a", default);

        var b = await db.ContentItems.FindAsync("b");
        var c = await db.ContentItems.FindAsync("c");
        Assert.Equal(groupId, b!.DuplicateGroupId);
        Assert.Equal(groupId, c!.DuplicateGroupId);
        await db.DisposeAsync();
    }

    // ── GetDuplicateGroupAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicateGroupAsync_returns_null_for_unknown_group()
    {
        var (db, svc) = Build();
        Assert.Null(await svc.GetDuplicateGroupAsync("dup-missing", default));
        await db.DisposeAsync();
    }
}
