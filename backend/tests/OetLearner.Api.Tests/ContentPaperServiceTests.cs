using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class ContentPaperServiceTests
{
    private static (LearnerDbContext db, ContentPaperService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ContentPaperService(db));
    }

    private static async Task<string> AddMediaAsync(LearnerDbContext db, string id = "media-1", string? sha = null)
    {
        db.MediaAssets.Add(new MediaAsset
        {
            Id = id,
            OriginalFilename = $"{id}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 1024,
            StoragePath = $"uploads/published/aa/bb/{id}.pdf",
            Status = MediaAssetStatus.Ready,
            Sha256 = sha ?? "aabbccddeeff" + id.PadRight(52, '0'),
            UploadedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Create_generates_slug_and_rejects_duplicates()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            SubtestCode: "listening",
            Title: "Listening Sample 1",
            Slug: null,
            ProfessionId: null,
            AppliesToAllProfessions: true,
            Difficulty: null,
            EstimatedDurationMinutes: 40,
            CardType: null, LetterType: null, Priority: 0, TagsCsv: null,
            SourceProvenance: "Authored by Dr Hesham"), "admin-1", default);
        Assert.Equal("listening-sample-1", p.Slug);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new ContentPaperCreate(
                "listening", "Listening Sample 1", null, null, true,
                null, 40, null, null, 0, null, "A"), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Create_rejects_ambiguous_profession_scope()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new ContentPaperCreate(
                "writing", "W1", null, "medicine", true,
                null, 45, null, "routine_referral", 0, null, "A"), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RequiredRolesFor_matches_spec()
    {
        var (db, svc) = Build();
        Assert.Contains(PaperAssetRole.Audio, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.QuestionPaper, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.AnswerKey, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.CaseNotes, svc.RequiredRolesFor("writing"));
        Assert.Contains(PaperAssetRole.RoleCard, svc.RequiredRolesFor("speaking"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_fails_when_required_roles_missing()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null,
            "Authored by Dr Hesham"), "admin-1", default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(p.Id, "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_fails_without_source_provenance()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, SourceProvenance: null), "admin-1", default);

        await AddMediaAsync(db, "m-cn");
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, "m-cn", null, null, 0, MakePrimary: true),
            "admin-1", default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(p.Id, "admin-1", default));
        Assert.Contains("SourceProvenance", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_succeeds_when_requirements_met()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, "Authored by Dr Hesham"), "admin-1", default);

        await AddMediaAsync(db, "m-cn");
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, "m-cn", null, null, 0, MakePrimary: true),
            "admin-1", default);

        await svc.PublishAsync(p.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        Assert.NotNull(reload.PublishedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AttachAsset_flips_previous_primary_for_same_role_part()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null,
            "Authored by Dr Hesham"), "admin-1", default);
        await AddMediaAsync(db, "m1", "sha-one");
        await AddMediaAsync(db, "m2", "sha-two");

        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.Audio, "m1", null, null, 0, true), "admin-1", default);
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.Audio, "m2", null, null, 1, true), "admin-1", default);

        var assets = await db.ContentPaperAssets.Where(a => a.PaperId == p.Id).ToListAsync();
        Assert.Equal(2, assets.Count);
        Assert.Single(assets, a => a.IsPrimary);
        Assert.Equal("m2", assets.Single(a => a.IsPrimary).MediaAssetId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Archive_sets_archived_status_and_timestamp()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null, "A"),
            "admin-1", default);
        await svc.ArchiveAsync(p.Id, "admin-1", default);
        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Archived, reload.Status);
        Assert.NotNull(reload.ArchivedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task List_filters_by_subtest_and_profession_including_applies_to_all()
    {
        var (db, svc) = Build();
        await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null, "A"),
            "admin-1", default);
        await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1-med", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, "A"), "admin-1", default);
        await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1-nurse", null, "nursing", false, null, 45, null, "routine_referral",
            0, null, "A"), "admin-1", default);

        var medicineWriting = await svc.ListAsync(new ContentPaperQuery(
            SubtestCode: "writing", ProfessionId: "medicine"), default);
        Assert.Single(medicineWriting);
        Assert.Equal("W1-med", medicineWriting[0].Title);

        var medicineAll = await svc.ListAsync(new ContentPaperQuery(
            ProfessionId: "medicine"), default);
        // Should include both W1-med (profession match) and L1 (applies-to-all)
        Assert.Equal(2, medicineAll.Count);
        await db.DisposeAsync();
    }
}
